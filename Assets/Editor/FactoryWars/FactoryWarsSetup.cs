namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.Editor;
  using Unity.AI.Navigation;
  using UnityEditor;
  using UnityEditor.SceneManagement;
  using UnityEngine;
  using UnityEngine.Rendering;
  using UnityEngine.Rendering.Universal;

  /// <summary>
  /// 프로젝트 설정·밸런스 데이터·테스트 씬 생성. 배치 모드에서
  /// -executeMethod FactoryWars.EditorTools.FactoryWarsSetup.RunAll 로 실행한다. 다시 실행해도 안전하다.
  /// </summary>
  public static partial class FactoryWarsSetup {
    const string AppId = "b0a33dd2-3c99-455f-ad7e-9c82b8a4cd40";
    const string DataRoot = "Assets/QuantumUser/Resources/FactoryWars";
    const string RenderRoot = "Assets/Settings";
    const string ScenesRoot = "Assets/QuantumUser/Scenes";
    const string ScenePath = ScenesRoot + "/QuantumGameScene.unity";
    const string MapRoot = "Assets/QuantumUser/Resources";
    const string BarrierRegion = "Barrier";
    const string BarricadeRegionPrefix = "Barricade";
    /// <summary>다리에 칠하는 Unity NavMesh 영역 번호. 0~2는 기본 영역이다.</summary>
    const int BarrierArea = 3;
    const int BarricadeArea = 4;
    const int WorldMargin = 32;

    [MenuItem("Tools/Factory Wars/Run All Setup")]
    public static void RunAll() {
      ConfigurePlayer();
      ConfigureRenderPipeline();
      ConfigureQuantum();
      BuildData();
      BuildVisualProfiles();
      KenneyTools.ConfigureImport();
      KenneyTools.ConvertMaterials();
      KenneyTools.RecolorAtlases();
      KenneyTools.BuildMap();
      BuildMaterials();
      BuildTestScene();
      BuildMapScene(FourPlayerMap);
      BuildMenuScene();
      AssetDatabase.SaveAssets();
      Debug.Log("FactoryWarsSetup: 완료");
    }

    public static void BuildMapsAndMenu() {
      BuildData();
      BuildTestScene();
      BuildMapScene(FourPlayerMap);
      BuildMenuScene();
      AssetDatabase.SaveAssets();
      Debug.Log("FactoryWarsSetup.BuildMapsAndMenu: 완료");
    }

    public static void ConfigurePlayer() {
      PlayerSettings.companyName = "MinGyu";
      PlayerSettings.productName = "Factory Wars";
      PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
      PlayerSettings.allowedAutorotateToPortrait = false;
      PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
      PlayerSettings.allowedAutorotateToLandscapeLeft = true;
      PlayerSettings.allowedAutorotateToLandscapeRight = true;
      PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.mingyu.factorywars");
      PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS, "com.mingyu.factorywars");
    }

    // 낮음은 모바일 기준이다. Bloom·톤매핑은 GraphicsQuality가 카메라에서 끈다.
    static readonly (string Name, bool Hdr, float Scale, int Msaa, float Shadow, int Resolution, int Cascades, bool Soft)[] Tiers = {
      ("Low", false, 0.75f, 1, 80f, 1024, 1, false),
      ("Medium", true, 0.85f, 1, 130f, 1024, 1, true),
      ("High", true, 1f, 2, 200f, 2048, 4, true),
    };

    const float ShadowDepthBias = 2f;
    const float ShadowNormalBias = 1.5f;

    public static void ConfigureRenderPipeline() {
      Directory.CreateDirectory(RenderRoot);
      var path = $"{RenderRoot}/FactoryWarsURP.asset";
      var rendererPath = $"{RenderRoot}/FactoryWarsURP_Renderer.asset";
      var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
      if (pipeline == null) {
        AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<UniversalRendererData>(), rendererPath);
        pipeline = UniversalRenderPipelineAsset.Create(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath));
        AssetDatabase.CreateAsset(pipeline, path);
      }
      var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
      // 코드로 만든 렌더러는 이 값이 비어 있고, 비어 있으면 Volume을 켜도 후처리가 전혀 돌지 않는다.
      renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
      // 화면 공간 차폐. 품질에 따라 GraphicsQuality가 켜고 끈다.
      if (renderer.rendererFeatures.Exists(f => f is ScreenSpaceAmbientOcclusion) == false) {
        var ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
        ssao.name = "SSAO";
        AssetDatabase.AddObjectToAsset(ssao, renderer);
        renderer.rendererFeatures.Add(ssao);
        var map = new SerializedObject(renderer).FindProperty("m_RendererFeatureMap");
        if (map != null) {
          AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out _, out long id);
          map.InsertArrayElementAtIndex(map.arraySize);
          map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
          map.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
      }
      EditorUtility.SetDirty(renderer);
      GraphicsSettings.defaultRenderPipeline = pipeline;

      var assets = new UniversalRenderPipelineAsset[Tiers.Length];
      for (var i = 0; i < Tiers.Length; i++) {
        var tier = Tiers[i];
        var tierPath = $"{RenderRoot}/FactoryWarsURP_{tier.Name}.asset";
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(tierPath);
        if (asset == null) {
          asset = UniversalRenderPipelineAsset.Create(renderer);
          AssetDatabase.CreateAsset(asset, tierPath);
        }
        asset.supportsHDR = tier.Hdr;
        asset.colorGradingMode = tier.Hdr ? ColorGradingMode.HighDynamicRange : ColorGradingMode.LowDynamicRange;
        asset.renderScale = tier.Scale;
        asset.msaaSampleCount = tier.Msaa;
        asset.shadowDistance = tier.Shadow;
        asset.mainLightShadowmapResolution = tier.Resolution;
        asset.shadowCascadeCount = tier.Cascades;
        // 게임 카메라의 화면 깊이는 기본 높이에서 약 46~115다. 가까운 분할이 이 구간을 덮게 한다.
        asset.cascade2Split = 0.4f;
        asset.cascade4Split = new Vector3(0.2f, 0.35f, 0.6f);
        asset.shadowDepthBias = ShadowDepthBias;
        asset.shadowNormalBias = ShadowNormalBias;
        var serialized = new SerializedObject(asset);
        serialized.FindProperty("m_SoftShadowsSupported").boolValue = tier.Soft;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        assets[i] = asset;
      }

      var quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
      var levels = quality.FindProperty("m_QualitySettings");
      while (levels.arraySize > Tiers.Length) {
        levels.DeleteArrayElementAtIndex(levels.arraySize - 1);
      }
      for (var i = 0; i < Tiers.Length; i++) {
        var level = levels.GetArrayElementAtIndex(i);
        level.FindPropertyRelative("name").stringValue = Tiers[i].Name;
        level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = assets[i];
      }
      quality.FindProperty("m_CurrentQuality").intValue = Tiers.Length - 1;
      var platforms = quality.FindProperty("m_PerPlatformDefaultQuality");
      for (var i = 0; i < platforms.arraySize; i++) {
        var platform = platforms.GetArrayElementAtIndex(i);
        var mobile = platform.FindPropertyRelative("first").stringValue == "Android";
        platform.FindPropertyRelative("second").intValue = mobile ? 0 : Tiers.Length - 1;
      }
      quality.ApplyModifiedPropertiesWithoutUndo();
      AssetDatabase.SaveAssets();
    }

    public static void ConfigureQuantum() {
      var photon = PhotonServerSettings.Global;
      photon.AppSettings.AppIdQuantum = AppId;
      EditorUtility.SetDirty(photon);

      var session = QuantumDeterministicSessionConfigAsset.Global;
      session.Config.LockstepSimulation = false;
      session.Config.UpdateFPS = 20;
      // 단위는 틱. 20Hz에서 4틱 = 200ms.
      session.Config.InputDelayMin = 4;
      EditorUtility.SetDirty(session);

      var simulation = QuantumDefaultConfigs.Global.SimulationConfig;
      simulation.Navigation.EnableNavigationCallbacks = true;
      EditorUtility.SetDirty(simulation);
    }

    const string MaterialRoot = DataRoot + "/Materials";

    /// <summary>에셋으로 두어 빌드에 셰이더가 포함되게 한다. 플레이어 색은 8벌을 공유한다.</summary>
    static Material ColorMaterial(string name, Color color) {
      Directory.CreateDirectory(MaterialRoot);
      var path = $"{MaterialRoot}/{name}.mat";
      var material = AssetDatabase.LoadAssetAtPath<Material>(path);
      if (material == null) {
        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        AssetDatabase.CreateAsset(material, path);
      }
      material.color = color;
      EditorUtility.SetDirty(material);
      return material;
    }

    public static void BuildMaterials() {
      for (var i = 0; i < FactoryWars.View.Palette.Count; i++) {
        ColorMaterial($"Player{i}", FactoryWars.View.Palette.Colors[i]);
      }
      ColorMaterial("Node", new Color32(0xE0, 0xB0, 0x30, 0xFF));
      AssetDatabase.SaveAssets();
    }

    static T Asset<T>(string name) where T : AssetObject {
      Directory.CreateDirectory(DataRoot);
      var path = $"{DataRoot}/{name}.asset";
      var asset = AssetDatabase.LoadAssetAtPath<T>(path);
      if (asset == null) {
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
      }
      // 파일 이름을 바꾼 에셋은 내부 이름이 옛날 그대로 남는다.
      asset.name = name;
      return asset;
    }

    static AssetRef<T> Ref<T>(T asset) where T : AssetObject {
      return new AssetRef<T>(QuantumUnityDBUtilities.GetExpectedAssetGuid(new LazyLoadReference<Object>(asset), out _));
    }

    static FP N(double value) => FP.FromFloat_UNSAFE((float)value);

    static ResourceCost R(int iron, int wood = 0, int steel = 0, int power = 0) {
      return new ResourceCost { Iron = iron, Wood = wood, Steel = steel, Power = power };
    }

    struct UnitRow {
      public string Id;
      public UnitSize Size;
      public bool Air;
      public ResourceCost Cost;
      public int Build, Health;
      public AttackType Attack;
      public double Damage, Range, MinRange, Splash, Interval, Speed;
      public int Spawn;
      public SpecialAbility Ability;
      public double AbilityMultiplier, AbilitySeconds, AbilityRadius, AbilityCooldown;
      public int AbilityCount;
    }

    // 비용은 DESIGN-RESOURCE-TERRAIN.md 5장. 생산·체력·공격 유형은 GDD. 피해·사거리·간격·속도는 임시값(SETUP-REPORT.md 참조).
    static readonly UnitRow[] Units = {
      new UnitRow { Id = "ff_engineer", Size = UnitSize.Engineer, Cost = R(100), Build = 10, Health = 80, Attack = AttackType.None, Speed = 4, Ability = SpecialAbility.EngineerWork },
      new UnitRow { Id = "ff_ranger_squad", Size = UnitSize.Small, Cost = R(70, 50), Build = 15, Health = 140, Attack = AttackType.AntiInfantry, Damage = 15, Range = 7, Interval = 1, Speed = 4, Ability = SpecialAbility.TargetDesignator, AbilityMultiplier = 1.25, AbilitySeconds = 5 },
      new UnitRow { Id = "ff_skyguard_squad", Size = UnitSize.Small, Cost = R(80, 50, 0, 20), Build = 18, Health = 120, Attack = AttackType.AntiAir, Damage = 20, Range = 8, Interval = 1, Speed = 4, Ability = SpecialAbility.AirPriority },
      new UnitRow { Id = "ff_eagle_tank", Size = UnitSize.Medium, Cost = R(250, 0, 80, 60), Build = 35, Health = 700, Attack = AttackType.AntiArmor, Damage = 60, Range = 9, Interval = 2, Speed = 5, Ability = SpecialAbility.FirstShotDouble, AbilityMultiplier = 2, AbilitySeconds = 8 },
      new UnitRow { Id = "ff_hawk_gunship", Size = UnitSize.Medium, Air = true, Cost = R(250, 0, 100, 100), Build = 40, Health = 450, Attack = AttackType.AntiArmor, Damage = 55, Range = 8, Interval = 2, Speed = 7, Ability = SpecialAbility.GroundArmorResist, AbilityMultiplier = 0.5 },
      new UnitRow { Id = "ff_skyfortress_bomber", Size = UnitSize.Large, Air = true, Cost = R(500, 0, 250, 350), Build = 70, Health = 1600, Attack = AttackType.Area, Damage = 200, Range = 4, Splash = 4, Interval = 5, Speed = 5, Ability = SpecialAbility.CarpetBomb, AbilityMultiplier = 1.5, AbilityRadius = 8, AbilityCount = 5, AbilityCooldown = 45 },
      new UnitRow { Id = "cf_engineer", Size = UnitSize.Engineer, Cost = R(80), Build = 8, Health = 70, Attack = AttackType.None, Speed = 4, Ability = SpecialAbility.EngineerWork },
      new UnitRow { Id = "cf_militia", Size = UnitSize.Small, Cost = R(40, 50), Build = 12, Health = 80, Attack = AttackType.AntiInfantry, Damage = 10, Range = 6, Interval = 1, Speed = 4, Spawn = 2, Ability = SpecialAbility.MilitiaPair },
      new UnitRow { Id = "cf_flame_squad", Size = UnitSize.Small, Cost = R(70, 60), Build = 15, Health = 130, Attack = AttackType.AntiInfantry, Damage = 20, Range = 3, Splash = 2, Interval = 1, Speed = 4, Ability = SpecialAbility.FlameBurst, AbilityMultiplier = 3 },
      new UnitRow { Id = "cf_dragonscale_tank", Size = UnitSize.Medium, Cost = R(190, 0, 50, 60), Build = 28, Health = 520, Attack = AttackType.AntiArmor, Damage = 45, Range = 8, Interval = 2, Speed = 5, Ability = SpecialAbility.SwarmSpeed, AbilityMultiplier = 1.15, AbilityCount = 5 },
      new UnitRow { Id = "cf_thunder_aa", Size = UnitSize.Medium, Cost = R(170, 0, 50, 60), Build = 28, Health = 450, Attack = AttackType.AntiAir, Damage = 40, Range = 10, Interval = 1.5, Speed = 5, Ability = SpecialAbility.MultiAirTarget, AbilityCount = 3 },
      new UnitRow { Id = "cf_longwall_mlrs", Size = UnitSize.Large, Cost = R(400, 0, 200, 200), Build = 55, Health = 1000, Attack = AttackType.Area, Damage = 120, Range = 18, MinRange = 6, Splash = 4, Interval = 6, Speed = 3, Ability = SpecialAbility.LongRangeBarrage },
    };

    static UnitSpec BuildUnit(UnitRow row) {
      var spec = Asset<UnitSpec>(row.Id);
      spec.Size = row.Size;
      spec.IsAir = row.Air;
      spec.Cost = row.Cost;
      spec.BuildTimeSeconds = row.Build;
      spec.MaxHealth = row.Health;
      spec.Attack = row.Attack;
      spec.Damage = N(row.Damage);
      spec.Range = N(row.Range);
      spec.MinRange = N(row.MinRange);
      spec.SplashRadius = N(row.Splash);
      spec.AttackInterval = N(row.Interval);
      spec.IsRanged = row.Range > 3;
      spec.MoveSpeed = N(row.Speed);
      spec.SpawnCount = row.Spawn > 0 ? row.Spawn : 1;
      // 인구: 소형 1, 중형 3, 대형 6. 공병은 1(임시). 민병대는 2명이 인구 1.
      var population = row.Size == UnitSize.Large ? 6 : row.Size == UnitSize.Medium ? 3 : 1;
      spec.Population = N((double)population / spec.SpawnCount);
      // 모듈 슬롯: 소형 1, 중형 2, 대형 3. 공병은 0(임시).
      spec.ModuleSlots = row.Size == UnitSize.Large ? 3 : row.Size == UnitSize.Medium ? 2 : row.Size == UnitSize.Small ? 1 : 0;
      spec.Ability = row.Ability;
      spec.AbilityMultiplier = row.AbilityMultiplier > 0 ? N(row.AbilityMultiplier) : 1;
      spec.AbilitySeconds = N(row.AbilitySeconds);
      spec.AbilityRadius = N(row.AbilityRadius);
      spec.AbilityCount = row.AbilityCount;
      spec.AbilityCooldownSeconds = N(row.AbilityCooldown);
      EditorUtility.SetDirty(spec);
      return spec;
    }

    static BuildingSpec BuildBuilding(string id, ResourceCost cost, int seconds, int health, int population, int slots, params UnitSpec[] producible) {
      var spec = Asset<BuildingSpec>(id);
      spec.Cost = cost;
      spec.BuildTimeSeconds = seconds;
      spec.MaxHealth = health;
      spec.PopulationCapBonus = population;
      spec.ModuleSlots = slots;
      spec.FootprintRadius = 3;
      spec.IsDropOff = id.EndsWith("_headquarters") || id.EndsWith("_collection_yard");
      spec.Producible = producible.Select(Ref).ToArray();
      spec.Attack = AttackType.None;
      EditorUtility.SetDirty(spec);
      return spec;
    }

    static FactionSpec BuildFaction(string factionId, string prefix, Dictionary<string, UnitSpec> units, double ranged, double smallBuild, double populationCap, string[] startingUnits, AssetRef<ModuleSpec>[] modules) {
      UnitSpec U(string id) => units[$"{prefix}_{id}"];
      var ids = units.Keys.Where(k => k.StartsWith(prefix + "_")).ToArray();
      var engineer = units[$"{prefix}_engineer"];
      var small = ids.Select(k => units[k]).Where(u => u.Size == UnitSize.Small).ToArray();
      var mediumGround = ids.Select(k => units[k]).Where(u => u.Size == UnitSize.Medium && u.IsAir == false).ToArray();
      var air = ids.Select(k => units[k]).Where(u => u.IsAir).ToArray();
      var large = ids.Select(k => units[k]).Where(u => u.Size == UnitSize.Large && u.IsAir == false).ToArray();

      var buildings = new[] {
        BuildBuilding($"{prefix}_headquarters", R(400, 300), 60, 3000, 10, 0, engineer),
        BuildBuilding($"{prefix}_power_plant", R(150, 150), 15, 600, 0, 0),
        BuildBuilding($"{prefix}_processor", R(250, 150, 0, 100), 20, 800, 0, 0),
        BuildBuilding($"{prefix}_supply_depot", R(100, 200), 15, 500, 20, 0),
        BuildBuilding($"{prefix}_barracks", R(200, 200), 20, 800, 0, 0, small),
        BuildBuilding($"{prefix}_research_lab", R(250, 200, 0, 150), 25, 700, 0, 0),
        BuildBuilding($"{prefix}_vehicle_factory", R(400, 200, 0, 200), 30, 1200, 0, 0, mediumGround),
        BuildBuilding($"{prefix}_airfield", R(400, 0, 100, 300), 35, 1000, 0, 0, air),
        BuildBuilding($"{prefix}_heavy_works", R(500, 0, 200, 400), 45, 1600, 0, 0, large),
        BuildBuilding($"{prefix}_ground_tower", R(250, 100, 0, 100), 20, 700, 0, 1),
        BuildBuilding($"{prefix}_collection_yard", R(0, 150), 15, 500, 0, 0),
        BuildBuilding($"{prefix}_air_defense", R(200, 100, 30, 100), 20, 700, 0, 1),
      };
      void Requires(int building, params int[] prerequisites) {
        buildings[building].Prerequisites = prerequisites.Select(i => Ref(buildings[i])).ToArray();
      }
      // 순서: 0 사령부, 1 발전소, 2 가공소, 3 보급창, 4 병영, 5 연구소, 6 군수공장, 7 비행장, 8 중공업 기지, 9 지상 방어탑, 10 집하장, 11 방공포대
      Requires(0, 0);
      Requires(1, 0);
      Requires(2, 1);
      Requires(3, 0);
      Requires(4, 0);
      Requires(5, 1);
      Requires(6, 4, 1);
      Requires(7, 6, 2);
      Requires(8, 6, 5, 2);
      Requires(9, 4, 1);
      Requires(10, 0);
      Requires(11, 4, 1);
      void Power(int building, int output, int storage, double upkeep, UpkeepMode mode) {
        buildings[building].PowerOutput = output;
        buildings[building].PowerStorage = storage;
        buildings[building].PowerUpkeep = N(upkeep);
        buildings[building].Upkeep = mode;
      }
      buildings[2].ProcessSeconds = 1;
      buildings[2].ProcessInput = R(2, 0, 0, 1);
      buildings[2].ProcessSteel = 1;
      Power(0, 0, 200, 0, UpkeepMode.None);
      Power(1, 5, 300, 0, UpkeepMode.None);
      Power(5, 0, 0, 1, UpkeepMode.WhileResearching);
      Power(6, 0, 0, 1, UpkeepMode.WhileProducing);
      Power(7, 0, 0, 2, UpkeepMode.WhileProducing);
      Power(8, 0, 0, 3, UpkeepMode.WhileProducing);
      Power(9, 0, 0, 0.5, UpkeepMode.Always);
      Power(11, 0, 0, 0.5, UpkeepMode.Always);
      for (var i = 0; i < buildings.Length; i++) {
        buildings[i].Role = (BuildingRole)i;
      }
      foreach (var core in new[] { 0, 4, 6, 7, 8 }) {
        buildings[core].IsCoreBuilding = true;
      }
      foreach (var building in buildings) {
        EditorUtility.SetDirty(building);
      }

      var tower = buildings[9];
      tower.Attack = AttackType.AntiArmor;
      tower.GroundOnly = true;
      tower.Damage = 40;
      tower.Range = 10;
      tower.AttackInterval = N(1.5);
      EditorUtility.SetDirty(tower);

      // 공중 배율 1.5는 상성표의 대공 VsAir다.
      var airDefense = buildings[11];
      airDefense.Attack = AttackType.AntiAir;
      airDefense.Damage = 30;
      airDefense.Range = 14;
      airDefense.AttackInterval = N(1.5);
      EditorUtility.SetDirty(airDefense);

      var faction = Asset<FactionSpec>($"faction_{factionId}");
      faction.FactionId = factionId;
      faction.RangedRangeMultiplier = N(ranged);
      faction.SmallBuildTimeMultiplier = N(smallBuild);
      faction.PopulationCapMultiplier = N(populationCap);
      faction.Headquarters = Ref(buildings[0]);
      faction.Engineer = Ref(engineer);
      // 사령부는 추가 건설이 드물어 패널 맨 끝에 둔다.
      faction.Buildings = buildings.Skip(1).Concat(buildings.Take(1)).Select(Ref).ToArray();
      faction.Units = ids.Select(k => Ref(units[k])).ToArray();
      faction.StartingUnits = startingUnits.Select(id => Ref(U(id))).ToArray();
      faction.Modules = modules;
      EditorUtility.SetDirty(faction);
      return faction;
    }

    static AiSpec BuildAi(string id, double think, int engineers, int variety, int maxYards, int maxHeadquarters, int maxProcessors, double lateExpansion, double firstAttack, double repeat,
        int wave, int growth, int maxWave, bool strategic, BuildingRole[] order) {
      var ai = Asset<AiSpec>(id);
      ai.ThinkSeconds = N(think);
      ai.EngineerTarget = engineers;
      ai.BuildOrder = order;
      ai.IronShareEarly = N(0.6);
      ai.IronShareLate = N(0.7);
      ai.MaxYards = maxYards;
      ai.YardWorkers = 4;
      ai.LateExpansionSeconds = N(lateExpansion);
      ai.SideLaneChance = N(0.4);
      ai.LaneArrivalDistance = 24;
      ai.LaneTimeoutSeconds = 70;
      ai.CaptureSquad = id == "ai_normal" ? 6 : 0;
      ai.SquadSeconds = 60;
      ai.CenterArmyRatio = N(1.5);
      ai.DropSquad = id == "ai_normal" ? 5 : 0;
      ai.BarricadeChance = id == "ai_normal" ? N(0.3) : 0;
      ai.HighGroundUnits = id == "ai_normal" ? 3 : 0;
      ai.HighGroundMinRange = 7;
      ai.GarrisonBuildings = id == "ai_normal" ? 2 : 1;
      ai.ShortcutChance = id == "ai_normal" ? N(0.25) : 0;
      ai.BreakGarrisons = id == "ai_normal";
      ai.ToppleDefense = id == "ai_normal";
      ai.MaxProcessors = maxProcessors;
      ai.SteelTarget = 500;
      ai.IronSurplus = 300;
      ai.ProcessorIronFloor = 150;
      ai.SteelCap = 1500;
      ai.PowerReserve = 20;
      ai.ExpandDistance = 30;
      ai.MaxHeadquarters = maxHeadquarters;
      ai.ExpandReserves = 5000;
      ai.ExpandReserveRadius = 25;
      ai.ExpandIron = 800;
      ai.ExpandWood = 500;
      ai.ExpandPopulationRoom = 4;
      ai.ConcurrentBuilds = 1;
      ai.QueueTarget = 2;
      ai.UnitVariety = variety;
      ai.FirstAttackSeconds = N(firstAttack);
      ai.AttackRepeatSeconds = N(repeat);
      ai.WaveSize = wave;
      ai.WaveGrowth = growth;
      ai.MaxWaveSize = maxWave;
      ai.StaleWaveSeconds = 120;
      ai.SweepArmyRatio = 4;
      ai.SweepSeconds = 20;
      ai.BreakReserveIronLeft = 4000;
      ai.PowerMargin = 3;
      ai.PopulationMargin = 4;
      // 건물 지름 6m + 간격(BuildingGap) 2m. 7이면 이웃 칸이 모두 막혀 4인 본진이 차서 인구가 막혔다(항목 19).
      ai.SiteGrid = 8;
      ai.SiteRings = 7;
      ai.StrategicDiplomacy = strategic;
      // 보통은 점수로만, 쉬움은 낮은 기준에 무작위를 더해 판단한다.
      ai.AcceptOpinion = strategic ? 40 : 10;
      ai.AcceptChance = strategic ? 0 : N(0.3);
      ai.StrengthUnitWeight = 1;
      ai.StrengthBuildingWeight = N(0.5);
      ai.StrengthIncomeWeight = N(0.1);
      ai.BreakStrengthRatio = N(1.5);
      ai.BreakAfterEnemySeconds = 30;
      ai.NoteIntervalSeconds = 20;
      ai.FriendlyNoteOpinion = 15;
      ai.PeaceOpinion = 1;
      ai.CommonEnemyOpinion = 5;
      ai.AttackedOpinion = -15;
      ai.StrongestOpinion = -3;
      ai.BrokenOpinion = -60;
      ai.RecentFightSeconds = 30;
      ai.ProposalIntervalSeconds = 90;
      ai.DiplomacyIronReserve = 600;
      EditorUtility.SetDirty(ai);
      return ai;
    }

    public static void BuildData() {
      var units = Units.ToDictionary(row => row.Id, BuildUnit);

      var piercing = Asset<ModuleSpec>("module_piercing_rounds");
      piercing.Effect = ModuleEffect.PiercingRounds;
      piercing.Cost = R(0, 0, 30, 10);
      piercing.DamageMultiplier = N(1.25);
      EditorUtility.SetDirty(piercing);

      var reactive = Asset<ModuleSpec>("module_reactive_armor");
      reactive.Effect = ModuleEffect.ReactiveArmor;
      reactive.Cost = R(0, 0, 30, 30);
      reactive.DamageMultiplier = N(0.5);
      reactive.CooldownSeconds = 20;
      EditorUtility.SetDirty(reactive);

      var modules = new[] { Ref(piercing), Ref(reactive) };
      BuildFaction("free_federation", "ff", units, 1.1, 1, 1, new[] { "ranger_squad", "ranger_squad", "eagle_tank" }, modules);
      BuildFaction("continental_federation", "cf", units, 1, 0.75, 1.2, new[] { "militia", "militia", "dragonscale_tank" }, modules);

      var rules = Asset<GameRulesSpec>("game_rules");
      rules.NavMeshName = "NavMesh";
      rules.PlayerColorCount = FactoryWars.View.Palette.Count;
      rules.TruceSeconds = 420;
      rules.BarrierRegion = BarrierRegion;
      rules.CaptureSeconds = 10;
      rules.CaptureDecaySeconds = 30;
      rules.CommsSteelPerSecond = 1;
      rules.RelayPowerPerSecond = 3;
      rules.BarricadeHealth = 1500;
      rules.CratePickRadius = 2;
      rules.BaseCrateRewards = new[] { R(100, 0), R(0, 100) };
      rules.DropReward = R(200, 0, 150, 0);
      rules.DropIntervalSeconds = 150;
      rules.DropWarningSeconds = 20;
      rules.HighGroundRangeMultiplier = N(1.15);
      rules.HighGroundRevealDistance = 6;
      rules.HighGroundAirRevealDistance = 12;
      rules.HighGroundRevealSeconds = 3;
      rules.RoadSpeedMultiplier = N(1.1);
      rules.FordSpeedMultiplier = N(0.75);
      rules.BaseAlertSeconds = 30;
      rules.CityKinds = new[] {
        new CityKindSpec { Health = 1500, Height = 7 },
        new CityKindSpec { Health = 3000, Height = 12 },
        new CityKindSpec { Health = 5000, Height = 9 },
        new CityKindSpec { Health = 3000, Height = 18 },
      };
      rules.CityAreaMultiplier = N(1.5);
      rules.GarrisonFlameMultiplier = 2;
      rules.RubbleSpeedMultiplier = N(0.7);
      rules.CollapseRadius = 6;
      rules.CollapseDamage = 200;
      rules.GarrisonEjectFraction = N(0.25);
      rules.ToppleDamage = 600;
      rules.TopplePushMargin = N(1.5);
      rules.GarrisonCapacity = 3;
      rules.GarrisonRangeBonus = 2;
      rules.GarrisonEnterDistance = 2;
      rules.CityCrackedFraction = N(0.6);
      rules.CityBurningFraction = N(0.3);
      rules.MinDiplomacyPlayers = 3;
      rules.ProposalSeconds = 30;
      rules.ProposalCost = 300;
      rules.ProposalRefund = 150;
      rules.AcceptCost = 150;
      rules.BreakCost = 500;
      rules.JointVictorySeconds = 60;
      rules.BreakGraceSeconds = 10;
      rules.RealliableSeconds = 60;
      rules.NoteCooldownSeconds = 5;
      rules.NotePhraseCount = 12;
      // 순서는 Texts.Phrases: 0 동맹 어때요?, 3 고맙습니다, 5 함께 공격합시다, 9 좋은 경기였습니다, 11 알겠습니다
      rules.FriendlyPhrases = new[] { 0, 3, 5, 9, 11 };
      rules.OpinionIntervalSeconds = 10;
      rules.OpinionLimit = 100;
      rules.NoteSingleCost = 50;
      rules.NoteGroupCost = 100;
      rules.PowerResumeLevel = 5;
      rules.OutageTowerIntervalMultiplier = 2;
      rules.SpawnOffset = 6;
      rules.AirAltitude = 6;
      rules.CarryCapacity = 50;
      rules.IronGatherSeconds = 2;
      rules.WoodGatherSeconds = 3;
      rules.MineSpots = 6;
      rules.ForestSpotSpacing = 4;
      rules.SpotOffset = N(1.5);
      rules.SpotArrival = N(1.5);
      rules.WorkRange = 2;
      rules.NodeSearchRadius = 30;
      rules.ResourceBuildMargin = 3;
      rules.TerrainBuildMargin = 2;
      rules.RepairPerSecond = 20;
      rules.RepairCostRatio = N(0.5);
      rules.FoundationHealthRatio = N(0.1);
      // 본진 사령부 크기 수용이 25채 이상이라 2m를 쓴다(25채 미만이면 1.5m로 줄일 것). MapTests가 잰다.
      rules.BuildingGap = N(2);
      // 항목 17B: 2·3인 경기의 빈 본진 자원은 70%만 둔다(중립 대형 확장지).
      rules.EmptyBaseResourceRatio = N(0.7);
      rules.EasyAi = Ref(BuildAi("ai_easy", 5, 6, 1, 1, 2, 2, 0, 420, 90, 6, 2, 12, false, new[] {
        BuildingRole.SupplyDepot, BuildingRole.Barracks, BuildingRole.PowerPlant, BuildingRole.Processor, BuildingRole.VehicleFactory, BuildingRole.PowerPlant,
      }));
      rules.NormalAi = Ref(BuildAi("ai_normal", 2, 10, 3, 4, 3, 5, 600, 300, 60, 8, 3, 24, true, new[] {
        BuildingRole.SupplyDepot, BuildingRole.Barracks, BuildingRole.PowerPlant, BuildingRole.Processor, BuildingRole.VehicleFactory, BuildingRole.ResearchLab,
        BuildingRole.SupplyDepot, BuildingRole.PowerPlant, BuildingRole.Airfield, BuildingRole.HeavyWorks, BuildingRole.GroundTower, BuildingRole.AirDefense, BuildingRole.SupplyDepot,
      }));
      rules.UnitClearance = N(0.5);
      rules.SlideMemorySeconds = 1;
      rules.Matchups = new[] {
        new DamageMatchup { Attack = AttackType.AntiInfantry, VsSmall = N(1.5), VsMedium = N(0.6), VsLarge = N(0.4), VsAir = 1, VsBuilding = N(0.5) },
        new DamageMatchup { Attack = AttackType.AntiArmor, VsSmall = N(0.6), VsMedium = N(1.5), VsLarge = N(1.5), VsAir = 1, VsBuilding = 1 },
        new DamageMatchup { Attack = AttackType.AntiAir, VsSmall = N(0.5), VsMedium = N(0.25), VsLarge = N(0.25), VsAir = N(1.5), VsBuilding = N(0.25) },
        new DamageMatchup { Attack = AttackType.Area, VsSmall = 1, VsMedium = 1, VsLarge = 1, VsAir = 1, VsBuilding = 1 },
      };
      EditorUtility.SetDirty(rules);

      var testMap = Asset<MapSpec>("map_test");
      testMap.MaxPlayers = 4;
      testMap.BoundsMin = new FPVector2(-50, -50);
      testMap.BoundsMax = new FPVector2(50, 50);
      testMap.StartPositions = new[] {
        new FPVector3(-30, 0, 0), new FPVector3(30, 0, 0), new FPVector3(0, 0, 30), new FPVector3(0, 0, -30),
      };
      // 테스트 맵은 건설·외교 시험을 위해 넉넉히 준다.
      testMap.StartingResources = R(5000, 10000, 5000, 200);
      testMap.StartingEngineers = 1;
      testMap.SpawnFactionUnits = true;
      testMap.ResourceNodes = new[] {
        Mine(-30, 14, 5000),
        Mine(-30, -14, 100),
        Forest(-38, 30, 8, 3, 0, 3000),
      };
      testMap.Terrain = new[] {
        Feature(TerrainKind.Rock, Circle(40, -40, 3)),
      };
      testMap.NoBuildZones = new[] { Rect(40, -22, 6, 6) };
      // 전장 규칙 시험용. 다른 시험이 지나가지 않는 맵 남쪽에 둔다.
      testMap.CaptureSites = new[] { Site(CaptureKind.Comms, -35, -38, 6), Site(CaptureKind.Relay, -20, -44, 3) };
      testMap.HighGrounds = new[] { Circle(42, 24, 6) };
      testMap.Roads = new[] { Rect(10, -44, 20, 3) };
      testMap.Fords = new[] { Rect(10, -36, 20, 2) };
      // 도시 시험용: 진지 하나와 쓰러지는 건물 하나.
      testMap.CityBuildings = new[] {
        new CityBuildingPlacement { Shape = Rect(-10, -24, 4, 4), Kind = 0, Garrison = true, Region = "City0" },
        new CityBuildingPlacement { Shape = Rect(14, -12, 3.5, 3.5), Kind = 3, Topples = true, FallZone = Rect(14, -25, 9, 3.5, 90), Region = "City1", FallRegion = "Fall1" },
      };
      BakeGroundGrid(testMap);
      EditorUtility.SetDirty(testMap);

      BuildMapSpec(FourPlayerMap);

      AssetDatabase.SaveAssets();
      QuantumUnityDBUtilities.RefreshGlobalDB(true);
    }

    /// <summary>맵 중심 기준으로 90도씩 quarterTurns번 돌린다.</summary>
    static MapShape Turn(MapShape shape, int quarterTurns) {
      for (var i = 0; i < quarterTurns; i++) {
        shape.Center = new FPVector3(-shape.Center.Z, 0, shape.Center.X);
        shape.Yaw -= 90;
      }
      return shape;
    }

    /// <summary>플레이어 수만큼 회전 대칭 복사본을 돌려준다. 2인은 180도, 4인은 90도 간격.</summary>
    static IEnumerable<MapShape> Orbit(MapDef def, MapShape shape) {
      var step = 4 / def.Players;
      return Enumerable.Range(0, def.Players).Select(i => Turn(shape, i * step));
    }

    static void BuildMapSpec(MapDef def) {
      var spec = Asset<MapSpec>($"map_{def.Id}");
      spec.MaxPlayers = def.Players;
      spec.BoundsMin = new FPVector2(-def.HalfSize, -def.HalfSize);
      spec.BoundsMax = new FPVector2(def.HalfSize, def.HalfSize);
      spec.StartPositions = Orbit(def, new MapShape { Center = def.Start }).Select(s => s.Center).ToArray();
      spec.StartingResources = R(500, 300);
      spec.StartingEngineers = 4;
      spec.ResourceNodes = def.Nodes
        .SelectMany(node => Orbit(def, node.Shape).Select(shape => new ResourceNodePlacement { Kind = node.Kind, Amount = node.Amount, Shape = shape }))
        .Concat(def.CenterNodes).ToArray();
      // 강은 한 사분면만 만들고 나머지는 회전 복제한다.
      var area = def.BaseArea;
      var corner = new Vector2(area.Center.X.AsFloat + area.HalfLength.AsFloat, area.Center.Z.AsFloat + area.HalfWidth.AsFloat);
      var bridges = def.Bridges.Select(b => new Vector2(b.Center.X.AsFloat, b.Center.Z.AsFloat)).ToArray();
      var pieces = RiverBuilder.Build(corner, -def.HalfSize, bridges, def.RiverAmplitude, def.RiverSeed);
      MapShape Shape(RiverBuilder.Piece piece) => Rect(piece.Center.x, piece.Center.y, piece.HalfLength, piece.HalfWidth, piece.Yaw);
      var step = 4 / def.Players;
      spec.River = Enumerable.Range(0, def.Players).SelectMany(player => pieces.Select(piece => Turn(Shape(piece), player * step))).ToArray();
      var edge = def.HalfSize + 2f;
      var blocks = pieces.Where(p => p.UnderBridge == false && Mathf.Abs(p.Center.x) < edge && Mathf.Abs(p.Center.y) < edge)
        .SelectMany(p => Orbit(def, Shape(p)).Select(shape => Feature(TerrainKind.River, shape)));
      spec.Terrain = def.Terrain.SelectMany(feature => Orbit(def, feature.Shape).Select(shape => Feature(feature.Kind, shape))).Concat(blocks).ToArray();
      spec.NoBuildZones = def.NoBuildZones.SelectMany(zone => Orbit(def, zone)).ToArray();
      spec.Bridges = def.Bridges.SelectMany(bridge => Orbit(def, bridge)).ToArray();
      spec.BaseAreas = Orbit(def, def.BaseArea).ToArray();
      FillBattlefield(def, spec);
      EditorUtility.SetDirty(spec);
    }

    /// <summary>점령지·바리케이드·상자·고지대·도로·여울·확장 후보. 규칙은 항목 20의 시스템이 읽는다.</summary>
    static void FillBattlefield(MapDef def, MapSpec spec) {
      var step = 4 / def.Players;
      FPVector3 Turned(FPVector3 position, int seat) => Turn(new MapShape { Center = position }, seat * step).Center;

      spec.CaptureSites = def.CaptureSites
        .SelectMany(site => Enumerable.Range(0, def.Players).Select(seat => new CaptureSitePlacement { Kind = site.Kind, Center = Turned(site.Center, seat), Radius = site.Radius }))
        .Concat(def.CenterSites).ToArray();
      spec.Barricades = def.Barricades.SelectMany(shape => Orbit(def, shape))
        .Select((shape, index) => new BarricadePlacement { Shape = shape, Region = $"{BarricadeRegionPrefix}{index}" }).ToArray();

      var crates = new List<CrateSpot>();
      for (var seat = 0; seat < def.Players; seat++) {
        crates.AddRange(def.BaseCrates.Select((position, index) => new CrateSpot { Position = Turned(position, seat), Seat = seat, Group = index }));
      }
      for (var group = 0; group < def.DropCrates.Length; group++) {
        for (var seat = 0; seat < def.Players; seat++) {
          crates.Add(new CrateSpot { Position = Turned(def.DropCrates[group], seat), Seat = -1, Group = group });
        }
      }
      spec.CrateSpots = crates.ToArray();

      spec.HighGrounds = def.HighGrounds.SelectMany(shape => Orbit(def, shape)).ToArray();
      spec.Ramps = def.Ramps.SelectMany(shape => Orbit(def, shape)).ToArray();
      spec.Roads = def.Roads.SelectMany(shape => Orbit(def, shape)).ToArray();
      spec.Fords = def.Fords.SelectMany(shape => Orbit(def, shape)).ToArray();

      var sites = new List<ExpansionSite>();
      for (var seat = 0; seat < def.Players; seat++) {
        sites.AddRange(def.Expansions.Select(site => new ExpansionSite { Kind = site.Kind, Position = Turned(site.Position, seat), SeatMask = 1 << seat }));
        foreach (var site in def.SharedExpansions) {
          var mask = def.SharedSeatOffsets.Aggregate(0, (bits, offset) => bits | 1 << (seat + offset) % def.Players);
          sites.Add(new ExpansionSite { Kind = site.Kind, Position = Turned(site.Position, seat), SeatMask = mask });
        }
      }
      var everyone = (1 << def.Players) - 1;
      sites.AddRange(def.CenterExpansions.Select(site => new ExpansionSite { Kind = site.Kind, Position = site.Position, SeatMask = everyone }));
      spec.Expansions = sites.ToArray();
      CityLayout(def, spec);
      BakeGroundGrid(spec);
    }

    /// <summary>도로와 여울을 격자로 칠한다. 칸 중심이 모양 안이면 그 칸이다. 여울이 도로보다 우선이다.</summary>
    static void BakeGroundGrid(MapSpec spec) {
      const int Cell = 2;
      var size = (int)((spec.BoundsMax.X - spec.BoundsMin.X).AsFloat / Cell);
      spec.GroundGridSize = size;
      spec.GroundGridCell = Cell;
      spec.GroundGrid = new byte[size * size];
      for (var z = 0; z < size; z++) {
        for (var x = 0; x < size; x++) {
          var point = new FPVector3(spec.BoundsMin.X + x * Cell + 1, 0, spec.BoundsMin.Y + z * Cell + 1);
          var ground = 0;
          if ((spec.Roads ?? new MapShape[0]).Any(road => road.Distance(point) <= FP._0)) {
            ground = MapSpec.GroundRoad;
          }
          if ((spec.Fords ?? new MapShape[0]).Any(ford => ford.Distance(point) <= FP._0)) {
            ground = MapSpec.GroundFord;
          }
          spec.GroundGrid[z * size + x] = (byte)ground;
        }
      }
      var cities = spec.CityBuildings ?? new CityBuildingPlacement[0];
      spec.CityGrid = new short[cities.Length > 0 ? size * size : 0];
      for (var i = 0; i < cities.Length; i++) {
        var reach = cities[i].Shape.Reach.AsFloat;
        var center = cities[i].Shape.Center;
        for (var z = 0; z < size; z++) {
          for (var x = 0; x < size; x++) {
            var point = new FPVector3(spec.BoundsMin.X + x * Cell + 1, 0, spec.BoundsMin.Y + z * Cell + 1);
            if (Mathf.Abs((point.X - center.X).AsFloat) <= reach && Mathf.Abs((point.Z - center.Z).AsFloat) <= reach && cities[i].Shape.Distance(point) <= FP._0) {
              spec.CityGrid[z * size + x] = (short)(i + 1);
            }
          }
        }
      }
    }

    static void BuildMapScene(MapDef def) {
      var scenePath = $"{ScenesRoot}/{def.Id}.unity";
      if (File.Exists(scenePath)) {
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
      } else {
        QuantumEditorMenuCreateScene.CreateNewQuantumScene(scenePath, $"{MapRoot}/{def.Id}.asset", true, true, false);
      }
      var scene = EditorSceneManager.GetActiveScene();
      var map = Object.FindFirstObjectByType<QuantumMapData>();

      var ground = EnsureGround();
      ground.transform.localScale = new Vector3(def.HalfSize / 5f, 1, def.HalfSize / 5f);
      EnsureNavMesh(map);
      EnsureDebugView();

      var spec = AssetDatabase.LoadAssetAtPath<MapSpec>($"{DataRoot}/map_{def.Id}.asset");
      BuildTerrain(spec);

      var camera = Camera.main;
      camera.transform.position = def.CameraPosition;
      camera.transform.rotation = Quaternion.Euler(50, 0, 0);

      ConfigureRunner(def.Players);

      // NavMesh 격자와 맵 크기가 경계를 덮어야 한다. 기본값 256은 4인 맵보다 작다.
      var size = def.HalfSize * 2 + WorldMargin;
      var asset = map.GetAsset(true);
      asset.WorldSize = size;
      asset.GridSizeX = size / asset.GridNodeSize;
      asset.GridSizeY = size / asset.GridNodeSize;
      Bake(map, spec, scene);
    }

    /// <summary>MapSpec의 자원과 지형을 도형으로 만든다. NavMesh 굽기·건설 검사·미니맵이 같은 데이터를 쓴다.</summary>
    static void BuildTerrain(MapSpec spec) {
      foreach (var name in new[] { "Obstacles", "Terrain" }) {
        var old = GameObject.Find(name);
        if (old != null) {
          Object.DestroyImmediate(old);
        }
      }
      var root = new GameObject("Terrain").transform;
      foreach (var node in spec.ResourceNodes) {
        AddBlock(root, node.Kind.ToString(), node.Shape, node.Kind == ResourceKind.Iron ? 3 : 5);
      }
      foreach (var feature in spec.Terrain ?? new TerrainFeature[0]) {
        AddBlock(root, feature.Kind.ToString(), feature.Shape, feature.Kind == TerrainKind.Rock ? 3 : 6);
      }
      foreach (var bridge in spec.Bridges ?? new MapShape[0]) {
        AddBridge(root, spec, bridge);
      }
      foreach (var barricade in spec.Barricades ?? new BarricadePlacement[0]) {
        AddRegion(root, "Barricade", barricade.Shape, BarricadeArea, barricade.Region);
      }
      foreach (var city in spec.CityBuildings ?? new CityBuildingPlacement[0]) {
        AddRegion(root, "City", city.Shape, BarricadeArea, city.Region);
        if (city.Topples) {
          AddRegion(root, "Fall", city.FallZone, BarricadeArea, city.FallRegion);
        }
      }
    }

    /// <summary>
    /// 다리 하나를 NavMesh 영역으로 칠한다. Quantum은 같은 Unity 영역의 삼각형 덩어리가 렌더러 경계 안에 다 들어올 때
    /// 그 영역 이름을 붙이므로, 렌더러는 꺼 둔 채 경계로만 쓴다. 자리별 본진이 있으면 자리 번호를 붙여 빈 본진 다리는 처음부터 열 수 있게 한다.
    /// </summary>
    static void AddBridge(Transform root, MapSpec spec, MapShape shape) {
      if (spec.BaseAreas != null && spec.BaseAreas.Length > 0) {
        var seat = 0;
        var best = float.MaxValue;
        for (var i = 0; i < spec.BaseAreas.Length; i++) {
          var distance = (shape.Center - spec.BaseAreas[i].Center).SqrMagnitude.AsFloat;
          if (distance < best) {
            best = distance;
            seat = i;
          }
        }
        AddRegion(root, "Bridge", shape, BarrierArea, $"{BarrierRegion}{seat}");
      } else {
        AddRegion(root, "Bridge", shape, BarrierArea, BarrierRegion);
      }
    }

    static void AddRegion(Transform root, string name, MapShape shape, int area, string regionId) {
      var bridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
      bridge.name = name;
      Object.DestroyImmediate(bridge.GetComponent<Collider>());
      bridge.transform.SetParent(root, false);
      bridge.transform.position = new Vector3(shape.Center.X.AsFloat, 0, shape.Center.Z.AsFloat);
      bridge.transform.rotation = Quaternion.Euler(0, shape.Yaw.AsFloat, 0);
      bridge.transform.localScale = new Vector3(shape.HalfLength.AsFloat * 2, 3, shape.HalfWidth.AsFloat * 2);
      bridge.GetComponent<MeshRenderer>().enabled = false;
      bridge.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
      var volume = bridge.AddComponent<NavMeshModifierVolume>();
      volume.center = Vector3.zero;
      volume.size = Vector3.one;
      volume.area = area;
      var region = bridge.AddComponent<QuantumNavMeshRegion>();
      region.Id = regionId;
      region.CastRegion = QuantumNavMeshRegion.RegionCastType.CastRegion;
    }

    static void AddBlock(Transform root, string name, MapShape shape, float height) {
      var block = GameObject.CreatePrimitive(shape.IsCircle ? PrimitiveType.Cylinder : PrimitiveType.Cube);
      block.name = name;
      block.transform.SetParent(root, false);
      block.transform.position = new Vector3(shape.Center.X.AsFloat, height / 2, shape.Center.Z.AsFloat);
      if (shape.IsCircle) {
        var diameter = shape.Radius.AsFloat * 2;
        block.transform.localScale = new Vector3(diameter, height / 2, diameter);
      } else {
        block.transform.rotation = Quaternion.Euler(0, shape.Yaw.AsFloat, 0);
        block.transform.localScale = new Vector3(shape.HalfLength.AsFloat * 2, height, shape.HalfWidth.AsFloat * 2);
      }
      var modifier = block.AddComponent<NavMeshModifier>();
      modifier.overrideArea = true;
      modifier.area = 1; // Not Walkable
    }

    public static void BuildTestScene() {
      var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
      var map = Object.FindFirstObjectByType<QuantumMapData>();

      var ground = EnsureGround();
      ground.transform.localScale = new Vector3(10, 1, 10);
      EnsureNavMesh(map);
      EnsureDebugView();

      var camera = Camera.main;
      if (camera != null) {
        camera.transform.position = new Vector3(0, 55, -45);
        camera.transform.rotation = Quaternion.Euler(50, 0, 0);
      }

      var spec = AssetDatabase.LoadAssetAtPath<MapSpec>($"{DataRoot}/map_test.asset");
      BuildTerrain(spec);

      ConfigureRunner(2);
      Bake(map, spec, scene);
    }

    /// <summary>시작 화면 씬을 만들고 빌드 첫 씬으로 둔다. 테스트 씬은 빌드에 넣지 않는다.</summary>
    static void BuildMenuScene() {
      var path = $"{ScenesRoot}/{FactoryWars.View.MenuController.SceneName}.unity";
      var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
      var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
      camera.tag = "MainCamera";
      camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
      camera.GetComponent<Camera>().backgroundColor = new Color(0.1f, 0.13f, 0.17f);
      new GameObject("Menu").AddComponent<FactoryWars.View.MenuController>();
      EditorSceneManager.SaveScene(scene, path);
      EditorBuildSettings.scenes = new[] { path, $"{ScenesRoot}/{FourPlayerMap.Id}.unity" }
        .Select(scenePath => new EditorBuildSettingsScene(scenePath, true)).ToArray();
    }

    static GameObject EnsureGround() {
      var ground = GameObject.Find("Ground");
      if (ground == null) {
        ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
      }
      return ground;
    }

    static void EnsureNavMesh(QuantumMapData map) {
      var navObject = GameObject.Find("NavMesh");
      if (navObject == null) {
        navObject = new GameObject("NavMesh");
        navObject.transform.SetParent(map.transform, false);
        navObject.AddComponent<NavMeshSurface>();
        navObject.AddComponent<QuantumMapNavMeshUnity>().NavMeshSurfaces = new[] { navObject };
      }
      // QuantumNavMeshRegion을 쓰려면 Advanced 모드여야 한다. Simple은 Unity 영역 이름으로 만든다.
      var quantumNavMesh = navObject.GetComponent<QuantumMapNavMeshUnity>();
      var settings = quantumNavMesh.Settings;
      settings.ImportRegionMode = NavmeshRegionImportMode.Advanced;
      settings.RegionAreaIds = new List<int> { BarrierArea, BarricadeArea };
      quantumNavMesh.Settings = settings;
    }

    static void EnsureDebugView() {
      var old = GameObject.Find("FactoryWarsDebugView");
      if (old != null) {
        Object.DestroyImmediate(old);
      }
      var view = Object.FindFirstObjectByType<FactoryWars.View.BattleView>();
      if (view == null) {
        view = new GameObject("BattleView").AddComponent<FactoryWars.View.BattleView>();
      }
      if (view.GetComponent<FactoryWars.View.MatchController>() == null) {
        view.gameObject.AddComponent<FactoryWars.View.MatchController>();
      }
      if (view.GetComponent<FactoryWars.View.MatchFlow>() == null) {
        view.gameObject.AddComponent<FactoryWars.View.MatchFlow>();
      }
    }

    static void ConfigureRunner(int players) {
      var rules = AssetDatabase.LoadAssetAtPath<GameRulesSpec>($"{DataRoot}/game_rules.asset");
      var factions = new[] {
        AssetDatabase.LoadAssetAtPath<FactionSpec>($"{DataRoot}/faction_free_federation.asset"),
        AssetDatabase.LoadAssetAtPath<FactionSpec>($"{DataRoot}/faction_continental_federation.asset"),
      };
      var names = new[] { "Free", "Continental" };

      var runner = Object.FindFirstObjectByType<QuantumRunnerLocalDebug>();
      runner.RuntimeConfig.Rules = Ref(rules);
      runner.RuntimeConfig.JointVictory = true;
      runner.MaxPlayerCount = players;
      runner.LocalPlayers = Enumerable.Range(0, players)
        .Select(i => new RuntimePlayer { PlayerNickname = $"{names[i % 2]}{i}", Faction = Ref(factions[i % 2]), Team = i, ColorIndex = i })
        .ToArray();
      EditorUtility.SetDirty(runner);
    }

    static void Bake(QuantumMapData map, MapSpec spec, UnityEngine.SceneManagement.Scene scene) {
      var mapAsset = map.GetAsset(true);
      mapAsset.UserAsset = new AssetRef(Ref(spec).Id);
      EditorUtility.SetDirty(mapAsset);
      EditorSceneManager.MarkSceneDirty(scene);
      EditorSceneManager.SaveScene(scene);

      // 첫 베이크에서 만들어진 NavMesh 에셋은 GUID가 아직 없어 맵 링크가 0이 된다. 두 번째 베이크가 연결한다.
      for (var pass = 0; pass < 2; pass++) {
        QuantumEditorAutoBaker.BakeMap(map, QuantumMapDataBakeFlags.BakeMapData | QuantumMapDataBakeFlags.GenerateAssetDB
          | QuantumMapDataBakeFlags.BakeUnityNavMesh | QuantumMapDataBakeFlags.ImportUnityNavMesh | QuantumMapDataBakeFlags.BakeNavMesh);
        AssetDatabase.SaveAssets();
        QuantumUnityDBUtilities.RefreshGlobalDB(true);
      }
      EditorUtility.SetDirty(map);
      EditorSceneManager.MarkSceneDirty(scene);
      EditorSceneManager.SaveScene(scene);
      QuantumUnityDBUtilities.RefreshGlobalDB(true);
    }
  }
}
