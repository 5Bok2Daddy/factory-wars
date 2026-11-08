namespace FactoryWars.View {
  using System.Collections.Generic;
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>
  /// 뒷면을 바깥으로 늘린 외곽선(inverted hull). 원래 메쉬에 두 번째 서브메쉬로 붙이고, 같은 팔레트 머티리얼의 외곽선 칸을 쓴다.
  /// 셰이더가 같아 SRP Batcher가 끊기지 않는다. 품질 낮음에서는 머티리얼을 하나만 줘서 두 번째 서브메쉬를 그리지 않는다.
  /// 메쉬마다 한 번 만들어 공유한다.
  /// </summary>
  public static class OutlineKit {
    static readonly Dictionary<(Mesh, int), Mesh> Cache = new Dictionary<(Mesh, int), Mesh>();

    public static bool Enabled => VisualProfile.Current.Outline == OutlineMode.Hull && GraphicsQuality.Current != QualityTier.Low;

    /// <summary>width는 메쉬 좌표의 두께다.</summary>
    public static Mesh WithHull(Mesh source, float width) {
      var key = (source, Mathf.RoundToInt(width * 1000f));
      if (Cache.TryGetValue(key, out var cached)) {
        return cached;
      }
      var vertices = source.vertices;
      var normals = source.normals;
      var uvs = source.uv;
      var triangles = source.GetTriangles(0);
      // 같은 위치의 노멀을 평균해 늘려야 평면 셰이딩 메쉬의 모서리가 갈라지지 않는다.
      var smooth = new Dictionary<Vector3Int, Vector3>();
      Vector3Int Key(Vector3 v) => new Vector3Int(Mathf.RoundToInt(v.x * 1000f), Mathf.RoundToInt(v.y * 1000f), Mathf.RoundToInt(v.z * 1000f));
      for (var i = 0; i < vertices.Length; i++) {
        var k = Key(vertices[i]);
        smooth[k] = (smooth.TryGetValue(k, out var sum) ? sum : Vector3.zero) + normals[i];
      }
      var count = vertices.Length;
      var allVertices = new Vector3[count * 2];
      var allNormals = new Vector3[count * 2];
      var allUv = new Vector2[count * 2];
      var outline = ArtPalette.Uv(Swatch.Outline);
      for (var i = 0; i < count; i++) {
        allVertices[i] = vertices[i];
        allNormals[i] = normals[i];
        allUv[i] = uvs.Length > i ? uvs[i] : Vector2.zero;
        var direction = smooth[Key(vertices[i])].normalized;
        allVertices[count + i] = vertices[i] + direction * width;
        allNormals[count + i] = -direction;
        allUv[count + i] = outline;
      }
      var hull = new int[triangles.Length];
      for (var t = 0; t < triangles.Length; t += 3) {
        hull[t] = triangles[t] + count;
        hull[t + 1] = triangles[t + 2] + count;
        hull[t + 2] = triangles[t + 1] + count;
      }
      var mesh = new Mesh { name = source.name + "_outline", indexFormat = count * 2 > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
      mesh.vertices = allVertices;
      mesh.normals = allNormals;
      mesh.uv = allUv;
      mesh.subMeshCount = 2;
      mesh.SetTriangles(triangles, 0);
      mesh.SetTriangles(hull, 1);
      mesh.RecalculateBounds();
      Cache[key] = mesh;
      return mesh;
    }

    /// <summary>모델의 메쉬에 외곽선 서브메쉬를 붙인다. scale은 모델에 걸린 표시 배율이다.</summary>
    public static void Attach(GameObject model, float scale) {
      var width = VisualProfile.Current.OutlineWidth / Mathf.Max(scale, 0.01f);
      foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true)) {
        if (filter.sharedMesh == null || filter.sharedMesh.subMeshCount != 1 || filter.sharedMesh.isReadable == false) {
          continue;
        }
        filter.sharedMesh = WithHull(filter.sharedMesh, width);
      }
      Refresh(model);
    }

    /// <summary>품질에 맞게 머티리얼 수를 맞춘다. 두 번째 칸은 첫 칸과 같은 머티리얼이다.</summary>
    public static void Refresh(GameObject model) {
      var on = Enabled;
      foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true)) {
        var filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null || filter.sharedMesh.subMeshCount < 2) {
          continue;
        }
        var first = renderer.sharedMaterial;
        renderer.sharedMaterials = on ? new[] { first, first } : new[] { first };
      }
    }
  }
}
