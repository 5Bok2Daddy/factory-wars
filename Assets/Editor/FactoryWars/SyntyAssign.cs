namespace FactoryWars.EditorTools {
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEditor.Animations;
  using UnityEngine;

  /// <summary>
  /// 구입한 Synty 팩 모델을 VisualCatalog 칸에 연결한다(항목 32-0·1·2, 사용자 결정 2026-11-14).
  /// 원칙: 팩 폴더(Assets/ExternalAssets/Synty)의 어떤 파일도 고치지 않는다. 읽기만 한다.
  /// - 진영 구분: 팩 안의 서로 다른 모델과 팩이 주는 색 변형 머티리얼을 고른다(우리 폴더의 프리팹 변형에서 머티리얼 참조만 바꾼다). 칠하기·부착물·키우기 없음.
  /// - 분대: 팩 캐릭터 프리팹 여러 개를 우리 프리팹 안에 중첩해 놓는다(캐릭터 파일은 그대로).
  /// - 걷기·달리기·웅크리기 애니메이션: 팩의 Base Locomotion 클립을 우리 Animator 컨트롤러에 연결한다.
  /// 결과물은 Assets/FactoryWarsArt/Synty(저장소에 올리지 않음)에 있고, 칸 연결만 VisualCatalog.asset에 남는다.
  /// 메뉴 Tools/Factory Wars/Assets/5. Assign Synty Pack, 배치: -executeMethod FactoryWars.EditorTools.SyntyAssign.Run
  /// 팩 파일 이름에는 실존 국가명이 들어 있는 것이 있어 코드에는 별칭과 GUID만 쓴다. 별칭 → 실제 파일 표는 Game-logs/assets/synty/slot-table.csv.
  /// </summary>
  public static partial class SyntyAssign {
    public const string PackRoot = "Assets/ExternalAssets/Synty";
    public const string OutRoot = "Assets/FactoryWarsArt/Synty";
    public const string ControllerPath = OutRoot + "/Animation/PackInfantry.controller";
    public const string WorkerControllerPath = OutRoot + "/Animation/AmbientWorker.controller";
    const string WeldingPrefabPath = "Assets/ExternalAssets/JMO Assets/WarFX/_Effects/Bullet Impacts/WFX_BImpact Metal.prefab";

    static readonly Dictionary<string, string> Pack = new Dictionary<string, string> {
      ["tank_a"] = "bfdfb82c9fb773c43ab776ec7ef9d363",
      ["tank_b"] = "9562f24f293ad134389b7d263ef42b9d",
      ["tank_c"] = "ea0d444837ec0b8459330243396f4c95",
      ["tank_light"] = "2c85954619f23494f9cf482f62ce3867",
      ["apc"] = "35d01d2cf8b8d12408e9773a94e25813",
      ["radar_tank"] = "4bf16b3eb89c6a640bc88203fdb53727",
      ["rocket_truck"] = "cf064357e7bb8f2489276c4b0e425db1",
      ["heli_a"] = "b108d85799ddb2148973fd94b0d538d6",
      ["heli_b"] = "95f1d29e34dd7f945a42e7ed594bee5c",
      ["transport_plane"] = "62adb5e2e2e9b78458385cff46277ff9",
      ["drone"] = "e7a1b80b0c1acd9478899577752e2f0f",
      ["buggy"] = "90a858f72a2fe1642a597df2b78b0b93",
      ["bike"] = "f80118c48fd56b44eb7776ff08001ba9",
      ["boat"] = "70fd0bd89e9f9144b982ec327a051c90",
      ["cargo_plane"] = "acaaf9ebbaefe2744b8efca7ac70d859",
      ["chr_contractor_1"] = "7808f0aeae535744d839b436592d7285",
      ["chr_contractor_2"] = "96e947df06e21f948a8c6ad8fc919f5d",
      ["chr_soldier_1"] = "a28775a11e80de44baa17ef63cd37f4b",
      ["chr_soldier_1_alt1"] = "3b83cbe22b3a0de4b836a9b21d1bc432",
      ["chr_soldier_1_alt2"] = "82bf002e6f0093b4486817df2ad566d1",
      ["chr_soldier_2"] = "7f745586162057b4ba599160c0885d7c",
      ["chr_soldier_2_alt1"] = "9eaeaec5e951a394c8158fffa41ec36e",
      ["chr_soldier_2_alt2"] = "686c27708b4bb8e48b8a4c79e61fb7bb",
      ["chr_soldier_2_alt3"] = "bf8ef63526b05c447b0cb44e4035ad06",
      ["chr_bombsuit"] = "d5047b6b50f166b40bec1a3fafed03d1",
      ["chr_ghillie"] = "90029427947776a4e96a6bc39fc00a85",
      ["wep_rifle"] = "fbe32035b0baf224186c9be363d01250",
      ["wep_launcher"] = "ceb546d0cae63de459e72391bb340828",
      ["wep_rpg"] = "cdc01cd9a5a3c5c438b1f0d4cb2ee3f3",
      ["bld_barracks"] = "ecbfecae04105374e96c367fbc9d49dc",
      ["bld_shed"] = "77430e9a204ca6b458f3a96c8946c6a6",
      ["bld_gas_tower_2"] = "2fe91475d3425dd42a702ab61573845e",
      ["bld_gas_tower_1"] = "723effe061735aa4e89b785c95e7ba15",
      ["bld_control_room"] = "f7927326215742e4485ae667e41cd170",
      ["bld_hangar"] = "a9e9e135da71ccc4493b0c4ae14bd703",
      ["bld_hangar_open"] = "45ed59faee1581b429e5f6f4ef4f70bc",
      ["bld_control_tower"] = "84d8c6ba71512d64683c775414d73d96",
      ["bld_gas_station"] = "e927b4c822e05b1458cfdf087756b34a",
      ["bld_guard_tower_2"] = "e80bc785a6eb0ae42b4812ad0cf387c5",
      ["bld_guard_tower_1"] = "7fa410500dabdde4cb375f3c7e442e92",
      ["bld_tent_open"] = "51d513146d10f39428c02c89bccbfdea",
      ["bld_tent_closed"] = "c2bb2fb18a13814499d432cabd1a54f0",
      ["bld_tent_shelter"] = "7b45a2bad65a6414793d27e45a4b87d9",
      ["bld_tent_cover"] = "f324ade2ab3e5ec4a84331d023a7d50e",
      ["bld_tent_sandbags"] = "a6177be75e0734247b4f446d0fe1db3a",
      ["bld_tent_entrance"] = "7d13503d7d052f74fa3dc51fd271aed8",
      ["bld_oil_tower"] = "3071c9508cae9ba4a81309637a8c3e81",
      ["prop_launcher_1"] = "94f609b70f27de84a9195dc833aa7424",
      ["prop_launcher_2"] = "9d1f1324f6d71b141b71faee86ce3bec",
      ["prop_barrier_1"] = "aa4d92eb8537c774e87a303615d4e04c",
      ["prop_barrier_2"] = "a1b4e8a719a023141b004ab174989f10",
      ["prop_radar"] = "5f19effbe79f5a44bbc16823c322f522",
      ["idle"] = "a4b8f2deec6a99945987cfc59b1b4e54",
      ["idle_crouch"] = "52a2eb3158000934eb33267c807b8b15",
      ["walk"] = "eead3eb07cb89cb4eb25b3550df5e84b",
      ["run"] = "6c670ea8c84cf324980a21b4b9b927a0",
      ["sprint"] = "d2995d8a62be0ce42a4e73698b62b275",
      ["crouch_move"] = "db828642e342e7a469d83568b41af133"
    };

    enum Kind { Squad, Vehicle, Building }

    class Row {
      public string Id;
      public Kind Kind;
      public string[] Models;
      public string[] Swap = new string[0];
      /// <summary>분대: 모델 배율. 건물: 가로(미터) 목표.</summary>
      public float Scale = 1f;
      public float Height = 9f;
      public string Turret;
      public string Barrel;
      public string Muzzle;
      public string[] Spin = new string[0];
    }

    // 진영별 색 변형(팩이 주는 머티리얼 이름). 앞 = 바꿀 것, 뒤 = 바꿔 넣을 것.
    static readonly string[] SoldiersFree = { "PolygonMilitary_01_A", "PolygonMilitary_02_A" };
    static readonly string[] BuildingsFree = { "PolygonMilitary_01_A", "PolygonMilitary_02_A" };
    static readonly string[] BuildingsContinental = { "PolygonMilitary_01_A", "PolygonMilitary_03_A" };
    static readonly string[] LandFree = { "Land_Vehicles_03", "Land_Vehicles_04" };
    static readonly string[] LandContinental = { "Land_Vehicles_03", "Land_Vehicles_05" };
    static readonly string[] HeliContinental = { "Helicopters_01_A", "Helicopters_01_C" };
    // 발사대·레이더 소품은 차량 아틀라스(01 Alt, 02)를 쓴다.
    static readonly string[] PropsFree = { "Land_Vehicles_01_Alt", "Land_Vehicles_04", "Land_Vehicles_02", "Land_Vehicles_04" };
    static readonly string[] PropsContinental = { "Land_Vehicles_01_Alt", "Land_Vehicles_05", "Land_Vehicles_02", "Land_Vehicles_05" };
    static readonly string[] LightFree = { "PolygonBattleRoyale_Veh_01", "PolygonBattleRoyale_Veh_06" };
    static readonly string[] LightContinental = { "PolygonBattleRoyale_Veh_01", "PolygonBattleRoyale_Veh_02" };

    static Row Squad(string id, float scale, string[] swap, params string[] members) => new Row { Id = "unit/" + id, Kind = Kind.Squad, Models = members, Swap = swap, Scale = scale };
    static Row Vehicle(string id, string model, string[] swap, float targetLength, string turret = null, string barrel = null, string muzzle = null, params string[] spin) =>
      new Row { Id = "unit/" + id, Kind = Kind.Vehicle, Models = new[] { model }, Swap = swap, Scale = targetLength, Turret = turret, Barrel = barrel, Muzzle = muzzle, Spin = spin };
    static Row Building(string id, string model, string[] swap, float width = 6.4f, float height = 9f) =>
      new Row { Id = "building/" + id, Kind = Kind.Building, Models = new[] { model }, Swap = swap, Scale = width, Height = height };

    static readonly string[] None = new string[0];

    static IEnumerable<Row> Rows() {
      // 모바일 최적 스케일 밸런스: 보병 시각 모델 0.8x(터치 선택 반경 1.0x+ 유지), 차량·전차 1.0~1.1x, 공장·건물 1.25x.
      yield return Squad("ff_engineer", 0.8f, SoldiersFree, "chr_contractor_1");
      yield return Squad("cf_engineer", 0.8f, None, "chr_contractor_2");
      yield return Squad("ff_ranger_squad", 0.8f, SoldiersFree, "chr_soldier_1");
      yield return Squad("ff_skyguard_squad", 0.8f, SoldiersFree, "chr_soldier_1");
      yield return Squad("cf_militia", 0.8f, None, "chr_soldier_2");
      yield return Squad("cf_flame_squad", 0.8f, None, "chr_bombsuit");
      yield return Squad("ff_at_rocket", 0.8f, SoldiersFree, "chr_soldier_1");
      yield return Squad("cf_at_rocket", 0.8f, None, "chr_soldier_2");
      yield return Squad("ff_breacher", 0.8f, SoldiersFree, "chr_bombsuit");
      yield return Squad("cf_breacher", 0.8f, None, "chr_bombsuit");
      yield return Squad("ff_sniper", 0.8f, SoldiersFree, "chr_ghillie");
      yield return Squad("cf_sniper", 0.8f, None, "chr_ghillie");
      yield return Squad("trader_guard", 0.8f, None, "chr_soldier_2_alt3");
      // 차량·전차·공중·해상: 1.0x~1.1x 전술 비율(0.8x 보병 대비 확연한 덩치 차이 + 1.25x 공장과 자연스러운 조화).
      yield return Vehicle("ff_scout_buggy", "buggy", LightFree, 4.2f);
      yield return Vehicle("cf_scout_bike", "bike", LightContinental, 3.3f);
      yield return Vehicle("ff_eagle_tank", "tank_c", LandFree, 6.0f, "Turret", "Barrel", "Barrel");
      yield return Vehicle("cf_dragonscale_tank", "tank_light", LandContinental, 6.0f, "Turret", "Barrel", "Barrel");
      yield return Vehicle("ff_bulwark_tank", "tank_a", LandFree, 7.0f, "Turret", "Barrel", "Barrel");
      yield return Vehicle("cf_fortress_tank", "tank_b", LandContinental, 7.0f, "Turret", "Barrel", "Barrel");
      yield return Vehicle("ff_field_support", "apc", LandFree, 5.0f);
      yield return Vehicle("cf_apc_transport", "apc", LandContinental, 5.0f);
      yield return Vehicle("cf_thunder_aa", "radar_tank", LandContinental, 5.0f, null, null, null, "Radar_Base:Y");
      yield return Vehicle("cf_longwall_mlrs", "rocket_truck", LandContinental, 6.6f, "_Base", null, "Rocket_Battery");
      yield return Vehicle("ff_mlrs_artillery", "rocket_truck", LandFree, 5.8f, "_Base", null, "Rocket_Battery");
      yield return Vehicle("cf_rocket_artillery", "rocket_truck", LandContinental, 5.8f, "_Base", null, "Rocket_Battery");
      yield return Vehicle("ff_mobile_sam", "radar_tank", LandFree, 5.0f, null, null, null, "Radar_Base:Y");
      yield return Vehicle("cf_mobile_sam", "radar_tank", LandContinental, 5.0f, null, null, null, "Radar_Base:Y");
      yield return Vehicle("ff_hawk_gunship", "heli_a", None, 6.4f, null, null, null, "Blades_Main", "Blades_Back");
      yield return Vehicle("cf_gale_gunship", "heli_b", HeliContinental, 6.4f, null, null, null, "Blades_Main", "Blades_Back");
      yield return Vehicle("ff_transport_heli", "heli_a", None, 6.6f, null, null, null, "Blades_Main", "Blades_Back");
      yield return Vehicle("cf_transport_heli", "heli_b", HeliContinental, 6.6f, null, null, null, "Blades_Main", "Blades_Back");
      yield return Vehicle("ff_skyfortress_bomber", "transport_plane", None, 9.0f);
      yield return Vehicle("ff_stormnest_carrier", "cargo_plane", None, 10.5f, null, null, null, "Prop_0");
      yield return Vehicle("ff_nest_drone", "drone", None, 1.8f, null, null, null, "Drone_Prop");
      yield return Vehicle("ff_scout_boat", "boat", LightFree, 5.0f);
      yield return Vehicle("cf_scout_boat", "boat", LightContinental, 5.0f);
      yield return Vehicle("ff_landing_craft", "boat", LightFree, 6.8f);
      yield return Vehicle("cf_landing_craft", "boat", LightContinental, 6.8f);
      // 공장·사령부·기지 건물: 1.25x 스케일(도로 이격 및 NavMesh 재굽기 기준).
      foreach (var faction in new[] { "ff", "cf" }) {
        var swap = faction == "ff" ? BuildingsFree : BuildingsContinental;
        yield return Building(faction + "_barracks", "bld_barracks", swap, 8.2f, 11.2f);
        yield return Building(faction + "_supply_depot", "bld_shed", swap, 8.0f, 11.0f);
        yield return Building(faction + "_power_plant", "bld_gas_tower_2", swap, 8.6f, 9.0f);
        yield return Building(faction + "_processor", "bld_gas_tower_1", swap, 10.0f, 7.8f);
        yield return Building(faction + "_research_lab", "bld_control_room", swap, 8.2f, 11.2f);
        yield return Building(faction + "_vehicle_factory", "bld_hangar", swap, 11.2f, 9.0f);
        yield return Building(faction + "_airfield", "bld_control_tower", swap, 8.4f, 12.8f);
        yield return Building(faction + "_heavy_works", "bld_gas_station", swap, 10.2f, 7.8f);
        yield return Building(faction + "_ground_tower", "bld_guard_tower_2", swap, 8.0f, 11.2f);
        var props = faction == "ff" ? PropsFree : PropsContinental;
        yield return Building(faction + "_air_defense", "prop_launcher_2", props, 8.0f, 11.2f);
        yield return Building(faction + "_collection_yard", "bld_tent_open", swap, 8.0f, 11.0f);
        yield return Building(faction + "_lumber_mill", "bld_tent_open", swap, 8.0f, 11.0f);
        yield return Building(faction + "_iron_extractor", "bld_gas_tower_1", swap, 10.0f, 7.8f);
        yield return Building(faction + "_sandbags", "prop_barrier_2", swap, 6.8f, 8.5f);
        yield return Building(faction + "_bunker", "prop_barrier_1", swap, 5.8f, 8.5f);
        yield return Building(faction + "_medic_tent", "bld_tent_closed", swap, 8.0f, 11.0f);
        yield return Building(faction + "_repair_bay", "bld_tent_shelter", swap, 8.0f, 11.0f);
        yield return Building(faction + "_radar", "prop_radar", props, 8.0f, 11.2f);
        yield return Building(faction + "_helipad", "bld_tent_cover", swap, 8.0f, 11.0f);
        yield return Building(faction + "_artillery", "prop_launcher_1", props, 8.0f, 11.2f);
        yield return Building(faction + "_patriot_turret", "prop_launcher_2", props, 8.0f, 11.2f);
        yield return Building(faction + "_headquarters", faction == "ff" ? "bld_tent_sandbags" : "bld_tent_entrance", swap, 10.0f, 11.8f);
      }
      yield return Building("ff_carrier_yard", "bld_hangar_open", BuildingsFree, 11.4f, 9.2f);
      yield return Building("cf_missile_silo", "bld_oil_tower", BuildingsContinental, 8.4f, 12.8f);
    }

    [MenuItem("Tools/Factory Wars/Assets/5. Assign Synty Pack")]
    public static void Menu() {
      var count = Assign();
      Debug.Log($"[FW-Synty] 칸 {count}개 연결");
    }

    public static void Run() {
      var count = Assign();
      Debug.Log($"[FW-Synty] 칸 {count}개 연결");
    }

    /// <summary>팩이 없으면 아무것도 하지 않고 0을 돌려준다(칸은 지금 모델 그대로).</summary>
    public static int Assign() {
      if (AssetDatabase.IsValidFolder(PackRoot + "/PolygonMilitary") == false) {
        Debug.LogWarning("[FW-Synty] 팩 없음: Tools/import_synty.py로 먼저 들여오세요.");
        return 0;
      }
      AssetDatabase.DeleteAsset(OutRoot + "/Variants");
      AssetDatabase.DeleteAsset(OutRoot + "/Prefabs");
      EnsureFolder(OutRoot + "/Variants");
      EnsureFolder(OutRoot + "/Prefabs");
      EnsureFolder(OutRoot + "/Animation");
      var catalog = VisualCatalogTools.BuildSlots();
      var controller = BuildController();
      var variants = new Dictionary<string, GameObject>();
      var csv = new StringBuilder("Id,Kind,Models(별칭),PackFiles(실제 파일),ColourSwap,Scale,Notes\n");
      var count = 0;
      foreach (var row in Rows()) {
        var slot = catalog.Slots.FirstOrDefault(s => s != null && s.Id == row.Id);
        if (slot == null) {
          slot = new VisualSlot { Id = row.Id };
          catalog.Slots.Add(slot);
        }
        var members = row.Models.Select(m => Member(m, row.Swap, variants)).ToArray();
        if (members.Any(m => m == null)) {
          Debug.LogWarning($"[FW-Synty] {row.Id}: 팩에 모델이 없어 건너뜀");
          continue;
        }
        GameObject prefab = members[0];
        slot.MembersByQuality = new int[0];
        if (row.Kind == Kind.Squad) {
          prefab = BuildSquad(row.Id, members);
          slot.MembersByQuality = new[] { 1, Mathf.Min(2, members.Length), members.Length };
        }
        // 크기·바닥 가운데는 켜져 있는 렌더러로 잰다(팩 캐릭터는 바꿔 끼우는 꺼진 메쉬가 많아 메쉬 바운드로는 치우친다).
        var (pivot, size) = MeasureActive(prefab);
        slot.Category = row.Id.StartsWith("unit/") ? VisualCategory.Unit : VisualCategory.Building;
        slot.Prefabs = new List<GameObject> { prefab };
        slot.PivotOffset = pivot;
        slot.Size = size;
        slot.YawOffset = 0f;
        slot.TeamColor = TeamColorMode.None;
        slot.TeamTextures = new Texture2D[0];
        slot.RealWorld = RealWorldCheck.Clear;
        slot.OwnerRing = row.Id.StartsWith("unit/");
        slot.Animator = row.Kind == Kind.Squad ? controller : null;
        slot.DeathSeconds = 0f;
        slot.TurretPart = row.Turret;
        slot.BarrelPart = row.Barrel;
        slot.SpinParts = row.Spin;
        slot.MuzzlePart = row.Muzzle;
        switch (row.Kind) {
          case Kind.Squad:
            slot.AutoFit = false;
            slot.Scale = row.Scale;
            slot.MuzzleOffset = new Vector3(0f, 1.2f, 0.7f);
            break;
          case Kind.Vehicle:
            slot.AutoFit = false;
            slot.Scale = row.Scale / Mathf.Max(0.1f, Mathf.Max(size.x, size.z));
            slot.MuzzleOffset = MuzzleOf(prefab, row.Muzzle, size);
            break;
          default:
            slot.AutoFit = false;
            slot.Scale = Mathf.Min(row.Scale / Mathf.Max(0.1f, Mathf.Max(size.x, size.z)), row.Height / Mathf.Max(0.1f, size.y));
            slot.MuzzleOffset = new Vector3(0f, size.y * 0.8f, 0f);
            break;
        }
        var look = row.Swap.Length >= 2 ? row.Swap[1] : "기본";
        slot.Notes = $"Synty 팩 {string.Join("+", row.Models)} / 색 변형 {look}";
        csv.AppendLine(string.Join(",", row.Id, row.Kind, string.Join("+", row.Models), string.Join("+", row.Models.Select(m => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(Pack[m])))), row.Swap.Length >= 2 ? row.Swap[1] : "", slot.Scale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), slot.Notes));
        count++;
      }
      BuildRealSyntyCompositesAndFx(catalog);
      catalog.Invalidate();
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
      var logs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "assets", "synty"));
      Directory.CreateDirectory(logs);
      File.WriteAllText(Path.Combine(logs, "slot-table.csv"), csv.ToString(), new UTF8Encoding(true));
      return count;
    }

    struct KitPart {
      public string Path;
      public Vector3 Pos;
      public Vector3 Rot;
      public Vector3 Scale;
      public KitPart(string path, Vector3 pos, Vector3 rot, float scale = 1f) {
        Path = path; Pos = pos; Rot = rot; Scale = Vector3.one * scale;
      }
      public KitPart(string path, Vector3 pos, Vector3 rot, Vector3 scale) {
        Path = path; Pos = pos; Rot = rot; Scale = scale;
      }
    }

    /// <summary>
    /// 100% 실제 Synty 4대 팩(Construction · Military · BattleRoyale · City) 프리팹으로 8대 산업 공장 · Lv.2/Lv.3 확장 · 사령부 · 미사일 격납고 · 공중 항모 · 포격함 · 패트리어트 · 상륙정 · 수송헬기를 조립하고,
    /// Jean Moreno War FX(WFX_*) 프리팹과 5종 맵 바이옴 소품을 VisualCatalog & ViewCatalog에 바인딩한다. (PrimitiveType 큐브·실린더·구체 전면 금지)
    /// </summary>
    static void BuildRealSyntyCompositesAndFx(VisualCatalog catalog) {
      const string PM = PackRoot + "/PolygonMilitary/Prefabs/";
      const string BR = PackRoot + "/PolygonBattleRoyale/Prefabs/";
      const string PC = PackRoot + "/PolygonCity/Prefabs/";
      const string PCN = PackRoot + "/PolygonConstruction/Prefabs/";

      // 1) War FX 머티리얼 중 Built-in Surface / Linear 불호환 Multiply Soft 셰이더를 URP 호환으로 안전 변환.
      UpgradeWarFxMaterialsForUrp();

      GameObject Assemble(string assetName, string[] swap, params KitPart[] parts) {
        var root = new GameObject(assetName);
        var map = new Dictionary<string, Material>();
        for (var i = 0; swap != null && i + 1 < swap.Length; i += 2) {
          var mat = FindMaterial(swap[i + 1]);
          if (mat != null) map[swap[i]] = mat;
        }
        var added = 0;
        for (var p = 0; p < parts.Length; p++) {
          var src = AssetDatabase.LoadAssetAtPath<GameObject>(parts[p].Path);
          if (src == null) {
            Debug.LogWarning($"[FW-Synty] Composite part missing: {parts[p].Path}");
            continue;
          }
          var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
          if (inst == null) {
            inst = UnityEngine.Object.Instantiate(src, root.transform);
          }
          inst.name = $"Part{p}_{src.name}";
          inst.transform.localPosition = parts[p].Pos;
          inst.transform.localRotation = Quaternion.Euler(parts[p].Rot);
          inst.transform.localScale = parts[p].Scale;
          if (map.Count > 0) {
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) {
              var mats = r.sharedMaterials;
              var touched = false;
              for (var m = 0; m < mats.Length; m++) {
                if (mats[m] != null && map.TryGetValue(mats[m].name, out var to)) {
                  mats[m] = to;
                  touched = true;
                }
              }
              if (touched) r.sharedMaterials = mats;
            }
          }
          added++;
        }
        if (added == 0) {
          UnityEngine.Object.DestroyImmediate(root);
          return null;
        }
        var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{OutRoot}/Prefabs/{assetName}.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return saved;
      }

      void RegisterComposite(string slotId, VisualCategory category, GameObject prefab, float targetSpan, float maxHeight, string[] spin = null, string notes = "") {
        if (prefab == null) return;
        var slot = catalog.Slots.FirstOrDefault(s => s != null && s.Id == slotId);
        if (slot == null) {
          slot = new VisualSlot { Id = slotId };
          catalog.Slots.Add(slot);
        }
        var (pivot, size) = MeasureActive(prefab);
        slot.Category = category;
        slot.Prefabs = new List<GameObject> { prefab };
        slot.PivotOffset = pivot;
        slot.Size = size;
        slot.YawOffset = 0f;
        slot.TeamColor = TeamColorMode.None;
        slot.TeamTextures = new Texture2D[0];
        slot.RealWorld = RealWorldCheck.Clear;
        slot.OwnerRing = category == VisualCategory.Unit;
        slot.Animator = null;
        slot.AutoFit = false;
        var horiz = Mathf.Max(0.1f, Mathf.Max(size.x, size.z));
        slot.Scale = maxHeight > 0f ? Mathf.Min(targetSpan / horiz, maxHeight / Mathf.Max(0.1f, size.y)) : (targetSpan / horiz);
        slot.SpinParts = spin ?? new string[0];
        slot.Notes = string.IsNullOrEmpty(notes) ? "Synty 4-pack composite prefab" : (notes.StartsWith("Synty") ? notes : "Synty " + notes);
      }

      // 2) 8대 산업 공장 + 산업 사령부 (양 진영 모두 100% 실제 Synty 4대 팩: Construction + Military + BattleRoyale + City 조합)
      foreach (var faction in new[] { "ff", "cf" }) {
        var bSwap = faction == "ff" ? BuildingsFree : BuildingsContinental;
        var pSwap = faction == "ff" ? PropsFree : PropsContinental;

        // (1) 보병 무장 공장: Military 막사 + Construction 대형 군장 선반 + 투광등 + Military 탄약 상자·방벽
        RegisterComposite($"building/{faction}_barracks", VisualCategory.Building,
          Assemble($"{faction}_barracks_kit", bSwap,
            new KitPart(PM + "Buildings/SM_Bld_Barracks_01.prefab", Vector3.zero, Vector3.zero, 1f),
            new KitPart(PCN + "Props/SM_Prop_StorageShelf_Large_01.prefab", new Vector3(-4.6f, 0f, -1.6f), new Vector3(0f, 90f, 0f), 1.0f),
            new KitPart(PCN + "Props/SM_Prop_Floodlights_01.prefab", new Vector3(4.6f, 0f, -2.6f), new Vector3(0f, -35f, 0f), 1.0f),
            new KitPart(PM + "Props/Military/SM_Prop_Crate_Stack_01.prefab", new Vector3(-3.8f, 0f, 3.2f), new Vector3(0f, 15f, 0f), 1.25f),
            new KitPart(PM + "Props/Military/SM_Prop_Barrier_Base_Row_01.prefab", new Vector3(3.6f, 0f, 3.4f), Vector3.zero, 0.95f),
            new KitPart(PM + "Props/Military/SM_Prop_AmmoBox_01.prefab", new Vector3(-2.4f, 0f, 3.5f), new Vector3(0f, -20f, 0f), 1.4f)),
          9.0f, 10.2f, null, "Synty Barracks (Military + Construction)");

        // (2) 기동·화력 조립 공장: Military 격납고 + Construction 조립 비계 + 파이프 적재 선반 + 대형 발전기 + Military 드럼통
        RegisterComposite($"building/{faction}_vehicle_factory", VisualCategory.Building,
          Assemble($"{faction}_vehicle_factory_kit", bSwap,
            new KitPart(PM + "Buildings/SM_Bld_Hangar_01.prefab", Vector3.zero, Vector3.zero, 1f),
            new KitPart(PCN + "Props/SM_Prop_Scaffold_Preset_01.prefab", new Vector3(-7.0f, 0f, -0.5f), new Vector3(0f, 90f, 0f), 0.72f),
            new KitPart(PCN + "Props/SM_Prop_StorageShelf_Large_Pipe_01.prefab", new Vector3(7.0f, 0f, 1.8f), new Vector3(0f, -90f, 0f), 1.05f),
            new KitPart(PCN + "Props/SM_Prop_Generator_Large_01.prefab", new Vector3(6.8f, 0f, -3.2f), new Vector3(0f, 90f, 0f), 1.15f),
            new KitPart(PM + "Props/Military/SM_Prop_Barrel_Stack_01.prefab", new Vector3(-6.4f, 0f, 3.8f), new Vector3(0f, 20f, 0f), 1.35f)),
          10.8f, 9.4f, null, "Synty Vehicle Factory (Military + Construction)");

        // (3) 중기갑 전차 공장: Military 개방형 중격납고 + Construction 타워 크레인(SM_Bld_Crane_01) + 강철 H빔/거더 + 산업 굴뚝 + 컨테이너
        RegisterComposite($"building/{faction}_heavy_works", VisualCategory.Building,
          Assemble($"{faction}_heavy_works_kit", bSwap,
            new KitPart(PM + "Buildings/SM_Bld_Hangar_Open_01.prefab", Vector3.zero, Vector3.zero, 1f),
            new KitPart(PCN + "Buildings/SM_Bld_Crane_01.prefab", new Vector3(-6.4f, 0f, -2.6f), new Vector3(0f, 35f, 0f), 0.34f),
            new KitPart(PCN + "Buildings/SM_Bld_SmokeStack_01.prefab", new Vector3(5.8f, 0f, -3.8f), Vector3.zero, 0.42f),
            new KitPart(PCN + "Props/SM_Prop_Beam_Stack_01.prefab", new Vector3(-5.8f, 0f, 3.6f), new Vector3(0f, 15f, 0f), 1.15f),
            new KitPart(PCN + "Props/SM_Prop_I_Beam_01.prefab", new Vector3(0f, 0.2f, 5.2f), new Vector3(0f, 90f, 0f), 1.05f),
            new KitPart(PCN + "Props/SM_Prop_Shipping_Container_01.prefab", new Vector3(6.6f, 0f, 1.4f), new Vector3(0f, 90f, 0f), 0.92f)),
          11.0f, 10.6f, null, "Synty Heavy Works (Military + Construction Crane & Girders)");

        // (4) 항공·드론 조립 격납고: Military 헬리패드 + 관제탑 + Construction 대형 연료 탱크 + 투광등 + Military 이동식 레이더
        RegisterComposite($"building/{faction}_airfield", VisualCategory.Building,
          Assemble($"{faction}_airfield_kit", bSwap,
            new KitPart(PM + "Environment/SM_Env_Helipad_01.prefab", new Vector3(0f, 0.04f, 0.8f), Vector3.zero, 0.82f),
            new KitPart(PM + "Buildings/SM_Bld_ControlTower_01.prefab", new Vector3(-4.2f, 0f, -3.2f), new Vector3(0f, 180f, 0f), 0.72f),
            new KitPart(PCN + "Props/SM_Prop_Fuel_Tank_01.prefab", new Vector3(4.5f, 0f, -3.2f), Vector3.zero, 0.95f),
            new KitPart(PCN + "Props/SM_Prop_Floodlights_01.prefab", new Vector3(-4.4f, 0f, 3.4f), new Vector3(0f, 45f, 0f), 1.0f),
            new KitPart(PM + "Props/Military/SM_Prop_Mobile_Radar_01.prefab", new Vector3(4.2f, 0f, 3.0f), Vector3.zero, 0.7f)),
          10.2f, 11.4f, new[] { "Radar:Y" }, "Synty Airfield (Military + Construction)");

        // (5) 강철 제련소: BattleRoyale 대형 산업 창고 + Construction 쌍 산업 굴뚝 + 콘크리트 믹서 + 대형 파이프/강철 빔 적재 + 슬래그 호퍼
        RegisterComposite($"building/{faction}_processor", VisualCategory.Building,
          Assemble($"{faction}_processor_kit", bSwap,
            new KitPart(BR + "Buildings/SM_Bld_Warehouse_01.prefab", new Vector3(-0.6f, 0f, -0.4f), Vector3.zero, 0.62f),
            new KitPart(PCN + "Buildings/SM_Bld_SmokeStack_01.prefab", new Vector3(-4.2f, 0f, -2.8f), Vector3.zero, 0.44f),
            new KitPart(PCN + "Buildings/SM_Bld_SmokeStack_01.prefab", new Vector3(2.8f, 0f, -2.8f), Vector3.zero, 0.38f),
            new KitPart(PCN + "Props/SM_Prop_Concrete_Mixer_01.prefab", new Vector3(4.6f, 0f, 1.4f), new Vector3(0f, -45f, 0f), 1.35f),
            new KitPart(PCN + "Props/SM_Prop_Pipe_Stack_01.prefab", new Vector3(-3.8f, 0f, 3.6f), new Vector3(0f, 90f, 0f), 1.15f),
            new KitPart(PCN + "Props/SM_Prop_Skip_Large_01.prefab", new Vector3(2.2f, 0f, 3.8f), Vector3.zero, 1.05f),
            new KitPart(PM + "Props/Pipeline/SM_Prop_Pipeline_Pipe_Small_Straight_01.prefab", new Vector3(4.2f, 0f, -1.2f), Vector3.zero, 0.9f)),
          9.4f, 9.2f, null, "Synty Steel Refinery (BattleRoyale + Construction + Military)");

        // (6) 터빈 발전소: Construction 냉각수 탱크 타워 + 대형 터빈 발전기 + 배전반 + 전선 릴 + 현장 변전실 + BattleRoyale 변압기
        RegisterComposite($"building/{faction}_power_plant", VisualCategory.Building,
          Assemble($"{faction}_power_plant_kit", bSwap,
            new KitPart(PCN + "Buildings/SM_Bld_WaterTank_02.prefab", new Vector3(-2.4f, 0f, -1.2f), Vector3.zero, 0.82f),
            new KitPart(PCN + "Props/SM_Prop_Generator_Large_01.prefab", new Vector3(2.4f, 0f, -1.6f), new Vector3(0f, 90f, 0f), 1.35f),
            new KitPart(PCN + "Buildings/SM_Bld_Portable_Office_01.prefab", new Vector3(1.8f, 0f, 2.2f), Vector3.zero, 0.78f),
            new KitPart(BR + "Props/SM_Prop_Transformer_01.prefab", new Vector3(-2.8f, 0f, 2.4f), Vector3.zero, 1.2f),
            new KitPart(PCN + "Props/SM_Prop_PowerBoxes_01.prefab", new Vector3(4.2f, 0f, 0.4f), new Vector3(0f, -90f, 0f), 1.2f),
            new KitPart(PCN + "Props/SM_Prop_Wirespool_01.prefab", new Vector3(-0.4f, 0f, 3.4f), Vector3.zero, 1.25f)),
          9.0f, 8.8f, null, "Synty Power Plant (Construction + BattleRoyale)");

        // (7) 군수·연료 보급창: Construction 연료 탱크·받침 + 해운 컨테이너 + 방수포 적재 선반 + 드럼통 + Military 탄약 상자·연료 블래더
        RegisterComposite($"building/{faction}_supply_depot", VisualCategory.Building,
          Assemble($"{faction}_supply_depot_kit", bSwap,
            new KitPart(PCN + "Props/SM_Prop_Shipping_Container_01.prefab", new Vector3(-2.2f, 0f, -1.6f), Vector3.zero, 0.92f),
            new KitPart(PCN + "Props/SM_Prop_Fuel_Tank_01_Base_01.prefab", new Vector3(2.6f, 0f, -1.6f), Vector3.zero, 1.0f),
            new KitPart(PCN + "Props/SM_Prop_Fuel_Tank_01.prefab", new Vector3(2.6f, 0.55f, -1.6f), Vector3.zero, 1.0f),
            new KitPart(PCN + "Props/SM_Prop_StorageShelf_Large_Pipe_02_Tarp.prefab", new Vector3(-2.4f, 0f, 2.2f), Vector3.zero, 0.95f),
            new KitPart(PM + "Props/Military/SM_Prop_Fuel_Bladder_01.prefab", new Vector3(2.8f, 0f, 2.2f), Vector3.zero, 0.82f),
            new KitPart(PCN + "Props/SM_Prop_BarrelStack_01.prefab", new Vector3(0.4f, 0f, 3.2f), Vector3.zero, 1.15f),
            new KitPart(PM + "Props/Military/SM_Prop_Crate_Ammo_01.prefab", new Vector3(-4.2f, 0f, 3.2f), Vector3.zero, 1.35f)),
          9.4f, 8.4f, null, "Synty Supply Depot (Construction + Military)");

        // (8) 군수 연구 공장: City 콘크리트 공업동(SM_Bld_Shop_03) + Construction 지휘 컨테이너 오피스 + 배전반 + Military 레이더 + BattleRoyale 무선 송신탑
        RegisterComposite($"building/{faction}_research_lab", VisualCategory.Building,
          Assemble($"{faction}_research_lab_kit", bSwap,
            new KitPart(PC + "Buildings/SM_Bld_Shop_03.prefab", new Vector3(-1.4f, 0f, -0.8f), Vector3.zero, 0.62f),
            new KitPart(PM + "Props/Military/SM_Prop_Drone_Control_Room_01.prefab", new Vector3(2.6f, 0f, 1.6f), new Vector3(0f, -90f, 0f), 0.95f),
            new KitPart(PM + "Props/Military/SM_Prop_Mobile_Radar_01.prefab", new Vector3(3.4f, 0f, -2.4f), Vector3.zero, 0.85f),
            new KitPart(BR + "Props/SM_Prop_RadioTower_01.prefab", new Vector3(-4.0f, 0f, -2.6f), Vector3.zero, 0.42f),
            new KitPart(PCN + "Props/SM_Prop_PowerBoxes_02.prefab", new Vector3(-3.6f, 0f, 2.8f), Vector3.zero, 1.25f)),
          9.6f, 10.6f, new[] { "Radar:Y" }, "Synty Research Lab (City + Military + Construction + BattleRoyale)");

        // (9) 군사 산업 사령부(HQ): BattleRoyale 대형 창고 + Military 지휘 통제실·관제탑·레이더·방호벽 + Construction 투광등·크레인 섹션
        RegisterComposite($"building/{faction}_headquarters", VisualCategory.Building,
          Assemble($"{faction}_headquarters_kit", bSwap,
            new KitPart(BR + "Buildings/SM_Bld_Warehouse_01.prefab", new Vector3(0f, 0f, -0.4f), Vector3.zero, 0.82f),
            new KitPart(PM + "Props/Military/SM_Prop_Drone_Control_Room_01.prefab", new Vector3(-3.4f, 0f, 3.6f), Vector3.zero, 0.95f),
            new KitPart(PM + "Buildings/SM_Bld_ControlTower_01.prefab", new Vector3(5.0f, 0f, -3.2f), new Vector3(0f, 180f, 0f), 0.72f),
            new KitPart(PM + "Props/Military/SM_Prop_Mobile_Radar_01.prefab", new Vector3(-5.0f, 0f, -2.8f), Vector3.zero, 0.9f),
            new KitPart(PCN + "Props/SM_Prop_Floodlights_01.prefab", new Vector3(5.2f, 0f, 3.6f), new Vector3(0f, -135f, 0f), 1.05f),
            new KitPart(PM + "Props/Military/SM_Prop_Barrier_Base_Group_01.prefab", new Vector3(2.2f, 0f, 5.0f), Vector3.zero, 0.95f)),
          11.2f, 12.2f, new[] { "Radar:Y" }, "Synty Headquarters (BattleRoyale + Military + Construction)");

        // (10) 패트리어트 대탄도 요격 포대: SM_Prop_Missile_Launcher_02 + 이동식 레이더 + 방벽
        RegisterComposite($"building/{faction}_patriot_turret", VisualCategory.Building,
          Assemble($"{faction}_patriot_turret_kit", pSwap,
            new KitPart(PM + "Props/Military/SM_Prop_Missile_Launcher_02.prefab", Vector3.zero, Vector3.zero, 1.3f),
            new KitPart(PM + "Props/Military/SM_Prop_Mobile_Radar_01.prefab", new Vector3(2.8f, 0f, -1.6f), Vector3.zero, 0.8f),
            new KitPart(PM + "Props/Military/SM_Prop_Barrier_Base_Group_01.prefab", new Vector3(0f, 0f, 2.4f), Vector3.zero, 0.75f)),
          7.6f, 10.2f, new[] { "Radar:Y" }, "Synty Patriot Turret (Military)");
      }

      // 3) 항모 조립창(ff_carrier_yard)
      RegisterComposite("building/ff_carrier_yard", VisualCategory.Building,
        Assemble("ff_carrier_yard_kit", BuildingsFree,
          new KitPart(PM + "Buildings/SM_Bld_Hangar_Open_01.prefab", Vector3.zero, Vector3.zero, 1f),
          new KitPart(PCN + "Buildings/SM_Bld_Crane_01.prefab", new Vector3(6.4f, 0f, -2.6f), new Vector3(0f, -45f, 0f), 0.36f),
          new KitPart(PM + "Buildings/SM_Bld_ControlTower_01.prefab", new Vector3(-6.4f, 0f, -3.0f), new Vector3(0f, 180f, 0f), 0.78f),
          new KitPart(PM + "Props/Military/SM_Prop_Mobile_Radar_01.prefab", new Vector3(6.2f, 0f, 2.6f), Vector3.zero, 0.9f)),
        11.8f, 12.2f, new[] { "Radar:Y" }, "Synty Carrier Yard (Military + Construction)");

      // 4) 전략 미사일 격납고(cf_missile_silo): 어두운 콘크리트 발사대(Helipad) + 중형 미사일 발사기 + 초대형 직립 탄도탄 + Construction 발사대 비계·투광등 + 발사 통제 벙커 + 레이더 + 방폭벽
      RegisterComposite("building/cf_missile_silo", VisualCategory.Building,
        Assemble("cf_missile_silo_kit", BuildingsContinental,
          new KitPart(PM + "Environment/SM_Env_Helipad_01.prefab", new Vector3(0f, 0.03f, 0f), Vector3.zero, 0.95f),
          new KitPart(PM + "Props/Military/SM_Prop_Missile_Launcher_01.prefab", new Vector3(-0.6f, 0.08f, -0.4f), Vector3.zero, 1.75f),
          new KitPart(PM + "Props/Military/SM_Prop_Missile_01.prefab", new Vector3(0.4f, 0.35f, 0.4f), new Vector3(-90f, 0f, 0f), 3.6f),
          new KitPart(PCN + "Props/SM_Prop_Scaffold_01.prefab", new Vector3(-2.4f, 0.08f, 0.8f), new Vector3(0f, 90f, 0f), 0.85f),
          new KitPart(PM + "Props/Military/SM_Prop_Drone_Control_Room_02.prefab", new Vector3(4.5f, 0f, -2.6f), new Vector3(0f, -30f, 0f), 1.0f),
          new KitPart(PM + "Props/Military/SM_Prop_Mobile_Radar_01.prefab", new Vector3(-4.4f, 0f, -2.4f), Vector3.zero, 0.9f),
          new KitPart(PM + "Props/Military/SM_Prop_Barrier_Base_Row_01.prefab", new Vector3(0f, 0f, 4.9f), Vector3.zero, 1.3f),
          new KitPart(PM + "Props/Military/SM_Prop_Barrier_Base_Row_01.prefab", new Vector3(0f, 0f, -4.9f), new Vector3(0f, 180f, 0f), 1.3f),
          new KitPart(PM + "Props/Military/SM_Prop_Barrier_Base_Row_01.prefab", new Vector3(-4.9f, 0f, 0f), new Vector3(0f, 90f, 0f), 1.15f)),
        10.6f, 13.6f, new[] { "Radar:Y" }, "Synty Strategic Missile Silo kitbash (Military + Construction)");

      // 5) 스톰네스트 공중 항모(ff_stormnest_carrier): 실제 Synty 대형 수송기(SM_Veh_TransportPlane_01) + 좌우 제트 편대 포드 + 상부 레이더 + 드론
      RegisterComposite("unit/ff_stormnest_carrier", VisualCategory.Unit,
        Assemble("ff_stormnest_carrier_kit", None,
          new KitPart(PM + "Vehicles/SM_Veh_TransportPlane_01.prefab", Vector3.zero, Vector3.zero, 1.0f),
          new KitPart(PM + "Vehicles/SM_Veh_Jet_01.prefab", new Vector3(-6.2f, 0.8f, -0.8f), Vector3.zero, 0.36f),
          new KitPart(PM + "Vehicles/SM_Veh_Jet_01.prefab", new Vector3(6.2f, 0.8f, -0.8f), Vector3.zero, 0.36f),
          new KitPart(PM + "Props/Military/SM_Prop_Mobile_Radar_01.prefab", new Vector3(0f, 4.2f, -0.6f), Vector3.zero, 0.45f),
          new KitPart(PM + "Vehicles/SM_Veh_Drone_01.prefab", new Vector3(-3.8f, 1.1f, 2.2f), Vector3.zero, 0.85f),
          new KitPart(PM + "Vehicles/SM_Veh_Drone_01.prefab", new Vector3(3.8f, 1.1f, 2.2f), Vector3.zero, 0.85f)),
        13.2f, 0f, new[] { "Prop_0:Z", "Radar:Y" }, "Synty Flying Carrier kitbash");

      // 6) 전술 수송헬기(ff/cf_transport_heli): 실제 Synty SM_Veh_Helicopter_Transport_01
      RegisterComposite("unit/ff_transport_heli", VisualCategory.Unit,
        Assemble("ff_transport_heli_kit", None,
          new KitPart(PM + "Vehicles/SM_Veh_Helicopter_Transport_01.prefab", Vector3.zero, Vector3.zero, 1f)),
        6.8f, 0f, new[] { "Blades_Main:Y", "Blades_Back:X" }, "Synty Transport Helicopter");
      RegisterComposite("unit/cf_transport_heli", VisualCategory.Unit,
        Assemble("cf_transport_heli_kit", HeliContinental,
          new KitPart(PM + "Vehicles/SM_Veh_Helicopter_Transport_01.prefab", Vector3.zero, Vector3.zero, 1f)),
        6.8f, 0f, new[] { "Blades_Main:Y", "Blades_Back:X" }, "Synty Transport Helicopter");

      // 7) 포격함(ff/cf_artillery_ship): NavalKit.cs(Kenney 화물선 선체 + Synty 다연장 발사대·함교·레이더 마스트). 다른 팩 조립과 같은 칸 규칙.
      foreach (var faction in new[] { "ff", "cf" }) {
        BuildArtilleryShip(catalog, faction);
      }
      foreach (var faction in new[] { "ff", "cf" }) {
        var bSwapNaval = faction == "ff" ? LightFree : LightContinental;
        // 8) 현대식 수륙양용 상륙정(ff/cf_landing_craft): 실제 Synty 군용 보트 광폭 선체 + 함수 장갑 상륙 램프 + 양현 방호벽 + 탄약·화물 스택
        RegisterComposite($"unit/{faction}_landing_craft", VisualCategory.Unit,
          Assemble($"{faction}_landing_craft_kit", bSwapNaval,
            new KitPart(BR + "Vehicles/SM_Veh_Boat_01.prefab", Vector3.zero, Vector3.zero, new Vector3(2.1f, 1.15f, 1.75f)),
            new KitPart(PM + "Vehicles/Veh_Attachments/SM_Veh_Attach_Ramp_01.prefab", new Vector3(0f, 0.42f, 3.65f), Vector3.zero, 1.35f),
            new KitPart(PM + "Props/Military/SM_Prop_Barrier_Base_Single_01.prefab", new Vector3(-1.45f, 0.72f, 0.3f), new Vector3(0f, 90f, 0f), 0.72f),
            new KitPart(PM + "Props/Military/SM_Prop_Barrier_Base_Single_01.prefab", new Vector3(1.45f, 0.72f, 0.3f), new Vector3(0f, 90f, 0f), 0.72f),
            new KitPart(PM + "Props/Military/SM_Prop_Crate_Stack_01.prefab", new Vector3(0f, 0.52f, -0.4f), Vector3.zero, 0.75f)),
          7.4f, 0f, null, "Synty military assault landing craft kitbash");
      }

      // 9) 실제 Synty Construction Pack 공장 업그레이드(Lv.2/Lv.3) · 건설 비계 · 직선 시너지 배관 · 잔해 · 5맵 바이옴 소품 슬롯
      void BindFx(string fxName, string prefabPath, float scale, float lifetime = 5f) =>
        BindEffectSlot(catalog, fxName, prefabPath, scale, lifetime);

      // Lv.2 공장 증축: 실제 PolygonConstruction 조립 비계 + 대형 발전기 + 파이프 스택 + 강철 빔 스택
      var lv2Prefab = Assemble("factory_upgrade_lv2", None,
        new KitPart(PCN + "Props/SM_Prop_Scaffold_Preset_01.prefab", new Vector3(-4.2f, 0f, -1.6f), new Vector3(0f, 90f, 0f), 0.72f),
        new KitPart(PCN + "Props/SM_Prop_Generator_Large_01.prefab", new Vector3(4.2f, 0f, 1.8f), Vector3.zero, 1.05f),
        new KitPart(PCN + "Props/SM_Prop_Pipe_Stack_02.prefab", new Vector3(-3.6f, 0f, 2.8f), Vector3.zero, 1.0f),
        new KitPart(PCN + "Props/SM_Prop_Beam_Stack_01.prefab", new Vector3(3.6f, 0f, -2.6f), Vector3.zero, 0.95f));
      if (lv2Prefab != null) BindFx("factory_lv2", AssetDatabase.GetAssetPath(lv2Prefab), 1f, 999f);

      // Lv.3 공장 증축: 실제 PolygonConstruction 산업 타워 크레인 + 방수포 비계 + 산업 굴뚝 + 강철 트러스/배전반
      var lv3Prefab = Assemble("factory_upgrade_lv3", None,
        new KitPart(PCN + "Buildings/SM_Bld_Crane_01.prefab", new Vector3(4.4f, 0f, -2.6f), new Vector3(0f, -35f, 0f), 0.28f),
        new KitPart(PCN + "Props/SM_Prop_Scaffold_Preset_02_Tarp.prefab", new Vector3(-4.4f, 0f, 1.8f), new Vector3(0f, 90f, 0f), 0.68f),
        new KitPart(PCN + "Buildings/SM_Bld_SmokeStack_01.prefab", new Vector3(0f, 0f, -4.4f), Vector3.zero, 0.34f),
        new KitPart(PCN + "Props/SM_Prop_PowerBoxes_01.prefab", new Vector3(4.2f, 0f, 2.4f), Vector3.zero, 1.15f));
      if (lv3Prefab != null) BindFx("factory_lv3", AssetDatabase.GetAssetPath(lv3Prefab), 1f, 999f);

      BindFx("synergy_pipe", PM + "Props/Pipeline/SM_Prop_Pipeline_Pipe_Small_Straight_01.prefab", 0.85f, 999f);
      BindFx("rubble_pile", PCN + "Props/SM_Prop_Rubble_Concrete_01.prefab", 1.25f, 999f);
      BindFx("icbm_rocket", PM + "Props/Military/SM_Prop_Missile_01.prefab", 2.2f, 30f);

      BindLivingBaseVisuals(catalog, BuildWorkerController());

      // 사막 바이옴 실제 Synty 소품 (모래언덕 · 선인장 · 사막 바위 · 아치 · 폐허 · 오아시스 · 고사목 + 사막 군수 전초기지)
      BindFx("desert_dune_1", PM + "Environment/SM_Env_SandDunes_01.prefab", 1.0f, 999f);
      BindFx("desert_dune_2", PM + "Environment/SM_Env_SandDunes_02.prefab", 1.0f, 999f);
      BindFx("desert_dune_3", PM + "Environment/SM_Env_SandDunes_03.prefab", 1.0f, 999f);
      BindFx("desert_cactus_1", PM + "Environment/SM_Env_Cactus_01.prefab", 1.25f, 999f);
      BindFx("desert_cactus_2", PM + "Environment/SM_Env_Cactus_02.prefab", 1.25f, 999f);
      BindFx("desert_cactus_3", PM + "Environment/SM_Env_Cactus_03.prefab", 1.25f, 999f);
      BindFx("desert_rock_1", PM + "Environment/SM_Env_Rock_01.prefab", 0.85f, 999f);
      BindFx("desert_rock_2", PM + "Environment/SM_Env_Rock_02.prefab", 0.85f, 999f);
      BindFx("desert_rock_3", PM + "Environment/SM_Env_Rock_03.prefab", 0.85f, 999f);
      BindFx("desert_rock_arch", PM + "Environment/SM_Env_Rock_Archway_01.prefab", 0.9f, 999f);
      BindFx("desert_ruin_1", PM + "Environment/SM_Env_Ruin_01.prefab", 0.85f, 999f);
      BindFx("desert_oasis", PM + "Environment/SM_Env_Ground_Oasis_01.prefab", 0.85f, 999f);
      BindFx("desert_tree", BR + "Generic/SM_Generic_TreeDead_01.prefab", 0.85f, 999f);
      BindFx("desert_outpost_1", PCN + "Props/SM_Prop_Fuel_Tank_01.prefab", 1.0f, 999f);
      BindFx("desert_outpost_2", PCN + "Buildings/SM_Bld_Portable_Office_01.prefab", 0.95f, 999f);
      BindFx("desert_outpost_3", PM + "Buildings/SM_Bld_Tent_Desert_01.prefab", 0.9f, 999f);

      // 다도해·해협 항만 산업 지구 실제 Synty 소품 (BattleRoyale 창고 + Construction 타워 크레인·컨테이너·적재 선반)
      BindFx("waterfront_warehouse", BR + "Buildings/SM_Bld_Warehouse_01.prefab", 0.72f, 999f);
      BindFx("waterfront_crane", PCN + "Buildings/SM_Bld_Crane_01.prefab", 0.36f, 999f);
      BindFx("waterfront_container", PCN + "Props/SM_Prop_Shipping_Container_01.prefab", 1.0f, 999f);
      BindFx("waterfront_pipes", PCN + "Props/SM_Prop_StorageShelf_Large_Pipe_01.prefab", 1.0f, 999f);

      // 시가전 바이옴 실제 PolygonCity 고밀도 건물 소품
      BindFx("urban_bld_1", PC + "Buildings/SM_Bld_Apartment_01.prefab", 1.0f, 999f);
      BindFx("urban_bld_2", PC + "Buildings/SM_Bld_Apartment_02.prefab", 1.0f, 999f);
      BindFx("urban_bld_3", PC + "Buildings/SM_Bld_Apartment_03.prefab", 1.0f, 999f);
      BindFx("urban_bld_4", PC + "Buildings/SM_Bld_OfficeOld_Large_01.prefab", 1.0f, 999f);
      BindFx("urban_bld_5", PC + "Buildings/SM_Bld_OfficeSquare_01.prefab", 1.0f, 999f);
      BindFx("urban_bld_6", PC + "Buildings/SM_Bld_CityHall_01.prefab", 0.9f, 999f);

      // 10) Jean Moreno War FX(WFX_*) 프리팹을 VisualCatalog 및 ViewCatalog.WarFx에 모두 직접 바인딩 (원시 주황 구체·링 전면 대체)
      const string WFX = "Assets/ExternalAssets/JMO Assets/WarFX/";
      var warFxRows = new (string Key, string Path, float Scale, float Life)[] {
        ("nuke", WFX + "_Effects/Explosions/WFX_Nuke.prefab", 1.55f, 8.5f),
        ("nuke_extra", WFX + "_Effects/Explosions/WFX_ExplosiveSmokeGround Big.prefab", 1.25f, 6.0f),
        ("explosion", WFX + "_Effects/Explosions/WFX_ExplosiveSmokeGround.prefab", 1.25f, 4.0f),
        ("explosion_generic", WFX + "_Effects/Explosions/WFX_Explosion.prefab", 1.15f, 3.6f),
        ("explosion_small", WFX + "_Effects/Explosions/WFX_Explosion Small.prefab", 0.95f, 2.4f),
        ("bullet_impact", WFX + "_Effects/Bullet Impacts/WFX_BImpact Dirt.prefab", 0.85f, 1.2f),
        ("muzzle", WFX + "_Effects/MuzzleFlashes/4Planes/WFX_MF 4P RIFLE1.prefab", 0.72f, 0.22f),
        ("muzzle_heavy", WFX + "_Effects/MuzzleFlashes/4Planes/WFX_MF 4P RIFLE2.prefab", 1.18f, 0.28f),
        ("flame", WFX + "_Effects/Fire/WFX_FlameThrower.prefab", 1.15f, 1.2f),
        ("fire_black", WFX + "_Effects/Fire/Black Smoke/WFX_Fire Natural Broad (Black Smoke).prefab", 1.25f, 7.0f),
        ("smoke_black", WFX + "_Effects/Smoke/WFX_SmokeGrenade AlphaBlend Black.prefab", 0.88f, 4.2f),
        ("smoke", WFX + "_Effects/Smoke/WFX_SmokeGrenade AlphaBlend Gray.prefab", 0.65f, 3.6f),
        ("smoke_white", WFX + "_Effects/Smoke/WFX_SmokeGrenade White.prefab", 0.85f, 3.8f),
        ("welding_sparks", WeldingPrefabPath, 1.35f, 1.2f),
      };
      var viewCatalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>("Assets/QuantumUser/Resources/" + ViewCatalog.ResourcePath + ".asset");
      var vcList = new List<ViewCatalog.WarFxEntry>();
      foreach (var row in warFxRows) {
        BindFx(row.Key, row.Path, row.Scale, row.Life);
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(row.Path);
        if (pf != null) {
          vcList.Add(new ViewCatalog.WarFxEntry { Key = row.Key, Prefab = pf, Scale = row.Scale, Lifetime = row.Life });
        }
      }
      if (viewCatalog != null) {
        viewCatalog.WarFx = vcList.ToArray();
        EditorUtility.SetDirty(viewCatalog);
      }
      Debug.Log($"[FW-WarFX] Bound {vcList.Count}/{warFxRows.Length} War FX prefabs to VisualCatalog & ViewCatalog");
    }

    static void UpgradeWarFxMaterialsForUrp() {
      const string wfxRoot = "Assets/ExternalAssets/JMO Assets/WarFX";
      if (AssetDatabase.IsValidFolder(wfxRoot) == false) return;
      var urpParticle = Shader.Find("Universal Render Pipeline/Particles/Unlit");
      var wfxAlphaScroll = Shader.Find("WFX/Scroll/Alpha Blended");
      if (urpParticle == null) return;
      var fixedCount = 0;
      foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { wfxRoot })) {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null || mat.shader == null) continue;
        var sName = mat.shader.name ?? "";
        var mName = mat.name ?? "";
        // Linear 색공간 URP에서 WFX Multiply Soft(Blend DstColor SrcColor)는 연기가 새까만 타원 덩어리로 뭉개지므로 부드러운 AlphaBlend 스크롤로 전환.
        if (wfxAlphaScroll != null && (sName.IndexOf("Multiply Soft", StringComparison.OrdinalIgnoreCase) >= 0 || sName.Equals("WFX/Scroll/Smoke", StringComparison.OrdinalIgnoreCase))
            && mName.IndexOf("Smoke", StringComparison.OrdinalIgnoreCase) >= 0) {
          mat.shader = wfxAlphaScroll;
          mat.SetOverrideTag("RenderType", "Transparent");
          mat.renderQueue = 3000;
          EditorUtility.SetDirty(mat);
          fixedCount++;
          continue;
        }
        var isUrpAlready = sName.Equals("Universal Render Pipeline/Particles/Unlit", StringComparison.OrdinalIgnoreCase);
        var isSmokeNotAdd = mName.IndexOf("Smoke", StringComparison.OrdinalIgnoreCase) >= 0 && mName.IndexOf("Add", StringComparison.OrdinalIgnoreCase) < 0;
        var isAdditive = isSmokeNotAdd == false && (sName.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0 || mName.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0 || mName.IndexOf("Flame", StringComparison.OrdinalIgnoreCase) >= 0);
        var isMultiply = isSmokeNotAdd == false && (sName.IndexOf("Mult", StringComparison.OrdinalIgnoreCase) >= 0 || mName.IndexOf("Mult", StringComparison.OrdinalIgnoreCase) >= 0);
        var isUnconverted = sName.IndexOf("Diffuse", StringComparison.OrdinalIgnoreCase) >= 0
          || sName.IndexOf("Specular", StringComparison.OrdinalIgnoreCase) >= 0
          || sName.StartsWith("Particles/", StringComparison.OrdinalIgnoreCase)
          || sName.StartsWith("Mobile/Particles/", StringComparison.OrdinalIgnoreCase)
          || sName.StartsWith("Legacy Shaders/", StringComparison.OrdinalIgnoreCase)
          || sName.Contains("Error")
          || (isUrpAlready && (mat.GetTag("RenderType", false, "Opaque") != "Transparent" || (isSmokeNotAdd && mat.GetInt("_DstBlend") == (int)UnityEngine.Rendering.BlendMode.One)));
        if (isUnconverted) {
          var mainTex = mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null ? mat.GetTexture("_BaseMap")
            : mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
          var tint = mat.HasProperty("_TintColor") ? mat.GetColor("_TintColor")
            : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
          mat.shader = urpParticle;
          if (mainTex != null) {
            mat.SetTexture("_BaseMap", mainTex);
            mat.mainTexture = mainTex;
          }
          mat.SetColor("_BaseColor", isAdditive ? new Color(tint.r * 1.4f, tint.g * 1.4f, tint.b * 1.4f, tint.a)
            : isSmokeNotAdd ? new Color(0.62f, 0.62f, 0.62f, 0.62f) : tint);
          mat.SetFloat("_Surface", 1f); // Transparent
          mat.SetFloat("_ZWrite", 0f);
          mat.SetOverrideTag("RenderType", "Transparent");
          mat.DisableKeyword("_BLENDMODE_ADD");
          mat.DisableKeyword("_BLENDMODE_MULTIPLY");
          mat.DisableKeyword("_BLENDMODE_PREMULTIPLY");
          mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
          if (isAdditive) {
            mat.SetFloat("_Blend", 2f); // Additive
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.EnableKeyword("_BLENDMODE_ADD");
          } else if (isMultiply) {
            mat.SetFloat("_Blend", 3f); // Multiply
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.DstColor);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            mat.EnableKeyword("_BLENDMODE_MULTIPLY");
          } else {
            mat.SetFloat("_Blend", 0f); // Alpha
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
          }
          mat.renderQueue = 3000;
          EditorUtility.SetDirty(mat);
          fixedCount++;
        }
      }
      if (fixedCount > 0) {
        Debug.Log($"[FW-WarFX] Converted/fixed {fixedCount} WarFX materials for Linear URP.");
      }
    }

    /// <summary>모델 하나(필요하면 색 변형 프리팹 변형). 색 변형은 머티리얼 참조만 바꾼 우리 폴더의 프리팹 변형이다.</summary>
    static GameObject Member(string alias, string[] swap, Dictionary<string, GameObject> cache) {
      if (Pack.TryGetValue(alias, out var guid) == false) {
        return null;
      }
      var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
      if (basePrefab == null || swap.Length < 2) {
        return basePrefab;
      }
      var key = alias + "__" + string.Join("_", Enumerable.Range(0, swap.Length / 2).Select(i => swap[i * 2 + 1]));
      if (cache.TryGetValue(key, out var found)) {
        return found;
      }
      var map = new Dictionary<string, Material>();
      for (var i = 0; i + 1 < swap.Length; i += 2) {
        map[swap[i]] = FindMaterial(swap[i + 1]);
      }
      var path = $"{OutRoot}/Variants/{key}.prefab";
      var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
      var changed = 0;
      foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true)) {
        var materials = renderer.sharedMaterials;
        var touched = false;
        for (var i = 0; i < materials.Length; i++) {
          if (materials[i] != null && map.TryGetValue(materials[i].name, out var to) && to != null) {
            materials[i] = to;
            touched = true;
            changed++;
          }
        }
        if (touched) {
          renderer.sharedMaterials = materials;
        }
      }
      var variant = PrefabUtility.SaveAsPrefabAsset(instance, path);
      UnityEngine.Object.DestroyImmediate(instance);
      if (changed == 0) {
        Debug.LogWarning($"[FW-Synty] {alias}: 바꿀 머티리얼({string.Join(",", map.Keys)}) 없음(색 변형 적용 안 됨)");
      }
      cache[key] = variant;
      return variant;
    }

    static Material FindMaterial(string name) {
      foreach (var guid in AssetDatabase.FindAssets("t:Material " + name, new[] { PackRoot })) {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        if (Path.GetFileNameWithoutExtension(path) == name) {
          return AssetDatabase.LoadAssetAtPath<Material>(path);
        }
      }
      Debug.LogWarning("[FW-Synty] 머티리얼 없음: " + name);
      return null;
    }

    static readonly Vector3[][] Formations = {
      new[] { Vector3.zero },
      new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0.12f) },
      new[] { new Vector3(0f, 0f, 0.7f), new Vector3(-0.72f, 0f, -0.42f), new Vector3(0.72f, 0f, -0.34f) },
    };
    static readonly float[] Yaws = { 0f, -7f, 6f };

    static GameObject BuildSquad(string id, GameObject[] members) {
      var root = new GameObject(id.Replace("unit/", ""));
      var formation = Formations[Mathf.Clamp(members.Length, 1, 3) - 1];
      for (var i = 0; i < members.Length; i++) {
        var item = (GameObject)PrefabUtility.InstantiatePrefab(members[i], root.transform);
        item.name = "Member" + i;
        item.transform.localPosition = formation[i];
        item.transform.localRotation = Quaternion.Euler(0f, Yaws[i % Yaws.Length], 0f);
      }
      var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{OutRoot}/Prefabs/{id.Replace("unit/", "")}.prefab");
      UnityEngine.Object.DestroyImmediate(root);
      return prefab;
    }

    /// <summary>켜진 렌더러의 월드 바운드(원점에 둔 사본)로 바닥 가운데 보정과 크기를 잰다.</summary>
    static (Vector3 Pivot, Vector3 Size) MeasureActive(GameObject prefab) {
      var copy = UnityEngine.Object.Instantiate(prefab);
      try {
        var renderers = copy.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToArray();
        if (renderers.Length == 0) {
          var fallback = VisualCatalog.LocalBounds(prefab);
          return (new Vector3(-fallback.center.x, -fallback.min.y, -fallback.center.z), fallback.size);
        }
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) {
          bounds.Encapsulate(renderer.bounds);
        }
        return (new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z), bounds.size);
      } finally {
        UnityEngine.Object.DestroyImmediate(copy);
      }
    }

    static Vector3 MuzzleOf(GameObject prefab, string muzzlePart, Vector3 size) {
      if (string.IsNullOrEmpty(muzzlePart)) {
        return new Vector3(0f, size.y * 0.7f, size.z * 0.45f);
      }
      foreach (var child in prefab.GetComponentsInChildren<Transform>(true)) {
        if (child.name.Contains(muzzlePart)) {
          var filter = child.GetComponent<MeshFilter>();
          if (filter != null && filter.sharedMesh != null) {
            var bounds = filter.sharedMesh.bounds;
            return new Vector3(bounds.center.x, bounds.center.y, bounds.max.z);
          }
        }
      }
      return new Vector3(0f, size.y * 0.7f, size.z * 0.45f);
    }

    static AnimationClip Clip(string alias) {
      var path = AssetDatabase.GUIDToAssetPath(Pack[alias]);
      return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name.StartsWith("__preview__") == false);
    }

    /// <summary>Kubold Cover Rifle Animset Pro 클립 검색(경로에 Kubold/Cover/Rifle 포함).</summary>
    public static void InspectController() {
      var kubold = FindKuboldClips();
      Debug.Log($"[KuboldInspect] Total clips in CoverRifleAnimsetPro: {kubold.Count}");
      Debug.Log("[KuboldInspect] All clips: " + string.Join(", ", kubold.Select(c => c.name)));
      var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
      if (ctrl == null) {
        Debug.LogError("[KuboldInspect] PackInfantry.controller not found!");
        return;
      }
      foreach (var layer in ctrl.layers) {
        Debug.Log($"[KuboldInspect] Layer '{layer.name}' blend={layer.blendingMode} defaultWeight={layer.defaultWeight}");
        foreach (var child in layer.stateMachine.states) {
          var st = child.state;
          if (st.motion is BlendTree bt) {
            var children = string.Join(", ", bt.children.Select(c => $"{c.threshold:F1}:{(c.motion != null ? c.motion.name : "null")}({AssetDatabase.GetAssetPath(c.motion)})"));
            Debug.Log($"[KuboldInspect]   State '{st.name}' BlendTree -> [{children}]");
          } else {
            var m = st.motion;
            Debug.Log($"[KuboldInspect]   State '{st.name}' -> {(m != null ? m.name : "null")} ({(m != null ? AssetDatabase.GetAssetPath(m) : "")})");
          }
        }
      }
      var catalog = Resources.Load<FactoryWars.View.VisualCatalog>(FactoryWars.View.VisualCatalog.ResourcePath);
      if (catalog != null) {
        foreach (var slot in catalog.Slots) {
          if (slot.Animator != null) {
            Debug.Log($"[KuboldInspect] Slot '{slot.Id}' Animator={AssetDatabase.GetAssetPath(slot.Animator)}");
          }
        }
      }
    }

    static List<AnimationClip> FindKuboldClips() {
      var list = new List<AnimationClip>();
      foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip")) {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        if (path.IndexOf("Kubold", StringComparison.OrdinalIgnoreCase) < 0 && path.IndexOf("Cover Rifle", StringComparison.OrdinalIgnoreCase) < 0 && path.IndexOf("CoverRifle", StringComparison.OrdinalIgnoreCase) < 0) {
          continue;
        }
        foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()) {
          if (clip.name.StartsWith("__preview__") == false) {
            list.Add(clip);
          }
        }
      }
      return list;
    }

    static AnimationClip ExactKubold(List<AnimationClip> clips, AnimationClip fallback, string exactName) {
      return clips.FirstOrDefault(c => string.Equals(c.name, exactName, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    /// <summary>
    /// 보병 Animator 컨트롤러(우리 파일): Kubold Cover Rifle Animset Pro의 정확한 클립
    /// (Rifle_Idle, CoverLo_CoverR, CoverLo_AimR, CoverHi_AimR, CoverHi_AimR_Burst_Add)과
    /// Synty 전진 이동(Walk/Run/Sprint)을 조합한다.
    /// </summary>
    public static RuntimeAnimatorController BuildController() {
      EnsureFolder(OutRoot + "/Animation");
      var kubold = FindKuboldClips();
      Debug.Log($"[SyntyAssign] BuildController: Kubold clips found = {kubold.Count}");

      AssetDatabase.DeleteAsset(ControllerPath);
      var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
      controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
      controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
      controller.AddParameter("Cover", AnimatorControllerParameterType.Bool);
      controller.AddParameter("Building", AnimatorControllerParameterType.Bool);
      controller.AddParameter("Gathering", AnimatorControllerParameterType.Bool);
      controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
      controller.AddParameter("Aiming", AnimatorControllerParameterType.Bool);

      var machine = controller.layers[0].stateMachine;
      var locomotion = controller.CreateBlendTreeInController("Locomotion", out var tree);
      tree.blendType = BlendTreeType.Simple1D;
      tree.blendParameter = "Speed";
      tree.useAutomaticThresholds = false;

      var idleClip = ExactKubold(kubold, Clip("idle"), "Rifle_Idle");
      var walkClip = Clip("walk");
      var runClip = Clip("run");
      var sprintClip = Clip("sprint");
      var coverClip = ExactKubold(kubold, Clip("idle_crouch"), "CoverLo_CoverR");
      var coverAimClip = ExactKubold(kubold, coverClip, "CoverLo_AimR");
      var standAimClip = ExactKubold(kubold, idleClip, "CoverHi_AimR");
      var burstAddClip = ExactKubold(kubold, null, "CoverHi_AimR_Burst_Add");

      tree.AddChild(idleClip, 0f);
      tree.AddChild(walkClip, 1.6f);
      tree.AddChild(runClip, 4f);
      tree.AddChild(sprintClip, 7f);

      var standAim = machine.AddState("StandAim");
      standAim.motion = standAimClip;
      var toStandAim = locomotion.AddTransition(standAim);
      toStandAim.hasExitTime = false;
      toStandAim.duration = 0.15f;
      toStandAim.AddCondition(AnimatorConditionMode.If, 0f, "Aiming");
      toStandAim.AddCondition(AnimatorConditionMode.IfNot, 0f, "Cover");
      toStandAim.AddCondition(AnimatorConditionMode.Less, 0.3f, "Speed");
      var fromStandAim = standAim.AddTransition(locomotion);
      fromStandAim.hasExitTime = false;
      fromStandAim.duration = 0.15f;
      fromStandAim.AddCondition(AnimatorConditionMode.IfNot, 0f, "Aiming");
      var standAimMove = standAim.AddTransition(locomotion);
      standAimMove.hasExitTime = false;
      standAimMove.duration = 0.15f;
      standAimMove.AddCondition(AnimatorConditionMode.Greater, 0.3f, "Speed");

      var crouch = machine.AddState("CoverIdle");
      crouch.motion = coverClip;
      var toCover = locomotion.AddTransition(crouch);
      toCover.hasExitTime = false;
      toCover.duration = 0.2f;
      toCover.AddCondition(AnimatorConditionMode.If, 0f, "Cover");
      toCover.AddCondition(AnimatorConditionMode.Less, 0.3f, "Speed");
      var fromCover = crouch.AddTransition(locomotion);
      fromCover.hasExitTime = false;
      fromCover.duration = 0.2f;
      fromCover.AddCondition(AnimatorConditionMode.IfNot, 0f, "Cover");
      var coverMove = crouch.AddTransition(locomotion);
      coverMove.hasExitTime = false;
      coverMove.duration = 0.15f;
      coverMove.AddCondition(AnimatorConditionMode.Greater, 0.3f, "Speed");

      var coverAim = machine.AddState("CoverAim");
      coverAim.motion = coverAimClip;
      var toCoverAim = crouch.AddTransition(coverAim);
      toCoverAim.hasExitTime = false;
      toCoverAim.duration = 0.15f;
      toCoverAim.AddCondition(AnimatorConditionMode.If, 0f, "Aiming");
      var fromCoverAim = coverAim.AddTransition(crouch);
      fromCoverAim.hasExitTime = false;
      fromCoverAim.duration = 0.15f;
      fromCoverAim.AddCondition(AnimatorConditionMode.IfNot, 0f, "Aiming");
      var coverAimExit = coverAim.AddTransition(locomotion);
      coverAimExit.hasExitTime = false;
      coverAimExit.duration = 0.15f;
      coverAimExit.AddCondition(AnimatorConditionMode.IfNot, 0f, "Cover");

      if (burstAddClip != null) {
        controller.AddLayer("AdditiveFire");
        var layers = controller.layers;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Additive;
        layers[1].defaultWeight = 1f;
        controller.layers = layers;
        var fireMachine = controller.layers[1].stateMachine;
        var emptyState = fireMachine.AddState("Idle");
        var burstState = fireMachine.AddState("Burst");
        burstState.motion = burstAddClip;
        fireMachine.defaultState = emptyState;
        var toBurst = fireMachine.AddAnyStateTransition(burstState);
        toBurst.hasExitTime = false;
        toBurst.duration = 0.04f;
        toBurst.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
        var back = burstState.AddTransition(emptyState);
        back.hasExitTime = true;
        back.exitTime = 0.9f;
        back.duration = 0.08f;
      }

      machine.defaultState = locomotion;
      EditorUtility.SetDirty(controller);
      AssetDatabase.SaveAssets();
      return controller;
    }

    static void BindEffectSlot(VisualCatalog catalog, string fxName, string prefabPath, float scale, float lifetime) {
      var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
      if (prefab == null) {
        Debug.LogWarning($"[FW-Synty] FX/Prop prefab missing: {prefabPath}");
        return;
      }
      var id = VisualCatalog.FxId(fxName);
      var slot = catalog.Slots.FirstOrDefault(s => s != null && s.Id == id);
      if (slot == null) {
        slot = new VisualSlot { Id = id };
        catalog.Slots.Add(slot);
      }
      slot.Category = VisualCategory.Effect;
      slot.Prefabs = new List<GameObject> { prefab };
      slot.Scale = scale;
      slot.Lifetime = lifetime;
      slot.AutoFit = false;
      slot.Notes = Path.GetFileNameWithoutExtension(prefabPath);
    }

    public static void BuildLivingBaseVisuals() {
      var catalog = Resources.Load<VisualCatalog>(VisualCatalog.ResourcePath);
      if (catalog == null || AssetDatabase.IsValidFolder(PackRoot + "/PolygonConstruction") == false) {
        return;
      }
      EnsureFolder(OutRoot + "/Prefabs");
      var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(WorkerControllerPath) ?? BuildWorkerController();
      BindLivingBaseVisuals(catalog, controller);
      var views = Resources.Load<ViewCatalog>(ViewCatalog.ResourcePath);
      if (views != null && views.EngineerLocomotion != controller) {
        views.EngineerLocomotion = controller;
        EditorUtility.SetDirty(views);
      }
      AssetDatabase.SaveAssets();
    }

    static void BindLivingBaseVisuals(VisualCatalog catalog, RuntimeAnimatorController workerController) {
      const string PCN = PackRoot + "/PolygonConstruction/Prefabs/";
      void BindFx(string key, string path, float scale, float lifetime) => BindEffectSlot(catalog, key, path, scale, lifetime);
      if (catalog.Get(VisualCatalog.FxId("extractor_conveyor"))?.IsFilled != true) {
        BindFx("extractor_conveyor", "Assets/ExternalAssets/Kenney/factory-kit/conveyor-long-stripe-sides.fbx", 1f, 999f);
      }
      catalog.Slots.RemoveAll(slot => slot != null && slot.Id == VisualCatalog.FxId("extractor_pipe"));
      catalog.Invalidate();
      BindFx("factory_service_annex", PCN + "Buildings/SM_Bld_Portable_Office_01.prefab", 1f, 999f);
      BindFx("factory_expansion_stack", PCN + "Buildings/SM_Bld_SmokeStack_01.prefab", 1f, 999f);
      foreach (var faction in new[] { "ff", "cf" }) {
        foreach (var (suffix, alias) in new[] { ("lumber_mill", "bld_tent_open"), ("iron_extractor", "bld_gas_tower_1") }) {
          var id = VisualCatalog.ViewSlotId($"{faction}_{suffix}", false);
          if (catalog.Get(id)?.IsFilled == true) continue;
          var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(Pack[alias]));
          if (prefab == null) continue;
          var slot = catalog.Get(id);
          if (slot == null) {
            slot = new VisualSlot { Id = id };
            catalog.Slots.Add(slot);
          }
          slot.Category = VisualCategory.Building;
          slot.Prefabs = new List<GameObject> { prefab };
          slot.AutoFit = true;
        }
      }
      // 9-B) 3단계 건설 현장 키트(Stage 1 부지 정리 · Stage 2 골조 상승) + 기지 상주 건설/물류 작업자(안전모 + Base Locomotion) + 운반 상자/핸드트럭
      BindFx("build_scaffold", PCN + "Props/SM_Prop_Scaffold_Preset_01.prefab", 0.95f, 999f);
      if (catalog.Get(VisualCatalog.FxId("welding_sparks"))?.IsFilled != true) {
        BindFx("welding_sparks", WeldingPrefabPath, 1.35f, 1.2f);
      }
      var view = AssetDatabase.LoadAssetAtPath<ViewCatalog>("Assets/QuantumUser/Resources/" + ViewCatalog.ResourcePath + ".asset");
      if (view != null && view.TryGetWarFx("welding_sparks", out _) == false) {
        var sparks = AssetDatabase.LoadAssetAtPath<GameObject>(WeldingPrefabPath);
        if (sparks != null) {
          view.WarFx = (view.WarFx ?? Array.Empty<ViewCatalog.WarFxEntry>()).Append(new ViewCatalog.WarFxEntry {
            Key = "welding_sparks", Prefab = sparks, Scale = 1.35f, Lifetime = 1.2f,
          }).ToArray();
          EditorUtility.SetDirty(view);
        }
      }
      GameObject MakeNormalizedPad(string padName, string floorPath, float spanX, float thicknessY, float spanZ) {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(floorPath);
        if (src == null) return null;
        var root = new GameObject(padName);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
        if (inst == null) inst = UnityEngine.Object.Instantiate(src, root.transform);
        foreach (var col in inst.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);
        var (piv, sz) = MeasureActive(src);
        var sx = spanX / Mathf.Max(0.1f, sz.x);
        var sy = thicknessY / Mathf.Max(0.05f, sz.y);
        var szScale = spanZ / Mathf.Max(0.1f, sz.z);
        inst.transform.localScale = new Vector3(sx, sy, szScale);
        inst.transform.localPosition = new Vector3(piv.x * sx, piv.y * sy + 0.01f, piv.z * szScale);
        return root;
      }

      GameObject AssembleConstructionStage(string assetName, bool stage2) {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>($"{OutRoot}/Prefabs/{assetName}.prefab");
        if (existing != null) return existing;
        var root = new GameObject(assetName);
        var pad = MakeNormalizedPad("FoundationPad", PCN + "Buildings/SM_Bld_Concrete_Floor_01.prefab", stage2 ? 8.8f : 8.4f, 0.16f, stage2 ? 8.8f : 8.4f);
        if (pad != null) pad.transform.SetParent(root.transform, false);
        var parts = stage2
          ? new[] {
              // Stage 2 (33~99%): 양측 대형 비계 + 방수포 비계 + 건설 타워 크레인 + 투광등 + 안전 바리케이드·라바콘 + H빔
              new KitPart(PCN + "Props/SM_Prop_Scaffold_Preset_01.prefab", new Vector3(-4.2f, 0f, 0.2f), new Vector3(0f, 90f, 0f), 0.78f),
              new KitPart(PCN + "Props/SM_Prop_Scaffold_Preset_02_Tarp.prefab", new Vector3(4.2f, 0f, -0.2f), new Vector3(0f, -90f, 0f), 0.78f),
              new KitPart(PCN + "Buildings/SM_Bld_Crane_01.prefab", new Vector3(-4.2f, 0f, -3.8f), new Vector3(0f, 38f, 0f), 0.30f),
              new KitPart(PCN + "Props/SM_Prop_Floodlights_01.prefab", new Vector3(4.1f, 0f, -3.7f), new Vector3(0f, -135f, 0f), 1.05f),
              new KitPart(PCN + "Props/SM_Prop_Barrier_Plastic_01.prefab", new Vector3(-1.4f, 0f, 4.45f), Vector3.zero, 1.05f),
              new KitPart(PCN + "Props/SM_Prop_Barricade_01.prefab", new Vector3(1.6f, 0f, 4.45f), Vector3.zero, 1.05f),
              new KitPart(PCN + "Props/SM_Prop_Barricade_01.prefab", new Vector3(0f, 0f, -4.45f), new Vector3(0f, 180f, 0f), 1.05f),
              new KitPart(PCN + "Props/SM_Prop_Cone_01.prefab", new Vector3(-4.2f, 0f, 4.2f), Vector3.zero, 1.35f),
              new KitPart(PCN + "Props/SM_Prop_Cone_01.prefab", new Vector3(4.2f, 0f, 4.2f), Vector3.zero, 1.35f),
              new KitPart(PCN + "Props/SM_Prop_Cone_01.prefab", new Vector3(4.2f, 0f, -4.2f), Vector3.zero, 1.35f),
              new KitPart(PCN + "Props/SM_Prop_I_Beam_01.prefab", new Vector3(2.8f, 0.16f, 3.6f), new Vector3(0f, 18f, 0f), 0.85f),
              new KitPart(PCN + "Props/SM_Prop_Crate_Stack_01.prefab", new Vector3(-3.4f, 0.14f, 3.2f), new Vector3(0f, 20f, 0f), 1.0f),
            }
          : new[] {
              // Stage 1 (0~33%): 콘크리트 기초 패드 + 4모서리 라바콘 + 안전 바리케이드 + 팔레트/자재 상자 + 저층 강철 비계
              new KitPart(PCN + "Props/SM_Prop_Cone_01.prefab", new Vector3(-4.0f, 0f, 4.0f), Vector3.zero, 1.35f),
              new KitPart(PCN + "Props/SM_Prop_Cone_01.prefab", new Vector3(4.0f, 0f, 4.0f), Vector3.zero, 1.35f),
              new KitPart(PCN + "Props/SM_Prop_Cone_01.prefab", new Vector3(-4.0f, 0f, -4.0f), Vector3.zero, 1.35f),
              new KitPart(PCN + "Props/SM_Prop_Cone_01.prefab", new Vector3(4.0f, 0f, -4.0f), Vector3.zero, 1.35f),
              new KitPart(PCN + "Props/SM_Prop_Barricade_01.prefab", new Vector3(-1.5f, 0f, 4.25f), Vector3.zero, 1.05f),
              new KitPart(PCN + "Props/SM_Prop_Barrier_Plastic_01.prefab", new Vector3(1.5f, 0f, 4.25f), Vector3.zero, 1.05f),
              new KitPart(PCN + "Props/SM_Prop_Barricade_01.prefab", new Vector3(0f, 0f, -4.25f), new Vector3(0f, 180f, 0f), 1.05f),
              new KitPart(PCN + "Props/SM_Prop_Pallet_01.prefab", new Vector3(-2.1f, 0.16f, 1.8f), new Vector3(0f, 12f, 0f), 1.15f),
              new KitPart(PCN + "Props/SM_Prop_Crate_Stack_01.prefab", new Vector3(2.2f, 0.16f, -1.6f), new Vector3(0f, -20f, 0f), 1.05f),
              new KitPart(PCN + "Props/SM_Prop_Beam_Stack_01.prefab", new Vector3(-1.8f, 0.16f, -2.0f), new Vector3(0f, 8f, 0f), 0.95f),
              new KitPart(PCN + "Props/SM_Prop_Scaffold_01.prefab", new Vector3(-3.8f, 0f, 0f), new Vector3(0f, 90f, 0f), 0.85f),
              new KitPart(PCN + "Props/SM_Prop_Scaffold_01.prefab", new Vector3(3.8f, 0f, 0f), new Vector3(0f, -90f, 0f), 0.85f),
            };
        for (var p = 0; p < parts.Length; p++) {
          var src = AssetDatabase.LoadAssetAtPath<GameObject>(parts[p].Path);
          if (src == null) {
            Debug.LogWarning($"[FW-Synty] Construction stage part missing: {parts[p].Path}");
            continue;
          }
          var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
          if (inst == null) inst = UnityEngine.Object.Instantiate(src, root.transform);
          inst.name = $"Part{p}_{src.name}";
          inst.transform.localPosition = parts[p].Pos;
          inst.transform.localRotation = Quaternion.Euler(parts[p].Rot);
          inst.transform.localScale = parts[p].Scale;
          foreach (var col in inst.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);
        }
        var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{OutRoot}/Prefabs/{assetName}.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return saved;
      }

      var stage1Prefab = AssembleConstructionStage("const_stage1", false);
      var stage2Prefab = AssembleConstructionStage("const_stage2", true);
      if (stage1Prefab != null) BindFx("const_stage1", AssetDatabase.GetAssetPath(stage1Prefab), 1f, 999f);
      if (stage2Prefab != null) BindFx("const_stage2", AssetDatabase.GetAssetPath(stage2Prefab), 1f, 999f);

      // 안전모 착용 건설/물류 작업자 4종 (PolygonConstruction 캐릭터 + Head 본 안전모 부착 + Base Locomotion Animator + 콜라이더 0)
      Transform FindBone(Transform root, string boneName) {
        if (root.name == boneName) return root;
        foreach (Transform c in root) {
          var f = FindBone(c, boneName);
          if (f != null) return f;
        }
        return null;
      }

      GameObject BuildWorkerPrefab(string assetName, string charPath, string helmetPath) {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>($"{OutRoot}/Prefabs/{assetName}.prefab");
        if (existing != null) return existing;
        var charSrc = AssetDatabase.LoadAssetAtPath<GameObject>(charPath);
        if (charSrc == null) {
          Debug.LogWarning($"[FW-Synty] Worker prefab missing: {charPath}");
          return null;
        }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(charSrc);
        if (inst == null) inst = UnityEngine.Object.Instantiate(charSrc);
        inst.name = assetName;
        inst.transform.position = Vector3.zero;
        inst.transform.rotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one;
        foreach (var col in inst.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);
        var anim = inst.GetComponentInChildren<Animator>(true);
        if (anim != null && workerController != null) {
          anim.runtimeAnimatorController = workerController;
          anim.applyRootMotion = false;
          anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }
        // 캐릭터 머리 꼭대기 높이를 실측해 안전모(SM_Chr_Attach_Helmet_01/02)를 바인드 포즈에 정확히 씌운 뒤 Head 본에 종속시킨다.
        var helmetSrc = AssetDatabase.LoadAssetAtPath<GameObject>(helmetPath);
        var head = FindBone(inst.transform, "Head");
        if (helmetSrc != null && head != null) {
          var charBounds = new Bounds(Vector3.up * 0.9f, new Vector3(0.5f, 1.8f, 0.5f));
          var hasChar = false;
          foreach (var r in inst.GetComponentsInChildren<Renderer>(false)) {
            if (r.enabled == false) continue;
            if (!hasChar) { charBounds = r.bounds; hasChar = true; } else { charBounds.Encapsulate(r.bounds); }
          }
          var helmet = (GameObject)PrefabUtility.InstantiatePrefab(helmetSrc);
          if (helmet == null) helmet = UnityEngine.Object.Instantiate(helmetSrc);
          helmet.name = "HardHat";
          helmet.transform.position = Vector3.zero;
          helmet.transform.rotation = Quaternion.identity;
          helmet.transform.localScale = Vector3.one;
          foreach (var col in helmet.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);
          var hBounds = new Bounds(Vector3.zero, Vector3.one * 0.25f);
          var hasH = false;
          foreach (var r in helmet.GetComponentsInChildren<Renderer>(true)) {
            if (!hasH) { hBounds = r.bounds; hasH = true; } else { hBounds.Encapsulate(r.bounds); }
          }
          helmet.transform.position = new Vector3(
            head.position.x - hBounds.center.x,
            charBounds.max.y + 0.045f - hBounds.max.y,
            head.position.z - hBounds.center.z);
          helmet.transform.SetParent(head, true);
        }
        var saved = PrefabUtility.SaveAsPrefabAsset(inst, $"{OutRoot}/Prefabs/{assetName}.prefab");
        UnityEngine.Object.DestroyImmediate(inst);
        return saved;
      }

      var workerSpecs = new[] {
        ("const_worker_0", PCN + "Characters/SM_Chr_Builder_HighVis_01.prefab", PCN + "Characters/Attachments/SM_Chr_Attach_Helmet_01.prefab"),
        ("const_worker_1", PCN + "Characters/SM_Chr_Builder_Male_01.prefab", PCN + "Characters/Attachments/SM_Chr_Attach_Helmet_02.prefab"),
        ("const_worker_2", PCN + "Characters/SM_Chr_Builder_Female_01.prefab", PCN + "Characters/Attachments/SM_Chr_Attach_Helmet_01.prefab"),
        ("const_worker_3", PCN + "Characters/SM_Chr_Builder_Overalls_01.prefab", PCN + "Characters/Attachments/SM_Chr_Attach_Helmet_02.prefab"),
      };
      foreach (var ws in workerSpecs) {
        var wp = BuildWorkerPrefab(ws.Item1, ws.Item2, ws.Item3);
        if (wp != null) {
          BindFx(ws.Item1, AssetDatabase.GetAssetPath(wp), 0.85f, 999f);
          var wSlot = catalog.Slots.FirstOrDefault(s => s != null && s.Id == VisualCatalog.FxId(ws.Item1));
          if (wSlot != null) {
            wSlot.Animator = workerController;
            wSlot.Notes = "Synty Construction Hardhat Worker";
          }
        }
      }

      // 운반 목재 상자 · 보급 박스 · 핸드트럭: 실측 정규화(콜라이더 0, 중심·바닥 피벗 보정)로 공중 부유·비율 왜곡 원천 차단
      GameObject BuildNormalizedProp(string assetName, string propPath, float targetSize, bool centerY, float yawDeg = 0f) {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>($"{OutRoot}/Prefabs/{assetName}.prefab");
        if (existing != null) return existing;
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(propPath);
        if (src == null) return null;
        var root = new GameObject(assetName);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
        if (inst == null) inst = UnityEngine.Object.Instantiate(src, root.transform);
        foreach (var col in inst.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);
        inst.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
        var (piv, sz) = MeasureActive(src);
        var span = Mathf.Max(0.05f, centerY ? Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z)) : sz.y);
        var s = targetSize / span;
        inst.transform.localScale = Vector3.one * s;
        var yOff = centerY ? (piv.y - sz.y * 0.5f) * s : piv.y * s;
        var localCenter = Quaternion.Euler(0f, yawDeg, 0f) * new Vector3(piv.x * s, yOff, piv.z * s);
        inst.transform.localPosition = localCenter;
        var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{OutRoot}/Prefabs/{assetName}.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return saved;
      }

      var crate0 = BuildNormalizedProp("const_crate_0", PCN + "Props/SM_Prop_Crate_01.prefab", 0.48f, true);
      var crate1 = BuildNormalizedProp("const_crate_1", PCN + "Props/SM_Prop_CardboardBox_01.prefab", 0.46f, true);
      var trolley = BuildNormalizedProp("const_trolley", PCN + "Props/SM_Prop_HandTrolley_01.prefab", 1.12f, false, 180f);
      if (crate0 != null) BindFx("const_crate_0", AssetDatabase.GetAssetPath(crate0), 1f, 999f);
      if (crate1 != null) BindFx("const_crate_1", AssetDatabase.GetAssetPath(crate1), 1f, 999f);
      if (trolley != null) BindFx("const_trolley", AssetDatabase.GetAssetPath(trolley), 1f, 999f);
      EditorUtility.SetDirty(catalog);
    }

    /// <summary>
    /// 기지 상주 건설/물류 작업자 전용 Animator 컨트롤러: Synty ANIMATION - Base Locomotion의
    /// Idle(0) / Walk(1.6) / Run(4.0) 1D 블렌드 트리(전투 조준·엄폐 없음).
    /// </summary>
    public static RuntimeAnimatorController BuildWorkerController() {
      EnsureFolder(OutRoot + "/Animation");
      AssetDatabase.DeleteAsset(WorkerControllerPath);
      var controller = AnimatorController.CreateAnimatorControllerAtPath(WorkerControllerPath);
      controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
      var machine = controller.layers[0].stateMachine;
      var locomotion = controller.CreateBlendTreeInController("WorkerLocomotion", out var tree);
      tree.blendType = BlendTreeType.Simple1D;
      tree.blendParameter = "Speed";
      tree.useAutomaticThresholds = false;
      tree.AddChild(Clip("idle"), 0f);
      tree.AddChild(Clip("walk"), 1.6f);
      tree.AddChild(Clip("run"), 4.0f);
      machine.defaultState = locomotion;
      EditorUtility.SetDirty(controller);
      AssetDatabase.SaveAssets();
      return controller;
    }

    static void EnsureFolder(string assetPath) {
      var parts = assetPath.Split('/');
      var current = parts[0];
      for (var i = 1; i < parts.Length; i++) {
        var next = current + "/" + parts[i];
        if (AssetDatabase.IsValidFolder(next) == false) {
          AssetDatabase.CreateFolder(current, parts[i]);
        }
        current = next;
      }
    }
  }
}
