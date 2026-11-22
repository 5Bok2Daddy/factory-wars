namespace FactoryWars.Tests {
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using UnityEngine;

  /// <summary>사용자 CC BY 모델(드론·전투함) 슬롯 연결과 삼각형 예산(PROMPT-ASSETS-DRONE-SHIPS 3절).</summary>
  public class ThirdPartyModelTests {
    static int Count(Mesh mesh) {
      if (mesh == null) return 0;
      long indices = 0;
      for (var i = 0; i < mesh.subMeshCount; i++) indices += mesh.GetIndexCount(i);
      return (int)(indices / 3);
    }

    // 읽기 불가 팩 메쉬도 셀 수 있게 인덱스 수로 센다.
    static int Triangles(GameObject prefab) =>
      prefab.GetComponentsInChildren<MeshFilter>(true).Sum(f => Count(f.sharedMesh)) +
      prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(s => Count(s.sharedMesh));

    static VisualSlot Slot(string id) {
      var catalog = Resources.Load<VisualCatalog>(VisualCatalog.ResourcePath);
      Assert.IsNotNull(catalog, "VisualCatalog");
      return catalog.Slots.FirstOrDefault(s => s != null && s.Id == id);
    }

    [Test]
    public void Visual_AllShipAndDroneSlotsResolve() {
      foreach (var id in new[] { "unit/ff_artillery_ship", "unit/cf_artillery_ship", "unit/ff_scout_boat", "unit/cf_scout_boat",
                                 "unit/ff_nest_drone", "unit/shared_scout_drone", "unit/ff_stormnest_carrier" }) {
        var slot = Slot(id);
        Assert.IsNotNull(slot, $"{id} slot");
        Assert.IsTrue(slot.IsFilled, $"{id} has a prefab");
        Assert.IsNotNull(slot.Prefab, $"{id} first prefab");
        Assert.Greater(slot.Scale, 0f, $"{id} scale");
      }
      // 두 진영 포격함은 서로 다른 모델이어야 한다.
      Assert.AreNotEqual(Slot("unit/ff_artillery_ship").Prefab, Slot("unit/cf_artillery_ship").Prefab, "faction ships differ");
      // 공용 드론: 운반선 드론과 상인 드론이 같은 모델.
      Assert.AreEqual(Slot("unit/shared_scout_drone").Prefab, Slot("unit/ff_nest_drone").Prefab, "shared drone model");
      Assert.AreEqual("shared_scout_drone", Slot("unit/ff_nest_drone").Prefab.name);
      Assert.AreEqual("cf_ironwall_battleship", Slot("unit/cf_artillery_ship").Prefab.name);
    }

    [Test]
    public void Visual_ThirdPartyTriangleBudgets() {
      Assert.LessOrEqual(Triangles(Slot("unit/shared_scout_drone").Prefab), 3000, "drone <= 3,000 tris");
      Assert.LessOrEqual(Triangles(Slot("unit/cf_artillery_ship").Prefab), 18000, "battleship <= 18,000 tris");
      Assert.LessOrEqual(Triangles(Slot("unit/ff_stormnest_carrier").Prefab), 30000, "carrier <= 30,000 tris");
      // 한 모델 = 공유 머티리얼 1개.
      foreach (var id in new[] { "unit/shared_scout_drone", "unit/cf_artillery_ship" }) {
        var mats = Slot(id).Prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct().ToArray();
        Assert.AreEqual(1, mats.Length, $"{id} single shared material");
      }
    }
  }
}
