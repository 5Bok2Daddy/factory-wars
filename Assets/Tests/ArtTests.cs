namespace FactoryWars.Tests {
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using UnityEngine;

  /// <summary>절차적 프리팹 규칙. 보기 좋은지는 사람이 판단하고, 여기서는 규칙과 예산만 본다.</summary>
  public partial class LocalMatchTests {
    static readonly string[] Infantry = { "ff_ranger_squad", "ff_skyguard_squad", "cf_militia", "cf_flame_squad" };
    static readonly string[] Vehicles = {
      "ff_engineer", "ff_eagle_tank", "ff_hawk_gunship", "ff_skyfortress_bomber",
      "cf_engineer", "cf_dragonscale_tank", "cf_thunder_aa", "cf_longwall_mlrs",
    };
    static readonly string[] Headquarters2 = { "ff_headquarters", "cf_headquarters" };

    static ViewCatalog Catalog() => Resources.Load<ViewCatalog>(ViewCatalog.ResourcePath);

    static IEnumerable<string> AllArt() => Infantry.Concat(Vehicles).Concat(Headquarters2).Concat(new[] { "iron_mine", "bridge" });

    static int Triangles(GameObject prefab) => prefab.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh.triangles.Length / 3);

    [Test]
    public void Art_CatalogHasEveryId() {
      foreach (var id in AllArt()) {
        Assert.IsNotNull(Catalog().Find(id), id);
      }
    }

    [Test]
    public void Art_RendererLimits() {
      foreach (var id in Infantry) {
        Assert.AreEqual(1, Catalog().Find(id).GetComponentsInChildren<Renderer>(true).Length, $"{id}: 분대는 메쉬 하나");
      }
      foreach (var id in Vehicles) {
        Assert.That(Catalog().Find(id).GetComponentsInChildren<Renderer>(true).Length, Is.InRange(2, 4), id);
      }
      foreach (var id in Headquarters2) {
        Assert.That(Catalog().Find(id).GetComponentsInChildren<Renderer>(true).Length, Is.InRange(2, 5), id);
      }
    }

    [Test]
    public void Art_TriangleBudgets() {
      foreach (var id in Infantry.Concat(Vehicles)) {
        Assert.LessOrEqual(Triangles(Catalog().Find(id)), 1200, id);
      }
      foreach (var id in Headquarters2) {
        Assert.LessOrEqual(Triangles(Catalog().Find(id)), 2600, id);
      }
    }

    [Test]
    public void Art_NoBuiltInPrimitivesAndOnlySharedMaterials() {
      foreach (var id in AllArt()) {
        foreach (var renderer in Catalog().Find(id).GetComponentsInChildren<MeshRenderer>(true)) {
          var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
          Assert.IsTrue(mesh.name.StartsWith(id), $"{id}: 직접 만든 메쉬가 아님 {mesh.name}");
          Assert.AreEqual(mesh.subMeshCount, renderer.sharedMaterials.Length, id);
          foreach (var material in renderer.sharedMaterials) {
            Assert.IsTrue(material.name == "ArtBase" || material.name == ArtLook.GlowOn || material.name.StartsWith(ArtLook.TeamPrefix), $"{id}: {material.name}");
          }
        }
      }
    }

    [Test]
    public void Art_EveryUnitAndHeadquartersHasTeamColor() {
      foreach (var id in Infantry.Concat(Vehicles).Concat(Headquarters2)) {
        var materials = Catalog().Find(id).GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials);
        Assert.IsTrue(materials.Any(m => m.name.StartsWith(ArtLook.TeamPrefix)), id);
      }
    }

    [Test]
    public void Art_ColorAndPowerSwapSharedMaterialsOnly() {
      var instance = Object.Instantiate(Catalog().Find("ff_headquarters"));
      ArtLook.Apply(instance, 3, powered: false);
      var renderers = instance.GetComponentsInChildren<Renderer>(true);
      var names = renderers.SelectMany(r => r.sharedMaterials).Select(m => m.name).ToArray();
      Assert.Contains("Player3", names, "고른 색");
      Assert.Contains(ArtLook.GlowOff, names, "정전이면 꺼짐");
      Assert.IsFalse(names.Any(n => n.Contains("Instance")), "인스턴스 머티리얼 없음");
      Assert.IsFalse(renderers.Any(r => r.HasPropertyBlock()), "MaterialPropertyBlock 없음");
      ArtLook.Apply(instance, 3, powered: true);
      Assert.Contains(ArtLook.GlowOn, instance.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Select(m => m.name).ToArray(), "복구");
      Object.Destroy(instance);
    }

    [Test]
    public void Art_SameSeedSameShape() {
      Assert.AreEqual(MeshKit.Hash(7, 3), MeshKit.Hash(7, 3));
      Assert.AreNotEqual(MeshKit.Hash(7, 3), MeshKit.Hash(8, 3));
      Vector3[] Build(int seed) {
        var kit = new MeshKit();
        ForestKit.Forest(kit, 7f, 4f, seed, 0.5f);
        return kit.ToMesh("test", new List<ArtSlot>()).vertices;
      }
      Assert.AreEqual(Build(11), Build(11), "같은 ID면 같은 숲");
    }

    [Test]
    public void Art_HarvestKeepsForestShape() {
      Bounds Build(float remaining, out int triangles) {
        var kit = new MeshKit();
        ForestKit.Forest(kit, 7f, 4f, 11, remaining);
        triangles = kit.TriangleCount;
        return kit.ToMesh("test", new List<ArtSlot>()).bounds;
      }
      var full = Build(1f, out var fullTriangles);
      var half = Build(0.5f, out var halfTriangles);
      Assert.Less(halfTriangles, fullTriangles, "나무가 앙상해짐");
      Assert.Greater(half.size.x, full.size.x * 0.9f, "숲 너비는 그대로");
      Assert.Greater(half.size.z, full.size.z * 0.9f);
      Assert.Greater(half.size.y, full.size.y * 0.8f, "가운데 나무는 그대로");
    }

    [Test]
    public void Art_MineHidesOnlyCrystals() {
      var mine = Catalog().Find("iron_mine");
      Assert.IsNotNull(mine.transform.Find("Crystals"), "결정은 따로 끌 수 있음");
      Assert.IsNotNull(mine.transform.Find("Body"), "바위는 남음");
    }

    [Test]
    public void Art_BridgeDeckRotatesFromHinge() {
      Assert.IsNotNull(Catalog().Find("bridge").transform.Find("Deck"));
    }
  }
}
