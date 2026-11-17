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
  public static class SyntyAssign {
    public const string PackRoot = "Assets/ExternalAssets/Synty";
    public const string OutRoot = "Assets/FactoryWarsArt/Synty";
    public const string ControllerPath = OutRoot + "/Animation/PackInfantry.controller";

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
    static Row Vehicle(string id, string model, string[] swap, string turret = null, string barrel = null, string muzzle = null, params string[] spin) =>
      new Row { Id = "unit/" + id, Kind = Kind.Vehicle, Models = new[] { model }, Swap = swap, Turret = turret, Barrel = barrel, Muzzle = muzzle, Spin = spin };
    static Row Building(string id, string model, string[] swap, float width = 6.4f, float height = 9f) =>
      new Row { Id = "building/" + id, Kind = Kind.Building, Models = new[] { model }, Swap = swap, Scale = width, Height = height };

    static readonly string[] None = new string[0];

    static IEnumerable<Row> Rows() {
      // 보병(캐릭터는 실제 크기 1.8m, 배율은 화면 가독성용 슬롯 데이터)
      yield return Squad("ff_engineer", 1.3f, SoldiersFree, "chr_contractor_1");
      yield return Squad("cf_engineer", 1.3f, None, "chr_contractor_2");
      yield return Squad("ff_ranger_squad", 1.25f, SoldiersFree, "chr_soldier_1", "chr_soldier_1", "chr_soldier_1");
      yield return Squad("ff_skyguard_squad", 1.25f, SoldiersFree, "chr_soldier_1", "chr_soldier_1", "chr_soldier_1");
      yield return Squad("cf_militia", 1.25f, None, "chr_soldier_2", "chr_soldier_2");
      yield return Squad("cf_flame_squad", 1.25f, None, "chr_bombsuit", "chr_bombsuit");
      // 저격수: 위장복 한 명. 중앙 암시장 경비: 진영 색이 아닌 다른 군복 한 명(TraderView가 칸에서 직접 꺼내 쓴다).
      yield return Squad("ff_sniper", 1.25f, SoldiersFree, "chr_ghillie");
      yield return Squad("cf_sniper", 1.25f, None, "chr_ghillie");
      yield return Squad("trader_guard", 1.25f, None, "chr_soldier_2_alt3");
      // 차량·배·공중
      yield return Vehicle("ff_scout_buggy", "buggy", LightFree);
      yield return Vehicle("cf_scout_bike", "bike", LightContinental);
      yield return Vehicle("ff_eagle_tank", "tank_c", LandFree, "Turret", "Barrel", "Barrel");
      yield return Vehicle("cf_dragonscale_tank", "tank_light", LandContinental, "Turret", "Barrel", "Barrel");
      yield return Vehicle("ff_bulwark_tank", "tank_a", LandFree, "Turret", "Barrel", "Barrel");
      yield return Vehicle("cf_fortress_tank", "tank_b", LandContinental, "Turret", "Barrel", "Barrel");
      yield return Vehicle("ff_field_support", "apc", LandFree);
      yield return Vehicle("cf_thunder_aa", "radar_tank", LandContinental, null, null, null, "Radar_Base:Y");
      yield return Vehicle("cf_longwall_mlrs", "rocket_truck", LandContinental, "_Base", null, "Rocket_Battery");
      yield return Vehicle("ff_hawk_gunship", "heli_a", None, null, null, null, "Blades_Main", "Blades_Back");
      yield return Vehicle("cf_gale_gunship", "heli_b", HeliContinental, null, null, null, "Blades_Main", "Blades_Back");
      yield return Vehicle("ff_skyfortress_bomber", "transport_plane", None);
      yield return Vehicle("ff_stormnest_carrier", "cargo_plane", None, null, null, null, "Prop_0");
      yield return Vehicle("ff_nest_drone", "drone", None, null, null, null, "Drone_Prop");
      yield return Vehicle("ff_scout_boat", "boat", LightFree);
      yield return Vehicle("cf_scout_boat", "boat", LightContinental);
      // 건물(양 진영이 같은 모델, 색 변형만 다르다)
      foreach (var faction in new[] { "ff", "cf" }) {
        var swap = faction == "ff" ? BuildingsFree : BuildingsContinental;
        yield return Building(faction + "_barracks", "bld_barracks", swap);
        yield return Building(faction + "_supply_depot", "bld_shed", swap);
        yield return Building(faction + "_power_plant", "bld_gas_tower_2", swap, 6.8f, 7f);
        yield return Building(faction + "_processor", "bld_gas_tower_1", swap, 8f, 6f);
        yield return Building(faction + "_research_lab", "bld_control_room", swap);
        yield return Building(faction + "_vehicle_factory", "bld_hangar", swap, 9f, 7f);
        yield return Building(faction + "_airfield", "bld_control_tower", swap, 6.4f, 10f);
        yield return Building(faction + "_heavy_works", "bld_gas_station", swap, 8f, 6f);
        yield return Building(faction + "_ground_tower", "bld_guard_tower_2", swap);
        var props = faction == "ff" ? PropsFree : PropsContinental;
        yield return Building(faction + "_air_defense", "prop_launcher_2", props);
        yield return Building(faction + "_collection_yard", "bld_tent_open", swap);
        yield return Building(faction + "_sandbags", "prop_barrier_2", swap, 5.4f);
        yield return Building(faction + "_bunker", "prop_barrier_1", swap, 4.6f);
        yield return Building(faction + "_medic_tent", "bld_tent_closed", swap);
        yield return Building(faction + "_repair_bay", "bld_tent_shelter", swap);
        yield return Building(faction + "_radar", "prop_radar", props);
        yield return Building(faction + "_helipad", "bld_tent_cover", swap);
        yield return Building(faction + "_artillery", "prop_launcher_1", props);
        yield return Building(faction + "_headquarters", faction == "ff" ? "bld_tent_sandbags" : "bld_tent_entrance", swap, 7.8f);
      }
      yield return Building("ff_carrier_yard", "bld_hangar_open", BuildingsFree, 9f, 7f);
      yield return Building("cf_missile_silo", "bld_oil_tower", BuildingsContinental, 6.4f, 10f);
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
            slot.AutoFit = true;
            slot.Scale = 1f;
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
      catalog.Invalidate();
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
      var logs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "assets", "synty"));
      Directory.CreateDirectory(logs);
      File.WriteAllText(Path.Combine(logs, "slot-table.csv"), csv.ToString(), new UTF8Encoding(true));
      return count;
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
