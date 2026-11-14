namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Security.Cryptography;
  using System.Text;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using Quantum.FactoryWars;
  using Unity.Profiling;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 32 에셋 팩 전/후 캡처와 렌더 측정(GPU 필요, -testCategory CaptureSynty).
  /// 전 = VisualCatalog.Enabled=false(지금까지의 절차 모델), 후 = 팩 모델. 같은 장면·같은 카메라로 찍고 파일 해시가 서로 달라야 한다.
  /// 결과: screenshots/synty32/{장면}_{before|after}.png, Game-logs/assets/synty/render-stats.csv
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static string SyntyFolder() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "synty32"));

    static string ShootSynty(string name) {
      var camera = Camera.main;
      var surface = new RenderTexture(1920, 1080, 24);
      camera.targetTexture = surface;
      camera.Render();
      RenderTexture.active = surface;
      var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
      image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
      image.Apply();
      RenderTexture.active = null;
      var path = Path.Combine(SyntyFolder(), name + ".png");
      Directory.CreateDirectory(Path.GetDirectoryName(path));
      File.WriteAllBytes(path, image.EncodeToPNG());
      Object.Destroy(image);
      camera.targetTexture = null;
      surface.Release();
      return path;
    }

    static string Sha(string path) {
      using (var sha = SHA256.Create()) {
        using (var stream = File.OpenRead(path)) {
          return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
        }
      }
    }

    readonly List<string> _syntyStats = new List<string>();

    /// <summary>한 장면: 새로 시작해 카메라를 맞추고, 잠시(걷는 모습이 보이도록) 기다린 뒤 찍고 렌더 수치를 잰다.</summary>
    IEnumerator SyntyScene(string scene, string label, Vector2 focus, float height, float seconds,
      (int Owner, string Unit, int X, int Z)[] units, (int Owner, string Building, int X, int Z)[] buildings = null,
      (int Owner, string Unit, int X, int Z, int ToX, int ToZ)[] marching = null, QualityTier? tier = null) {
      TearDown();
      yield return null;
      yield return StartMatch(units: units, buildings: buildings);
      yield return WaitForHud();
      yield return WaitForBattle();
      Time.timeScale = 1f;
      if (tier.HasValue) {
        GraphicsQuality.Apply(tier.Value);
      }
      var camera = Controller().Camera;
      camera.MoveTo(focus);
      camera.Height = height;
      camera.Apply();
      if (marching != null) {
        foreach (var group in marching.GroupBy(m => (m.ToX, m.ToZ))) {
          var ids = group.SelectMany(m => UnitsNamed(m.Owner, m.Unit)).ToArray();
          yield return Send(group.First().Owner, new MoveUnitsCommand { Units = ids, Target = V(group.Key.ToX, group.Key.ToZ) });
        }
      }
      yield return WaitFrames((int)(seconds * 30f));
      using (var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"))
      using (var triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count"))
      using (var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count")) {
        float drawSum = 0, triSum = 0, batchSum = 0, time = 0;
        const int Samples = 30;
        for (var i = 0; i < Samples; i++) {
          var start = Time.realtimeSinceStartup;
          yield return null;
          time += Time.realtimeSinceStartup - start;
          drawSum += draws.LastValue;
          triSum += triangles.LastValue;
          batchSum += batches.LastValue;
        }
        // 프로파일러 카운터는 배치 모드에서 0일 수 있어, 카메라를 직접 그려 보이는 렌더러 × 머티리얼 칸(그리기 호출 추정)과 삼각형을 센다.
        var renderCamera = Camera.main;
        var surface = new RenderTexture(1920, 1080, 24);
        renderCamera.targetTexture = surface;
        for (var i = 0; i < 6; i++) {
          renderCamera.Render();
        }
        var renderStart = Time.realtimeSinceStartup;
        for (var i = 0; i < 10; i++) {
          renderCamera.Render();
        }
        var renderMs = (Time.realtimeSinceStartup - renderStart) / 10f * 1000f;
        var visible = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.isVisible && r.gameObject.activeInHierarchy).ToArray();
        var guessDraws = visible.Sum(r => r.sharedMaterials.Length);
        var guessTriangles = TrianglesOf(visible);
        var skinned = visible.Count(r => r is SkinnedMeshRenderer);
        renderCamera.targetTexture = null;
        surface.Release();
        _syntyStats.Add($"{scene},{label},{Battle().Actors.Count},{guessDraws},{guessTriangles},{skinned},{renderMs:0.0},{drawSum / Samples:0},{triSum / Samples:0}");
      }
      ShootSynty($"{scene}_{label}");
    }

    [UnityTest, Explicit, Category("CaptureSynty"), Timeout(1800000)]
    public IEnumerator CaptureSynty_BeforeAfter() {
      if (PackPresent == false) {
        Assert.Pass("팩 없음");
      }
      Directory.CreateDirectory(SyntyFolder());
      var catalog = VisualCatalog.Current;
      Assert.IsNotNull(catalog);
      _syntyStats.Clear();
      _syntyStats.Add("scene,label,actors,visibleRendererMaterials(그리기 호출 추정),visibleTriangles,skinnedRenderers,cameraRenderMs(CPU),profilerDrawCalls,profilerTriangles");
      foreach (var after in new[] { false, true }) {
        var label = after ? "after" : "before";
        catalog.Enabled = after;
        catalog.Invalidate();
        // 1. 보병: 자유 연방 분대가 동쪽으로 달리고 대륙 연방 보병이 서 있다(걷기·달리기 애니메이션).
        yield return SyntyScene("infantry", label, new Vector2(2, 24), 26f, 1.5f, new[] {
          (0, "ff_ranger_squad", -8, 22), (0, "ff_ranger_squad", -8, 26), (0, "ff_skyguard_squad", -8, 30), (0, "ff_engineer", -6, 34),
          (1, "cf_militia", 14, 22), (1, "cf_militia", 14, 26), (1, "cf_flame_squad", 14, 30), (1, "cf_engineer", 14, 34) },
          marching: new[] { (0, "ff_ranger_squad", -8, 22, 4, 24), (0, "ff_skyguard_squad", -8, 30, 4, 30) });
        // 2. 차량: 양 진영 전차·지원차·정찰차가 마주 선다.
        yield return SyntyScene("vehicles", label, new Vector2(0, 26), 44f, 0.5f, new[] {
          (0, "ff_eagle_tank", -14, 14), (0, "ff_bulwark_tank", -14, 22), (0, "ff_field_support", -14, 30), (0, "ff_scout_buggy", -14, 38),
          (1, "cf_dragonscale_tank", 14, 14), (1, "cf_fortress_tank", 14, 22), (1, "cf_thunder_aa", 14, 30), (1, "cf_longwall_mlrs", 8, 38), (1, "cf_scout_bike", 18, 38) });
        // 3. 공중과 배.
        yield return SyntyScene("air", label, new Vector2(0, 26), 52f, 0.5f, new[] {
          (0, "ff_hawk_gunship", -16, 18), (0, "ff_hawk_gunship", -22, 26), (0, "ff_skyfortress_bomber", -8, 34),
          (1, "cf_gale_gunship", 14, 18), (1, "cf_gale_gunship", 22, 26) });
        // 4. 기지 건물: 자유 연방과 대륙 연방.
        foreach (var faction in new[] { "ff", "cf" }) {
          var owner = faction == "ff" ? 0 : 1;
          var names = new[] { "barracks", "supply_depot", "power_plant", "processor", "research_lab", "vehicle_factory", "airfield", "heavy_works", "ground_tower", "air_defense", "collection_yard", "sandbags", "bunker", "medic_tent", "repair_bay", "radar", "helipad", "artillery" };
          var list = names.Select((n, i) => (owner, $"{faction}_{n}", -30 + (i % 6) * 12, 16 + (i / 6) * 12)).ToArray();
          yield return SyntyScene("base_" + faction, label, new Vector2(0, 28), 52f, 0.5f, units: new (int, string, int, int)[0], buildings: list);
        }
      }
      // 품질 단계별 분대원 수 효과: 같은 보병 장면을 보통·낮음으로 다시 센다.
      catalog.Enabled = true;
      catalog.Invalidate();
      var infantry = new[] {
        (0, "ff_ranger_squad", -8, 22), (0, "ff_ranger_squad", -8, 26), (0, "ff_skyguard_squad", -8, 30), (0, "ff_engineer", -6, 34),
        (1, "cf_militia", 14, 22), (1, "cf_militia", 14, 26), (1, "cf_flame_squad", 14, 30), (1, "cf_engineer", 14, 34) };
      yield return SyntyScene("infantry", "after_medium", new Vector2(2, 24), 26f, 0.5f, infantry, tier: QualityTier.Medium);
      yield return SyntyScene("infantry", "after_low", new Vector2(2, 24), 26f, 0.5f, infantry, tier: QualityTier.Low);
      GraphicsQuality.Apply(QualityTier.High);
      File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "assets", "synty", "render-stats.csv")), _syntyStats);
      var problems = new List<string>();
      foreach (var scene in new[] { "infantry", "vehicles", "air", "base_ff", "base_cf" }) {
        var before = Path.Combine(SyntyFolder(), scene + "_before.png");
        var after = Path.Combine(SyntyFolder(), scene + "_after.png");
        if (Sha(before) == Sha(after)) {
          problems.Add(scene + ": 전/후 캡처가 같음");
        }
      }
      Assert.IsEmpty(problems, string.Join("\n", problems));
    }
  }
}
