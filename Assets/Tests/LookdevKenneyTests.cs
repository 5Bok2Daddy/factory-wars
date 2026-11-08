namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;
  using Object = UnityEngine.Object;

  /// <summary>
  /// 항목 19B 비교: 같은 고정 장면·시점에서 before(기본 프로필, 절차 메쉬) / after(같은 프로필 + Kenney 모델).
  /// 시점마다 19b_compare_<시점>.png. 공용 셰이더 채도·밝기 3안은 19b_shader.png. 수치는 Game-logs/lookdev/19b.csv.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    IEnumerator ShootProfile(VisualProfile profile, string label, Dictionary<string, List<(Texture2D, string)>> byView) {
      VisualProfile.Current = profile;
      Battle().Rebuild();
      yield return null;
      yield return null;
      GraphicsQuality.Apply(GraphicsQuality.Current);
      yield return null;
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var camera = Camera.main;
      foreach (var view in ViewPoints(map)) {
        Aim(camera, view.Focus, view.Height);
        var seen = Visible(camera, 1920, 1080);
        LookdevNote("19b.csv", $"{label},{GraphicsQuality.Current},{view.Name},{seen.Renderers},{seen.Triangles},{seen.Casters},{EstimateDrawCalls(seen)},{DressingRenderer.Active?.InstanceCount ?? 0}");
        Cells(byView, view.Name).Add((Shoot(camera, CellWidth * 2, CellHeight * 2), $"{label} [{view.Name}] draw~{EstimateDrawCalls(seen)} tris {seen.Triangles / 1000}k"));
      }
      var engineer = Engineers(0).FirstOrDefault();
      if (engineer != default) {
        Controller().Input.Selection.SelectUnit(engineer);
        Controller().Hud.Refresh(Controller().Frame);
      }
      yield return null;
      Aim(camera, map.StartPositions[0].ToUnityVector3() + new Vector3(8, 0, 6), profile.DefaultHeight);
      Cells(byView, "E").Add((ShootHud(camera, CellWidth * 2, CellHeight * 2), $"{label} [E] HUD"));
    }

    static List<(Texture2D, string)> Cells(Dictionary<string, List<(Texture2D, string)>> byView, string name) {
      if (byView.TryGetValue(name, out var list) == false) {
        list = new List<(Texture2D, string)>();
        byView[name] = list;
      }
      return list;
    }

    [UnityTest, Explicit, Category("Lookdev"), Timeout(6000000)]
    public IEnumerator Lookdev_19B() {
      yield return FixedScene(FinalScenes[1], human: true);
      var after = VisualProfile.Current;
      Assert.IsNotNull(after.Dressing, "기본 프로필이 Kenney 맵을 가리킴");
      var before = Object.Instantiate(after);
      before.Dressing = null;
      var byView = new Dictionary<string, List<(Texture2D, string)>>();
      foreach (var tier in new[] { QualityTier.Medium, QualityTier.Low }) {
        GraphicsQuality.Apply(tier);
        yield return ShootProfile(before, $"before {tier}", byView);
        yield return ShootProfile(after, $"after {tier}", byView);
      }
      foreach (var pair in byView) {
        SaveSheet($"19b_compare_{pair.Key}", $"19B 시점 {pair.Key}: before(절차 메쉬) / after(Kenney) — 위 보통, 아래 낮음", pair.Value, 2);
        foreach (var (texture, _) in pair.Value) {
          Object.Destroy(texture);
        }
      }

      // 공용 셰이더 3안: 채도 빼기·밝기만 바꿔 같은 시점을 찍는다(머티리얼 값은 끝나면 되돌린다).
      GraphicsQuality.Apply(QualityTier.Medium);
      VisualProfile.Current = after;
      Battle().Rebuild();
      yield return null;
      yield return null;
      var materials = after.Dressing.Entries.SelectMany(e => e.Variants).Where(v => v.Prefab != null)
        .SelectMany(v => v.Prefab.GetComponentsInChildren<MeshRenderer>(true)).SelectMany(r => r.sharedMaterials).Where(m => m != null && m.HasProperty("_Desaturate")).Distinct().ToList();
      var saved = materials.Select(m => (m, m.GetFloat("_Desaturate"), m.GetFloat("_Brightness"))).ToList();
      var cells = new List<(Texture2D, string)>();
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var camera = Camera.main;
      foreach (var (name, desaturate, brightness) in new[] { ("원래 값", 0f, 0f), ("채도 -25% 밝기 0.95", 0.25f, 0.95f), ("채도 -65% 밝기 0.85", 0.65f, 0.85f) }) {
        // 색을 직접 바꾼 nature-kit(채도 빼기 0)은 그대로 두고, 아틀라스 키트만 안마다 바꾼다. 0은 "원래 값"이다.
        foreach (var (material, d, b) in saved) {
          material.SetFloat("_Desaturate", d > 0f && desaturate > 0f ? desaturate : d);
          material.SetFloat("_Brightness", d > 0f && brightness > 0f ? brightness : b);
        }
        yield return null;
        foreach (var view in ViewPoints(map).Where(v => v.Name == "A" || v.Name == "C")) {
          Aim(camera, view.Focus, view.Height);
          cells.Add((Shoot(camera, CellWidth * 2, CellHeight * 2), $"{name} [{view.Name}]"));
        }
      }
      foreach (var (material, d, b) in saved) {
        material.SetFloat("_Desaturate", d);
        material.SetFloat("_Brightness", b);
      }
      SaveSheet("19b_shader", "19B 공용 셰이더 3안 (색 바꾼 nature-kit 제외, 키트 채도·밝기)", cells, 2);
      foreach (var (texture, _) in cells) {
        Object.Destroy(texture);
      }
    }
  }
}
