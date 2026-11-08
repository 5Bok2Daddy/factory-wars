namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;
  using Object = UnityEngine.Object;

  /// <summary>항목 19C: 빗금은 건설 위치를 고를 때만, Kenney 도시 건물은 부지 밖으로 1m 넘게 나가지 않는다.</summary>
  public unsafe partial class LocalMatchTests {
    [UnityTest]
    public IEnumerator Kenney_NoHazardStripesOutsideBuildMode() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      yield return WaitForBattle();
      yield return null;
      var stripes = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
        .Where(f => f.gameObject.activeInHierarchy && f.sharedMesh != null && (f.sharedMesh.name.Contains("nobuild") || f.sharedMesh.name.Contains("fall_warning")))
        .Select(f => f.name).ToList();
      Assert.IsEmpty(stripes, "건설 모드가 아닐 때 보이는 빗금: " + string.Join(", ", stripes));
    }

    [UnityTest]
    public IEnumerator Kenney_CityModelsStayInsidePlots() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      yield return WaitForBattle();
      yield return null;
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      Assume.That(VisualProfile.Current.Dressing, Is.Not.Null, "Kenney 맵이 있는 프로필");
      var outside = new System.Collections.Generic.List<string>();
      var checkedCount = 0;
      for (var i = 0; map.CityBuildings != null && i < map.CityBuildings.Length; i++) {
        var root = GameObject.Find($"City{i}");
        var model = root != null ? root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Kenney") : null;
        if (model == null) {
          continue;
        }
        checkedCount++;
        var shape = map.CityBuildings[i].Shape;
        var turn = Quaternion.Euler(0, shape.Yaw.AsFloat, 0);
        var center = shape.Center.ToUnityVector3();
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>()) {
          var lb = renderer.localBounds;
          for (var c = 0; c < 4; c++) {
            var localCorner = new Vector3((c & 1) == 0 ? lb.min.x : lb.max.x, lb.center.y, (c & 2) == 0 ? lb.min.z : lb.max.z);
            var worldCorner = renderer.transform.TransformPoint(localCorner);
            var local = Quaternion.Inverse(turn) * (worldCorner - center);
            if (Mathf.Abs(local.x) > shape.HalfLength.AsFloat + 1f || Mathf.Abs(local.z) > shape.HalfWidth.AsFloat + 1f) {
              outside.Add($"City{i}/{renderer.name}");
              break;
            }
          }
        }
      }
      Assert.Greater(checkedCount, 0, "Kenney 도시 건물이 있음");
      Assert.IsEmpty(outside, "부지 밖 1m 넘게 나간 모델: " + string.Join(", ", outside.Distinct().Take(10)));
    }

    [UnityTest]
    public IEnumerator Kenney_CliffModelsStayOutOfWalkable() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      yield return WaitForBattle();
      yield return null;
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      Assume.That(DressingRenderer.Active, Is.Not.Null, "DressingRenderer 활성");
      var items = DressingRenderer.Active.Group("world")
        .Where(i => i.Kind == DressKind.Cliff || i.Kind == DressKind.RockLarge || i.Kind == DressKind.RockSmall).ToList();
      Assert.Greater(items.Count, 0, "지형 절벽·바위 모델이 있음");
      var outside = new System.Collections.Generic.List<string>();
      foreach (var item in items) {
        var pos = item.World.GetColumn(3);
        var fp = new Photon.Deterministic.FPVector3(Photon.Deterministic.FP.FromFloat_UNSAFE(pos.x), default, Photon.Deterministic.FP.FromFloat_UNSAFE(pos.z));
        var best = float.MaxValue;
        foreach (var feature in map.Terrain ?? System.Array.Empty<TerrainFeature>()) {
          best = Mathf.Min(best, feature.Shape.Distance(fp).AsFloat);
        }
        if (best > 1.5f) {
          outside.Add($"{item.Kind}@({pos.x:0.#},{pos.z:0.#}):+{best:0.##}m");
        }
      }
      Assert.IsEmpty(outside, "지형 바깥 1.5m 넘게 나간 절벽·바위: " + string.Join(", ", outside.Take(10)));
    }

    [UnityTest]
    public IEnumerator Kenney_ToppleFallZoneStillValid() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      yield return WaitForBattle();
      yield return null;
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var topplers = 0;
      for (var i = 0; map.CityBuildings != null && i < map.CityBuildings.Length; i++) {
        var placement = map.CityBuildings[i];
        if (placement.Topples == false) {
          continue;
        }
        topplers++;
        Assert.GreaterOrEqual(placement.FallZone.HalfLength.AsFloat, 6f, $"City{i} FallZone 길이");
        Assert.GreaterOrEqual(placement.FallZone.HalfWidth.AsFloat, 2.5f, $"City{i} FallZone 폭");
        var root = GameObject.Find($"City{i}");
        Assert.IsNotNull(root, $"City{i} 루트");
        var model = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Kenney");
        Assert.IsNotNull(model, $"City{i} Kenney 타워");
      }
      Assert.Greater(topplers, 0, "쓰러지는 건물이 있음");
    }
  }
}
