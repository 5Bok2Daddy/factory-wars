namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Object = UnityEngine.Object;

  /// <summary>항목 25-1(19D 성능): Kenney 개체를 종류별 개수·삼각형으로 나눠 Game-logs/lookdev/budget.csv에 남긴다. 무엇을 줄일지 고르는 근거다.</summary>
  public unsafe partial class LocalMatchTests {
    static int PrefabTriangles(GameObject prefab) {
      var total = 0;
      foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true)) {
        var mesh = filter.sharedMesh;
        for (var s = 0; mesh != null && s < mesh.subMeshCount; s++) {
          total += (int)(mesh.GetIndexCount(s) / 3);
        }
      }
      return total;
    }

    [UnityTest, Explicit, Category("Lookdev"), Timeout(3000000)]
    public IEnumerator Lookdev_DressingBudget() {
      yield return FixedScene(FinalScenes[1], human: true);
      foreach (var tier in new[] { QualityTier.Medium, QualityTier.Low }) {
        GraphicsQuality.Apply(tier);
        Battle().Rebuild();
        yield return null;
        yield return null;
        var rows = new Dictionary<string, (int Count, long Tris)>();
        var renderer = DressingRenderer.Active;
        foreach (var group in renderer.GroupNames.ToList()) {
          var key = group.StartsWith("forest") ? "forest*" : group.StartsWith("iron") ? "iron*" : group;
          foreach (var item in renderer.Group(group)) {
            var skip = item.Prefab == null || (tier == QualityTier.Low && item.Keep > item.LowKeep) || (tier == QualityTier.Medium && item.Keep > item.MediumKeep);
            if (skip) {
              continue;
            }
            var name = $"{key}/{item.Kind}";
            var row = rows.TryGetValue(name, out var r) ? r : (0, 0L);
            rows[name] = (row.Item1 + 1, row.Item2 + PrefabTriangles(item.Prefab));
          }
        }
        var spawned = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name == "Kenney" && t.gameObject.activeInHierarchy).ToList();
        var spawnedTris = spawned.Sum(t => t.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null).Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(i => (int)(f.sharedMesh.GetIndexCount(i) / 3))));
        rows["spawned/objects"] = (spawned.Count, spawnedTris);
        foreach (var pair in rows.OrderByDescending(p => p.Value.Tris)) {
          LookdevNote("budget.csv", $"{tier},{pair.Key},{pair.Value.Count},{pair.Value.Tris}");
        }
        LookdevNote("budget.csv", $"{tier},TOTAL,{rows.Values.Sum(v => v.Count)},{rows.Values.Sum(v => v.Tris)}");
      }
    }
  }
}
