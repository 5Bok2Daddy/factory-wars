namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 36 전/후 캡처와 렌더 수치(GPU 필요, -testCategory CaptureUrban).
  /// 전 = 시가지 칸을 끄고(VisualCatalog 꺼짐) 옛 바닥 색·시가지 장식 끔, 후 = 지금 설정. 같은 장면·같은 카메라, 파일 해시는 서로 달라야 한다.
  /// 결과: screenshots/urban36/{장면}_{before|after}.png, Game-logs/assets/urban/render-stats.csv
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static string UrbanFolder() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "urban36"));

    string ShootUrban(string name) {
      var camera = Camera.main;
      var surface = new RenderTexture(1920, 1080, 24);
      camera.targetTexture = surface;
      camera.Render();
      RenderTexture.active = surface;
      var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
      image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
      image.Apply();
      RenderTexture.active = null;
      var path = Path.Combine(UrbanFolder(), name + ".png");
      Directory.CreateDirectory(Path.GetDirectoryName(path));
      File.WriteAllBytes(path, image.EncodeToPNG());
      // 노출 과다 검사용: 밝기 245 이상인 픽셀 비율.
      var pixels = image.GetPixels32();
      var bright = 0;
      foreach (var pixel in pixels) {
        if (pixel.r >= 245 && pixel.g >= 245 && pixel.b >= 245) {
          bright++;
        }
      }
      _urbanBright[name] = bright / (float)pixels.Length;
      Object.Destroy(image);
      camera.targetTexture = null;
      surface.Release();
      return path;
    }

    readonly Dictionary<string, float> _urbanBright = new Dictionary<string, float>();
    readonly List<string> _urbanStats = new List<string>();

    /// <summary>옛 모습(전): 시가지 칸 끔, 옛 바닥 색, 시가지 장식 끔. 후(true)면 저장된 프로필 그대로.</summary>
    void UrbanLook(bool urban) {
      var profile = VisualProfile.Current;
      var catalog = VisualCatalog.Current;
      var saved = (profile.GroundGrass, profile.GroundGrassLight, profile.GroundGrassDark, profile.GroundDirt, profile.GroundDirtDark, profile.GroundPlaza, profile.GroundPlazaDark, profile.UrbanDecor);
      var enabled = catalog.Enabled;
      if (urban == false) {
        profile.GroundGrass = new Color32(0x7B, 0xE5, 0xBA, 0xFF);
        profile.GroundGrassLight = new Color32(0x8C, 0xEE, 0xC6, 0xFF);
        profile.GroundGrassDark = new Color32(0x6A, 0xD4, 0xA8, 0xFF);
        profile.GroundDirt = new Color32(0xC2, 0xCB, 0xD9, 0xFF);
        profile.GroundDirtDark = new Color32(0xA8, 0xB3, 0xC5, 0xFF);
        profile.GroundPlaza = new Color32(0xAB, 0xB3, 0xCC, 0xFF);
        profile.GroundPlazaDark = new Color32(0xC1, 0xC9, 0xE4, 0xFF);
        profile.UrbanDecor = false;
        catalog.Enabled = false;
        catalog.Invalidate();
      }
      Restores.Add(() => {
        (profile.GroundGrass, profile.GroundGrassLight, profile.GroundGrassDark, profile.GroundDirt, profile.GroundDirtDark, profile.GroundPlaza, profile.GroundPlazaDark, profile.UrbanDecor) = saved;
        catalog.Enabled = enabled;
        catalog.Invalidate();
      });
    }

    IEnumerator UrbanShoot(string scene, string label, Vector2 focus, float height) {
      var camera = Controller().Camera;
      camera.MoveTo(focus);
      camera.Height = height;
      camera.Apply();
      yield return WaitFrames(Fps / 2);
      var renderCamera = Camera.main;
      var surface = new RenderTexture(1920, 1080, 24);
      renderCamera.targetTexture = surface;
      for (var i = 0; i < 6; i++) {
        renderCamera.Render();
      }
      var start = Time.realtimeSinceStartup;
      for (var i = 0; i < 10; i++) {
        renderCamera.Render();
      }
      var renderMs = (Time.realtimeSinceStartup - start) / 10f * 1000f;
      var visible = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.isVisible && r.gameObject.activeInHierarchy).ToArray();
      var draws = visible.Sum(r => r.sharedMaterials.Length);
      var triangles = TrianglesOf(visible);
      var planes = GeometryUtility.CalculateFrustumPlanes(renderCamera);
      var dressing = DressingRenderer.Active != null ? DressingRenderer.Active.Count(planes) : (0, 0, 0);
      renderCamera.targetTexture = null;
      surface.Release();
      _urbanStats.Add($"{scene},{label},{draws},{triangles},{dressing.Item1},{dressing.Item2},{renderMs:0.0}");
      ShootUrban($"{scene}_{label}");
    }

    [UnityTest, Explicit, Category("CaptureUrban"), Timeout(2400000)]
    public IEnumerator CaptureUrban_BeforeAfter() {
      if (PackPresent == false) {
        Assert.Pass("팩 없음");
      }
      Directory.CreateDirectory(UrbanFolder());
      _urbanStats.Clear();
      _urbanStats.Add("scene,label,visibleRendererMaterials(그리기 호출 추정),visibleTriangles,dressingBatches,dressingTriangles,cameraRenderMs(CPU)");
      var map = Resources.LoadAll<MapSpec>("FactoryWars").First(m => m.name == "map_Map4P");
      var spots = new List<(string Scene, Vector2 Focus, float Height)> {
        ("base", new Vector2(map.StartPositions[0].X.AsFloat, map.StartPositions[0].Z.AsFloat), 52f),
        ("crossroads", Vector2.zero, 52f),
        ("overview", Vector2.zero, 140f),
      };
      if (map.ResourceNodes != null && map.ResourceNodes.Length > 0) {
        var node = map.ResourceNodes[0].Shape.Center;
        spots.Add(("resource", new Vector2(node.X.AsFloat, node.Z.AsFloat), 50f));
      }
      if (map.Channel != null && map.Channel.Length > 0) {
        spots.Add(("canal", new Vector2(map.Channel[0].Center.X.AsFloat, map.Channel[0].Center.Z.AsFloat), 56f));
      }
      if (map.WaterGates != null && map.WaterGates.Length > 0) {
        spots.Add(("gate", new Vector2(map.WaterGates[0].Center.X.AsFloat, map.WaterGates[0].Center.Z.AsFloat), 46f));
      }
      if (map.CrateSpots != null && map.CrateSpots.Length > 0) {
        spots.Add(("supply", new Vector2(map.CrateSpots[0].Position.X.AsFloat, map.CrateSpots[0].Position.Z.AsFloat), 50f));
      }
      foreach (var urban in new[] { false, true }) {
        var label = urban ? "after" : "before";
        TearDown();
        yield return null;
        UrbanLook(urban);
        yield return StartMatch(Map4PScene, 2, units: new[] { (0, "ff_ranger_squad", 0, 6), (0, "ff_eagle_tank", 4, 10), (1, "cf_militia", 0, -8), (1, "cf_dragonscale_tank", 6, -12) }, truce: -1);
        yield return WaitForHud();
        yield return WaitForBattle();
        Time.timeScale = 1f;
        foreach (var spot in spots) {
          yield return UrbanShoot(spot.Scene, label, spot.Focus, spot.Height);
        }
      }
      // 품질별 장식 수: 같은 시가지를 보통·낮음으로 다시 센다(장식 예산은 보통 품질 기준).
      foreach (var tier in new[] { QualityTier.Medium, QualityTier.Low }) {
        GraphicsQuality.Apply(tier);
        yield return WaitFrames(Fps);
        foreach (var spot in spots.Where(sp => sp.Scene == "crossroads" || sp.Scene == "overview" || sp.Scene == "base")) {
          yield return UrbanShoot(spot.Scene, "after_" + tier.ToString().ToLowerInvariant(), spot.Focus, spot.Height);
        }
      }
      GraphicsQuality.Apply(QualityTier.High);
      File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "assets", "urban", "render-stats.csv")), _urbanStats);
      var problems = new List<string>();
      foreach (var spot in spots) {
        if (Sha(Path.Combine(UrbanFolder(), spot.Scene + "_before.png")) == Sha(Path.Combine(UrbanFolder(), spot.Scene + "_after.png"))) {
          problems.Add(spot.Scene + ": 전/후 캡처가 같음");
        }
        if (_urbanBright.TryGetValue(spot.Scene + "_after", out var bright) && bright > 0.03f) {
          problems.Add($"{spot.Scene}: 밝기 245 이상 픽셀 {bright:P1} (한도 3%)");
        }
      }
      File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "assets", "urban", "brightness.csv")), _urbanBright.Select(p => $"{p.Key},{p.Value:0.0000}"));
      Assert.IsEmpty(problems, string.Join("\n", problems));
    }
  }
}
