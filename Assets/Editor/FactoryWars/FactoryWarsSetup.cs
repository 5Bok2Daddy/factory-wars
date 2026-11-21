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
    const string BridgeRegion = "Bridge";
    const string BarricadeRegionPrefix = "Barricade";
    /// <summary>다리에 칠하는 Unity NavMesh 영역 번호. 0~2는 기본 영역이다.</summary>
    const int BarrierArea = 3;
    const int BarricadeArea = 4;
    /// <summary>수상 NavMesh에서 수문 영역 번호(항목 24). 물 판은 Unity 레이어 4(Water)에 두어 지상 NavMesh가 모으지 않게 한다.</summary>
    const int WaterGateArea = 5;
    const int WaterLayer = 4;
    const string WaterGateRegion = "WaterGate";
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
      BuildMapScene(DesertMap);
      BuildMapScene(UrbanMap);
      BuildMapScene(ArchipelagoMap);
      BuildMapScene(StraitMap);
      BuildMenuScene();
      IconBaker.BakeAll();
      ArtBaker.BakeAll();
      AssetDatabase.SaveAssets();
      Debug.Log("FactoryWarsSetup: 완료");
    }

    public static void BuildMapsAndMenu() {
      BuildData();
      BuildTestScene();
      BuildMapScene(FourPlayerMap);
      BuildMapScene(DesertMap);
      BuildMapScene(UrbanMap);
      BuildMapScene(ArchipelagoMap);
      BuildMapScene(StraitMap);
      BuildMenuScene();
      AssetDatabase.SaveAssets();
      Debug.Log("FactoryWarsSetup.BuildMapsAndMenu: 완료");
    }

    [MenuItem("Tools/Factory Wars/Bake Standardized Maps")]
    public static void BakeStandardizedMaps() {
      QuantumRunner.Init(false);
      foreach (var definition in new[] { FourPlayerMap, DesertMap, UrbanMap, ArchipelagoMap, StraitMap }) {
        Debug.Log($"Factory Wars building {definition.Id}");
        BuildMapSpec(definition);
        BuildMapScene(definition);
      }
      AssetDatabase.SaveAssets();
      Debug.Log("Factory Wars baked all five land and water maps");
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

    /// <summary>배 에이전트 설정: 기본 에이전트 값을 그대로 복사하고, 물 밖 목표를 40m 안의 물 위로 보정한다(항목 24).</summary>
    static NavMeshAgentConfig NavalAgent() {
      var agent = Asset<NavMeshAgentConfig>("naval_agent");
      var source = AssetDatabase.LoadAllAssetsAtPath("Assets/QuantumUser/Resources/QuantumDefaultConfigs.asset").OfType<NavMeshAgentConfig>().FirstOrDefault();
      if (source != null) {
        foreach (var field in typeof(NavMeshAgentConfig).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) {
          if (field.Name != nameof(AssetObject.Identifier)) {
            field.SetValue(agent, field.GetValue(source));
          }
        }
      }
      agent.AutomaticTargetCorrection = true;
      agent.AutomaticTargetCorrectionRadius = N(40);
      agent.AvoidanceRadius = N(1.4);
      EditorUtility.SetDirty(agent);
      return agent;
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
      public bool Naval;
      public bool Vehicle;
      public ResourceCost Cost;
      public int Build, Health;
      public AttackType Attack;
      public double Damage, Range, MinRange, Splash, Interval, Speed;
      public int Spawn;
      public SpecialAbility Ability;
      public double AbilityMultiplier, AbilitySeconds, AbilityRadius, AbilityCooldown;
      public int AbilityCount;
      public int Population;
      public int TransportCapacity;
      public bool TransportAllowsVehicles;
      public double TransportEjectHealthRatio;
      /// <summary>생산 건물 id 뒷부분(barracks, vehicle_factory, airfield, heavy_works, shipyard, headquarters). 항목 31.</summary>
      public string At;
    }

    // 비용은 DESIGN-RESOURCE-TERRAIN.md 5장. 생산·체력·공격 유형은 GDD. 피해·사거리·간격·속도는 임시값(SETUP-REPORT.md 참조).
    static readonly UnitRow[] Units = {
      new UnitRow { Id = "ff_engineer", At = "headquarters", Size = UnitSize.Engineer, Cost = R(100), Build = 10, Health = 80, Attack = AttackType.None, Speed = 4, Ability = SpecialAbility.EngineerWork },
      new UnitRow { Id = "ff_ranger_squad", At = "barracks", Size = UnitSize.Small, Cost = R(28, 20), Build = 6, Health = 54, Attack = AttackType.AntiInfantry, Damage = 5.4, Range = 7, Interval = 1, Speed = 4, Ability = SpecialAbility.TargetDesignator, AbilityMultiplier = 1.25, AbilitySeconds = 5 },
      // 항목 34-C 주둔 저격수(진영별 수치가 약간 다름). 2단계, 보유 3, 인구 2. 사격·은신·교란 값은 아래 SniperTable.
      new UnitRow { Id = "ff_sniper", At = "barracks", Size = UnitSize.Small, Cost = R(120, 60, 40), Build = 20, Health = 60, Attack = AttackType.AntiInfantry, Damage = 140, Range = 24, Interval = 6, Speed = 2.4, Population = 2 },
      new UnitRow { Id = "ff_skyguard_squad", At = "barracks", Size = UnitSize.Small, Cost = R(28, 18, 0, 8), Build = 7, Health = 45, Attack = AttackType.AntiAir, Damage = 7, Range = 8, Interval = 1, Speed = 4, Ability = SpecialAbility.AirPriority },
      // 신규 공격 유닛 4종(자유 연방): 대전차 로켓병(T1.5), 화염 돌격병(T1.5), 다연장 로켓차(T2), 기동 대공차(T2).
      new UnitRow { Id = "ff_at_rocket", At = "barracks", Size = UnitSize.Small, Cost = R(65, 25, 15, 10), Build = 11, Health = 62, Attack = AttackType.AntiArmor, Damage = 26, Range = 8.5, Interval = 2.8, Speed = 3.6, Ability = SpecialAbility.AntiArmorBonus, AbilityMultiplier = 1.6 },
      new UnitRow { Id = "ff_breacher", At = "barracks", Size = UnitSize.Small, Cost = R(55, 25, 15, 0), Build = 10, Health = 78, Attack = AttackType.AntiInfantry, Damage = 14, Range = 4, Splash = 2.4, Interval = 1.2, Speed = 4.2, Ability = SpecialAbility.GarrisonBreach, AbilityMultiplier = 1.5 },
      // 항목 31 새 소형: 군수공장의 빠른 정찰차.
      new UnitRow { Id = "ff_scout_buggy", At = "vehicle_factory", Vehicle = true, Size = UnitSize.Small, Cost = R(90, 30, 0, 10), Build = 14, Health = 160, Attack = AttackType.AntiInfantry, Damage = 12, Range = 7, Interval = 1, Speed = 7.5 },
      new UnitRow { Id = "ff_eagle_tank", At = "vehicle_factory", Size = UnitSize.Medium, Cost = R(220, 0, 68, 60), Build = 31, Health = 720, Attack = AttackType.AntiArmor, Damage = 58, Range = 9, Interval = 2, Speed = 5, Ability = SpecialAbility.FirstShotDouble, AbilityMultiplier = 2, AbilitySeconds = 8 },
      // 항목 31 새 중형: 주변 아군 차량을 고치는 야전 정비차 + 보병 6기 수송 장갑차(APC).
      new UnitRow { Id = "ff_field_support", At = "vehicle_factory", Size = UnitSize.Medium, Cost = R(200, 50, 60, 40), Build = 30, Health = 500, Attack = AttackType.AntiInfantry, Damage = 8, Range = 6, Interval = 1, Speed = 5, Population = 2, Ability = SpecialAbility.FieldRepair, AbilityMultiplier = 15, AbilityRadius = 7, AbilityCount = 3, TransportCapacity = 6, TransportEjectHealthRatio = 0.4 },
      new UnitRow { Id = "ff_mlrs_artillery", At = "vehicle_factory", Vehicle = true, Size = UnitSize.Medium, Cost = R(220, 0, 85, 50), Build = 30, Health = 340, Attack = AttackType.Area, Damage = 24, Range = 16, MinRange = 5, Splash = 2.6, Interval = 4.8, Speed = 4.2, Population = 3, Ability = SpecialAbility.RocketSalvo, AbilityCount = 4 },
      new UnitRow { Id = "ff_mobile_sam", At = "vehicle_factory", Vehicle = true, Size = UnitSize.Medium, Cost = R(190, 0, 70, 55), Build = 26, Health = 400, Attack = AttackType.AntiAir, Damage = 38, Range = 13, Interval = 1.6, Speed = 6.5, Population = 2, Ability = SpecialAbility.CarrierHunter, AbilityMultiplier = 1.5 },
      new UnitRow { Id = "ff_hawk_gunship", At = "airfield", Size = UnitSize.Medium, Air = true, Cost = R(250, 0, 100, 100), Build = 40, Health = 450, Attack = AttackType.AntiArmor, Damage = 55, Range = 8, Interval = 2, Speed = 7, Ability = SpecialAbility.GroundArmorResist, AbilityMultiplier = 0.5 },
      new UnitRow { Id = "ff_transport_heli", At = "airfield", Size = UnitSize.Medium, Air = true, Cost = R(210, 0, 75, 70), Build = 28, Health = 420, Attack = AttackType.None, Speed = 7.8, Population = 2, TransportCapacity = 4, TransportEjectHealthRatio = 0 },
      new UnitRow { Id = "ff_skyfortress_bomber", At = "airfield", Size = UnitSize.Large, Air = true, Cost = R(500, 0, 250, 350), Build = 70, Health = 1600, Attack = AttackType.Area, Damage = 200, Range = 4, Splash = 4, Interval = 5, Speed = 5, Ability = SpecialAbility.CarpetBomb, AbilityMultiplier = 1.5, AbilityRadius = 8, AbilityCount = 5, AbilityCooldown = 45 },
      // 항목 31 새 대형: 중공업 기지의 중전차(B1: 자유 연방 중공업 기지가 만들 것이 없던 문제).
      new UnitRow { Id = "ff_bulwark_tank", At = "heavy_works", Size = UnitSize.Large, Cost = R(400, 0, 170, 125), Build = 50, Health = 2000, Attack = AttackType.AntiArmor, Damage = 125, Range = 9, Interval = 2.5, Speed = 3.5 },
      new UnitRow { Id = "cf_engineer", At = "headquarters", Size = UnitSize.Engineer, Cost = R(80), Build = 8, Health = 70, Attack = AttackType.None, Speed = 4, Ability = SpecialAbility.EngineerWork },
      // 항목 30 밸런스(AI 40판: 자유 연방 75~85%): 용린 전차 체력·사거리 보강. 항목 31: 테크 단계로 자유 연방 전차가 2단계로 늦어져
      // 대륙 연방이 94%를 이겨(ai-31-2p-v1) 민병대 90→80·사거리 7→6, 화염 145→130으로 되돌림. v4(대륙 80%)에서 민병대 비용 철 40→50·목재 50→55, v5(자유 67%)에서 철 45·목재 50으로 중간값. 항목 32(엄폐 후 2인 자유 67%·4인 79%, 대륙 중형 생산 적음): 용린 전차 철 190→175·강철 50→45.
      // 항목 33(4인 쏠림 원인 규명, Game-logs/probe/matchup.csv): 같은 값의 군대끼리 들판에서 붙이면 자유 연방이 60~70%를 남기고 이겼다(레인저 20 대 민병대 50).
      // 1차(ai-33-*-t1): 민병대 사거리 6→7·공격 간격 1→0.9, 용린 전차 체력 570→620(같은 값 교전 2승 2패) → 2인 대륙 79%, 4인 자유 71%.
      // 2차: 2인이 대륙으로 넘어가 간격 0.9→1, 용린 체력 620→570으로 되돌리고 사거리 7만 남긴다(같은 값 교전에서 자유 연방이 30~40% 남기고 이김).
      // 3차(ai-33-*-t2: 2인 대륙 81%, 4인 자유 64%): 사거리 7→6.5.
      new UnitRow { Id = "cf_militia", At = "barracks", Size = UnitSize.Small, Cost = R(44, 48), Build = 12, Health = 49, Attack = AttackType.AntiInfantry, Damage = 5.3, Range = 6.5, Interval = 1, Speed = 4, Spawn = 2, Ability = SpecialAbility.MilitiaPair },
      new UnitRow { Id = "cf_sniper", At = "barracks", Size = UnitSize.Small, Cost = R(100, 60, 30), Build = 18, Health = 70, Attack = AttackType.AntiInfantry, Damage = 140, Range = 22, Interval = 5.5, Speed = 2.4, Population = 2 },
      new UnitRow { Id = "cf_flame_squad", At = "barracks", Size = UnitSize.Small, Cost = R(35, 30), Build = 8, Health = 65, Attack = AttackType.AntiInfantry, Damage = 10, Range = 3, Splash = 2, Interval = 1, Speed = 4, Ability = SpecialAbility.FlameBurst, AbilityMultiplier = 3 },
      // 신규 공격 유닛 4종(대륙 연방): 대전차 로켓병(T1.5), 화염 돌격병(T1.5), 화력지원 로켓차(T2), 기동 대공미사일차(T2).
      new UnitRow { Id = "cf_at_rocket", At = "barracks", Size = UnitSize.Small, Cost = R(60, 25, 15, 10), Build = 10, Health = 66, Attack = AttackType.AntiArmor, Damage = 25, Range = 8, Interval = 2.8, Speed = 3.6, Ability = SpecialAbility.AntiArmorBonus, AbilityMultiplier = 1.6 },
      new UnitRow { Id = "cf_breacher", At = "barracks", Size = UnitSize.Small, Cost = R(50, 25, 15, 0), Build = 9, Health = 82, Attack = AttackType.AntiInfantry, Damage = 14, Range = 4, Splash = 2.4, Interval = 1.2, Speed = 4.2, Ability = SpecialAbility.GarrisonBreach, AbilityMultiplier = 1.5 },
      // 항목 31 새 소형: 군수공장의 싸고 빠른 척후 오토바이.
      new UnitRow { Id = "cf_scout_bike", At = "vehicle_factory", Vehicle = true, Size = UnitSize.Small, Cost = R(60, 30), Build = 10, Health = 110, Attack = AttackType.AntiInfantry, Damage = 10, Range = 6, Interval = 0.8, Speed = 8 },
      new UnitRow { Id = "cf_dragonscale_tank", At = "vehicle_factory", Size = UnitSize.Medium, Cost = R(175, 0, 45, 60), Build = 28, Health = 575, Attack = AttackType.AntiArmor, Damage = 50, Range = 9, Interval = 2, Speed = 5, Ability = SpecialAbility.SwarmSpeed, AbilityMultiplier = 1.15, AbilityCount = 5 },
      new UnitRow { Id = "cf_thunder_aa", At = "vehicle_factory", Size = UnitSize.Medium, Cost = R(170, 0, 50, 60), Build = 28, Health = 450, Attack = AttackType.AntiAir, Damage = 40, Range = 10, Interval = 1.5, Speed = 5, Ability = SpecialAbility.MultiAirTarget, AbilityCount = 3 },
      new UnitRow { Id = "cf_rocket_artillery", At = "vehicle_factory", Vehicle = true, Size = UnitSize.Medium, Cost = R(205, 0, 80, 45), Build = 28, Health = 360, Attack = AttackType.Area, Damage = 23, Range = 16, MinRange = 5, Splash = 2.6, Interval = 4.8, Speed = 4.2, Population = 3, Ability = SpecialAbility.RocketSalvo, AbilityCount = 4 },
      new UnitRow { Id = "cf_mobile_sam", At = "vehicle_factory", Vehicle = true, Size = UnitSize.Medium, Cost = R(180, 0, 65, 50), Build = 25, Health = 420, Attack = AttackType.AntiAir, Damage = 37, Range = 13, Interval = 1.6, Speed = 6.5, Population = 2, Ability = SpecialAbility.CarrierHunter, AbilityMultiplier = 1.5 },
      new UnitRow { Id = "cf_apc_transport", At = "vehicle_factory", Vehicle = true, Size = UnitSize.Medium, Cost = R(185, 45, 55, 35), Build = 26, Health = 520, Attack = AttackType.AntiInfantry, Damage = 10, Range = 6, Interval = 1, Speed = 5.2, Population = 2, TransportCapacity = 6, TransportEjectHealthRatio = 0.4 },
      // 항목 31 새 중형 공중: 비행장의 공격헬기(B2: 대륙 연방 비행장이 만들 것이 없던 문제).
      new UnitRow { Id = "cf_gale_gunship", At = "airfield", Size = UnitSize.Medium, Air = true, Cost = R(200, 0, 75, 75), Build = 30, Health = 480, Attack = AttackType.AntiArmor, Damage = 56, Range = 8, Interval = 2, Speed = 7, Ability = SpecialAbility.GroundArmorResist, AbilityMultiplier = 0.5 },
      new UnitRow { Id = "cf_transport_heli", At = "airfield", Size = UnitSize.Medium, Air = true, Cost = R(210, 0, 75, 70), Build = 28, Health = 420, Attack = AttackType.None, Speed = 7.8, Population = 2, TransportCapacity = 4, TransportEjectHealthRatio = 0 },
      new UnitRow { Id = "cf_longwall_mlrs", At = "heavy_works", Size = UnitSize.Large, Cost = R(360, 0, 170, 170), Build = 48, Health = 1100, Attack = AttackType.Area, Damage = 195, Range = 18, MinRange = 6, Splash = 4, Interval = 4.5, Speed = 3, Ability = SpecialAbility.LongRangeBarrage },
      // 항목 31 새 대형: 중공업 기지의 요새 전차(물량형이라 자유 연방 중전차보다 싸고 약하다).
      new UnitRow { Id = "cf_fortress_tank", At = "heavy_works", Size = UnitSize.Large, Cost = R(340, 0, 140, 100), Build = 42, Health = 2000, Attack = AttackType.AntiArmor, Damage = 128, Range = 9, Interval = 2.5, Speed = 3.5, Population = 5 },
      new UnitRow { Id = "ff_scout_boat", At = "shipyard", Size = UnitSize.Small, Naval = true, Cost = R(120, 50, 0, 20), Build = 16, Health = 180, Attack = AttackType.AntiInfantry, Damage = 20, Range = 6, Interval = 1, Speed = 8, Population = 2 },
      new UnitRow { Id = "ff_artillery_ship", At = "shipyard", Size = UnitSize.Medium, Naval = true, Cost = R(350, 0, 120, 100), Build = 35, Health = 600, Attack = AttackType.Area, Damage = 90, Range = 24, MinRange = 8, Splash = 4, Interval = 4, Speed = 3, Population = 4 },
      new UnitRow { Id = "ff_landing_craft", At = "shipyard", Size = UnitSize.Medium, Naval = true, Cost = R(200, 60, 60, 40), Build = 24, Health = 550, Attack = AttackType.AntiInfantry, Damage = 12, Range = 7, Interval = 1.2, Speed = 6.5, Population = 2, TransportCapacity = 6, TransportAllowsVehicles = true, TransportEjectHealthRatio = 0 },
      // 항목 34-D 작전기(비조작, 비행장 격납 칸). 전투기=기총소사(2단계), 공격기=폭격(3단계), 수송기=공수 투하(2단계). 수치는 ApplySortie 표.
      new UnitRow { Id = "ff_sortie_fighter", At = "airfield", Size = UnitSize.Medium, Air = true, Cost = R(180, 0, 60, 80), Build = 24, Health = 300, Attack = AttackType.AntiInfantry, Damage = 8, Range = 0, Interval = 1, Speed = 0 },
      new UnitRow { Id = "ff_sortie_striker", At = "airfield", Size = UnitSize.Large, Air = true, Cost = R(300, 0, 140, 160), Build = 40, Health = 450, Attack = AttackType.Area, Damage = 160, Range = 0, Interval = 1, Speed = 0 },
      new UnitRow { Id = "ff_sortie_transport", At = "airfield", Size = UnitSize.Medium, Air = true, Cost = R(200, 0, 60, 80), Build = 30, Health = 350, Attack = AttackType.None, Speed = 0 },
      new UnitRow { Id = "cf_sortie_fighter", At = "airfield", Size = UnitSize.Medium, Air = true, Cost = R(150, 0, 50, 70), Build = 22, Health = 260, Attack = AttackType.AntiInfantry, Damage = 7, Range = 0, Interval = 1, Speed = 0 },
      new UnitRow { Id = "cf_sortie_striker", At = "airfield", Size = UnitSize.Large, Air = true, Cost = R(260, 0, 120, 140), Build = 36, Health = 400, Attack = AttackType.Area, Damage = 140, Range = 0, Interval = 1, Speed = 0 },
      new UnitRow { Id = "cf_sortie_transport", At = "airfield", Size = UnitSize.Medium, Air = true, Cost = R(170, 0, 50, 70), Build = 28, Health = 320, Attack = AttackType.None, Speed = 0 },
      new UnitRow { Id = "cf_scout_boat", At = "shipyard", Size = UnitSize.Small, Naval = true, Cost = R(120, 50, 0, 20), Build = 16, Health = 180, Attack = AttackType.AntiInfantry, Damage = 20, Range = 6, Interval = 1, Speed = 8, Population = 2 },
      new UnitRow { Id = "cf_artillery_ship", At = "shipyard", Size = UnitSize.Medium, Naval = true, Cost = R(350, 0, 120, 100), Build = 35, Health = 600, Attack = AttackType.Area, Damage = 90, Range = 24, MinRange = 8, Splash = 4, Interval = 4, Speed = 3, Population = 4 },
      new UnitRow { Id = "cf_landing_craft", At = "shipyard", Size = UnitSize.Medium, Naval = true, Cost = R(200, 60, 60, 40), Build = 24, Health = 550, Attack = AttackType.AntiInfantry, Damage = 12, Range = 7, Interval = 1.2, Speed = 6.5, Population = 2, TransportCapacity = 6, TransportAllowsVehicles = true, TransportEjectHealthRatio = 0 },
    };

    // 항목 33 궁극 전력 유닛(가칭, 실존 함정·항공기 이름과 무관). 진영 유닛 목록(FactionSpec.Units)에는 넣지 않고 UltimateSpec에서만 가리킨다.
    // 비용 칸은 AI의 가치 계산용이다(실제 비용은 UltimateSpec). 인구는 BuildUltimates에서 따로 정한다.
    static readonly UnitRow[] UltimateUnits = {
      // 공중 항모 "스톰네스트": 철 2800·강철 2200, 80초 지상 조립. 선체 직사 기관포 + 6기 드론 윙 + 광역 정찰·은신 탐지(26m) + 지휘 오라(14m).
      new UnitRow { Id = "ff_stormnest_carrier", Size = UnitSize.Large, Air = true, Cost = R(2800, 0, 2200, 500), Build = 80, Health = 5200, Attack = AttackType.Area, Damage = 75, Range = 14, Splash = 3, Interval = 2, Speed = 2.8 },
      // 함재 드론 "둥지 드론": 소형 공중, 대전차 경무장(6기 정예 윙). 항모 반경 안에서만 싸운다.
      new UnitRow { Id = "ff_nest_drone", Size = UnitSize.Small, Air = true, Cost = R(60, 0, 20, 20), Build = 12, Health = 180, Attack = AttackType.AntiArmor, Damage = 18, Range = 6, Interval = 1, Speed = 9 },
      // 상륙정 "파도머리": 탑재 부대를 해안에 내리고 포격 지원한다(차기 진영용, 시험 전용 진영에만 연결).
      new UnitRow { Id = "tf_wavecrest_lander", Size = UnitSize.Medium, Naval = true, Cost = R(300, 0, 150, 100), Build = 30, Health = 900, Attack = AttackType.Area, Damage = 50, Range = 14, Splash = 3, Interval = 4, Speed = 5 },
    };

    static UnitSpec BuildUnit(UnitRow row) {
      var spec = Asset<UnitSpec>(row.Id);
      spec.Size = row.Size;
      // 항목 31: 소형 1단계, 중형 2단계, 대형 3단계(공병은 1).
      spec.RequiredTier = row.Size == UnitSize.Large ? 3 : row.Size == UnitSize.Medium ? 2 : 1;
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
      spec.ColliderRadius = N(row.Air ? (row.Size == UnitSize.Large ? 3.2 : row.Size == UnitSize.Medium ? 1.8 : 1.2)
        : row.Naval ? (row.Size == UnitSize.Small ? 2.0 : 3.0)
        : row.Size == UnitSize.Large ? 2.6
        : row.Size == UnitSize.Medium ? 2.2
        : row.Vehicle ? 1.4
        : 0.65);
      spec.SpawnCount = row.Spawn > 0 ? row.Spawn : 1;
      spec.IsNaval = row.Naval;
      spec.IsVehicle = row.Vehicle;
      // 항목 34 시야 반경(m): 보병 10, 차량·중형 9, 정찰 버기·오토바이 16, 정찰선 18, 공중 14. 값은 이 표가 원본이다.
      spec.SightRange = N(row.Id.Contains("scout_boat") ? 18 : row.Id.Contains("scout") ? 16 : row.Air ? 14 : row.Naval ? 12 : row.Vehicle || row.Size != UnitSize.Small ? 9 : 10);
      var population = row.Population > 0 ? row.Population : (row.Size == UnitSize.Large ? 6 : row.Size == UnitSize.Medium ? 3 : 1);
      spec.Population = N((double)population / spec.SpawnCount);
      spec.ModuleSlots = row.Size == UnitSize.Large ? 3 : row.Size == UnitSize.Medium ? 2 : row.Size == UnitSize.Small ? 1 : 0;
      spec.Ability = row.Ability;
      spec.AbilityMultiplier = row.AbilityMultiplier > 0 ? N(row.AbilityMultiplier) : 1;
      spec.AbilitySeconds = N(row.AbilitySeconds);
      spec.AbilityRadius = N(row.AbilityRadius);
      spec.AbilityCount = row.AbilityCount;
      spec.AbilityCooldownSeconds = N(row.AbilityCooldown);
      spec.TransportCapacity = row.TransportCapacity;
      spec.TransportAllowsVehicles = row.TransportAllowsVehicles;
      spec.TransportEjectHealthRatio = N(row.TransportEjectHealthRatio);
      ApplySniper(spec, row);
      ApplySortie(spec, row);
      EditorUtility.SetDirty(spec);
      return spec;
    }

    /// <summary>항목 34-C 주둔 저격수 값(전부 데이터, 설계: NEXT-TASK-34 C절). 은신 20초·대기 60초, 이동 -30%, 4m 접근 노출, 교란 장치 10m·설치 4초·60초·체력 50·채집 -40%.</summary>
    static void ApplySniper(UnitSpec spec, UnitRow row) {
      spec.GarrisonFireOnly = false;
      spec.GarrisonSightRange = 0;
      spec.SetupSeconds = 0;
      spec.VehicleDamageRatio = 1;
      spec.MaxOwned = 0;
      spec.StealthSeconds = 0;
      spec.StealthCooldownSeconds = 0;
      spec.StealthSpeedMultiplier = 1;
      spec.StealthExposeRadius = 0;
      spec.JammerRange = 0;
      spec.JammerSetupSeconds = 0;
      spec.JammerSeconds = 0;
      spec.JammerHealth = 0;
      spec.JammerGatherMultiplier = 1;
      // 정찰 버기·척후 오토바이는 10m 탐지, 공중 항모(ff_stormnest_carrier)는 26m 광역 정찰 및 주둔 저격수 은신 탐지.
      spec.DetectRadius = row.Id == "ff_stormnest_carrier" ? N(26) : (row.Id.EndsWith("scout_buggy") || row.Id.EndsWith("scout_bike")) ? N(10) : 0;
      if (row.Id == "ff_stormnest_carrier") {
        spec.SightRange = N(26);
      }
      if (row.Id.EndsWith("_sniper") == false) {
        return;
      }
      spec.GarrisonFireOnly = true;
      spec.RequiredTier = 2;
      spec.SightRange = N(10);
      spec.GarrisonSightRange = N(26);
      spec.SetupSeconds = N(5);
      spec.VehicleDamageRatio = N(30.0 / 140.0);
      spec.MaxOwned = 3;
      spec.StealthSeconds = N(20);
      spec.StealthCooldownSeconds = N(60);
      spec.StealthSpeedMultiplier = N(0.7);
      spec.StealthExposeRadius = N(4);
      spec.JammerRange = N(10);
      spec.JammerSetupSeconds = N(4);
      spec.JammerSeconds = N(60);
      spec.JammerHealth = N(50);
      spec.JammerGatherMultiplier = N(0.6);
    }

    /// <summary>항목 34-D 작전기 값(설계: NEXT-TASK-34 D절). 진영별로 조금 다르다. 속도는 m/s, 재정비는 초.</summary>
    static void ApplySortie(UnitSpec spec, UnitRow row) {
      spec.SortieKind = SortieKind.None;
      if (row.Id.Contains("_sortie_") == false) {
        return;
      }
      var free = row.Id.StartsWith("ff_");
      spec.Population = 0;
      spec.IsAir = true;
      spec.SpawnCount = 1;
      spec.SortieHealth = N(row.Health);
      spec.SortieCargo = default;
      spec.SortieCargoCount = 0;
      spec.SortieBombs = 0;
      spec.SortieTicks = 0;
      spec.SortieBombGap = 0;
      spec.SortieBuildingMultiplier = 1;
      if (row.Id.EndsWith("fighter")) {
        spec.SortieKind = SortieKind.Strafe;
        spec.SortieSpeed = N(free ? 26 : 24);
        spec.SortieRearmSeconds = 45;
        spec.SortieApproach = 24;
        spec.SortieExit = 24;
        spec.SortieTurn = 14;
        spec.SortieWidth = 5;
        spec.SortieTicks = 2;
        spec.SortieCost = R(30, 0, 10);
      } else if (row.Id.EndsWith("striker")) {
        spec.SortieKind = SortieKind.Bomb;
        spec.SortieSpeed = N(free ? 22 : 20);
        spec.SortieRearmSeconds = 90;
        spec.SortieApproach = 30;
        spec.SortieExit = 30;
        spec.SortieTurn = 18;
        spec.SortieWidth = 6;
        spec.SortieBombs = 4;
        spec.SortieBombGap = 6;
        spec.SortieBuildingMultiplier = 2;
        spec.SortieCost = R(60, 0, 40);
      } else {
        spec.SortieKind = SortieKind.Drop;
        spec.SortieSpeed = N(free ? 20 : 18);
        spec.SortieRearmSeconds = 120;
        spec.SortieApproach = 30;
        spec.SortieExit = 20;
        spec.SortieTurn = 16;
        spec.SortieWidth = 6;
        spec.SortieCargoCount = 6;
        spec.SortieCost = R(40, 0, 20);
      }
    }

    static BuildingSpec BuildBuilding(string id, ResourceCost cost, int seconds, int health, int population, int slots, params UnitSpec[] producible) {
      var spec = Asset<BuildingSpec>(id);
      spec.Cost = cost;
      spec.BuildTimeSeconds = seconds;
      spec.MaxHealth = health;
      spec.PopulationCapBonus = population;
      spec.ModuleSlots = slots;
      var isGrand = id.EndsWith("_headquarters") || id.EndsWith("_airfield") || id.EndsWith("_vehicle_factory")
        || id.EndsWith("_heavy_works") || id.EndsWith("_carrier_yard") || id.EndsWith("_missile_silo") || id.EndsWith("_assault_dock");
      spec.FootprintRadius = isGrand ? N(3.8) : N(3.5);
      // 항목 34: 건물 시야 8m, 레이더 기지 40m.
      spec.SightRange = id.EndsWith("_radar") ? 40 : 8;
      spec.IsDropOff = id.EndsWith("_headquarters") || id.EndsWith("_collection_yard");
      spec.Producible = producible.Select(Ref).ToArray();
      spec.Attack = AttackType.None;
      EditorUtility.SetDirty(spec);
      return spec;
    }

    static FactionSpec BuildFaction(string factionId, string prefix, Dictionary<string, UnitSpec> units, double ranged, double smallBuild, double populationCap, string[] startingUnits, AssetRef<ModuleSpec>[] modules,
        AssetRef<ResearchSpec>[] researches, string siteId, int[] sitePrerequisites) {
      UnitSpec U(string id) => units[$"{prefix}_{id}"];
      var ids = units.Keys.Where(k => k.StartsWith(prefix + "_")).ToArray();
      var engineer = units[$"{prefix}_engineer"];
      // 항목 31: 생산 건물은 유닛 표의 At으로 정한다(표 순서 = 생산 패널 순서).
      UnitSpec[] At(string building) => Units.Where(row => row.Id.StartsWith(prefix + "_") && row.At == building).Select(row => units[row.Id]).ToArray();

      var buildings = new[] {
        BuildBuilding($"{prefix}_headquarters", R(400, 300), 60, 3000, 10, 0, At("headquarters")),
        BuildBuilding($"{prefix}_power_plant", R(150, 150), 15, 600, 0, 0),
        BuildBuilding($"{prefix}_processor", R(250, 150, 0, 100), 20, 800, 0, 0),
        BuildBuilding($"{prefix}_supply_depot", R(100, 200), 15, 500, 20, 0),
        BuildBuilding($"{prefix}_barracks", R(200, 200), 20, 800, 0, 0, At("barracks")),
        BuildBuilding($"{prefix}_research_lab", R(250, 200, 0, 150), 25, 700, 0, 0),
        BuildBuilding($"{prefix}_vehicle_factory", R(400, 200, 0, 200), 30, 1200, 0, 0, At("vehicle_factory")),
        BuildBuilding($"{prefix}_airfield", R(400, 0, 100, 300), 35, 1000, 0, 0, At("airfield")),
        // 항목 31: 3단계 조건이라 전기 400→250·강철 200→150(ai-31-2p-v3: 군수공장보다 5분 늦게 지어짐).
        BuildBuilding($"{prefix}_heavy_works", R(500, 0, 150, 250), 45, 1600, 0, 0, At("heavy_works")),
        BuildBuilding($"{prefix}_ground_tower", R(250, 100, 0, 100), 20, 700, 0, 1),
        BuildBuilding($"{prefix}_collection_yard", R(0, 150), 15, 500, 0, 0),
        BuildBuilding($"{prefix}_air_defense", R(200, 100, 30, 100), 20, 700, 0, 1),
        BuildBuilding($"{prefix}_shipyard", R(350, 150, 0, 150), 25, 1000, 0, 0, At("shipyard")),
        // 항목 32 새 건물(13~19): 모래주머니 진지, 벙커, 의무대, 정비소, 레이더 기지, 헬기장, 포병 진지.
        BuildBuilding($"{prefix}_sandbags", R(60, 40), 8, 400, 0, 0),
        BuildBuilding($"{prefix}_bunker", R(200, 50, 30), 20, 1400, 0, 0),
        BuildBuilding($"{prefix}_medic_tent", R(150, 100, 0, 50), 20, 600, 0, 0),
        BuildBuilding($"{prefix}_repair_bay", R(200, 50, 40, 60), 25, 900, 0, 0),
        BuildBuilding($"{prefix}_radar", R(200, 0, 60, 120), 25, 700, 0, 0),
        BuildBuilding($"{prefix}_helipad", R(250, 0, 60, 150), 25, 800, 0, 0, At("airfield").Where(u => u.IsAir && u.Size == UnitSize.Medium && u.name.Contains("_sortie_") == false).ToArray()),
        BuildBuilding($"{prefix}_artillery", R(350, 0, 150, 150), 35, 1200, 0, 0),
        // 항목 33 궁극 전력 전용 건물(20): 항모 조립창 / 미사일 격납고. 미사일 격납고는 75초 취약 건설(+30% 피해).
        BuildBuilding(siteId, R(600, 0, 300, 300), siteId.EndsWith("_missile_silo") ? 75 : 30, 2200, 0, 0),
        // 패트리어트 대탄도 요격 포대(21, T2.5): 비행 중인 전략 미사일을 자동 요격하고 공중 항모에 +50% 추가 대공 피해.
        BuildBuilding($"{prefix}_patriot_turret", R(280, 0, 120, 140), 28, 950, 0, 1),
      };
      // 항목 31 기지 단계: 군수공장·비행장·중공업 기지·방공포대·조선소는 2단계. 항목 32: 의무대·정비소·레이더·헬기장 2단계, 포병 진지 3단계. 패트리어트(21): 2단계 + 방공포대·레이더(T2.5).
      var tiers = new Dictionary<int, int> { { 6, 2 }, { 7, 2 }, { 8, 2 }, { 11, 2 }, { 12, 2 }, { 15, 2 }, { 16, 2 }, { 17, 2 }, { 18, 2 }, { 19, 3 }, { 20, 3 }, { 21, 2 } };
      for (var i = 0; i < buildings.Length; i++) {
        buildings[i].RequiredTier = tiers.TryGetValue(i, out var tier) ? tier : 1;
        buildings[i].Defensive = i == 9 || i == 11 || i == 13 || i == 14 || i == 21;
        buildings[i].MinRange = 0;
        buildings[i].SplashRadius = 0;
        buildings[i].CoverSpots = 0;
        buildings[i].CoverReduction = 0;
        buildings[i].Heals = HealTarget.None;
        buildings[i].HealRadius = 0;
        buildings[i].HealPerSecond = 0;
        buildings[i].HealCount = 0;
        buildings[i].RevealRadius = 0;
        buildings[i].NeedsWater = false;
        buildings[i].GarrisonCapacity = 0;
        buildings[i].RequiresNeutralOutpost = false;
        buildings[i].InterceptRange = 0;
        buildings[i].InterceptDamage = 0;
        buildings[i].InterceptInterval = 0;
      }
      // 전략 미사일 격납고는 점령한 중립 거점·암시장 반경 18m 이내에만 배치 가능.
      buildings[20].RequiresNeutralOutpost = siteId.EndsWith("_missile_silo");
      // 모래주머니: 작고 낮은 엄폐(25%) 4칸.
      buildings[13].FootprintRadius = N(2.5);
      buildings[13].CoverSpots = 4;
      buildings[13].CoverReduction = N(0.25);
      // 벙커(항목 33): 보병 분대 4개가 실제로 들어가 안에서 쏜다. 안에서는 맞지 않고 벙커가 대신 맞는다. 둘레 엄폐점은 없앴다(항목 32의 임시 구현).
      buildings[14].GarrisonCapacity = 4;
      buildings[15].Heals = HealTarget.Infantry;
      buildings[15].HealRadius = 10;
      buildings[15].HealPerSecond = 6;
      buildings[15].HealCount = 6;
      buildings[16].Heals = HealTarget.Vehicles;
      buildings[16].HealRadius = 10;
      buildings[16].HealPerSecond = 12;
      buildings[16].HealCount = 3;
      buildings[17].RevealRadius = 40;
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
      Requires(12, 6, 1);
      Requires(13, 0);
      Requires(14, 4);
      Requires(15, 4);
      Requires(16, 6);
      Requires(17, 1);
      Requires(18, 6, 1);
      Requires(19, 8);
      Requires(20, sitePrerequisites);
      Requires(21, 11, 17);
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
      Power(12, 0, 0, 1, UpkeepMode.WhileProducing);
      Power(15, 0, 0, 0.5, UpkeepMode.Always);
      Power(16, 0, 0, 1, UpkeepMode.Always);
      Power(17, 0, 0, 1, UpkeepMode.Always);
      Power(18, 0, 0, 1.5, UpkeepMode.WhileProducing);
      Power(19, 0, 0, 1, UpkeepMode.Always);
      Power(20, 0, 0, 4, UpkeepMode.WhileUltimate);
      Power(21, 0, 0, 1.5, UpkeepMode.Always);
      for (var i = 0; i < buildings.Length; i++) {
        buildings[i].Role = (BuildingRole)i;
      }
      foreach (var core in new[] { 0, 4, 6, 7, 8, 12, 18 }) {
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

      // 항목 32 포병 진지: 장거리 범위 포격, 최소 사거리 안은 못 쏜다. 지상만.
      var artillery = buildings[19];
      artillery.Attack = AttackType.Area;
      artillery.GroundOnly = true;
      artillery.Damage = 70;
      artillery.Range = 22;
      artillery.MinRange = 8;
      artillery.SplashRadius = 3;
      artillery.AttackInterval = 6;
      EditorUtility.SetDirty(artillery);
      // 벙커: 안쪽 기관총(소형 보병 상대).
      var bunker = buildings[14];
      bunker.Attack = AttackType.AntiInfantry;
      bunker.GroundOnly = true;
      bunker.Damage = 14;
      bunker.Range = 8;
      bunker.AttackInterval = 1;
      EditorUtility.SetDirty(bunker);

      // 패트리어트 대탄도 요격 포대(T2.5): 대공 + 항모 저격(+50%) + 비행 중 전략 미사일 요격(반경 36m, 60 피해 / 0.8초).
      var patriot = buildings[21];
      patriot.Attack = AttackType.AntiAir;
      patriot.Damage = 48;
      patriot.Range = 24;
      patriot.AttackInterval = N(1.2);
      patriot.InterceptRange = 36;
      patriot.InterceptDamage = 60;
      patriot.InterceptInterval = N(0.8);
      EditorUtility.SetDirty(patriot);

      var faction = Asset<FactionSpec>($"faction_{factionId}");
      faction.FactionId = factionId;
      faction.RangedRangeMultiplier = N(ranged);
      faction.SmallBuildTimeMultiplier = N(smallBuild);
      faction.PopulationCapMultiplier = N(populationCap);
      faction.Headquarters = Ref(buildings[0]);
      faction.Engineer = Ref(engineer);
      // 사령부는 추가 건설이 드물어 패널 맨 끝에 둔다.
      faction.Buildings = buildings.Skip(1).Concat(buildings.Take(1)).Select(Ref).ToArray();
      ConfigureExtractors(faction, prefix);
      ConfigureMlrs(faction, prefix);
      faction.Units = ids.Select(k => Ref(units[k])).ToArray();
      faction.StartingUnits = startingUnits.Select(id => Ref(U(id))).ToArray();
      // 항목 22: 공중 유닛이 없는 진영은 AI가 비행장을 짓지 않는다(항목 31부터 두 진영 모두 공중이 있다). 대륙 연방은 물량형이라 중공업 기지를 차량 공장 바로 다음에 짓는다.
      faction.AiSkipRoles = ids.Any(k => units[k].IsAir) ? new BuildingRole[0] : new[] { BuildingRole.Airfield };
      faction.Researches = researches;
      faction.PreferredDoctrine = factionId == "continental_federation" ? 3 : 1;
      faction.AiEarlyRoles = factionId == "continental_federation" ? new[] { BuildingRole.HeavyWorks } : new BuildingRole[0];
      faction.Modules = modules;
      EditorUtility.SetDirty(faction);
      return faction;
    }

    static ResearchSpec Research(string id, ResearchKind kind, UnitSize size, int level, ResourceCost cost, int seconds, int tier, FactionSpecial special = FactionSpecial.None) {
      var spec = Asset<ResearchSpec>(id);
      spec.Kind = kind;
      spec.Size = size;
      spec.Level = level;
      spec.Cost = cost;
      spec.Seconds = seconds;
      spec.RequiredTier = tier;
      spec.Requires = default;
      spec.EliteUnit = default;
      spec.EliteHealthMultiplier = 1;
      spec.EliteRangeBonus = 0;
      spec.EliteDamageMultiplier = 1;
      spec.Special = special;
      EditorUtility.SetDirty(spec);
      return spec;
    }

    /// <summary>
    /// 항목 33 궁극 전력 데이터. 비용은 대형 유닛 4~5기 수준에 강철 비중을 크게, 준비 3분(처음 210·200초에서 줄임: AI 첫 사용이 너무 늦었다).
    /// 자유 연방: 공중 항모. 대륙 연방: 전략 미사일. 시험 전용 진영(차기 진영 예약): 상륙 강습단.
    /// </summary>
    static void BuildUltimates(Dictionary<string, UnitSpec> units, FactionSpec free, FactionSpec continental, AssetRef<ModuleSpec>[] modules,
        ResearchSpec freeResearch, ResearchSpec continentalResearch, ResearchSpec assaultResearch) {
      var extra = UltimateUnits.ToDictionary(row => row.Id, BuildUnit);
      // 항모 인구 8, 드론·상륙정은 인구를 쓰지 않는다(항모·강습단에 딸린 것).
      extra["ff_stormnest_carrier"].Population = 8;
      extra["ff_nest_drone"].Population = 0;
      extra["tf_wavecrest_lander"].Population = 0;
      foreach (var unit in extra.Values) {
        unit.ModuleSlots = 0;
        EditorUtility.SetDirty(unit);
      }
      BuildingSpec Site(FactionSpec faction) => faction.Buildings.Select(AssetOf).First(b => b.Role == BuildingRole.UltimateSite);

      var carrier = Asset<UltimateSpec>("ultimate_ff_carrier");
      carrier.Kind = UltimateKind.Carrier;
      carrier.Site = Ref(Site(free));
      carrier.RequiredTier = 3;
      carrier.Research = Ref(freeResearch);
      // 공중 항모: 철 2800, 강철 2200, 지상 조립 80초, 보유 상한 1기, 자율 6드론 윙, 14m 지휘 오라(+15% 연사·+10% 이동).
      carrier.Cost = R(2800, 0, 2200, 500);
      carrier.PrepareSeconds = 80;
      carrier.RearmCost = R(2800, 0, 2200, 500);
      carrier.RearmSeconds = 80;
      carrier.OwnedLimit = 1;
      carrier.CooldownSeconds = 120;
      carrier.RefundFraction = N(0.5);
      carrier.CancelRefundFraction = N(0.75);
      carrier.Carrier = Ref(extra["ff_stormnest_carrier"]);
      carrier.Drone = Ref(extra["ff_nest_drone"]);
      carrier.DroneCount = 6;
      carrier.DroneRadius = 16;
      carrier.DroneLaunchSeconds = N(1.5);
      carrier.DroneBuildSeconds = 12;
      carrier.DroneCost = R(60, 0, 20, 20);
      carrier.DroneRecoverSeconds = 8;
      carrier.DroneDockDistance = 2;
      carrier.AntiAirTakenMultiplier = N(1.5);
      carrier.CarrierDamagedRatio = N(0.5);
      carrier.CarrierDamagedMultiplier = N(0.7);
      carrier.CarrierAuraRadius = 14;
      carrier.CarrierAuraAttackBonus = N(0.15);
      carrier.CarrierAuraSpeedBonus = N(0.10);
      carrier.ConstructionVulnerabilityMultiplier = N(1.3);
      EditorUtility.SetDirty(carrier);
      free.Ultimate = Ref(carrier);
      EditorUtility.SetDirty(free);

      var missile = Asset<UltimateSpec>("ultimate_cf_missile");
      missile.Kind = UltimateKind.StrategicMissile;
      missile.Site = Ref(Site(continental));
      missile.RequiredTier = 3;
      missile.Research = Ref(continentalResearch);
      // 전략 미사일 격납고: 철 2500, 강철 2000, 준비 75초, 재충전 120초 + 발사당 강철 300, 비행 궤적 7초(300 HP), 폭발 12m(2400 피해), EMP 5초, 초토화 15초.
      missile.Cost = R(2500, 0, 2000, 500);
      missile.PrepareSeconds = 75;
      missile.RearmCost = R(0, 0, 300, 350);
      missile.RearmSeconds = 60;
      missile.OwnedLimit = 1;
      missile.CooldownSeconds = 120;
      missile.RefundFraction = N(0.5);
      missile.CancelRefundFraction = N(0.75);
      missile.WarningSeconds = 7;
      missile.MissileHealth = 300;
      missile.LaunchBackblastRadius = 6;
      missile.LaunchBackblastDamage = 180;
      missile.SiloOutpostRadius = 18;
      missile.ConstructionVulnerabilityMultiplier = N(1.3);
      missile.EmpDurationSeconds = 5;
      missile.ScorchedDurationSeconds = 15;
      missile.ScorchedDamagePerSecond = 28;
      missile.ScorchedSlowMultiplier = N(0.65);
      missile.BlastRadius = 12;
      missile.BlastDamage = 2400;
      missile.BlastEdgeFraction = N(0.45);
      missile.BlastBuildingMultiplier = N(1.0);
      missile.BlastCityMultiplier = 2;
      missile.FriendlyFire = true;
      // 요격: 착탄 지점 30m 안의 방공포대 하나당 2점, 방공포대가 있을 때 레이더 기지가 있으면 +2점. 2점 70%, 4점 45%, 6점 25% 피해.
      missile.InterceptRadius = 30;
      missile.InterceptPerAirDefense = 2;
      missile.InterceptRadarBonus = 2;
      missile.InterceptSteps = new[] {
        new InterceptStep { Score = 2, DamageMultiplier = N(0.7) },
        new InterceptStep { Score = 4, DamageMultiplier = N(0.45) },
        new InterceptStep { Score = 6, DamageMultiplier = N(0.25) },
      };
      EditorUtility.SetDirty(missile);
      continental.Ultimate = Ref(missile);
      EditorUtility.SetDirty(continental);

      // 시험 전용 진영(차기 진영 예약): 자유 연방 데이터를 빌리고 전용 건물·연구·궁극 전력만 상륙 강습으로 바꾼다. 메뉴·AI 대전에는 나오지 않는다.
      var dock = BuildBuilding("tf_assault_dock", R(600, 0, 300, 300), 30, 2200, 0, 0);
      var freeSite = Site(free);
      dock.Role = BuildingRole.UltimateSite;
      dock.RequiredTier = 3;
      dock.NeedsWater = true;
      dock.Prerequisites = new[] { free.Buildings.First(b => AssetOf(b).Role == BuildingRole.Shipyard), free.Buildings.First(b => AssetOf(b).Role == BuildingRole.VehicleFactory) };
      dock.PowerUpkeep = freeSite.PowerUpkeep;
      dock.Upkeep = UpkeepMode.WhileUltimate;
      EditorUtility.SetDirty(dock);

      var assault = Asset<UltimateSpec>("ultimate_tf_assault");
      assault.Kind = UltimateKind.AmphibiousAssault;
      assault.Site = Ref(dock);
      assault.RequiredTier = 3;
      assault.Research = Ref(assaultResearch);
      assault.Cost = R(1700, 0, 1000, 500);
      assault.PrepareSeconds = 200;
      assault.RearmCost = R(1000, 0, 600, 300);
      assault.RearmSeconds = 150;
      assault.OwnedLimit = 1;
      assault.CooldownSeconds = 60;
      assault.RefundFraction = N(0.5);
      assault.CancelRefundFraction = N(0.75);
      assault.LandingCraft = Ref(extra["tf_wavecrest_lander"]);
      assault.CraftCount = 2;
      // 탑재 부대: 중형 기갑 4 + 소형 보병 4(상륙정 두 척에 번갈아 싣는다).
      var tank = Ref(units["ff_eagle_tank"]);
      units["ff_sortie_transport"].SortieCargo = Ref(units["ff_ranger_squad"]);
      units["cf_sortie_transport"].SortieCargo = Ref(units["cf_militia"]);
      EditorUtility.SetDirty(units["ff_sortie_transport"]);
      EditorUtility.SetDirty(units["cf_sortie_transport"]);
      var squad = Ref(units["ff_ranger_squad"]);
      assault.Cargo = new[] { tank, tank, squad, squad, tank, tank, squad, squad };
      assault.LandedBonusSeconds = 30;
      assault.LandedDamageMultiplier = N(1.25);
      assault.SupportSeconds = 25;
      assault.LandingReach = 20;
      assault.UnloadDistance = 6;
      EditorUtility.SetDirty(assault);

      var test = Asset<FactionSpec>("faction_test_assault");
      test.FactionId = "test_assault";
      test.RangedRangeMultiplier = free.RangedRangeMultiplier;
      test.SmallBuildTimeMultiplier = free.SmallBuildTimeMultiplier;
      test.PopulationCapMultiplier = free.PopulationCapMultiplier;
      test.Headquarters = free.Headquarters;
      test.Engineer = free.Engineer;
      test.AiSkipRoles = free.AiSkipRoles;
      test.AiEarlyRoles = free.AiEarlyRoles;
      test.Buildings = free.Buildings.Select(b => b == Ref(freeSite) ? Ref(dock) : b).ToArray();
      test.Units = free.Units;
      test.Modules = modules;
      test.StartingUnits = free.StartingUnits;
      var tfRapidDeploy = Research("research_tf_rapid_deploy", ResearchKind.Special, UnitSize.Small, 1, R(180, 0, 90, 60), 45, 2, FactionSpecial.RapidDeployment);
      var tfTacticalSmoke = Research("research_tf_tactical_smoke", ResearchKind.Special, UnitSize.Medium, 1, R(200, 0, 110, 80), 50, 2, FactionSpecial.TacticalSmoke);
      test.Researches = free.Researches
        .Where(r => r != Ref(freeResearch) && AssetOfResearch(r).Kind != ResearchKind.Special)
        .Append(Ref(assaultResearch))
        .Append(Ref(tfRapidDeploy))
        .Append(Ref(tfTacticalSmoke))
        .ToArray();
      test.Ultimate = Ref(assault);
      EditorUtility.SetDirty(test);
    }

    static ResearchSpec AssetOfResearch(AssetRef<ResearchSpec> reference) {
      return AssetDatabase.FindAssets("t:ResearchSpec", new[] { DataRoot })
        .Select(guid => AssetDatabase.LoadAssetAtPath<ResearchSpec>(AssetDatabase.GUIDToAssetPath(guid)))
        .First(asset => Ref(asset) == reference);
    }

    static BuildingSpec AssetOf(AssetRef<BuildingSpec> reference) {
      return AssetDatabase.FindAssets("t:BuildingSpec", new[] { DataRoot }).Select(guid => AssetDatabase.LoadAssetAtPath<BuildingSpec>(AssetDatabase.GUIDToAssetPath(guid))).First(asset => Ref(asset) == reference);
    }

    /// <summary>정예화: 체력 +30%, 사거리 +1, 피해 +10%.</summary>
    static void Elite(ResearchSpec spec) {
      spec.EliteHealthMultiplier = N(1.3);
      spec.EliteRangeBonus = 1;
      spec.EliteDamageMultiplier = N(1.1);
      EditorUtility.SetDirty(spec);
    }

    /// <summary>
    /// 항목 31 공통 연구 12개: 크기(소·중·대) x 공격·방어 x 1·2단계. 1단계는 그 크기를 만들 수 있는 기지 단계,
    /// 2단계는 1단계 연구가 끝나야 하고 기지 3단계가 필요하다. 순서가 FactionSpec.Researches 앞부분이다.
    /// </summary>
    static AssetRef<ResearchSpec>[] BuildResearches() {
      var list = new List<AssetRef<ResearchSpec>>();
      var sizes = new[] { (UnitSize.Small, "small", 1, R(150, 100, 50, 50)), (UnitSize.Medium, "medium", 2, R(200, 0, 100, 80)), (UnitSize.Large, "large", 3, R(250, 0, 150, 100)) };
      foreach (var (size, name, tier, cost) in sizes) {
        foreach (var kind in new[] { ResearchKind.Attack, ResearchKind.Armor }) {
          var prefix = kind == ResearchKind.Attack ? "attack" : "armor";
          var first = Research($"research_{prefix}_{name}_1", kind, size, 1, cost, 45, tier);
          var second = Research($"research_{prefix}_{name}_2", kind, size, 2, cost * N(1.5), 75, 3);
          second.Requires = Ref(first);
          EditorUtility.SetDirty(second);
          list.Add(Ref(first));
          list.Add(Ref(second));
        }
      }
      return list.ToArray();
    }

    static AiSpec BuildAi(string id, double think, int engineers, int variety, int maxYards, int maxHeadquarters, int maxProcessors, double lateExpansion, double firstAttack, double repeat,
        int wave, int growth, int maxWave, bool strategic, BuildingRole[] order) {
      var ai = Asset<AiSpec>(id);
      // 항목 31 테크: 보통은 5.5분에 2단계, 10분에 3단계를 시작한다. 쉬움은 늦게.
      var normal = id == "ai_normal";
      // 항목 33(ai-33-2p-v1: 궁극 전력을 20판 중 1판만 썼다. 강철이 모자라 전용 건물이 16~17분에야 섰다): 3단계 시작 9분→8분.
      ai.TierSeconds = normal ? new FP[] { 330, 480 } : new FP[] { 480, 840 };
      ai.ResearchStartSeconds = normal ? 240 : 480;
      ai.TierLeadSeconds = 120;
      ResearchPick P(ResearchKind kind, UnitSize size = UnitSize.Small, int level = 1) => new ResearchPick { Kind = kind, Size = size, Level = level };
      ai.ResearchOrder = normal
        ? new[] {
          P(ResearchKind.Attack), P(ResearchKind.Armor), P(ResearchKind.Elite), P(ResearchKind.Attack, UnitSize.Medium), P(ResearchKind.Armor, UnitSize.Medium),
          P(ResearchKind.Attack, UnitSize.Large), P(ResearchKind.Attack, UnitSize.Medium, 2), P(ResearchKind.Armor, UnitSize.Medium, 2), P(ResearchKind.Armor, UnitSize.Large),
          P(ResearchKind.Attack, UnitSize.Small, 2), P(ResearchKind.Armor, UnitSize.Small, 2), P(ResearchKind.Attack, UnitSize.Large, 2), P(ResearchKind.Armor, UnitSize.Large, 2),
        }
        : new[] { P(ResearchKind.Attack), P(ResearchKind.Attack, UnitSize.Medium), P(ResearchKind.Elite) };
      // 항목 33 궁극 전력: 보통만 쓴다(쉬움은 준비하지 않고, 상대 알림에도 반응하지 않는다). 3단계 뒤 11분부터 전용 건물·연구·준비.
      ai.UseUltimate = normal;
      // 9분부터 선행 건물(비행장·레이더 기지)을 짓고, 3단계가 되면 전용 건물·연구·준비 순으로 모은다. 본진 25m 안에 적이 보이면 모으기를 멈춘다.
      ai.UltimateStartSeconds = 540;
      ai.UltimateSafeRadius = 25;
      ai.UltimateCheckSeconds = 5;
      ai.UltimateGuard = normal ? 6 : 0;
      ai.UltimateGuardRadius = 12;
      ai.CounterUltimate = normal;
      ai.ThreatAntiAir = 6;
      ai.ThreatAirDefense = 2;
      ai.EvacuateBlast = normal;
      ai.MissileMinValue = 2500;
      ai.UltimateHoldSeconds = 45;
      // 항목 33 소탕·후반: 사령부 없이 핵심 건물 4채 이하인 적을 먼저 끝낸다. 생산이 12초 넘게 멈췄으면 6기만 있어도 나간다.
      ai.FinishCoreBuildings = 4;
      ai.FinishDistanceFactor = N(0.1);
      ai.StalledWaveSize = normal ? 6 : 0;
      ai.StalledSeconds = 12;
      ai.LastStand = true;
      ai.BunkerSquads = normal ? 2 : 1;
      ai.SizeShares = normal ? new[] { N(0.35), N(0.45), N(0.2) } : new[] { N(0.5), N(0.4), N(0.1) };
      ai.SizeShareMargin = normal ? N(0.15) : N(0.2);
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
      ai.SquadSeconds = 40;
      ai.CenterArmyRatio = N(1.1);
      ai.DropSquad = id == "ai_normal" ? 5 : 0;
      ai.BarricadeChance = id == "ai_normal" ? N(0.3) : 0;
      ai.HighGroundUnits = id == "ai_normal" ? 3 : 0;
      ai.HighGroundMinRange = 7;
      ai.GarrisonBuildings = id == "ai_normal" ? 2 : 1;
      ai.ShortcutChance = id == "ai_normal" ? N(0.25) : 0;
      ai.BreakGarrisons = id == "ai_normal";
      ai.ToppleDefense = id == "ai_normal";
      ai.MaxProcessors = maxProcessors;
      // 항목 33: 강철이 궁극 전력의 병목이라(15분까지 번 강철 1500~2000, 궁극 경로에 1450~1550) 가공소를 더 돌린다. 목표 500→1000, 상한 1500→2000, 철 여유 300→250.
      ai.SteelTarget = normal ? 1000 : 500;
      ai.IronSurplus = normal ? 250 : 300;
      ai.ProcessorIronFloor = 150;
      ai.SteelCap = normal ? 2000 : 1500;
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
      // 항목 22: 보통은 상대 구성 대응 생산(상위 2개 중 RNG), 쉬움은 기존 무작위.
      ai.CounterPicks = id == "ai_normal";
      ai.ShipyardGridDivisor = 2;
      ai.ShipyardRingMultiplier = 2;
      ai.MaxShips = id == "ai_normal" ? 4 : 2;
      ai.AirShareForAntiAir = N(0.1);
      ai.IdleAntiAir = 2;
      ai.CounterTop = 2;
      // 항목 33: 보통 AI는 유리할 때만 먼저 친다(1.3배, 1분마다 0.15씩 0.6배까지 내려감, 방어 건물은 값의 절반으로 친다). 쉬움은 예전처럼 그냥 나간다.
      ai.AttackRatio = normal ? N(0.8) : 0;
      ai.AttackRatioDecay = N(0.25);
      ai.AttackRatioFloor = N(0.25);
      ai.DefenseWeight = N(0.2);
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
      // 1.25x 산업 공장·건물 지름(7~7.6m) + 간격(BuildingGap 2m)에 맞춰 AI 배치 격자 9m·8링 확보.
      ai.SiteGrid = 9;
      ai.SiteRings = 8;
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

    static void ConfigureExtractors(FactionSpec faction, string prefix) {
      BuildingSpec Extractor(string id, ResourceKind kind, int capacity, ResourceCost cost) {
        var extractor = BuildBuilding(id, cost, 20, 650, 0, 0);
        extractor.Role = kind == ResourceKind.Wood ? BuildingRole.LumberMill : BuildingRole.IronExtractor;
        extractor.RequiredTier = 1;
        extractor.Prerequisites = new[] { faction.Headquarters };
        extractor.RequiresResourceNode = true;
        extractor.RequiredNodeKind = kind;
        extractor.WorkerSlotCapacity = capacity;
        extractor.ExtractIntervalSeconds = 2;
        extractor.ExtractAmountPerCycle = 3;
        return extractor;
      }
      var lumber = Ref(Extractor($"{prefix}_lumber_mill", ResourceKind.Wood, 3, R(100, 120)));
      var iron = Ref(Extractor($"{prefix}_iron_extractor", ResourceKind.Iron, 3, R(120, 100)));
      faction.Buildings = faction.Buildings.Where(reference => reference != lumber && reference != iron && reference != faction.Headquarters)
        .Concat(new[] { lumber, iron, faction.Headquarters }).ToArray();
      EditorUtility.SetDirty(faction);
    }

    static void ConfigureExtractorRules(GameRulesSpec rules) {
      rules.ExtractorNodeSnapRadius = 4;
      rules.ExtractorYieldMultipliers = new[] { FP._0, FP._1, N(1.6), N(2.1), N(2.5) };
      EditorUtility.SetDirty(rules);
    }

    public static void BuildExtractorData() {
      ConfigureExtractors(Asset<FactionSpec>("faction_free_federation"), "ff");
      ConfigureExtractors(Asset<FactionSpec>("faction_continental_federation"), "cf");
      ConfigureExtractorRules(Asset<GameRulesSpec>("game_rules"));
      var views = Resources.Load<FactoryWars.View.ViewCatalog>(FactoryWars.View.ViewCatalog.ResourcePath);
      if (views != null) {
        const string construction = SyntyAssign.PackRoot + "/PolygonConstruction/Prefabs/";
        const string battleRoyale = SyntyAssign.PackRoot + "/PolygonBattleRoyale/Prefabs/";
        views.LumberMillSaw = AssetDatabase.LoadAssetAtPath<GameObject>(construction + "Tools/SM_Tool_Circular_Saw_01.prefab") ?? views.LumberMillSaw;
        views.IronExtractorDrill = AssetDatabase.LoadAssetAtPath<GameObject>(construction + "Tools/SM_Tool_Drill_01.prefab") ?? views.IronExtractorDrill;
        views.LumberCargo = AssetDatabase.LoadAssetAtPath<GameObject>(battleRoyale + "Props/SM_Prop_WoodPlank_01.prefab") ?? views.LumberCargo;
        views.IronCargo = AssetDatabase.LoadAssetAtPath<GameObject>(construction + "Props/SM_Prop_Rubble_Concrete_01.prefab") ?? views.IronCargo;
        EditorUtility.SetDirty(views);
      }
      SyntyAssign.BuildLivingBaseVisuals();
      AssetDatabase.SaveAssets();
    }

    public static void BuildData() {
      QuantumRunner.Init();
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
      var shared = BuildResearches();
      var ffElite = Research("research_elite_ff", ResearchKind.Elite, UnitSize.Small, 1, R(200, 100, 100, 50), 60, 2);
      ffElite.EliteUnit = Ref(units["ff_ranger_squad"]);
      Elite(ffElite);
      var cfElite = Research("research_elite_cf", ResearchKind.Elite, UnitSize.Small, 1, R(200, 100, 100, 50), 60, 2);
      cfElite.EliteUnit = Ref(units["cf_militia"]);
      Elite(cfElite);
      // 항목 33 궁극 전력 연구(진영마다 하나, 3단계). 순서: 공통 12 + 정예화 + 궁극.
      // 3차: 연구 90→60초.
      ResearchSpec UltimateResearch(string id) => Research(id, ResearchKind.Ultimate, UnitSize.Large, 1, R(400, 0, 250, 200), 60, 3);
      var ffUltimate = UltimateResearch("research_ultimate_ff");
      var cfUltimate = UltimateResearch("research_ultimate_cf");
      var tfUltimate = UltimateResearch("research_ultimate_tf");
      // 진영 특화 업그레이드(2단계): 자유 연방(고속 볼트 · 스텔스 도료), 대륙 연방(반응장갑 ERA · 고폭 철갑탄).
      var ffRapidBolts = Research("research_ff_rapid_bolts", ResearchKind.Special, UnitSize.Small, 1, R(180, 0, 90, 60), 45, 2, FactionSpecial.RapidCycleBolts);
      var ffStealthCoating = Research("research_ff_stealth_coating", ResearchKind.Special, UnitSize.Medium, 1, R(240, 0, 140, 100), 55, 2, FactionSpecial.StealthCoating);
      var cfEra = Research("research_cf_era", ResearchKind.Special, UnitSize.Medium, 1, R(220, 0, 120, 80), 50, 2, FactionSpecial.ReactiveArmorEra);
      var cfHeap = Research("research_cf_heap", ResearchKind.Special, UnitSize.Large, 1, R(250, 0, 150, 100), 55, 2, FactionSpecial.HighExplosiveAp);
      // 전용 건물 선행: 항모 조립창 = 비행장(7) + 중공업 기지(8), 미사일 격납고 = 중공업 기지(8) + 레이더 기지(17).
      var free = BuildFaction("free_federation", "ff", units, 1.1, 1, 1, new[] { "ranger_squad", "ranger_squad", "eagle_tank" }, modules,
        shared.Append(Ref(ffElite)).Append(Ref(ffUltimate)).Append(Ref(ffRapidBolts)).Append(Ref(ffStealthCoating)).ToArray(),
        "ff_carrier_yard", new[] { 7, 8 });
      var continental = BuildFaction("continental_federation", "cf", units, 1, 0.75, 1.2, new[] { "militia", "militia", "dragonscale_tank" }, modules,
        shared.Append(Ref(cfElite)).Append(Ref(cfUltimate)).Append(Ref(cfEra)).Append(Ref(cfHeap)).ToArray(),
        "cf_missile_silo", new[] { 8, 17 });
      BuildUltimates(units, free, continental, modules, ffUltimate, cfUltimate, tfUltimate);

      var rules = Asset<GameRulesSpec>("game_rules");
      ConfigureExtractorRules(rules);
      rules.NavMeshName = "NavMesh";
      rules.WaterNavMeshName = "WaterNavMesh";
      rules.WaterGateRegion = WaterGateRegion;
      rules.NavalAgent = Ref(NavalAgent());
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
      rules.DropWarningSeconds = 35;
      // 항목 34-B 보급품 사전 알람: 35초 전 반경 30m 원 → 10초 전 반경 10m → 착지, 신호탄 15m·20초(모두에게), 획득은 3m 안 5초.
      rules.DropZoneRadius = 30;
      rules.DropClosingSeconds = 10;
      rules.DropClosingRadius = 10;
      rules.DropFlareRadius = 15;
      rules.DropFlareSeconds = 20;
      rules.DropCaptureRadius = 3;
      rules.DropCaptureSeconds = 5;
      rules.DropRewardWeights = new FP[] { 5, 2, 2, 3 };
      rules.DropReconSeconds = 15;
      rules.DropReconRadius = 15;
      rules.DropHealAmount = 60;
      rules.DropHealRadius = 12;
      rules.HighGroundRangeMultiplier = N(1.15);
      rules.HighGroundRevealDistance = 6;
      rules.HighGroundAirRevealDistance = 12;
      rules.HighGroundRevealSeconds = 3;
      rules.FogOfWar = false;
      rules.FogUpdateTicks = 5;
      rules.FogHighGroundBonus = 4;
      rules.FogGarrisonBonus = 6;
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
      // 항목 34-C 주둔 저격수 규칙.
      rules.MuzzleRevealSeconds = 4;
      rules.MuzzleRevealRadius = 6;
      rules.SniperVsSniperMultiplier = 2;
      rules.RadarDetectRadius = 30;
      rules.JammerDisarmPerSecond = 10;
      rules.JammerDisarmRadius = 4;
      rules.SortieAntiAirFactor = N(3);
      // 베테랑 9킬 상한: 킬 3(★)·6(★★)·9(★★★ MAX). 별당 체력 +8%, 피해 +8%, 공격 속도 +5%(재장전 -5%).
      rules.VetKillsRank1 = 3;
      rules.VetKillsRank2 = 6;
      rules.VetKillsRank3 = 9;
      rules.VetHealthBonus = new[] { N(0.08), N(0.16), N(0.24) };
      rules.VetDamageBonus = new[] { N(0.08), N(0.16), N(0.24) };
      rules.VetRangeBonus = new[] { N(0.04), N(0.08), N(0.12) };
      rules.VetReloadBonus = new[] { N(0.05), N(0.10), N(0.15) };
      rules.VetRegenPerSecond = new[] { N(0.005), N(0.01), N(0.015) };
      rules.VetRegenDelaySeconds = 5;
      // 항목 37 전술 도크린(선택 30초, 기본값 1).
      rules.DoctrineSelectSeconds = 30;
      rules.DefaultDoctrine = 1;
      rules.AirRearmMultiplier = N(0.75);
      rules.ScoutSightMultiplier = N(1.2);
      rules.ScoutSpeedMultiplier = N(1.2);
      rules.ShadowSniperBonus = 1;
      rules.ShadowStealthBonusSeconds = 10;
      rules.ShadowCoverBonus = N(0.15);
      rules.ArmorCostMultiplier = N(0.85);
      rules.ArmorRepairMultiplier = N(1.5);
      // 항목 37 전술 깊이. 잔해 30초(생산비 철의 35% 회수, 기갑 군단은 2배), 다리 체력 1200·폭격 피해 x1.5·폭약 8초·폰툰 복구 10초.
      rules.WreckSeconds = 30;
      rules.WreckScrapRatio = N(0.35);
      rules.WreckCoverRadius = N(2.4);
      rules.WreckCoverReduction = N(0.5);
      rules.WreckSalvageSeconds = 4;
      rules.ArmorScrapMultiplier = 2;
      rules.BridgeHealth = 1200;
      rules.BridgeBlastMultiplier = N(1.5);
      rules.BridgeDemolishSeconds = 8;
      rules.BridgeRepairSeconds = 10;
      rules.BridgeWorkRange = N(4);
      rules.BridgeCollapseDamage = 250;
      rules.BridgeRegion = BridgeRegion;
      // 사령부 비상 프로토콜: 체력 40% 미만에서 한 번, 20초 피해 -60%, 수비 분대 2, 사거리 28 자동 사격(피해 60, 1.2초).
      rules.HqProtocolHealthRatio = N(0.4);
      rules.HqProtocolSeconds = 20;
      rules.HqProtocolDamageTaken = N(0.4);
      rules.HqProtocolSquads = 2;
      rules.HqProtocolRange = 28;
      rules.HqProtocolDamage = 60;
      rules.HqProtocolInterval = N(1.2);
      // 블랙박스: 첫 추락 9분, 6분 주기, 90초 유지, 반경 8m, 15초 점령, 영구 화력 +5%(최대 4번).
      rules.BlackBoxFirstSeconds = 540;
      rules.BlackBoxPeriodSeconds = 360;
      rules.BlackBoxLifeSeconds = 90;
      rules.BlackBoxRadius = 8;
      rules.BlackBoxCaptureSeconds = 15;
      rules.BlackBoxDamageBonus = N(0.05);
      rules.BlackBoxMaxStacks = 4;
      rules.RadarSiteRadius = 35;
      rules.FuelRearmMultiplier = N(0.7);
      // 중앙 암시장: 체력 3000, 철조망 반지름 10m, 경비 사거리 24m(구역 밖 공격자까지)·피해 45·1.2초, 현상금 철 1500·강철 600, 거래 대기 60초.
      rules.TraderHealth = 3000;
      rules.TraderRadius = 10;
      rules.TraderRange = 24;
      rules.TraderDamage = 45;
      rules.TraderInterval = N(1.2);
      rules.TraderBounty = R(1500, 600, 600, 300);
      rules.TraderCooldownSeconds = 60;
      rules.TraderMercCost = R(300, 0, 80);
      rules.TraderMercCount = 4;
      rules.TraderDroneCost = R(200, 0, 120);
      rules.TraderDroneDamage = 260;
      rules.TraderDroneRadius = 6;
      rules.TraderRefuelCost = R(150, 0, 100, 100);
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
      rules.LowPowerProductionMultiplier = N(0.65);
      rules.FactoryLv2Cost = R(200, 0, 100);
      rules.FactoryLv3Cost = R(380, 0, 220);
      rules.FactoryLv2SpeedBonus = N(0.25);
      rules.FactoryLv3SpeedBonus = N(0.50);
      rules.FactorySynergyRadius = 12;
      rules.FactorySynergyBonus = N(0.15);
      rules.OverdriveDurationSeconds = 20;
      rules.OverdriveCooldownSeconds = 35;
      rules.OverdriveSpeedBonus = N(0.50);
      rules.OverdrivePowerMultiplier = 2;
      rules.OverdriveHeatDamagePerSecond = 8;
      rules.DepotVehicleSpeedBonus = N(0.10);
      rules.DepotArtilleryReloadBonus = N(0.12);
      rules.DepotSortieCooldownReduction = N(0.15);
      rules.DepotExplosionRadius = 10;
      rules.DepotExplosionDamage = 480;
      rules.OutageTowerIntervalMultiplier = 2;
      rules.SpawnOffset = 6;
      // 항목 32 엄폐: 10m 안에서 찾고, 막는 방향 ±60도(cos 0.5) 안의 사격만 줄인다. 광역(포격·폭격·화염)은 절반 효과.
      rules.CoverSearchRadius = 10;
      rules.CoverFacingCos = N(0.5);
      rules.CoverAreaFactor = N(0.5);
      rules.CoverArrival = N(1.2);
      rules.CoverSearchPerTick = 8;
      rules.CoverOffset = N(1.2);
      rules.CoverHighReduction = N(0.5);
      rules.RadarAlertSeconds = 30;
      // 항목 33: 물가 찾기(상륙 접안, 배의 지원 위치)는 2m 고리, 16방향.
      rules.WaterSearchStep = 2;
      rules.WaterSearchDirections = 16;
      // 항목 31 크기 규칙: 소형 인구 0.5~1, 중형 2~3, 대형 5~6·동시 4대. 공격·방어 연구는 단계마다 10%.
      rules.SizeRules = new[] {
        new SizeRuleSpec { Size = UnitSize.Small, MinPopulation = N(0.5), MaxPopulation = 1, AttackPerLevel = N(0.1), ArmorPerLevel = N(0.1) },
        new SizeRuleSpec { Size = UnitSize.Medium, MinPopulation = 2, MaxPopulation = 3, AttackPerLevel = N(0.1), ArmorPerLevel = N(0.1) },
        new SizeRuleSpec { Size = UnitSize.Large, MinPopulation = 5, MaxPopulation = 6, OwnedLimit = 4, AttackPerLevel = N(0.1), ArmorPerLevel = N(0.1) },
      };
      // 항목 31 기지 단계: 2단계 "연대 본부"는 연구소, 3단계 "사단 본부"는 중공업 기지가 있어야 한다.
      rules.BaseTiers = new[] {
        new BaseTierSpec { Tier = 2, Cost = R(300, 200, 0, 100), Seconds = 75, RequiredRoles = new[] { BuildingRole.ResearchLab } },
        new BaseTierSpec { Tier = 3, Cost = R(450, 200, 150, 150), Seconds = 75, RequiredRoles = new[] { BuildingRole.HeavyWorks } },
      };
      rules.TierDropsWithHeadquarters = true;
      rules.ShipSpawnGap = 3;
      rules.ShipyardWaterReach = 3;
      rules.ShipLaunchClearance = 3;
      rules.AirfieldWorkClearance = 2;
      rules.RunwayBuildSeconds = 14;
      rules.RunwayHalfLength = 11;
      rules.RunwayHalfWidth = 4;
      rules.LandingPadBuildSeconds = 9;
      rules.LandingPadHalfSize = 5;
      rules.DisconnectBotDifficulty = BotDifficulty.Normal;
      rules.DisconnectForfeitSeconds = 60;
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
      // 항목 31: 2단계 건물(군수공장 등)은 단계가 오를 때까지 건너뛰므로 연구소를 앞에 둔다. 쉬움도 연구소·중공업 기지를 짓는다.
      // 3단계 조건인 중공업 기지는 군수공장 바로 뒤(ai-31-2p-v2: 3단계 평균 15분, 40자리 중 26). 보통 공격 간격 60→50초, 파도 증가 3→4·상한 24→30(경기 22분).
      rules.EasyAi = Ref(BuildAi("ai_easy", 5, 6, 1, 1, 2, 2, 0, 420, 90, 6, 2, 12, false, new[] {
        BuildingRole.SupplyDepot, BuildingRole.Barracks, BuildingRole.PowerPlant, BuildingRole.Processor, BuildingRole.ResearchLab, BuildingRole.VehicleFactory, BuildingRole.PowerPlant,
        BuildingRole.HeavyWorks,
      }));
      rules.NormalAi = Ref(BuildAi("ai_normal", 2, 10, 3, 4, 3, 5, 150, 150, 25, 8, 2, 18, true, new[] {
        BuildingRole.SupplyDepot, BuildingRole.Barracks, BuildingRole.PowerPlant, BuildingRole.Processor, BuildingRole.ResearchLab, BuildingRole.VehicleFactory,
        BuildingRole.HeavyWorks, BuildingRole.RepairBay, BuildingRole.SupplyDepot, BuildingRole.PowerPlant, BuildingRole.MedicTent, BuildingRole.Radar,
        BuildingRole.Bunker, BuildingRole.Shipyard, BuildingRole.Airfield, BuildingRole.Artillery, BuildingRole.Helipad, BuildingRole.Sandbags, BuildingRole.GroundTower, BuildingRole.AirDefense, BuildingRole.SupplyDepot,
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
      // 항목 31: 시험 맵은 3단계로 시작해 기존 건설·생산 시험이 단계에 막히지 않는다. 단계 시험은 RuntimeConfig.StartingTier로 1부터 한다.
      testMap.StartingTier = 3;
      testMap.HasTrader = true;
      testMap.TraderPosition = new FPVector3(40, 0, 20);
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
      BuildMapSpec(DesertMap);
      BuildMapSpec(UrbanMap);
      BuildMapSpec(ArchipelagoMap);
      BuildMapSpec(StraitMap);

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

    /// <summary>물길 조각에서 다리 상판(폭 방향 + 0.3m) 밖의 부분. 조각이 다리에 거의 덮이면 null. 물길이 다리에 수직일 때만 자른다.</summary>
    static MapShape? TrimBesideBridge(MapShape piece, MapShape[] bridges) {
      var yaw = piece.Yaw.AsFloat * Mathf.Deg2Rad;
      var along = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));
      var center = new Vector2(piece.Center.X.AsFloat, piece.Center.Z.AsFloat);
      var low = -piece.HalfLength.AsFloat;
      var high = piece.HalfLength.AsFloat;
      foreach (var bridge in bridges) {
        var bridgeCenter = new Vector2(bridge.Center.X.AsFloat, bridge.Center.Z.AsFloat);
        var by = bridge.Yaw.AsFloat * Mathf.Deg2Rad;
        var bridgeAxis = new Vector2(Mathf.Cos(by), -Mathf.Sin(by));
        if (Mathf.Abs(Vector2.Dot(bridgeAxis, along)) > 0.2f) continue;
        var offset = center - bridgeCenter;
        if (Mathf.Abs(Vector2.Dot(offset, bridgeAxis)) > bridge.HalfLength.AsFloat + piece.HalfWidth.AsFloat) continue;
        var s = Vector2.Dot(offset, along);
        var deck = bridge.HalfWidth.AsFloat + 0.3f;
        if (s >= 0f) low = Mathf.Max(low, deck - s);
        else high = Mathf.Min(high, -deck - s);
      }
      if (high - low < 0.6f) return null;
      var mid = center + along * ((low + high) / 2f);
      return Rect(mid.x, mid.y, (high - low) / 2f, piece.HalfWidth.AsFloat, piece.Yaw.AsFloat);
    }

    static void BuildMapSpec(MapDef def) {
      var spec = Asset<MapSpec>($"map_{def.Id}");
      spec.MaxPlayers = def.Players;
      spec.Theme = def.Theme;
      spec.SupplyIntervalMultiplier = N(def.SupplyInterval);
      spec.SupplyRewardMultiplier = N(def.SupplyReward);
      spec.HasTrader = def.Trader;
      spec.TraderPosition = def.TraderPosition;
      spec.BlackBoxSpots = def.BlackBoxSpots;
      spec.WeatherFirstSeconds = def.WeatherFirst;
      spec.WeatherDurationSeconds = def.WeatherDuration;
      spec.WeatherPeriodSeconds = def.WeatherPeriod;
      spec.WeatherSightMultiplier = N(def.WeatherSight);
      spec.BoundsMin = new FPVector2(-def.HalfSize, -def.HalfSize);
      spec.BoundsMax = new FPVector2(def.HalfSize, def.HalfSize);
      spec.SealedPerimeter = true;
      spec.ExtractorSnapRadius = 10;
      spec.StartPositions = Orbit(def, new MapShape { Center = def.Start }).Select(s => s.Center).ToArray();
      spec.StartingResources = R(500, 300);
      spec.StartingEngineers = 4;
      spec.ResourceNodes = def.Nodes
        .SelectMany((node, index) => Orbit(def, node.Shape).Select(shape => new ResourceNodePlacement {
          Kind = node.Kind, Amount = node.Amount, Shape = shape, Tier = index < 3 ? 1 : 2,
        }))
        .Concat(def.CenterNodes.Select(node => {
          node.Tier = 3;
          return node;
        })).ToArray();
      // 강은 한 사분면만 만들고 나머지는 회전 복제한다.
      var area = def.BaseArea;
      var corner = new Vector2(area.Center.X.AsFloat + area.HalfLength.AsFloat, area.Center.Z.AsFloat + area.HalfWidth.AsFloat);
      var bridges = def.Bridges.Select(b => new Vector2(b.Center.X.AsFloat, b.Center.Z.AsFloat)).ToArray();
      var pieces = RiverBuilder.Build(corner, -def.HalfSize, bridges, def.RiverAmplitude, def.RiverSeed, spec.SealedPerimeter);
      MapShape Shape(RiverBuilder.Piece piece) => Rect(piece.Center.x, piece.Center.y, piece.HalfLength, piece.HalfWidth, piece.Yaw);
      var step = 4 / def.Players;
      var riverList = Enumerable.Range(0, def.Players).SelectMany(player => pieces.Select(piece => Turn(Shape(piece), player * step))).ToList();
      var extraRiver = new List<MapShape>();
      if (def.ExtraWater != null) {
        foreach (var strip in def.ExtraWater) {
          if (strip.Ring) {
            const int RingSegs = 40;
            for (var r = 0; r <= RingSegs; r++) {
              var a0 = (r % RingSegs) * (Mathf.PI * 2f / RingSegs);
              var a1 = ((r + 1) % RingSegs) * (Mathf.PI * 2f / RingSegs);
              var p0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * strip.RingRadius;
              var p1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * strip.RingRadius;
              var mid = (p0 + p1) * 0.5f;
              var d = p1 - p0;
              var yaw = Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg;
              extraRiver.Add(Rect(mid.x, mid.y, d.magnitude * 0.62f, strip.HalfWidth, yaw));
            }
          } else {
            var delta = strip.To - strip.From;
            var totalLen = delta.magnitude;
            var dir = totalLen > 0.001f ? delta / totalLen : Vector2.right;
            var yaw = Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg;
            var count = Mathf.Max(2, Mathf.CeilToInt(totalLen / 6f));
            for (var k = 0; k <= (strip.OpenEnd ? count - 1 : count); k++) {
              var pt = strip.From + dir * (totalLen * k / count);
              var half = strip.HalfWidth;
              if (strip.Wobble > 0f) {
                // 두 겹 사인으로 폭만 좁힌다(넓히지 않아 본진·자원 쪽으로 물이 들어가지 않는다). 양 끝은 원래 폭.
                var t = k / (float)count;
                var wave = 0.5f + 0.32f * Mathf.Sin(t * 9.1f + strip.Seed * 1.7f) + 0.18f * Mathf.Sin(t * 21.3f + strip.Seed * 4.1f);
                half -= strip.Wobble * Mathf.Clamp01(wave) * Mathf.Sin(t * Mathf.PI);
                // 중심선을 옆으로 흔들어 양쪽 해안이 서로 다른 곶·만을 갖게 한다. 흔든 만큼 폭을 더 줄여 바깥으로는 원래 폭을 넘지 않는다.
                var sway = strip.Wobble * 0.32f * Mathf.Sin(t * 6.3f + strip.Seed * 2.9f) * Mathf.Sin(t * Mathf.PI);
                pt += new Vector2(-dir.y, dir.x) * sway;
                half = Mathf.Max(18f, half - Mathf.Abs(sway));
              }
              // 다각형 물길은 조각을 길게 해 볼록 꼭짓점 바깥 쐐기까지 막는다.
              extraRiver.Add(Rect(pt.x, pt.y, strip.OpenEnd ? 5f : 4.2f, half, yaw));
            }
          }
        }
      }
      riverList.AddRange(extraRiver);
      spec.River = riverList.ToArray();

      var bridgesList = def.Bridges.SelectMany(bridge => Orbit(def, bridge)).ToList();
      if (def.ExtraBridges != null && def.ExtraBridges.Length > 0) {
        bridgesList.AddRange(def.ExtraBridges);
      }
      spec.Bridges = bridgesList.ToArray();

      var allFords = def.Fords.SelectMany(shape => Orbit(def, shape)).ToArray();
      bool OverlapsExtraWater(FPVector3 pos, float margin) {
        for (var e = 0; e < extraRiver.Count; e++) {
          if (extraRiver[e].Distance(pos).AsFloat < margin) return true;
        }
        return false;
      }
      bool IsCrossing(MapShape piece) {
        for (var b = 0; b < spec.Bridges.Length; b++) {
          if (spec.Bridges[b].Distance(piece.Center).AsFloat < 3.5f) return true;
        }
        for (var f = 0; f < allFords.Length; f++) {
          if (allFords[f].Distance(piece.Center).AsFloat < 4.5f) return true;
        }
        return false;
      }

      var edge = def.HalfSize + 2f;
      var blocks = pieces.Where(p => p.UnderBridge == false && Mathf.Abs(p.Center.x) < edge && Mathf.Abs(p.Center.y) < edge)
        .SelectMany(p => Orbit(def, Shape(p)).Select(shape => Feature(TerrainKind.River, shape))).ToList();
      foreach (var er in extraRiver) {
        if (Mathf.Abs(er.Center.X.AsFloat) >= edge || Mathf.Abs(er.Center.Z.AsFloat) >= edge) continue;
        if (IsCrossing(er) == false) {
          blocks.Add(Feature(TerrainKind.River, er));
          continue;
        }
        // 다리 옆 조각: 다리 상판 폭 밖으로 삐져나온 부분만 남겨 막는다. 통째로 빼면 다리 옆 물을 걸어서 건넌다.
        if (allFords.Any(ford => ford.Distance(er.Center).AsFloat < 4.5f)) continue;
        var trimmed = TrimBesideBridge(er, spec.Bridges);
        if (trimmed.HasValue) blocks.Add(Feature(TerrainKind.River, trimmed.Value));
      }
      var landTerrain = def.Terrain.SelectMany(feature => Orbit(def, feature.Shape).Select(shape => Feature(feature.Kind, shape)))
        .Where(f => extraRiver.Count == 0 || OverlapsExtraWater(f.Shape.Center, 4.5f) == false);
      spec.Terrain = landTerrain.Concat(blocks).ToArray();
      if (extraRiver.Count > 0 && spec.ResourceNodes != null) {
        spec.ResourceNodes = spec.ResourceNodes
          .Where(n => OverlapsExtraWater(n.Shape.Center, Mathf.Max(6.5f, n.Shape.Radius.AsFloat + 2.5f)) == false)
          .ToArray();
      }
      spec.NoBuildZones = def.NoBuildZones.SelectMany(zone => Orbit(def, zone)).ToArray();
      spec.BaseAreas = Orbit(def, def.BaseArea).ToArray();
      spec.Channel = System.Array.Empty<MapShape>();
      spec.WaterGates = System.Array.Empty<MapShape>();
      spec.ChannelWidth = FP._0;
      FillBattlefield(def, spec, extraRiver);
      EditorUtility.SetDirty(spec);
    }

    /// <summary>점령지·바리케이드·상자·고지대·도로·여울·확장 후보. 규칙은 항목 20의 시스템이 읽는다.</summary>
    static void FillBattlefield(MapDef def, MapSpec spec, List<MapShape> extraRiver) {
      var step = 4 / def.Players;
      FPVector3 Turned(FPVector3 position, int seat) => Turn(new MapShape { Center = position }, seat * step).Center;
      bool InExtraWater(FPVector3 pos, float margin) {
        if (extraRiver == null || extraRiver.Count == 0) return false;
        for (var i = 0; i < extraRiver.Count; i++) {
          if (extraRiver[i].Distance(pos).AsFloat < margin) return true;
        }
        return false;
      }

      spec.CaptureSites = def.CaptureSites
        .SelectMany(site => Enumerable.Range(0, def.Players).Select(seat => new CaptureSitePlacement { Kind = site.Kind, Center = Turned(site.Center, seat), Radius = site.Radius }))
        .Concat(def.CenterSites)
        .Where(s => InExtraWater(s.Center, 3f) == false).ToArray();
      spec.Barricades = def.Barricades.SelectMany(shape => Orbit(def, shape))
        .Where(shape => InExtraWater(shape.Center, 3.5f) == false)
        .Select((shape, index) => new BarricadePlacement { Shape = shape, Region = $"{BarricadeRegionPrefix}{index}" }).ToArray();

      var crates = new List<CrateSpot>();
      for (var seat = 0; seat < def.Players; seat++) {
        crates.AddRange(def.BaseCrates.Select((position, index) => new CrateSpot { Position = Turned(position, seat), Seat = seat, Group = index }));
      }
      for (var group = 0; group < def.DropCrates.Length; group++) {
        for (var seat = 0; seat < def.Players; seat++) {
          var pos = Turned(def.DropCrates[group], seat);
          if (InExtraWater(pos, 3f) == false) {
            crates.Add(new CrateSpot { Position = pos, Seat = -1, Group = group });
          }
        }
      }
      spec.CrateSpots = crates.ToArray();

      spec.HighGrounds = def.HighGrounds.SelectMany(shape => Orbit(def, shape)).Where(s => InExtraWater(s.Center, 6f) == false).ToArray();
      spec.Ramps = def.Ramps.SelectMany(shape => Orbit(def, shape)).Where(s => InExtraWater(s.Center, 4f) == false).ToArray();
      bool RoadTouchesExtraWater(MapShape road) {
        if (extraRiver == null || extraRiver.Count == 0) return false;
        var c = new Vector2(road.Center.X.AsFloat, road.Center.Z.AsFloat);
        var rad = road.Yaw.AsFloat * Mathf.Deg2Rad;
        var along = new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad)) * road.HalfLength.AsFloat;
        foreach (var t in new[] { -1f, -0.5f, 0f, 0.5f, 1f }) {
          var pt = c + along * t;
          if (InExtraWater(new FPVector3(FP.FromFloat_UNSAFE(pt.x), default, FP.FromFloat_UNSAFE(pt.y)), 4f)) {
            return true;
          }
        }
        return false;
      }
      var roadsList = def.Roads.SelectMany(shape => Orbit(def, shape)).Where(s => RoadTouchesExtraWater(s) == false)
        .Where(s => def.RoadClearHalfX <= 0f || Mathf.Abs(s.Center.X.AsFloat) >= def.RoadClearHalfX).ToList();
      if (def.ExtraRoads != null && def.ExtraRoads.Length > 0) {
        roadsList.AddRange(def.ExtraRoads);
      }
      spec.Roads = roadsList.ToArray();
      spec.Fords = def.Fords.SelectMany(shape => Orbit(def, shape)).ToArray();

      var sites = new List<ExpansionSite>();
      for (var seat = 0; seat < def.Players; seat++) {
        sites.AddRange(def.Expansions.Select(site => new ExpansionSite { Kind = site.Kind, Position = Turned(site.Position, seat), SeatMask = 1 << seat }));
        foreach (var site in def.SharedExpansions) {
          var pos = Turned(site.Position, seat);
          if (InExtraWater(pos, 5f)) continue;
          var mask = def.SharedSeatOffsets.Aggregate(0, (bits, offset) => bits | 1 << (seat + offset) % def.Players);
          sites.Add(new ExpansionSite { Kind = site.Kind, Position = pos, SeatMask = mask });
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
      void Paint(MapShape shape, System.Action<int> write) {
        var reach = shape.Reach;
        var xx = shape.Center.X - spec.BoundsMin.X;
        var zz = shape.Center.Z - spec.BoundsMin.Y;
        var minX = Mathf.Max(0, FPMath.FloorToInt((xx - reach) / Cell));
        var maxX = Mathf.Min(size - 1, FPMath.FloorToInt((xx + reach) / Cell));
        var minZ = Mathf.Max(0, FPMath.FloorToInt((zz - reach) / Cell));
        var maxZ = Mathf.Min(size - 1, FPMath.FloorToInt((zz + reach) / Cell));
        for (var z = minZ; z <= maxZ; z++) {
          for (var x = minX; x <= maxX; x++) {
            var point = new FPVector3(spec.BoundsMin.X + x * Cell + 1, 0, spec.BoundsMin.Y + z * Cell + 1);
            if (shape.Distance(point) <= FP._0) write(z * size + x);
          }
        }
      }
      foreach (var road in spec.Roads ?? System.Array.Empty<MapShape>()) Paint(road, cell => spec.GroundGrid[cell] = MapSpec.GroundRoad);
      foreach (var ford in spec.Fords ?? System.Array.Empty<MapShape>()) Paint(ford, cell => spec.GroundGrid[cell] = MapSpec.GroundFord);
      var cities = spec.CityBuildings ?? new CityBuildingPlacement[0];
      spec.CityGrid = new short[cities.Length > 0 ? size * size : 0];
      for (var i = 0; i < cities.Length; i++) {
        var city = (short)(i + 1);
        Paint(cities[i].Shape, cell => spec.CityGrid[cell] = city);
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
      BuildWater(map, spec);

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
      var bridgeList = spec.Bridges ?? new MapShape[0];
      for (var i = 0; i < bridgeList.Length; i++) {
        AddBridge(root, spec, bridgeList[i], i);
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
    static void AddBridge(Transform root, MapSpec spec, MapShape shape, int index) {
      // 항목 37: 다리마다 영역 하나(Bridge{index}). 휴전 울타리·빈 본진 자리·폭파 상태가 DepthSystem.ApplyBridgeRegion에서 합쳐진다.
      if (spec.BaseAreas != null && spec.BaseAreas.Length > 0) {
        AddRegion(root, "Bridge", shape, BarrierArea, $"{BridgeRegion}{index}");
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
      EditorBuildSettings.scenes = new[] {
          path,
          $"{ScenesRoot}/{FourPlayerMap.Id}.unity",
          $"{ScenesRoot}/{DesertMap.Id}.unity",
          $"{ScenesRoot}/{UrbanMap.Id}.unity",
          $"{ScenesRoot}/{ArchipelagoMap.Id}.unity",
          $"{ScenesRoot}/{StraitMap.Id}.unity",
        }
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

    /// <summary>
    /// 항목 24: 물 판(운하 조각, 외곽 수로 띠)과 수문 영역을 레이어 4에 만들고, 그 레이어만 모으는 수상 NavMesh("WaterNavMesh")를 둔다.
    /// 수로가 없는 맵은 수상 NavMesh를 지운다.
    /// </summary>
    static void BuildWater(QuantumMapData map, MapSpec spec) {
      var old = GameObject.Find("Water");
      if (old != null) {
        Object.DestroyImmediate(old);
      }
      var surfaceObject = GameObject.Find("WaterNavMesh");
      var channel = spec.Channel ?? new MapShape[0];
      if (channel.Length == 0 && (!spec.SealedPerimeter || spec.River == null || spec.River.Length == 0)) {
        if (surfaceObject != null) {
          Object.DestroyImmediate(surfaceObject);
        }
        return;
      }
      var root = new GameObject("Water").transform;
      root.gameObject.layer = WaterLayer;
      var reach = spec.BoundsMax.X.AsFloat + spec.ChannelWidth.AsFloat + 4f;
      void Plate(string name, MapShape shape) {
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = name;
        plate.layer = WaterLayer;
        Object.DestroyImmediate(plate.GetComponent<Collider>());
        plate.transform.SetParent(root, false);
        plate.transform.position = new Vector3(shape.Center.X.AsFloat, -0.1f, shape.Center.Z.AsFloat);
        plate.transform.rotation = Quaternion.Euler(0, shape.Yaw.AsFloat, 0);
        plate.transform.localScale = new Vector3(shape.HalfLength.AsFloat * 2, 0.2f, shape.HalfWidth.AsFloat * 2);
      }
      foreach (var piece in spec.River ?? new MapShape[0]) {
        if (Mathf.Abs(piece.Center.X.AsFloat) < reach && Mathf.Abs(piece.Center.Z.AsFloat) < reach) {
          Plate("Canal", piece);
        }
      }
      foreach (var strip in channel) {
        Plate("Channel", strip);
      }
      if (spec.SealedPerimeter) SealWaterNavMesh(root, spec);
      var gates = spec.WaterGates ?? new MapShape[0];
      var perSeat = Mathf.Max(1, gates.Length / Mathf.Max(1, spec.BaseAreas?.Length ?? 1));
      for (var i = 0; i < gates.Length; i++) {
        AddRegion(root, "WaterGate", gates[i], WaterGateArea, $"{WaterGateRegion}{i / perSeat}");
      }
      foreach (Transform child in root) {
        child.gameObject.layer = WaterLayer;
      }
      if (surfaceObject == null) {
        surfaceObject = new GameObject("WaterNavMesh");
        surfaceObject.transform.SetParent(map.transform, false);
        surfaceObject.AddComponent<NavMeshSurface>();
        surfaceObject.AddComponent<QuantumMapNavMeshUnity>().NavMeshSurfaces = new[] { surfaceObject };
      }
      var surface = surfaceObject.GetComponent<NavMeshSurface>();
      surface.layerMask = 1 << WaterLayer;
      surface.collectObjects = CollectObjects.All;
      var quantumNavMesh = surfaceObject.GetComponent<QuantumMapNavMeshUnity>();
      // 새로 붙인 컴포넌트는 가져오기 설정이 비어 있다. 지상 NavMesh 설정을 복사해 쓴다.
      var ground = GameObject.Find("NavMesh")?.GetComponent<QuantumMapNavMeshUnity>();
      var settings = quantumNavMesh.Settings ?? (ground != null && ground.Settings != null
        ? JsonUtility.FromJson<QuantumNavMesh.ImportSettings>(JsonUtility.ToJson(ground.Settings)) : new QuantumNavMesh.ImportSettings());
      settings.ImportRegionMode = NavmeshRegionImportMode.Advanced;
      settings.RegionAreaIds = new List<int> { WaterGateArea };
      quantumNavMesh.Settings = settings;
    }

    static void SealWaterNavMesh(Transform root, MapSpec spec) {
      var min = new Vector2(spec.BoundsMin.X.AsFloat, spec.BoundsMin.Y.AsFloat);
      var max = new Vector2(spec.BoundsMax.X.AsFloat, spec.BoundsMax.Y.AsFloat);
      const float outside = 32f;
      void Barrier(Vector2 center, Vector2 size) {
        var edge = new GameObject("Water Boundary");
        edge.layer = WaterLayer;
        edge.transform.SetParent(root, false);
        edge.transform.localPosition = new Vector3(center.x, 0f, center.y);
        var volume = edge.AddComponent<NavMeshModifierVolume>();
        volume.area = 1;
        volume.size = new Vector3(size.x, 8f, size.y);
      }
      var middle = (min + max) * 0.5f;
      var span = max - min + Vector2.one * (2f * outside);
      Barrier(new Vector2(min.x - outside / 2, middle.y), new Vector2(outside, span.y));
      Barrier(new Vector2(max.x + outside / 2, middle.y), new Vector2(outside, span.y));
      Barrier(new Vector2(middle.x, min.y - outside / 2), new Vector2(span.x, outside));
      Barrier(new Vector2(middle.x, max.y + outside / 2), new Vector2(span.x, outside));
    }

    static void EnsureNavMesh(QuantumMapData map) {
      var navObject = GameObject.Find("NavMesh");
      if (navObject == null) {
        navObject = new GameObject("NavMesh");
        navObject.transform.SetParent(map.transform, false);
        navObject.AddComponent<NavMeshSurface>();
        navObject.AddComponent<QuantumMapNavMeshUnity>().NavMeshSurfaces = new[] { navObject };
      }
      // 항목 24: 지상 NavMesh는 물 판(레이어 4)을 모으지 않는다.
      if (navObject.GetComponent<NavMeshSurface>() == null) {
        navObject.AddComponent<NavMeshSurface>();
      }
      navObject.GetComponent<NavMeshSurface>().layerMask = ~(1 << WaterLayer);
      // QuantumNavMeshRegion을 쓰려면 Advanced 모드여야 한다. Simple은 Unity 영역 이름으로 만든다.
      var quantumNavMesh = navObject.GetComponent<QuantumMapNavMeshUnity>();
      if (quantumNavMesh == null) {
        // 새 Quantum 씬 틀이 "NavMesh" 오브젝트를 이미 갖고 있으면(항목 35 새 맵) 우리 컴포넌트만 더한다.
        quantumNavMesh = navObject.AddComponent<QuantumMapNavMeshUnity>();
        quantumNavMesh.NavMeshSurfaces = new[] { navObject };
      }
      var settings = quantumNavMesh.Settings ?? new QuantumNavMesh.ImportSettings();
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
