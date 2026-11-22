namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.IO;
  using UnityEditor;
  using UnityEngine;

  /// <summary>Synty 팩 프리팹 크기 측정(블록 설계용). 결과: Game-logs/prefab_sizes.csv (이름, 크기 x/y/z, 바닥 중심 기준점 오프셋, 삼각형, 렌더러 수).</summary>
  public static class PrefabMeasure {
    public static void Run() {
      var roots = new[] {
        "Assets/ExternalAssets/Synty/PolygonCity/Prefabs",
        "Assets/ExternalAssets/Synty/PolygonMilitary/Prefabs/Buildings",
        "Assets/ExternalAssets/Synty/PolygonMilitary/Prefabs/Props",
        "Assets/ExternalAssets/Synty/PolygonMilitary/Prefabs/Environment",
        "Assets/ExternalAssets/Synty/PolygonMilitary/Prefabs/Vehicles",
        "Assets/ExternalAssets/Synty/PolygonConstruction/Prefabs/Buildings",
        "Assets/ExternalAssets/Synty/PolygonConstruction/Prefabs/Props",
        "Assets/ExternalAssets/Synty/PolygonConstruction/Prefabs/Environments",
        "Assets/ExternalAssets/Synty/PolygonConstruction/Prefabs/Vehicles",
        "Assets/ExternalAssets/Synty/PolygonBattleRoyale/Prefabs/Buildings",
        "Assets/ExternalAssets/Synty/PolygonBattleRoyale/Prefabs/Environments",
        "Assets/ExternalAssets/Synty/PolygonBattleRoyale/Prefabs/Props",
        "Assets/ExternalAssets/Synty/PolygonBattleRoyale/Prefabs/Vehicles",
        "Assets/ExternalAssets/Synty/PolygonGeneric/Prefabs/Building",
        "Assets/ExternalAssets/Synty/PolygonGeneric/Prefabs/Environment",
        "Assets/ExternalAssets/Synty/PolygonGeneric/Prefabs/Props",
      };
      var lines = new List<string> { "path,sx,sy,sz,cx,miny,cz,tris,renderers,materials" };
      foreach (var guid in AssetDatabase.FindAssets("t:Prefab", roots)) {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        if (path.Contains("/Characters/") || path.Contains("/Weapons/") || path.Contains("/FX/")) continue;
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) continue;
        var item = (GameObject)PrefabUtility.InstantiatePrefab(go);
        item.transform.position = Vector3.zero;
        var rs = item.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) { Object.DestroyImmediate(item); continue; }
        var b = rs[0].bounds;
        long tris = 0;
        var mats = new HashSet<Material>();
        foreach (var r in rs) {
          b.Encapsulate(r.bounds);
          foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m);
          var f = r.GetComponent<MeshFilter>();
          if (f != null && f.sharedMesh != null) for (var i = 0; i < f.sharedMesh.subMeshCount; i++) tris += f.sharedMesh.GetIndexCount(i) / 3;
        }
        var shortPath = path.Replace("Assets/ExternalAssets/Synty/", "").Replace("/Prefabs/", ":").Replace(".prefab", "");
        lines.Add($"{shortPath},{b.size.x:0.0},{b.size.y:0.0},{b.size.z:0.0},{b.center.x:0.0},{b.min.y:0.0},{b.center.z:0.0},{tris},{rs.Length},{string.Join("|", System.Linq.Enumerable.Select(mats, m => m.name))}");
        Object.DestroyImmediate(item);
      }
      File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "prefab_sizes.csv")), lines);
      Debug.Log($"[FW-Measure] {lines.Count - 1} prefabs");
    }
  }
}
