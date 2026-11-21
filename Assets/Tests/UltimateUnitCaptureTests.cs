namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Photon.Deterministic;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 궁극·최상위 유닛 표시 모델 점검(2026-11-21): 초중전차, 공격 헬기, 이동식 미사일, 궁극 전력(공중 항모·미사일 사일로).
  /// 격리 스튜디오에서 전·후·측면·쿼터뷰 4장, Map4P 실제 전장 1장을 찍고 삼각형 수·재질 상태를 CSV로 남긴다.
  /// 화면 전용 점검이며 시뮬레이션 상태를 바꾸지 않는다(전장 장면의 유닛 배치는 시험 시작 데이터).
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static readonly (string Id, int Color, string Category)[] UltimateShowcase = {
      ("ff_bulwark_tank", 0, "super_heavy_tank"),
      ("cf_fortress_tank", 1, "super_heavy_tank"),
      ("ff_hawk_gunship", 0, "attack_helicopter"),
      ("cf_gale_gunship", 1, "attack_helicopter"),
      ("ff_mobile_sam", 0, "mobile_missile"),
      ("cf_rocket_artillery", 1, "mobile_missile"),
      ("ff_stormnest_carrier", 0, "ultimate"),
      ("cf_missile_silo", 1, "ultimate"),
    };

    static string UltimateUnitFolder() {
      var destination = System.Environment.GetEnvironmentVariable("FW_CAPTURE_DIR");
      var dir = string.IsNullOrEmpty(destination)
        ? Path.Combine(Application.dataPath, "..", "..", "DevelopmentReports", "Screenshots", "UltimateUnits")
        : Path.Combine(destination, "UltimateUnits");
      dir = Path.GetFullPath(dir);
      Directory.CreateDirectory(dir);
      return dir;
    }

    static (int Triangles, int Renderers, int Materials, int Broken) MeshStats(GameObject root) {
      var triangles = 0;
      var renderers = 0;
      var materials = new HashSet<Material>();
      var broken = 0;
      foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) {
        if (renderer.name == "Ring" || renderer.gameObject.name.Contains("Bar")) continue;
        Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh == null) continue;
        renderers++;
        for (var s = 0; s < mesh.subMeshCount; s++) triangles += (int)(mesh.GetIndexCount(s) / 3);
        foreach (var material in renderer.sharedMaterials) {
          if (material == null || material.shader == null || material.shader.isSupported == false || material.shader.name.Contains("InternalError")) {
            broken++;
            continue;
          }
          materials.Add(material);
        }
      }
      return (triangles, renderers, materials.Count, broken);
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator CaptureUltimateUnitsFourViewsAndBattlefield() {
      yield return StartMatch(Map4PScene, 2, truce: 600, stock: Stock(30000, 30000, 30000, 1000),
        units: new[] {
          (0, "ff_bulwark_tank", -98, -112), (0, "ff_hawk_gunship", -104, -100), (0, "ff_mobile_sam", -92, -100),
          (1, "cf_fortress_tank", -110, -96), (1, "cf_gale_gunship", -96, -90), (1, "cf_rocket_artillery", -112, -108),
        },
        buildings: new[] { (1, "cf_missile_silo", -126, -92) });
      yield return WaitFrames(4);
      var catalog = Resources.Load<ViewCatalog>(ViewCatalog.ResourcePath);
      Assert.IsNotNull(catalog, "ViewCatalog");
      var folder = UltimateUnitFolder();

      var studio = new GameObject("UltimateStudio");
      studio.transform.position = new Vector3(0f, -80f, 0f);
      var lightGo = new GameObject("StudioLight");
      lightGo.transform.SetParent(studio.transform, false);
      lightGo.transform.rotation = Quaternion.Euler(46f, -36f, 0f);
      var sun = lightGo.AddComponent<Light>();
      sun.type = LightType.Directional;
      sun.intensity = 1.15f;
      var camGo = new GameObject("StudioCamera");
      camGo.transform.SetParent(studio.transform, false);
      var cam = camGo.AddComponent<Camera>();
      cam.clearFlags = CameraClearFlags.SolidColor;
      cam.backgroundColor = new Color(0.11f, 0.14f, 0.18f, 1f);
      cam.fieldOfView = 32f;
      cam.nearClipPlane = 0.1f;
      cam.farClipPlane = 400f;

      var rows = new List<string> { "id,category,triangles,renderers,materials,broken_materials,prefab" };
      var problems = new List<string>();
      foreach (var item in UltimateShowcase) {
        var view = ArtView.Create(catalog, item.Id, studio.transform, item.Color);
        if (view == null) {
          problems.Add($"{item.Id}: 표시 모델 없음");
          continue;
        }
        view.transform.localPosition = Vector3.zero;
        view.SetSelected(false);
        if (view.Bar != null) view.Bar.SetActive(false);
        view.Turn.localPosition = Vector3.zero;
        view.SetYaw(0f);
        ArtLook.Apply(view.Model, item.Color, true);
        yield return null;
        yield return null;

        var bounds = new Bounds(view.transform.position, Vector3.one);
        var any = false;
        foreach (var r in view.GetComponentsInChildren<Renderer>(false)) {
          if (r.enabled == false || r.name == "Ring" || r.gameObject.name.Contains("Bar")) continue;
          if (any) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; any = true; }
        }
        var dist = Mathf.Max(1.2f, bounds.extents.magnitude) * 2.7f;
        var forward = view.Turn.forward;
        var right = view.Turn.right;
        var shots = new (string Name, Vector3 Offset)[] {
          ("front", forward * dist + Vector3.up * dist * 0.22f),
          ("rear", -forward * dist + Vector3.up * dist * 0.22f),
          ("side", right * dist + Vector3.up * dist * 0.18f),
          ("iso", (forward * 0.62f + right * 0.62f) * dist + Vector3.up * dist * 0.55f),
        };
        foreach (var (name, offset) in shots) {
          camGo.transform.position = bounds.center + offset;
          camGo.transform.LookAt(bounds.center);
          CaptureCameraPng(cam, Path.Combine(folder, $"{item.Id}_{name}.png"), 800, 600);
        }
        var stats = MeshStats(view.Model != null ? view.Model.gameObject : view.gameObject);
        var slot = VisualCatalog.Current?.Get(VisualCatalog.UnitId(item.Id)) ?? VisualCatalog.Current?.Get(VisualCatalog.BuildingId(item.Id));
        var prefab = slot != null && slot.IsFilled ? slot.Prefabs.FirstOrDefault()?.name ?? "" : "";
        rows.Add($"{item.Id},{item.Category},{stats.Triangles},{stats.Renderers},{stats.Materials},{stats.Broken},{prefab}");
        Debug.Log($"[ULT-UNIT] {item.Id} tris={stats.Triangles} renderers={stats.Renderers} materials={stats.Materials} broken={stats.Broken} prefab={prefab}");
        if (stats.Triangles == 0) problems.Add($"{item.Id}: 삼각형 0");
        if (stats.Broken > 0) problems.Add($"{item.Id}: 깨진 재질 {stats.Broken}");
        Object.Destroy(view.gameObject);
      }
      Object.Destroy(studio);
      File.WriteAllLines(Path.Combine(folder, "ultimate_units.csv"), rows);

      // 실제 전장: 휴전 중 자리 0 본진 앞마당에 모은 유닛과 사일로, 그리고 공중 항모.
      SpawnCarrierFor(0, new FPVector3(-100, 0, -122));
      yield return WaitFrames(Fps * 2);
      Controller().Camera.MoveTo(new Vector2(-106f, -104f));
      yield return WaitFrames(Fps);
      var main = Camera.main;
      CaptureCameraPng(main, Path.Combine(folder, "battlefield_ultimate_units.png"), 1600, 900);
      Assert.IsEmpty(problems, string.Join("\n", problems));
    }
  }
}
