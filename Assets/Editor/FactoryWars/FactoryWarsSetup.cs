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
  public static class FactoryWarsSetup {
    const string AppId = "b0a33dd2-3c99-455f-ad7e-9c82b8a4cd40";
    const string DataRoot = "Assets/QuantumUser/Resources/FactoryWars";
    const string RenderRoot = "Assets/Settings";
    const string ScenePath = "Assets/QuantumUser/Scenes/QuantumGameScene.unity";

    [MenuItem("Tools/Factory Wars/Run All Setup")]
    public static void RunAll() {
      ConfigurePlayer();
      ConfigureRenderPipeline();
      ConfigureQuantum();
      BuildData();
      BuildTestScene();
      AssetDatabase.SaveAssets();
      Debug.Log("FactoryWarsSetup: 완료");
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

    public static void ConfigureRenderPipeline() {
      Directory.CreateDirectory(RenderRoot);
      var path = $"{RenderRoot}/FactoryWarsURP.asset";
      var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
      if (pipeline == null) {
        var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(renderer, $"{RenderRoot}/FactoryWarsURP_Renderer.asset");
        pipeline = UniversalRenderPipelineAsset.Create(renderer);
        AssetDatabase.CreateAsset(pipeline, path);
      }
      GraphicsSettings.defaultRenderPipeline = pipeline;
      var current = QualitySettings.GetQualityLevel();
      for (var i = 0; i < QualitySettings.names.Length; i++) {
        QualitySettings.SetQualityLevel(i, false);
        QualitySettings.renderPipeline = pipeline;
      }
      QualitySettings.SetQualityLevel(current, false);
    }

    public static void ConfigureQuantum() {
      var photon = PhotonServerSettings.Global;
      photon.AppSettings.AppIdQuantum = AppId;
      EditorUtility.SetDirty(photon);

      var session = QuantumDeterministicSessionConfigAsset.Global;
      session.Config.LockstepSimulation = true;
      session.Config.UpdateFPS = 20;
      session.Config.InputDelayMin = 10;
      EditorUtility.SetDirty(session);

      var simulation = QuantumDefaultConfigs.Global.SimulationConfig;
      simulation.Navigation.EnableNavigationCallbacks = true;
      EditorUtility.SetDirty(simulation);
    }

    static T Asset<T>(string name) where T : AssetObject {
      Directory.CreateDirectory(DataRoot);
      var path = $"{DataRoot}/{name}.asset";
      var asset = AssetDatabase.LoadAssetAtPath<T>(path);
      if (asset == null) {
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
      }
      return asset;
    }

    static AssetRef<T> Ref<T>(T asset) where T : AssetObject {
      return new AssetRef<T>(QuantumUnityDBUtilities.GetExpectedAssetGuid(new LazyLoadReference<Object>(asset), out _));
    }

    static FP N(double value) => FP.FromFloat_UNSAFE((float)value);

    struct UnitRow {
      public string Id;
      public UnitSize Size;
      public bool Air;
      public int Cost, Build, Health;
      public AttackType Attack;
      public double Damage, Range, MinRange, Splash, Interval, Speed;
      public int Spawn;
      public SpecialAbility Ability;
    }

    // GDD 값: 비용·생산·체력·공격 유형. 피해·사거리·간격·속도는 임시값(SETUP-REPORT.md 참조).
    static readonly UnitRow[] Units = {
      new UnitRow { Id = "ff_engineer", Size = UnitSize.Engineer, Cost = 100, Build = 10, Health = 80, Attack = AttackType.None, Speed = 4, Ability = SpecialAbility.EngineerWork },
      new UnitRow { Id = "ff_ranger_squad", Size = UnitSize.Small, Cost = 120, Build = 15, Health = 140, Attack = AttackType.AntiInfantry, Damage = 15, Range = 7, Interval = 1, Speed = 4, Ability = SpecialAbility.TargetDesignator },
      new UnitRow { Id = "ff_skyguard_squad", Size = UnitSize.Small, Cost = 150, Build = 18, Health = 120, Attack = AttackType.AntiAir, Damage = 20, Range = 8, Interval = 1, Speed = 4, Ability = SpecialAbility.AirPriority },
      new UnitRow { Id = "ff_eagle_tank", Size = UnitSize.Medium, Cost = 550, Build = 35, Health = 700, Attack = AttackType.AntiArmor, Damage = 60, Range = 9, Interval = 2, Speed = 5, Ability = SpecialAbility.FirstShotDouble },
      new UnitRow { Id = "ff_hawk_gunship", Size = UnitSize.Medium, Air = true, Cost = 650, Build = 40, Health = 450, Attack = AttackType.AntiArmor, Damage = 55, Range = 8, Interval = 2, Speed = 7, Ability = SpecialAbility.GroundArmorResist },
      new UnitRow { Id = "ff_skyfortress_bomber", Size = UnitSize.Large, Air = true, Cost = 1600, Build = 70, Health = 1600, Attack = AttackType.Area, Damage = 200, Range = 4, Splash = 4, Interval = 5, Speed = 5, Ability = SpecialAbility.CarpetBomb },
      new UnitRow { Id = "cf_engineer", Size = UnitSize.Engineer, Cost = 80, Build = 8, Health = 70, Attack = AttackType.None, Speed = 4, Ability = SpecialAbility.EngineerWork },
      new UnitRow { Id = "cf_militia", Size = UnitSize.Small, Cost = 90, Build = 12, Health = 80, Attack = AttackType.AntiInfantry, Damage = 10, Range = 6, Interval = 1, Speed = 4, Spawn = 2, Ability = SpecialAbility.MilitiaPair },
      new UnitRow { Id = "cf_flame_squad", Size = UnitSize.Small, Cost = 130, Build = 15, Health = 130, Attack = AttackType.AntiInfantry, Damage = 20, Range = 3, Splash = 2, Interval = 1, Speed = 4, Ability = SpecialAbility.FlameBurst },
      new UnitRow { Id = "cf_dragonscale_tank", Size = UnitSize.Medium, Cost = 400, Build = 28, Health = 520, Attack = AttackType.AntiArmor, Damage = 45, Range = 8, Interval = 2, Speed = 5, Ability = SpecialAbility.SwarmSpeed },
      new UnitRow { Id = "cf_thunder_aa", Size = UnitSize.Medium, Cost = 380, Build = 28, Health = 450, Attack = AttackType.AntiAir, Damage = 40, Range = 10, Interval = 1.5, Speed = 5, Ability = SpecialAbility.MultiAirTarget },
      new UnitRow { Id = "cf_longwall_mlrs", Size = UnitSize.Large, Cost = 1200, Build = 55, Health = 1000, Attack = AttackType.Area, Damage = 120, Range = 18, MinRange = 6, Splash = 4, Interval = 6, Speed = 3, Ability = SpecialAbility.LongRangeBarrage },
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
      EditorUtility.SetDirty(spec);
      return spec;
    }

    static BuildingSpec BuildBuilding(string id, int cost, int seconds, int health, int powerOut, int powerIn, int population, int slots, params UnitSpec[] producible) {
      var spec = Asset<BuildingSpec>(id);
      spec.Cost = cost;
      spec.BuildTimeSeconds = seconds;
      spec.MaxHealth = health;
      spec.PowerProduced = powerOut;
      spec.PowerConsumed = powerIn;
      spec.PopulationCapBonus = population;
      spec.ModuleSlots = slots;
      spec.Producible = producible.Select(Ref).ToArray();
      spec.Attack = AttackType.None;
      EditorUtility.SetDirty(spec);
      return spec;
    }

    static FactionSpec BuildFaction(string factionId, string prefix, Dictionary<string, UnitSpec> units, double ranged, double smallBuild, double populationCap, string[] startingUnits) {
      UnitSpec U(string id) => units[$"{prefix}_{id}"];
      var ids = units.Keys.Where(k => k.StartsWith(prefix + "_")).ToArray();
      var engineer = units[$"{prefix}_engineer"];
      var small = ids.Select(k => units[k]).Where(u => u.Size == UnitSize.Small).ToArray();
      var mediumGround = ids.Select(k => units[k]).Where(u => u.Size == UnitSize.Medium && u.IsAir == false).ToArray();
      var air = ids.Select(k => units[k]).Where(u => u.IsAir).ToArray();
      var large = ids.Select(k => units[k]).Where(u => u.Size == UnitSize.Large && u.IsAir == false).ToArray();

      var buildings = new[] {
        BuildBuilding($"{prefix}_headquarters", 0, 0, 3000, 50, 0, 10, 0, engineer),
        BuildBuilding($"{prefix}_power_plant", 300, 15, 600, 100, 0, 0, 0),
        BuildBuilding($"{prefix}_refinery", 500, 20, 800, 0, 20, 0, 0),
        BuildBuilding($"{prefix}_supply_depot", 300, 15, 500, 0, 5, 20, 0),
        BuildBuilding($"{prefix}_barracks", 400, 20, 800, 0, 10, 0, 0, small),
        BuildBuilding($"{prefix}_research_lab", 600, 25, 700, 0, 20, 0, 0),
        BuildBuilding($"{prefix}_vehicle_factory", 900, 30, 1200, 0, 25, 0, 0, mediumGround),
        BuildBuilding($"{prefix}_airfield", 1100, 35, 1000, 0, 30, 0, 0, air),
        BuildBuilding($"{prefix}_heavy_works", 1500, 45, 1600, 0, 40, 0, 0, large),
        BuildBuilding($"{prefix}_defense_tower", 450, 20, 700, 0, 15, 0, 1),
      };
      var tower = buildings[buildings.Length - 1];
      tower.Attack = AttackType.AntiArmor;
      tower.Damage = 40;
      tower.Range = 10;
      tower.AttackInterval = N(1.5);
      EditorUtility.SetDirty(tower);

      var faction = Asset<FactionSpec>($"faction_{factionId}");
      faction.FactionId = factionId;
      faction.RangedRangeMultiplier = N(ranged);
      faction.SmallBuildTimeMultiplier = N(smallBuild);
      faction.PopulationCapMultiplier = N(populationCap);
      faction.Headquarters = Ref(buildings[0]);
      faction.Engineer = Ref(engineer);
      faction.Buildings = buildings.Skip(1).Select(Ref).ToArray();
      faction.Units = ids.Select(k => Ref(units[k])).ToArray();
      faction.StartingUnits = startingUnits.Select(id => Ref(U(id))).ToArray();
      EditorUtility.SetDirty(faction);
      return faction;
    }

    public static void BuildData() {
      var units = Units.ToDictionary(row => row.Id, BuildUnit);

      var piercing = Asset<ModuleSpec>("module_piercing_rounds");
      piercing.Effect = ModuleEffect.PiercingRounds;
      piercing.Cost = 100;
      piercing.DamageMultiplier = N(1.25);
      EditorUtility.SetDirty(piercing);

      var reactive = Asset<ModuleSpec>("module_reactive_armor");
      reactive.Effect = ModuleEffect.ReactiveArmor;
      reactive.Cost = 120;
      reactive.DamageMultiplier = N(0.5);
      reactive.CooldownSeconds = 20;
      EditorUtility.SetDirty(reactive);

      BuildFaction("free_federation", "ff", units, 1.1, 1, 1, new[] { "ranger_squad", "ranger_squad", "eagle_tank" });
      BuildFaction("continental_federation", "cf", units, 1, 0.75, 1.2, new[] { "militia", "militia", "dragonscale_tank" });

      var rules = Asset<GameRulesSpec>("game_rules");
      rules.StartingFunds = 5000;
      rules.StartingEngineers = 1;
      rules.StartPositions = new[] {
        new FPVector3(-30, 0, 0), new FPVector3(30, 0, 0), new FPVector3(0, 0, 30), new FPVector3(0, 0, -30),
      };
      rules.NavMeshName = "NavMesh";
      rules.ProposalSeconds = 30;
      rules.BreakGraceSeconds = 10;
      rules.RealliableSeconds = 60;
      rules.NoteCooldownSeconds = 5;
      rules.NotePhraseCount = 12;
      rules.LowPowerProductionMultiplier = N(0.5);
      rules.LowPowerTowerMultiplier = N(0.5);
      rules.SpawnOffset = 6;
      rules.AirAltitude = 6;
      EditorUtility.SetDirty(rules);

      AssetDatabase.SaveAssets();
      QuantumUnityDBUtilities.RefreshGlobalDB(true);
    }

    public static void BuildTestScene() {
      var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
      var map = Object.FindFirstObjectByType<QuantumMapData>();

      var ground = GameObject.Find("Ground");
      if (ground == null) {
        ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(10, 1, 10);
      }

      var navObject = GameObject.Find("NavMesh");
      if (navObject == null) {
        navObject = new GameObject("NavMesh");
        navObject.transform.SetParent(map.transform, false);
        navObject.AddComponent<NavMeshSurface>();
        var navUnity = navObject.AddComponent<QuantumMapNavMeshUnity>();
        navUnity.NavMeshSurfaces = new[] { navObject };
      }

      if (Object.FindFirstObjectByType<FactoryWarsDebugView>() == null) {
        new GameObject("FactoryWarsDebugView").AddComponent<FactoryWarsDebugView>();
      }

      var camera = Camera.main;
      if (camera != null) {
        camera.transform.position = new Vector3(0, 55, -45);
        camera.transform.rotation = Quaternion.Euler(50, 0, 0);
      }

      var rules = AssetDatabase.LoadAssetAtPath<GameRulesSpec>($"{DataRoot}/game_rules.asset");
      var free = AssetDatabase.LoadAssetAtPath<FactionSpec>($"{DataRoot}/faction_free_federation.asset");
      var continental = AssetDatabase.LoadAssetAtPath<FactionSpec>($"{DataRoot}/faction_continental_federation.asset");

      var runner = Object.FindFirstObjectByType<QuantumRunnerLocalDebug>();
      runner.RuntimeConfig.Rules = Ref(rules);
      runner.RuntimeConfig.JointVictory = true;
      runner.MaxPlayerCount = 2;
      runner.LocalPlayers = new[] {
        new RuntimePlayer { PlayerNickname = "Free", Faction = Ref(free), Team = 0 },
        new RuntimePlayer { PlayerNickname = "Continental", Faction = Ref(continental), Team = 1 },
      };
      EditorUtility.SetDirty(runner);

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
