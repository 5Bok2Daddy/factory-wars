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
    const string ScenesRoot = "Assets/QuantumUser/Scenes";
    const string ScenePath = ScenesRoot + "/QuantumGameScene.unity";
    const string MapRoot = "Assets/QuantumUser/Resources";

    [MenuItem("Tools/Factory Wars/Run All Setup")]
    public static void RunAll() {
      ConfigurePlayer();
      ConfigureRenderPipeline();
      ConfigureQuantum();
      BuildData();
      BuildTestScene();
      BuildMapScene(TwoPlayerMap);
      BuildMapScene(FourPlayerMap);
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
      session.Config.LockstepSimulation = false;
      session.Config.UpdateFPS = 20;
      // 단위는 틱. 20Hz에서 4틱 = 200ms.
      session.Config.InputDelayMin = 4;
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

    static FactionSpec BuildFaction(string factionId, string prefix, Dictionary<string, UnitSpec> units, double ranged, double smallBuild, double populationCap, string[] startingUnits) {
      UnitSpec U(string id) => units[$"{prefix}_{id}"];
      var ids = units.Keys.Where(k => k.StartsWith(prefix + "_")).ToArray();
      var engineer = units[$"{prefix}_engineer"];
      var small = ids.Select(k => units[k]).Where(u => u.Size == UnitSize.Small).ToArray();
      var mediumGround = ids.Select(k => units[k]).Where(u => u.Size == UnitSize.Medium && u.IsAir == false).ToArray();
      var air = ids.Select(k => units[k]).Where(u => u.IsAir).ToArray();
      var large = ids.Select(k => units[k]).Where(u => u.Size == UnitSize.Large && u.IsAir == false).ToArray();

      var buildings = new[] {
        BuildBuilding($"{prefix}_headquarters", R(0), 0, 3000, 10, 0, engineer),
        BuildBuilding($"{prefix}_power_plant", R(150, 150), 15, 600, 0, 0),
        BuildBuilding($"{prefix}_processor", R(250, 150, 0, 100), 20, 800, 0, 0),
        BuildBuilding($"{prefix}_supply_depot", R(100, 200), 15, 500, 20, 0),
        BuildBuilding($"{prefix}_barracks", R(200, 200), 20, 800, 0, 0, small),
        BuildBuilding($"{prefix}_research_lab", R(250, 200, 0, 150), 25, 700, 0, 0),
        BuildBuilding($"{prefix}_vehicle_factory", R(400, 200, 0, 200), 30, 1200, 0, 0, mediumGround),
        BuildBuilding($"{prefix}_airfield", R(400, 0, 100, 300), 35, 1000, 0, 0, air),
        BuildBuilding($"{prefix}_heavy_works", R(500, 0, 200, 400), 45, 1600, 0, 0, large),
        BuildBuilding($"{prefix}_defense_tower", R(250, 100, 0, 100), 20, 700, 0, 1),
        BuildBuilding($"{prefix}_collection_yard", R(0, 150), 15, 500, 0, 0),
      };
      void Requires(int building, params int[] prerequisites) {
        buildings[building].Prerequisites = prerequisites.Select(i => Ref(buildings[i])).ToArray();
      }
      // 순서: 0 사령부, 1 발전소, 2 가공소, 3 보급창, 4 병영, 5 연구소, 6 군수공장, 7 비행장, 8 중공업 기지, 9 방어탑, 10 집하장
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

    static AiSpec BuildAi(string id, double think, int engineers, int variety, int maxYards, double firstAttack, double repeat,
        int wave, int growth, int maxWave, bool strategic, BuildingRole[] order) {
      var ai = Asset<AiSpec>(id);
      ai.ThinkSeconds = N(think);
      ai.EngineerTarget = engineers;
      ai.BuildOrder = order;
      ai.MaxYards = maxYards;
      ai.ExpandDistance = 30;
      ai.ConcurrentBuilds = 1;
      ai.QueueTarget = 2;
      ai.UnitVariety = variety;
      ai.FirstAttackSeconds = N(firstAttack);
      ai.AttackRepeatSeconds = N(repeat);
      ai.WaveSize = wave;
      ai.WaveGrowth = growth;
      ai.MaxWaveSize = maxWave;
      ai.PowerMargin = 1;
      ai.PopulationMargin = 4;
      ai.SiteGrid = 10;
      ai.SiteRings = 5;
      ai.StrategicDiplomacy = strategic;
      ai.AcceptChance = N(0.5);
      ai.ProposalIntervalSeconds = 90;
      ai.DiplomacyFundsReserve = 1200;
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

      BuildFaction("free_federation", "ff", units, 1.1, 1, 1, new[] { "ranger_squad", "ranger_squad", "eagle_tank" });
      BuildFaction("continental_federation", "cf", units, 1, 0.75, 1.2, new[] { "militia", "militia", "dragonscale_tank" });

      var rules = Asset<GameRulesSpec>("game_rules");
      rules.NavMeshName = "NavMesh";
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
      rules.ResourceNodeRadius = 2;
      rules.RepairPerSecond = 20;
      rules.RepairCostRatio = N(0.5);
      rules.FoundationHealthRatio = N(0.1);
      rules.EasyAi = Ref(BuildAi("ai_easy", 5, 3, 1, 1, 420, 90, 6, 2, 12, false, new[] {
        BuildingRole.PowerPlant, BuildingRole.Processor, BuildingRole.Barracks, BuildingRole.SupplyDepot, BuildingRole.VehicleFactory, BuildingRole.PowerPlant,
      }));
      rules.NormalAi = Ref(BuildAi("ai_normal", 2, 6, 3, 2, 270, 60, 8, 3, 24, true, new[] {
        BuildingRole.PowerPlant, BuildingRole.Processor, BuildingRole.Barracks, BuildingRole.SupplyDepot, BuildingRole.Processor, BuildingRole.VehicleFactory,
        BuildingRole.PowerPlant, BuildingRole.ResearchLab, BuildingRole.Airfield, BuildingRole.SupplyDepot, BuildingRole.HeavyWorks, BuildingRole.DefenseTower,
      }));
      rules.UnitClearance = N(0.5);
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
        Mine(new Vector2Int(-30, 14), 5000),
        Mine(new Vector2Int(-30, -14), 100),
        Forest(new Vector2Int(-38, 30), 8, 3, 0, 3000),
      };
      EditorUtility.SetDirty(testMap);

      BuildMapSpec(TwoPlayerMap);
      BuildMapSpec(FourPlayerMap);

      AssetDatabase.SaveAssets();
      QuantumUnityDBUtilities.RefreshGlobalDB(true);
    }

    struct Obstacle {
      public Vector2Int Position;
      public Vector3 Size;
      public float Yaw;
    }

    /// <summary>
    /// 한 플레이어 몫의 배치만 적고 맵 중심 회전으로 나머지를 만든다. 정수 좌표라 회전해도 대칭이 정확하다.
    /// </summary>
    class MapDef {
      public string Id;
      public int Players;
      public int HalfSize;
      public Vector2Int Start;
      public Vector2Int[] BaseNodes;
      public Vector2Int[] ExpansionNodes;
      public int BaseFunds, ExpansionFunds, CenterFunds;
      public Obstacle[] Obstacles;
      public Vector3 CameraPosition;
    }

    static readonly MapDef TwoPlayerMap = new MapDef {
      Id = "Map2P", Players = 2, HalfSize = 80, Start = new Vector2Int(-55, -55),
      BaseNodes = new[] { new Vector2Int(-43, -55), new Vector2Int(-55, -43) },
      ExpansionNodes = new[] { new Vector2Int(-54, 50), new Vector2Int(-46, 50) },
      BaseFunds = 5000, ExpansionFunds = 4000, CenterFunds = 8000,
      Obstacles = new[] {
        new Obstacle { Position = new Vector2Int(-26, -26), Size = new Vector3(44, 6, 8), Yaw = 45 },
        new Obstacle { Position = new Vector2Int(-24, 24), Size = new Vector3(10, 6, 10) },
      },
      CameraPosition = new Vector3(0, 130, -105),
    };

    static readonly MapDef FourPlayerMap = new MapDef {
      Id = "Map4P", Players = 4, HalfSize = 120, Start = new Vector2Int(-90, -90),
      BaseNodes = new[] { new Vector2Int(-78, -90), new Vector2Int(-90, -78) },
      ExpansionNodes = new[] { new Vector2Int(-4, -90), new Vector2Int(4, -90) },
      BaseFunds = 5000, ExpansionFunds = 4000, CenterFunds = 12000,
      Obstacles = new[] {
        new Obstacle { Position = new Vector2Int(-45, -90), Size = new Vector3(6, 6, 34) },
        new Obstacle { Position = new Vector2Int(45, -90), Size = new Vector3(6, 6, 34) },
        new Obstacle { Position = new Vector2Int(-40, -40), Size = new Vector3(44, 6, 8), Yaw = 45 },
      },
      CameraPosition = new Vector3(0, 190, -155),
    };

    /// <summary>맵 중심 기준으로 90도씩 quarterTurns번 돌린다.</summary>
    static Vector2Int Turn(Vector2Int p, int quarterTurns) {
      for (var i = 0; i < quarterTurns; i++) {
        p = new Vector2Int(-p.y, p.x);
      }
      return p;
    }

    /// <summary>플레이어 수만큼 회전 대칭 복사본을 돌려준다. 2인은 180도, 4인은 90도 간격.</summary>
    static IEnumerable<(Vector2Int Position, int QuarterTurns)> Orbit(MapDef def, Vector2Int p) {
      var step = 4 / def.Players;
      for (var i = 0; i < def.Players; i++) {
        yield return (Turn(p, i * step), i * step);
      }
    }

    static FPVector3 Position(Vector2Int p) => new FPVector3(p.x, 0, p.y);

    static ResourceNodePlacement Mine(Vector2Int p, int amount) {
      return new ResourceNodePlacement { Kind = ResourceKind.Iron, Amount = amount, Shape = new MapShape { Center = Position(p), Radius = 3 } };
    }

    static ResourceNodePlacement Forest(Vector2Int p, int halfLength, int halfWidth, int yaw, int amount) {
      return new ResourceNodePlacement {
        Kind = ResourceKind.Wood, Amount = amount, Shape = new MapShape { Center = Position(p), HalfLength = halfLength, HalfWidth = halfWidth, Yaw = yaw },
      };
    }

    static void BuildMapSpec(MapDef def) {
      var spec = Asset<MapSpec>($"map_{def.Id}");
      spec.MaxPlayers = def.Players;
      spec.BoundsMin = new FPVector2(-def.HalfSize, -def.HalfSize);
      spec.BoundsMax = new FPVector2(def.HalfSize, def.HalfSize);
      spec.StartPositions = Orbit(def, def.Start).Select(o => Position(o.Position)).ToArray();
      // 임시: AI가 목재·강철을 다루는 3-0i 전까지 넉넉히 준다. 3-0i에서 설계값(철 500, 목재 300)으로 바꾼다.
      spec.StartingResources = R(5000, 20000, 20000, 20000);
      spec.StartingEngineers = 4;

      var nodes = new List<ResourceNodePlacement>();
      void Add(Vector2Int[] template, int funds) {
        foreach (var p in template) {
          nodes.AddRange(Orbit(def, p).Select(o => Mine(o.Position, funds)));
        }
      }
      Add(def.BaseNodes, def.BaseFunds);
      Add(def.ExpansionNodes, def.ExpansionFunds);
      nodes.Add(Mine(Vector2Int.zero, def.CenterFunds));
      spec.ResourceNodes = nodes.ToArray();
      EditorUtility.SetDirty(spec);
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

      var old = GameObject.Find("Obstacles");
      if (old != null) {
        Object.DestroyImmediate(old);
      }
      var root = new GameObject("Obstacles");
      foreach (var obstacle in def.Obstacles) {
        foreach (var (position, quarterTurns) in Orbit(def, obstacle.Position)) {
          var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
          block.name = "Rock";
          block.transform.SetParent(root.transform, false);
          block.transform.position = new Vector3(position.x, obstacle.Size.y / 2, position.y);
          block.transform.rotation = Quaternion.Euler(0, obstacle.Yaw - 90 * quarterTurns, 0);
          block.transform.localScale = obstacle.Size;
          var modifier = block.AddComponent<NavMeshModifier>();
          modifier.overrideArea = true;
          modifier.area = 1; // Not Walkable
        }
      }

      var camera = Camera.main;
      camera.transform.position = def.CameraPosition;
      camera.transform.rotation = Quaternion.Euler(50, 0, 0);

      ConfigureRunner(def.Players);

      var spec = AssetDatabase.LoadAssetAtPath<MapSpec>($"{DataRoot}/map_{def.Id}.asset");
      Bake(map, spec, scene);
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

      ConfigureRunner(2);
      Bake(map, AssetDatabase.LoadAssetAtPath<MapSpec>($"{DataRoot}/map_test.asset"), scene);
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
      if (GameObject.Find("NavMesh") != null) {
        return;
      }
      var navObject = new GameObject("NavMesh");
      navObject.transform.SetParent(map.transform, false);
      navObject.AddComponent<NavMeshSurface>();
      navObject.AddComponent<QuantumMapNavMeshUnity>().NavMeshSurfaces = new[] { navObject };
    }

    static void EnsureDebugView() {
      if (Object.FindFirstObjectByType<FactoryWarsDebugView>() == null) {
        new GameObject("FactoryWarsDebugView").AddComponent<FactoryWarsDebugView>();
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
        .Select(i => new RuntimePlayer { PlayerNickname = $"{names[i % 2]}{i}", Faction = Ref(factions[i % 2]), Team = i })
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
