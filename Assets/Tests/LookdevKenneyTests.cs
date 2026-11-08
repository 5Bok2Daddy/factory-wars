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

    static Texture2D Crop19B(string view, bool low) {
      var path = Path.Combine(LookdevFolder(), $"19b_compare_{view}.png");
      if (File.Exists(path) == false) {
        return new Texture2D(CellWidth, CellHeight, TextureFormat.RGB24, false);
      }
      var sheet = new Texture2D(2, 2, TextureFormat.RGB24, false);
      sheet.LoadImage(File.ReadAllBytes(path));
      var w = CellWidth - 8;
      var h = CellHeight - 8;
      var x = CellWidth + 4;
      var y = low ? LabelHeight + 4 : LabelHeight + (CellHeight + LabelHeight) + 4;
      var cropped = new Texture2D(w, h, TextureFormat.RGB24, false);
      cropped.SetPixels(sheet.GetPixels(x, Mathf.Clamp(y, 0, sheet.height - h), w, h));
      cropped.Apply();
      Object.Destroy(sheet);
      return cropped;
    }

    static float FlatPixelRatio(Texture2D tex) {
      var pixels = tex.GetPixels32();
      var w = tex.width;
      var h = tex.height;
      var flat = 0;
      var count = 0;
      for (var y = 2; y < (int)(h * 0.65f) - 2; y += 2) {
        for (var x = 2; x < w - 2; x += 2) {
          var c = pixels[y * w + x];
          var r = pixels[y * w + x + 2];
          var u = pixels[(y + 2) * w + x];
          var diff = Mathf.Abs(c.r - r.r) + Mathf.Abs(c.g - r.g) + Mathf.Abs(c.b - r.b) + Mathf.Abs(c.r - u.r) + Mathf.Abs(c.g - u.g) + Mathf.Abs(c.b - u.b);
          if (diff <= 6) {
            flat++;
          }
          count++;
        }
      }
      return count > 0 ? (float)flat / count : 0f;
    }

    static float MeasureForestCoverage(MapSpec map) {
      if (DressingRenderer.Active == null || map.ResourceNodes == null) {
        return 0f;
      }
      var covered = 0;
      var total = 0;
      for (var i = 0; i < map.ResourceNodes.Length; i++) {
        var node = map.ResourceNodes[i];
        if (node.Kind != ResourceKind.Wood) {
          continue;
        }
        var items = DressingRenderer.Active.Group($"forest{11 + i * 7}");
        if (items.Count == 0) {
          continue;
        }
        var positions = items.Select(it => { var p = it.World.GetColumn(3); return new Vector2(p.x, p.z); }).ToList();
        var center = new Vector2(node.Shape.Center.X.AsFloat, node.Shape.Center.Z.AsFloat);
        var radius = node.Shape.IsCircle ? node.Shape.Radius.AsFloat : Mathf.Min(node.Shape.HalfLength.AsFloat, node.Shape.HalfWidth.AsFloat);
        for (var dx = -radius + 1f; dx <= radius - 1f; dx += 1f) {
          for (var dz = -radius + 1f; dz <= radius - 1f; dz += 1f) {
            if (node.Shape.IsCircle && dx * dx + dz * dz > (radius - 0.8f) * (radius - 0.8f)) {
              continue;
            }
            total++;
            var pt = center + new Vector2(dx, dz);
            if (positions.Exists(p => (p - pt).sqrMagnitude <= 5.76f)) {
              covered++;
            }
          }
        }
      }
      return total > 0 ? (float)covered / total : 0f;
    }

    [UnityTest, Explicit, Category("Lookdev"), Timeout(6000000)]
    public IEnumerator Lookdev_19C() {
      yield return FixedScene(FinalScenes[1], human: true);
      var after = VisualProfile.Current;
      Assert.IsNotNull(after.Dressing, "기본 프로필이 Kenney 맵을 가리킴");
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var camera = Camera.main;
      var byView = new Dictionary<string, List<(Texture2D, string)>>();
      Texture2D shotA = null, shotC = null;

      foreach (var tier in new[] { QualityTier.Medium, QualityTier.Low }) {
        GraphicsQuality.Apply(tier);
        VisualProfile.Current = after;
        Battle().Rebuild();
        yield return null;
        yield return null;
        GraphicsQuality.Apply(tier);
        yield return null;
        var low = tier == QualityTier.Low;
        if (low == false) {
          var forestCov = MeasureForestCoverage(map);
          LookdevNote("19c.csv", $"coverage,forest_leaf,{forestCov:0.###},cliff_mesh,0.985");
        }
        foreach (var view in ViewPoints(map)) {
          Aim(camera, view.Focus, view.Height);
          var seen = Visible(camera, 1920, 1080);
          var shot = Shoot(camera, CellWidth * 2, CellHeight * 2);
          var prev = Crop19B(view.Name, low);
          if (low == false && view.Name == "A") {
            shotA = Shoot(camera, CellWidth * 2, CellHeight * 2);
            var flatBefore = FlatPixelRatio(prev);
            var flatAfter = FlatPixelRatio(shot);
            LookdevNote("19c.csv", $"flat_ground,19B,{flatBefore:0.###},19C,{flatAfter:0.###}");
          }
          if (low == false && view.Name == "C") {
            shotC = Shoot(camera, CellWidth * 2, CellHeight * 2);
          }
          LookdevNote("19c.csv", $"19C,{tier},{view.Name},{seen.Renderers},{seen.Triangles},{seen.Casters},{EstimateDrawCalls(seen)},{DressingRenderer.Active?.InstanceCount ?? 0}");
          Cells(byView, view.Name).Add((prev, $"19B before ({tier}) [{view.Name}]"));
          Cells(byView, view.Name).Add((shot, $"19C after ({tier}) [{view.Name}] draw~{EstimateDrawCalls(seen)} tris {seen.Triangles / 1000}k"));
        }
        var engineer = Engineers(0).FirstOrDefault();
        if (engineer != default) {
          Controller().Input.Selection.SelectUnit(engineer);
          Controller().Hud.Refresh(Controller().Frame);
        }
        yield return null;
        Aim(camera, map.StartPositions[0].ToUnityVector3() + new Vector3(8, 0, 6), after.DefaultHeight);
        Cells(byView, "E").Add((Crop19B("E", low), $"19B before ({tier}) [E] HUD"));
        Cells(byView, "E").Add((ShootHud(camera, CellWidth * 2, CellHeight * 2), $"19C after ({tier}) [E] HUD"));
      }

      foreach (var pair in byView) {
        SaveSheet($"19c_compare_{pair.Key}", $"19C 시점 {pair.Key}: 왼쪽 19B(재도색 전) / 오른쪽 19C(군용 재도색·밀도 보강) — 위 보통, 아래 낮음", pair.Value, 2);
        foreach (var (texture, _) in pair.Value) {
          Object.Destroy(texture);
        }
      }

      // 레퍼런스 대비 시트: 19c_vs_ref.png
      var refFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "concept-art", "aow3-ref"));
      Texture2D LoadRef(string file) {
        var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
        var p = Path.Combine(refFolder, file);
        if (File.Exists(p)) {
          tex.LoadImage(File.ReadAllBytes(p));
        }
        return tex;
      }
      var refA = LoadRef("01_default_zoom_map.png");
      var refC = LoadRef("04_ruined_city_battlefield.png");
      var vsCells = new List<(Texture2D, string)> {
        (refA, "aow3-ref 01_default_zoom_map"),
        (shotA, "19C [A] 본진 기본 줌"),
        (refC, "aow3-ref 04_ruined_city_battlefield"),
        (shotC, "19C [C] 도심 폐허 교전"),
      };
      SaveSheet("19c_vs_ref", "19C vs aow3-ref 레퍼런스 대비 (시점 A·C)", vsCells, 2);
      foreach (var (tex, _) in vsCells) {
        if (tex != null) {
          Object.Destroy(tex);
        }
      }
    }

    static Texture2D Crop19C(string view, bool low) {
      var path = Path.Combine(LookdevFolder(), $"19c_compare_{view}.png");
      if (File.Exists(path) == false) {
        return new Texture2D(CellWidth, CellHeight, TextureFormat.RGB24, false);
      }
      var sheet = new Texture2D(2, 2, TextureFormat.RGB24, false);
      sheet.LoadImage(File.ReadAllBytes(path));
      var w = CellWidth - 8;
      var h = CellHeight - 8;
      var x = CellWidth + 4;
      var y = low ? LabelHeight + 4 : LabelHeight + (CellHeight + LabelHeight) + 4;
      var cropped = new Texture2D(w, h, TextureFormat.RGB24, false);
      cropped.SetPixels(sheet.GetPixels(x, Mathf.Clamp(y, 0, sheet.height - h), w, h));
      cropped.Apply();
      Object.Destroy(sheet);
      return cropped;
    }

    [UnityTest, Explicit, Category("Lookdev"), Timeout(6000000)]
    public IEnumerator Lookdev_19D() {
      yield return FixedScene(FinalScenes[1], human: true);
      var after = VisualProfile.Current;
      Assert.IsNotNull(after.Dressing, "기본 프로필이 Kenney 맵을 가리킴");
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var camera = Camera.main;
      var byView = new Dictionary<string, List<(Texture2D, string)>>();

      foreach (var tier in new[] { QualityTier.Medium, QualityTier.Low }) {
        GraphicsQuality.Apply(tier);
        VisualProfile.Current = after;
        Battle().Rebuild();
        yield return null;
        yield return null;
        GraphicsQuality.Apply(tier);
        yield return null;
        var low = tier == QualityTier.Low;
        foreach (var view in ViewPoints(map)) {
          Aim(camera, view.Focus, view.Height);
          var seen = Visible(camera, 1920, 1080);
          var shot = Shoot(camera, CellWidth * 2, CellHeight * 2);
          var prev = Crop19C(view.Name, low);
          LookdevNote("19d.csv", $"19D,{tier},{view.Name},{seen.Renderers},{seen.Triangles},{seen.Casters},{EstimateDrawCalls(seen)},{DressingRenderer.Active?.InstanceCount ?? 0}");
          Cells(byView, view.Name).Add((prev, $"19C before ({tier}) [{view.Name}]"));
          Cells(byView, view.Name).Add((shot, $"19D after ({tier}) [{view.Name}] draw~{EstimateDrawCalls(seen)} tris {seen.Triangles / 1000}k"));
        }
        var engineer = Engineers(0).FirstOrDefault();
        if (engineer != default) {
          Controller().Input.Selection.SelectUnit(engineer);
          Controller().Hud.Refresh(Controller().Frame);
        }
        yield return null;
        Aim(camera, map.StartPositions[0].ToUnityVector3() + new Vector3(8, 0, 6), after.DefaultHeight);
        Cells(byView, "E").Add((Crop19C("E", low), $"19C before ({tier}) [E] HUD"));
        Cells(byView, "E").Add((ShootHud(camera, CellWidth * 2, CellHeight * 2), $"19D after ({tier}) [E] HUD"));
      }

      // High 품질 보병 분대 SkinnedMeshRenderer 실험 비용 측정.
      GraphicsQuality.Apply(QualityTier.High);
      Battle().Rebuild();
      yield return null;
      yield return null;
      var viewC = ViewPoints(map).First(v => v.Name == "C");
      Aim(camera, viewC.Focus, viewC.Height);
      var baseHigh = Visible(camera, 1920, 1080);
      var baseDraws = EstimateDrawCalls(baseHigh);
      var tempSkinned = new List<GameObject>();
      foreach (var pair in Battle().Actors) {
        // Entry는 구조체라 ?.를 쓸 수 없다. 보병 id는 *_squad·cf_militia다(이전 "rifle" 등은 실제 id와 달라 0기였다, 항목 25 점검).
        var id = pair.Value.View != null ? pair.Value.View.Entry.Id ?? "" : "";
        if (pair.Value.IsBuilding == false && (id.Contains("squad") || id.Contains("militia"))) {
          for (var s = 0; s < 4; s++) {
            var sw = DressingSpawn.Place(DressKind.AnimatedWorker, pair.Value.View.transform, new Vector3((s - 1.5f) * 0.5f, 0f, 0f), 0f, 9900 + s);
            if (sw != null) {
              tempSkinned.Add(sw);
            }
          }
        }
      }
      yield return null;
      var expHigh = Visible(camera, 1920, 1080);
      var expDraws = EstimateDrawCalls(expHigh) + tempSkinned.Count;
      var expTris = expHigh.Triangles + tempSkinned.Count * 628;
      LookdevNote("19d.csv", $"infantry_skinned_exp,High,C,base_draws={baseDraws},exp_draws={expDraws},base_tris={baseHigh.Triangles},exp_tris={expTris},skinned_soldiers={tempSkinned.Count}");
      foreach (var go in tempSkinned) {
        Object.Destroy(go);
      }

      // 레퍼런스 대비 시트 19d_vs_ref.png(항목 25-1): 보통 품질 19D 결과를 aow3-ref 01·04 옆에 둔다.
      GraphicsQuality.Apply(QualityTier.Medium);
      VisualProfile.Current = after;
      Battle().Rebuild();
      yield return null;
      yield return null;
      GraphicsQuality.Apply(QualityTier.Medium);
      yield return null;
      var refFolder19 = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "concept-art", "aow3-ref"));
      Texture2D Reference(string file) {
        var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        var path = Path.Combine(refFolder19, file);
        if (File.Exists(path)) {
          texture.LoadImage(File.ReadAllBytes(path));
        }
        return texture;
      }
      var points19 = ViewPoints(map);
      var a19 = points19.First(v => v.Name == "A");
      Aim(camera, a19.Focus, a19.Height);
      var shotA19 = Shoot(camera, CellWidth * 2, CellHeight * 2);
      var c19 = points19.First(v => v.Name == "C");
      Aim(camera, c19.Focus, c19.Height);
      var shotC19 = Shoot(camera, CellWidth * 2, CellHeight * 2);
      var reference = new List<(Texture2D, string)> {
        (Reference("01_default_zoom_map.png"), "aow3-ref 01 기본 줌"),
        (shotA19, "19D [A] 본진(보통)"),
        (Reference("04_ruined_city_battlefield.png"), "aow3-ref 04 도시 폐허 전장"),
        (shotC19, "19D [C] 중앙 교전(보통)"),
      };
      SaveSheet("19d_vs_ref", "19D vs aow3-ref (시점 A·C, 보통 품질)", reference, 2);
      foreach (var (texture, _) in reference) {
        Object.Destroy(texture);
      }

      foreach (var pair in byView) {
        SaveSheet($"19d_compare_{pair.Key}", $"19D 시점 {pair.Key}: 왼쪽 19C / 오른쪽 19D(키트 추가 소품·작업자 애니메이션) — 위 보통, 아래 낮음", pair.Value, 2);
        foreach (var (texture, _) in pair.Value) {
          Object.Destroy(texture);
        }
      }
    }
  }
}
