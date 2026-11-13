namespace FactoryWars.Tests {
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using UnityEngine;
  using UnityEngine.Rendering;
  using UnityEngine.Rendering.Universal;

  /// <summary>절차적 프리팹 규칙. 보기 좋은지는 사람이 판단하고, 여기서는 규칙과 예산만 본다.</summary>
  public partial class LocalMatchTests {
    static readonly string[] Infantry = { "ff_ranger_squad", "ff_skyguard_squad", "cf_militia", "cf_flame_squad" };
    static readonly string[] Vehicles = {
      "ff_engineer", "ff_eagle_tank", "ff_hawk_gunship", "ff_skyfortress_bomber", "ff_scout_boat", "ff_artillery_ship",
      "cf_engineer", "cf_dragonscale_tank", "cf_thunder_aa", "cf_longwall_mlrs", "cf_scout_boat", "cf_artillery_ship",
    };
    static readonly string[] Headquarters2 = { "ff_headquarters", "cf_headquarters" };
    static readonly string[] BuildingKinds = {
      "collection_yard", "supply_depot", "barracks", "power_plant", "processor",
      "research_lab", "vehicle_factory", "ground_tower", "air_defense", "airfield", "heavy_works", "shipyard",
    };

    static IEnumerable<string> Buildings() => BuildingKinds.SelectMany(kind => new[] { $"ff_{kind}", $"cf_{kind}" });

    static ViewCatalog Catalog() => Resources.Load<ViewCatalog>(ViewCatalog.ResourcePath);

    static IEnumerable<string> AllArt() => Infantry.Concat(Vehicles).Concat(Headquarters2).Concat(Buildings()).Concat(new[] { "iron_mine", "bridge" });

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
      foreach (var id in Headquarters2.Concat(Buildings())) {
        Assert.That(Catalog().Find(id).GetComponentsInChildren<Renderer>(true).Length, Is.InRange(2, 5), id);
      }
    }

    [Test]
    public void Art_TriangleBudgets() {
      foreach (var id in Infantry.Concat(Vehicles)) {
        Assert.LessOrEqual(Triangles(Catalog().Find(id)), 1200, id);
      }
      foreach (var id in Headquarters2.Concat(Buildings())) {
        Assert.LessOrEqual(Triangles(Catalog().Find(id)), 2600, id);
      }
    }

    [Test]
    public void Art_OneSubmeshAndOneSharedMaterialPerRenderer() {
      foreach (var id in AllArt()) {
        foreach (var renderer in Catalog().Find(id).GetComponentsInChildren<MeshRenderer>(true)) {
          var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
          Assert.IsTrue(mesh.name.StartsWith(id), $"{id}: 직접 만든 메쉬가 아님 {mesh.name}");
          Assert.AreEqual(1, mesh.subMeshCount, $"{id}: 서브메쉬 하나 = 호출 하나");
          Assert.AreEqual(1, renderer.sharedMaterials.Length, id);
          var name = renderer.sharedMaterial.name;
          Assert.IsTrue(name == ArtLook.Neutral || name.StartsWith(ArtLook.TeamPrefix), $"{id}: {name}");
        }
      }
    }

    static bool UsesSwatch(GameObject prefab, Swatch swatch) {
      var uv = ArtPalette.Uv(swatch);
      return prefab.GetComponentsInChildren<MeshFilter>(true).Any(f => f.sharedMesh.uv.Any(v => Mathf.Abs(v.x - uv.x) < 0.001f));
    }

    [Test]
    public void Art_EveryUnitAndHeadquartersHasTeamColor() {
      foreach (var id in Infantry.Concat(Vehicles).Concat(Headquarters2).Concat(Buildings())) {
        Assert.IsTrue(UsesSwatch(Catalog().Find(id), Swatch.Team), id);
      }
    }

    [Test]
    public void Art_EveryBuildingSpecHasAModelInsideItsFootprint() {
      var specs = Resources.LoadAll<Quantum.BuildingSpec>("FactoryWars");
      // 항목 32: 진영당 20개(새 7종은 기존 모델을 빌려 크기만 바꾼다, ViewCatalog.TemporaryModels).
      // 항목 33: 궁극 전력 전용 건물 3개(항모 조립창·미사일 격납고·강습 도크)도 모델을 빌린다.
      Assert.AreEqual(43, specs.Length, "진영당 20개 + 전용 건물 3개");
      var problems = new List<string>();
      foreach (var spec in specs) {
        var id = spec.name;
        Assert.IsNotNull(Catalog().Find(id), $"{id}: 모델 없음");
        var instance = Object.Instantiate(Catalog().Find(id));
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) {
          bounds.Encapsulate(renderer.bounds);
        }
        var radius = spec.FootprintRadius.AsFloat + 0.01f;
        var borrowed = ViewCatalog.TemporaryModels.Where(m => m.Id == id).Select(m => m.Scale).DefaultIfEmpty(1f).First();
        var reach = Mathf.Max(bounds.max.x, -bounds.min.x, bounds.max.z, -bounds.min.z) * borrowed;
        if (reach > radius) {
          problems.Add($"{id} 칸 초과 {reach:0.00}");
        }
        if (bounds.min.y < -0.01f) {
          problems.Add($"{id} 땅속 {bounds.min.y:0.00}");
        }
        Object.DestroyImmediate(instance);
      }
      Assert.IsEmpty(problems, string.Join(", ", problems));
    }

    [Test]
    public void Art_ElectricAndMoltenGlowFollowFunctionNotFaction() {
      foreach (var prefix in new[] { "ff", "cf" }) {
        Assert.IsTrue(UsesSwatch(Catalog().Find($"{prefix}_power_plant"), Swatch.GlowCyan), $"{prefix}: 발전소 코어는 청록");
        Assert.IsFalse(UsesSwatch(Catalog().Find($"{prefix}_power_plant"), Swatch.GlowOrange), prefix);
        Assert.IsTrue(UsesSwatch(Catalog().Find($"{prefix}_processor"), Swatch.GlowOrange), $"{prefix}: 쇳물은 주황");
        Assert.IsFalse(UsesSwatch(Catalog().Find($"{prefix}_processor"), Swatch.GlowCyan), prefix);
        Assert.IsTrue(UsesSwatch(Catalog().Find($"{prefix}_research_lab"), Swatch.GlowCyan), $"{prefix}: 연구소 창은 청록");
      }
    }

    [Test]
    public void Art_TowersHaveTurretAndBarrel() {
      foreach (var id in new[] { "ff_ground_tower", "cf_ground_tower", "ff_air_defense", "cf_air_defense" }) {
        var turret = Catalog().Find(id).transform.Find("Turret");
        Assert.IsNotNull(turret, id);
        Assert.IsNotNull(turret.Find("Barrel"), $"{id}: 포신은 포탑을 따라 돈다");
      }
    }

    [Test]
    public void Art_PaletteMapsTeamGlowAndSurface() {
      Assert.AreEqual(ArtPalette.CellCount, System.Enum.GetValues(typeof(Swatch)).Length, "칸 수");
      Assert.LessOrEqual(ArtPalette.CellCount, ArtPalette.Size);
      var team = new Color32(10, 20, 30, 255);
      Assert.AreEqual(team, ArtPalette.BasePixels(team)[(int)Swatch.Team], "팀 칸은 플레이어 색");
      var on = ArtPalette.EmissionPixels(true);
      var off = ArtPalette.EmissionPixels(false);
      Assert.Greater(on[(int)Swatch.GlowCyan].g, 200, "전기 발광");
      Assert.AreEqual(0, off[(int)Swatch.GlowCyan].g, "정전이면 전기가 꺼짐");
      Assert.Greater(off[(int)Swatch.GlowOrange].r, 0, "쇳물은 어두워질 뿐");
      Assert.Less(off[(int)Swatch.GlowOrange].r, on[(int)Swatch.GlowOrange].r / 2);
      Assert.AreEqual(on[(int)Swatch.GlowCrystal], off[(int)Swatch.GlowCrystal], "광석 결정은 전기와 무관");
      Assert.AreEqual(0, on[(int)Swatch.Metal].r + on[(int)Swatch.Team].r, "발광 칸 외에는 검정");
      foreach (var matte in new[] { Swatch.Olive, Swatch.Earth, Swatch.Rubber, Swatch.Bark }) {
        Assert.Greater(ArtPalette.Smoothness(Swatch.Metal), ArtPalette.Smoothness(matte) + 0.3f, matte.ToString());
        Assert.Greater(ArtPalette.Metallic(Swatch.Metal), ArtPalette.Metallic(matte) + 0.5f, matte.ToString());
      }
    }

    [Test]
    public void Art_ColorAndPowerSwapSharedMaterialsOnly() {
      var instance = Object.Instantiate(Catalog().Find("ff_headquarters"));
      ArtLook.Apply(instance, 3, powered: false);
      var renderers = instance.GetComponentsInChildren<Renderer>(true);
      var names = renderers.Select(r => r.sharedMaterial.name).ToArray();
      Assert.IsTrue(names.All(n => n == "ArtP3Off"), string.Join(",", names));
      Assert.IsFalse(renderers.Any(r => r.HasPropertyBlock()), "MaterialPropertyBlock 없음");
      ArtLook.Apply(instance, 3, powered: true);
      Assert.IsTrue(instance.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterial.name == "ArtP3On"), "복구");
      Object.Destroy(instance);
    }

    [Test]
    public void Art_AllMaterialsShareOneShaderVariant() {
      var expected = ArtLook.NeutralMaterial;
      Assert.IsTrue(expected.IsKeywordEnabled("_EMISSION") && expected.IsKeywordEnabled("_METALLICSPECGLOSSMAP"));
      for (var color = 0; color < Palette.Count; color++) {
        foreach (var powered in new[] { true, false }) {
          var material = ArtLook.Team(color, powered);
          Assert.AreEqual(expected.shader, material.shader);
          CollectionAssert.AreEquivalent(expected.shaderKeywords, material.shaderKeywords, material.name);
        }
      }
    }

    [Test]
    public void Art_NeutralArtIgnoresTeamAndPower() {
      var instance = Object.Instantiate(Catalog().Find("iron_mine"));
      ArtLook.Apply(instance, 3, powered: false);
      Assert.IsTrue(instance.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterial.name == ArtLook.Neutral));
      Object.Destroy(instance);
    }

    [Test]
    public void Art_DisplayScaleOnlyTouchesTheModel() {
      foreach (var id in Infantry) {
        Assert.IsTrue(Catalog().TryGet(id, out var entry));
        Assert.AreEqual(1.5f, entry.DisplayScale, id);
      }
      foreach (var id in Vehicles) {
        Assert.IsTrue(Catalog().TryGet(id, out var entry));
        Assert.AreEqual(1.3f, entry.DisplayScale, id);
      }
      foreach (var id in Infantry.Concat(Vehicles)) {
        var root = new GameObject("root").transform;
        var view = ArtView.Create(Catalog(), id, root, 2);
        Assert.AreEqual(Vector3.one, view.transform.lossyScale, $"{id}: 시뮬레이션 위치 노드는 배율 없음");
        Assert.AreEqual(1f, view.Ring.transform.parent.lossyScale.x, id);
        Assert.AreEqual(1f, view.Bar.transform.parent.lossyScale.x, id);
        Object.DestroyImmediate(root.gameObject);
      }
    }

    [Test]
    public void Art_RingAndBarStayOnTheModelAtEveryYaw() {
      foreach (var id in Infantry.Concat(Vehicles)) {
        var root = new GameObject("root").transform;
        root.position = new Vector3(40, 0, -12);
        var view = ArtView.Create(Catalog(), id, root, 1);
        foreach (var yaw in new[] { 0f, 45f, 90f, 180f, 270f }) {
          view.SetYaw(yaw);
          var renderers = view.Model.GetComponentsInChildren<Renderer>(true);
          var bounds = renderers[0].bounds;
          foreach (var renderer in renderers) {
            bounds.Encapsulate(renderer.bounds);
          }
          var offset = new Vector2(bounds.center.x - root.position.x, bounds.center.z - root.position.z);
          var ring = view.Entry.RingRadius * ArtView.ScaleOf(view.Entry);
          Assert.Less(offset.magnitude, ring * 0.25f, $"{id} {yaw}: 모델이 링 가운데");
          var far = Mathf.Max(bounds.max.x - root.position.x, root.position.x - bounds.min.x, bounds.max.z - root.position.z, root.position.z - bounds.min.z);
          Assert.LessOrEqual(far, ring * 1.45f, $"{id} {yaw}: 대각선을 빼면 링 안");
          Assert.Greater(view.Bar.transform.position.y, bounds.max.y, $"{id} {yaw}: 체력바가 모델 위");
          Assert.AreEqual(root.position.x, view.Bar.transform.position.x, 0.001f, id);
          Assert.AreEqual(root.position.z, view.Bar.transform.position.z, 0.001f, id);
          Assert.AreEqual(root.position.x, view.Ring.transform.position.x, 0.001f, id);
          Assert.Less(view.Ring.transform.position.y, 0.2f, $"{id}: 링은 땅에");
        }
        Object.DestroyImmediate(root.gameObject);
      }
    }

    [Test]
    public void Art_HealthBarShrinksFromTheLeft() {
      var root = new GameObject("root").transform;
      var view = ArtView.Create(Catalog(), "ff_eagle_tank", root, 0);
      view.SetHealth(0.25f);
      Assert.AreEqual(0.25f, view.Fill.localScale.x, 0.001f);
      Assert.AreEqual(-0.375f, view.Fill.localPosition.x, 0.001f);
      view.SetHealth(2f);
      Assert.AreEqual(1f, view.Fill.localScale.x, 0.001f);
      Object.DestroyImmediate(root.gameObject);
    }

    [Test]
    public void Art_FlameSquadDiffersFromMilitia() {
      var flame = Catalog().Find("cf_flame_squad");
      var militia = Catalog().Find("cf_militia");
      Assert.IsTrue(UsesSwatch(flame, Swatch.GlowOrange), "주황 포구");
      Assert.IsFalse(UsesSwatch(militia, Swatch.GlowOrange));
      Assert.IsTrue(UsesSwatch(flame, Swatch.MetalLight), "연료통");
      Assert.IsFalse(UsesSwatch(militia, Swatch.MetalLight));
      Assert.Greater(flame.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.max.y, militia.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.max.y + 0.15f, "머리 위 실루엣");
    }

    [Test]
    public void Art_InfantryHelmetsHaveNoGlow() {
      foreach (var id in new[] { "ff_ranger_squad", "ff_skyguard_squad", "cf_militia" }) {
        Assert.IsFalse(UsesSwatch(Catalog().Find(id), Swatch.GlowCyan), id);
        Assert.IsFalse(UsesSwatch(Catalog().Find(id), Swatch.GlowOrange), id);
      }
    }

    [Test]
    public void Art_MineFacesVaryInShadeAndAreNotBlack() {
      var mine = Catalog().Find("iron_mine").transform.Find("Body").gameObject;
      Assert.IsTrue(UsesSwatch(mine, Swatch.OreDark) && UsesSwatch(mine, Swatch.Ore) && UsesSwatch(mine, Swatch.OreLight), "면마다 명도 차이");
      Assert.IsFalse(UsesSwatch(mine, Swatch.Black));
      var darkest = ArtPalette.BasePixels(default)[(int)Swatch.OreDark];
      Assert.Greater(darkest.r, 0x30, "검정이 아닌 회색");
    }

    [Test]
    public void Quality_ThreeTiersFromMobileBaselineUp() {
      CollectionAssert.AreEqual(new[] { "Low", "Medium", "High" }, QualitySettings.names);
      var low = (UniversalRenderPipelineAsset)QualitySettings.GetRenderPipelineAssetAt(0);
      var medium = (UniversalRenderPipelineAsset)QualitySettings.GetRenderPipelineAssetAt(1);
      var high = (UniversalRenderPipelineAsset)QualitySettings.GetRenderPipelineAssetAt(2);
      Assert.IsFalse(low.supportsHDR, "낮음은 HDR 끔");
      Assert.IsTrue(medium.supportsHDR && high.supportsHDR);
      Assert.Less(low.shadowDistance, medium.shadowDistance);
      Assert.Less(medium.shadowDistance, high.shadowDistance);
      Assert.IsFalse(low.supportsSoftShadows);
      Assert.IsTrue(medium.supportsSoftShadows && high.supportsSoftShadows, "그림자 계단 완화");
      Assert.Less(low.renderScale, high.renderScale);
      var before = GraphicsQuality.Current;
      GraphicsQuality.Apply(QualityTier.Low);
      Assert.IsFalse(GraphicsQuality.PostProcessing, "낮음은 Bloom 끔");
      Assert.IsFalse(GraphicsQuality.UnitShadows);
      Assert.AreSame(low, QualitySettings.renderPipeline);
      GraphicsQuality.Apply(QualityTier.High);
      Assert.IsTrue(GraphicsQuality.PostProcessing);
      Assert.AreSame(high, QualitySettings.renderPipeline);
      GraphicsQuality.Apply(before);
    }

    [Test]
    public void Quality_PostProcessingIsWiredUp() {
      var renderer = UnityEditor.AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/FactoryWarsURP_Renderer.asset");
      Assert.IsNotNull(renderer.postProcessData, "비어 있으면 Volume이 조용히 무시됨");
      foreach (var tier in new[] { QualityTier.Medium, QualityTier.High }) {
        var profile = Resources.Load<VolumeProfile>($"FactoryWars/Art/{GraphicsQuality.ProfileName(tier)}");
        Assert.IsTrue(profile.TryGet<Bloom>(out var bloom), "새 프로세스에서도 Bloom이 남음");
        Assert.Greater(bloom.intensity.value, 0f);
        Assert.Greater(bloom.threshold.value, 1f, "임계값은 HDR 발광만 넘는다");
        Assert.IsTrue(profile.TryGet<Tonemapping>(out _));
      }
      Assert.IsNotNull(Resources.Load<Cubemap>("FactoryWars/Art/SkyReflection"), "금속 반사");
    }

    [Test]
    public void Quality_SettingsButtonCyclesAndSaves() {
      var before = GraphicsQuality.Current;
      var saved = PlayerPrefs.GetInt(GraphicsQuality.PrefsKey, -1);
      GraphicsQuality.Apply(QualityTier.Low);
      Assert.AreEqual(QualityTier.Medium, GraphicsQuality.Cycle());
      Assert.AreEqual((int)QualityTier.Medium, PlayerPrefs.GetInt(GraphicsQuality.PrefsKey));
      Assert.AreEqual(QualityTier.High, GraphicsQuality.Cycle());
      Assert.AreEqual(QualityTier.Low, GraphicsQuality.Cycle());
      GraphicsQuality.Apply(before);
      if (saved < 0) {
        PlayerPrefs.DeleteKey(GraphicsQuality.PrefsKey);
      } else {
        PlayerPrefs.SetInt(GraphicsQuality.PrefsKey, saved);
      }
    }

    [Test]
    public void Camera_DefaultsCloserAndZoomsOutLess() {
      Assert.AreEqual(VisualProfile.Current.DefaultHeight, RtsCamera.DefaultHeight);
      Assert.Greater(RtsCamera.MaxHeight, RtsCamera.DefaultHeight);
      Assert.AreEqual(RtsCamera.DefaultHeight, RtsCamera.ClampHeight(RtsCamera.DefaultHeight));
    }

    [Test]
    public void Art_SameSeedSameShape() {
      Assert.AreEqual(MeshKit.Hash(7, 3), MeshKit.Hash(7, 3));
      Assert.AreNotEqual(MeshKit.Hash(7, 3), MeshKit.Hash(8, 3));
      Vector3[] Build(int seed) {
        var kit = new MeshKit();
        ForestKit.Forest(kit, 7f, 4f, seed, 0.5f);
        return kit.ToMesh("test").vertices;
      }
      Assert.AreEqual(Build(11), Build(11), "같은 ID면 같은 숲");
    }

    [Test]
    public void Art_HarvestKeepsForestShape() {
      Bounds Build(float remaining, out int triangles) {
        var kit = new MeshKit();
        ForestKit.Forest(kit, 7f, 4f, 11, remaining);
        triangles = kit.TriangleCount;
        return kit.ToMesh("test").bounds;
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
  }
}
