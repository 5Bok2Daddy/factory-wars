namespace FactoryWars.Tests {
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using UnityEngine;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 19 3번: 절차 메쉬는 면마다 노멀이 분리돼 있어야 한다(삼각형 세 꼭짓점의 노멀이 같음). 스무스 노멀이면 면이 뭉개져 보인다.
  /// 외곽선 서브메쉬(1번)는 늘린 방향을 쓰므로 빼고, 0번 서브메쉬만 본다. Kenney 모델은 대상이 아니다.
  /// </summary>
  public partial class LocalMatchTests {
    static IEnumerable<string> SmoothTriangles(Mesh mesh, string label) {
      var normals = mesh.normals;
      var triangles = mesh.GetTriangles(0);
      for (var t = 0; t < triangles.Length; t += 3) {
        var a = normals[triangles[t]];
        var b = normals[triangles[t + 1]];
        var c = normals[triangles[t + 2]];
        if (Vector3.Angle(a, b) > 1f || Vector3.Angle(a, c) > 1f) {
          yield return $"{label} 삼각형 {t / 3}";
          yield break;
        }
      }
    }

    [Test]
    public void Art_ProceduralMeshesHaveFlatNormals() {
      var problems = new List<string>();
      foreach (var entry in Catalog().Entries) {
        foreach (var filter in entry.Prefab.GetComponentsInChildren<MeshFilter>(true)) {
          if (filter.sharedMesh != null && filter.sharedMesh.isReadable) {
            problems.AddRange(SmoothTriangles(filter.sharedMesh, $"{entry.Id}/{filter.name}"));
          }
        }
      }
      // 지형·나무·바위·도시 건물은 실행 중에 만든다.
      var kit = new MeshKit();
      ForestKit.Forest(kit, 6f, 4f, 3, 0.6f);
      problems.AddRange(SmoothTriangles(kit.ToMesh("forest"), "forest"));
      kit = new MeshKit();
      kit.Rock(Vector3.zero, 1.5f, 2f, 7);
      problems.AddRange(SmoothTriangles(kit.ToMesh("rock"), "rock"));
      for (var kind = 0; kind < 4; kind++) {
        for (var stage = 0; stage < 3; stage++) {
          problems.AddRange(SmoothTriangles(CityView.BlockMesh(kind, stage), $"city {kind}/{stage}"));
        }
      }
      Assert.IsEmpty(problems, "면 노멀이 아닌 메쉬: " + string.Join(", ", problems.Take(10)));
    }
  }
}
