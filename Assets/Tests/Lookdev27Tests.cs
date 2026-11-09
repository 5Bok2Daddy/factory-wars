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
  using Object = UnityEngine.Object;

  /// <summary>
  /// 항목 27 시안(K): K1 "깨끗한 시작"(0분, 시점 R·C·E), K2 "전투 누적"(AI 대 AI 같은 장소 1·10·20분), K-low(낮음 품질).
  /// 결과: screenshots/lookdev/27_sian_R_big_K.png, 27_sian_K1.png, 27_sian_K_low.png, 27_sian_battle_progress.png(+ _low).
  /// </summary>
  public unsafe partial class LocalMatchTests {
    /// <summary>시점 R: aow3-ref 01과 같은 구도. 본진 + 운하 + 다리 + 숲 가장자리가 한 화면에 든다(남서 자리).</summary>
    static Vector3 ViewR(MapSpec map) {
      var start = map.StartPositions[0].ToUnityVector3();
      var bridge = map.Bridges[0].Center.ToUnityVector3();
      return Vector3.Lerp(start, bridge, 0.55f) + new Vector3(4f, 0f, 8f);
    }

    static string LookdevShots() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "lookdev"));

    static Texture2D Reference(string folder, string file) {
      var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
      var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "concept-art", folder, file));
      if (File.Exists(path)) {
        texture.LoadImage(File.ReadAllBytes(path));
      }
      return texture;
    }

    IEnumerator ShootK1(List<(Texture2D, string)> cells, QualityTier tier, bool big) {
      GraphicsQuality.Apply(tier);
      Battle().Rebuild();
      yield return null;
      yield return null;
      GraphicsQuality.Apply(tier);
      yield return null;
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var camera = Camera.main;
      var profile = VisualProfile.Current;
      Aim(camera, ViewR(map), profile.DefaultHeight);
      var seen = Visible(camera, 1920, 1080);
      LookdevNote("27.csv", $"K1,{tier},R,{seen.Renderers},{seen.Triangles},{seen.Casters},{EstimateDrawCalls(seen)}");
      // K-0 값을 옮긴 게임 화면에서 바닥색 검증(목표 ±ΔE 10).
      var check = GameColorCheck(camera, map);
      LookdevNote("27.csv", $"K0-game,{tier},R,{check}");
      Debug.Log($"[FW-K0-GAME] {tier} {check}");
      if (big) {
        var full = Shoot(camera, 1920, 1080);
        File.WriteAllBytes(Path.Combine(LookdevShots(), "27_sian_R_big_K.png"), full.EncodeToPNG());
        Object.Destroy(full);
      }
      cells.Add((Shoot(camera, CellWidth * 2, CellHeight * 2), $"K {tier} [R] 본진·운하·다리 draw~{EstimateDrawCalls(seen)} tris {seen.Triangles / 1000}k"));
      var c = ViewPoints(map).First(v => v.Name == "C");
      Aim(camera, c.Focus, profile.DefaultHeight);
      seen = Visible(camera, 1920, 1080);
      LookdevNote("27.csv", $"K1,{tier},C,{seen.Renderers},{seen.Triangles},{seen.Casters},{EstimateDrawCalls(seen)}");
      cells.Add((Shoot(camera, CellWidth * 2, CellHeight * 2), $"K {tier} [C] 중앙 draw~{EstimateDrawCalls(seen)} tris {seen.Triangles / 1000}k"));
      Aim(camera, map.StartPositions[0].ToUnityVector3() + new Vector3(8, 0, 6), profile.DefaultHeight);
      cells.Add((ShootHud(camera, CellWidth * 2, CellHeight * 2), $"K {tier} [E] HUD"));
    }

    [UnityTest, Explicit, Category("Lookdev"), Timeout(6000000)]
    public IEnumerator Lookdev_27K1() {
      yield return FixedScene(30, human: true);
      Directory.CreateDirectory(LookdevShots());
      var cells = new List<(Texture2D, string)>();
      yield return ShootK1(cells, QualityTier.Medium, true);
      cells.Add((Reference("kenney-ref", "city-builder.png"), "참고: Kenney Starter Kit City Builder"));
      cells.Add((Reference("aow3-ref", "01_default_zoom_map.png"), "참고: aow3-ref 01 (구도·이펙트 참고만)"));
      SaveSheet("27_sian_K1", "27 시안 K1 깨끗한 시작(0분, 보통): R·C·E와 참고 이미지", cells, 3);
      foreach (var (texture, _) in cells) {
        Object.Destroy(texture);
      }
      var low = new List<(Texture2D, string)>();
      yield return ShootK1(low, QualityTier.Low, false);
      SaveSheet("27_sian_K_low", "27 시안 K-low(0분, 낮음): R·C·E", low, 3);
      foreach (var (texture, _) in low) {
        Object.Destroy(texture);
      }
      GraphicsQuality.Apply(QualityTier.Medium);
    }

    /// <summary>멈춘 경기를 seconds까지 빨리 돌리고 다시 멈춘다.</summary>
    IEnumerator AdvanceTo(int seconds) {
      QuantumRunner.Default.IsSessionUpdateDisabled = false;
      Time.timeScale = 12f;
      yield return WaitFor(() => Verified.Number >= seconds * Fps || GameEnded(), 1200f, $"경기 {seconds}초");
      QuantumRunner.Default.IsSessionUpdateDisabled = true;
      Time.timeScale = 1f;
      yield return null;
      yield return null;
    }

    [UnityTest, Explicit, Category("Lookdev"), Timeout(9000000)]
    public IEnumerator Lookdev_27K2() {
      yield return FixedScene(60, human: false);
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var camera = Camera.main;
      var places = new[] { ("중앙 교전지", Vector3.zero), ("앞마당 입구", map.Expansions[0].Position.ToUnityVector3() + new Vector3(18f, 0f, 10f)) };
      var byTier = new Dictionary<QualityTier, List<(Texture2D, string)>> { [QualityTier.Medium] = new List<(Texture2D, string)>(), [QualityTier.Low] = new List<(Texture2D, string)>() };
      var shots = new Dictionary<(QualityTier, int, int), (Texture2D, string)>();
      var times = new[] { 60, 600, 1200 };
      for (var t = 0; t < times.Length; t++) {
        if (t > 0) {
          yield return AdvanceTo(times[t]);
        }
        foreach (var tier in new[] { QualityTier.Medium, QualityTier.Low }) {
          GraphicsQuality.Apply(tier);
          yield return null;
          for (var p = 0; p < places.Length; p++) {
            Aim(camera, places[p].Item2, VisualProfile.Current.DefaultHeight);
            var seen = Visible(camera, 1920, 1080);
            LookdevNote("27.csv", $"K2,{tier},{places[p].Item1},{times[t]},{seen.Renderers},{seen.Triangles},{seen.Casters},{EstimateDrawCalls(seen)},{BattleScarsInfo()}");
            shots[(tier, p, t)] = (Shoot(camera, CellWidth * 2, CellHeight * 2), $"{places[p].Item1} {times[t] / 60}분 draw~{EstimateDrawCalls(seen)} tris {seen.Triangles / 1000}k");
          }
        }
      }
      foreach (var tier in byTier.Keys.ToList()) {
        for (var p = 0; p < places.Length; p++) {
          for (var t = 0; t < times.Length; t++) {
            byTier[tier].Add(shots[(tier, p, t)]);
          }
        }
      }
      SaveSheet("27_sian_battle_progress", "27 시안 K2 전투 누적(AI 대 AI, 보통): 같은 장소 1·10·20분", byTier[QualityTier.Medium], 3);
      SaveSheet("27_sian_battle_progress_low", "27 시안 K2 전투 누적(낮음): 같은 장소 1·10·20분", byTier[QualityTier.Low], 3);
      foreach (var list in byTier.Values) {
        foreach (var (texture, _) in list) {
          Object.Destroy(texture);
        }
      }
      GraphicsQuality.Apply(QualityTier.Medium);
    }

    /// <summary>
    /// 게임 화면의 바닥색 검증(K-0): 화면 격자 점을 땅(y=0)에 투영해 잔디·보도(광장)·도로로 나누고, 종류별 렌더 색의 밝기 중앙값을 K-0 목표와 비교한다.
    /// 유닛·건물·나무에 가린 점과 그림자 점은 중앙값으로 걸러진다.
    /// </summary>
    static string GameColorCheck(Camera camera, MapSpec map) {
      const int w = 960;
      const int h = 540;
      var target = RenderTexture.GetTemporary(new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 24) { sRGB = true });
      var aspect = camera.aspect;
      camera.targetTexture = target;
      camera.aspect = w / (float)h;
      camera.Render();
      RenderTexture.active = target;
      var image = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
      image.ReadPixels(new Rect(0, 0, w, h), 0, 0);
      image.Apply();
      RenderTexture.active = null;
      camera.targetTexture = null;
      var profile = VisualProfile.Current;
      var groups = new Dictionary<string, List<Color32>> { ["grass"] = new List<Color32>(), ["plaza"] = new List<Color32>(), ["road"] = new List<Color32>() };
      bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.01f;
      var ground = new Plane(Vector3.up, 0f);
      for (var gy = 1; gy < 36; gy++) {
        for (var gx = 1; gx < 64; gx++) {
          var u = gx / 64f;
          var v = gy / 36f;
          var ray = camera.ViewportPointToRay(new Vector3(u, v, 0f));
          if (ground.Raycast(ray, out var distance) == false) {
            continue;
          }
          var world = ray.GetPoint(distance);
          var fp = new Photon.Deterministic.FPVector3(Photon.Deterministic.FP.FromFloat_UNSAFE(world.x), default, Photon.Deterministic.FP.FromFloat_UNSAFE(world.z));
          var pixel = (Color32)image.GetPixel(Mathf.RoundToInt(u * w), Mathf.RoundToInt(v * h));
          if (map.Roads != null && map.Roads.Any(r => r.Distance(fp).AsFloat <= -0.6f)) {
            groups["road"].Add(pixel);
            continue;
          }
          var tint = TerrainKit.GroundTint(map, world.x, world.z, 0);
          if (Same(tint, profile.GroundGrass) || Same(tint, profile.GroundGrassLight) || Same(tint, profile.GroundGrassDark)) {
            groups["grass"].Add(pixel);
          } else if (Same(tint, profile.GroundPlaza) || Same(tint, profile.GroundPlazaDark)) {
            groups["plaza"].Add(pixel);
          }
        }
      }
      camera.aspect = aspect;
      RenderTexture.ReleaseTemporary(target);
      Object.Destroy(image);
      var goals = new Dictionary<string, Color32> { ["grass"] = new Color32(0x7B, 0xE5, 0xBA, 0xFF), ["plaza"] = new Color32(0xB6, 0xBE, 0xD8, 0xFF), ["road"] = new Color32(0x40, 0x4A, 0x50, 0xFF) };
      var parts = new List<string>();
      foreach (var pair in groups) {
        if (pair.Value.Count < 5) {
          parts.Add($"{pair.Key} -");
          continue;
        }
        var sorted = pair.Value.OrderBy(c => ((Color)c).grayscale).ToList();
        var median = sorted[sorted.Count / 2];
        parts.Add($"{pair.Key} #{ColorUtility.ToHtmlStringRGB(median)} ΔE {KenneyMatchTests.DeltaE(median, goals[pair.Key]):0.0} (n {pair.Value.Count})");
      }
      return string.Join(" / ", parts);
    }

    /// <summary>손상 지도 요약(평균 손상도·손상 칸 수). 손상 지도가 없으면 "-".</summary>
    static string BattleScarsInfo() => BattleScars.Active != null ? $"{BattleScars.Active.Average:0.000},{BattleScars.Active.Damaged}" : "-,-";
  }
}
