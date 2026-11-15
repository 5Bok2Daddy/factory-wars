namespace FactoryWars.EditorTools {
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// 항목 36: 숲·절벽·청록 바닥을 시가지로 바꾸기 위해 구입한 Synty 팩(City·Military·Battle Royale·Generic)의 모델을
  /// VisualCatalog의 소품·도시 칸(`dress/*`)에 연결한다. 팩 파일은 읽기만 한다(원본 수정 금지). 배율은 칸 데이터에만 둔다.
  /// 메뉴 Tools/Factory Wars/Assets/6. Assign Urban Dressing, 배치: -executeMethod FactoryWars.EditorTools.UrbanAssign.Run
  /// 팩이 없으면 아무것도 하지 않는다(칸은 지금 Kenney 모델 그대로).
  /// </summary>
  public static class UrbanAssign {
    class Row {
      public DressKind Kind;
      public string[] Names;
      /// <summary>모델의 가장 긴 수평 변을 이 미터로 맞춘다(변종 평균). 0이면 Scale을 쓴다.</summary>
      public float Meters;
      public float Scale = 1f;
    }

    /// <summary>절벽 칸: 칸 코드가 가로·높이·두께를 곱해 늘이므로 크기 1×1×1짜리 틀이 필요하다. 팩의 상자형 건물을 우리 폴더 틀(자식 배율만 다름)에 넣는다. 팩 파일은 그대로다.</summary>
    static GameObject UnitBlock(string source) {
      var prefab = Find(source);
      if (prefab == null) {
        return null;
      }
      var bounds = VisualCatalog.LocalBounds(prefab);
      var root = new GameObject("block_" + source);
      var child = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
      var scale = new Vector3(1f / Mathf.Max(0.1f, bounds.size.x), 1f / Mathf.Max(0.1f, bounds.size.y), 1f / Mathf.Max(0.1f, bounds.size.z));
      child.transform.localScale = scale;
      child.transform.localPosition = -Vector3.Scale(bounds.center, scale);
      var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{SyntyAssign.OutRoot}/Urban/block_{source}.prefab");
      UnityEngine.Object.DestroyImmediate(root);
      return saved;
    }

    /// <summary>
    /// 항목 36 수문 조립: 단일 수문 모델이 팩에 없어 옹벽(Port_Wall)·다리 기둥(Bridge_Support)·철제 문(Fence_Gate, 없으면 Village_Wall_Gate·방벽)·물보라(FX_Waterfall_Foam)를
    /// 우리 폴더 프리팹 한 개로 조립한다. 팩 모델은 감싸개 오브젝트의 자식으로 넣기만 한다(팩 원본 무수정). 배치·배율은 WaterGateRig가 런타임에 감싸개에 준다.
    /// </summary>
    static GameObject BuildWaterGate(StringBuilder csv) {
      var wall = Find("SM_Env_Port_Wall_02") ?? Find("SM_Env_Port_Wall_01");
      var pier = Find("SM_Env_Bridge_Support_01");
      var door = Find("SM_Env_Port_Wall_01") ?? Find("SM_Prop_Container_01") ?? Find("SM_Prop_Fence_Gate_01");
      var foam = Find("FX_Waterfall_Foam_01");
      if (wall == null || door == null) {
        Debug.LogWarning("[FW-Urban] 수문: 옹벽 또는 문 모델이 팩에 없어 건너뜀");
        return null;
      }
      var root = new GameObject("water_gate");
      var rig = root.AddComponent<WaterGateRig>();
      Transform Wrap(string name, GameObject model, out Vector3 size) {
        var holder = new GameObject(name).transform;
        holder.SetParent(root.transform, false);
        size = Vector3.one;
        if (model == null) {
          return holder;
        }
        var bounds = VisualCatalog.LocalBounds(model);
        size = bounds.size;
        var child = (GameObject)PrefabUtility.InstantiatePrefab(model, holder);
        // 모델의 바닥 중앙이 감싸개 원점이 되게 한다(자식 위치만 조정, 팩 파일은 그대로).
        child.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        return holder;
      }
      rig.Left = Wrap("PostL", wall, out rig.WallSize);
      rig.Right = Wrap("PostR", wall, out _);
      rig.PierL = Wrap("PierL", pier, out rig.PierSize);
      rig.PierR = Wrap("PierR", pier, out _);
      rig.Door = Wrap("Door", door, out rig.DoorSize);
      if (foam != null) {
        rig.Foam = Wrap("Foam", foam, out _);
        // 파티클 모델은 메쉬 크기가 없으므로 바닥 보정을 지운다.
        rig.Foam.GetChild(0).localPosition = Vector3.zero;
      }
      var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{SyntyAssign.OutRoot}/Urban/water_gate.prefab");
      UnityEngine.Object.DestroyImmediate(root);
      csv.AppendLine($"WaterGate,1,1,{wall.name}+{pier?.name}+{door.name}+{foam?.name}");
      return saved;
    }

    static Row Sized(DressKind kind, float meters, params string[] names) => new Row { Kind = kind, Names = names, Meters = meters };
    static Row Scaled(DressKind kind, float scale, params string[] names) => new Row { Kind = kind, Names = names, Scale = scale };

    static IEnumerable<Row> Rows() {
      // 목재 자원지(옛 숲): 나무 대신 팔레트·상자·드럼통 더미. 높이 3~5m, 간격은 숲 코드 그대로(1.2~1.4m)라 빽빽한 하치장이 된다.
      // 삼각형 예산: 숲 하나에 개체가 수백 개라 가벼운 모델(상자 288·팔레트 294)을 많이, 무거운 것(드럼통 더미 1.7천)은 조금만 섞는다.
      yield return Scaled(DressKind.Conifer, 1.7f, "SM_Prop_Crate_Large_01", "SM_Prop_Crate_Large_01", "SM_Prop_Crate_Large_01", "SM_Prop_Pallet_01", "SM_Prop_Pallet_01", "SM_Prop_Barrel_Stack_01");
      yield return Scaled(DressKind.Autumn, 1.5f, "SM_Prop_Crate_Large_01", "SM_Prop_Pallet_01");
      yield return Scaled(DressKind.Broadleaf, 1.8f, "SM_Prop_Pallet_01", "SM_Prop_Crate_Large_01");
      yield return Scaled(DressKind.TreeSparse, 1.3f, "SM_Prop_Pallet_01", "SM_Prop_Pallet_02");
      yield return Scaled(DressKind.Stump, 0.9f, "SM_Prop_Pallet_01", "SM_Prop_Pallet_02");
      yield return Sized(DressKind.Bush, 1.5f, "SM_Env_Bush_Small_01", "SM_Env_Bush_Small_02", "SM_Env_Bush_Small_03");
      // 빈 땅 흩뿌리기와 바위: 자갈·잔해·콘크리트 더미.
      yield return Sized(DressKind.Grass, 1.1f, "SM_Env_Grass_01", "SM_Env_Grass_02", "SM_Env_Grass_03", "SM_Env_Grass_04");
      yield return Sized(DressKind.RockSmall, 2.2f, "SM_Prop_Rubble_Pile_01", "SM_Prop_Rubble_Pile_02", "SM_Prop_Rubble_Pile_03");
      yield return Sized(DressKind.RockLarge, 5f, "SM_Env_Rubble_Pile_01", "SM_Env_Rubble_Pile_02", "SM_Prop_DebrisPile_02");
      yield return Sized(DressKind.GroundPatch, 3f, "SM_Env_Road_Patch_01");
      yield return Sized(DressKind.Debris, 3f, "SM_Prop_DebrisPile_03", "SM_Prop_Vehicle_Debris_01", "SM_Prop_Vehicle_Debris_02", "SM_Prop_Rubble_Pile_03");
      yield return Sized(DressKind.Wreck, 4.6f, "SM_Veh_Car_Destroyed_01", "SM_Veh_Light_Car_Destroyed_01");
      yield return Sized(DressKind.Ruin, 5f, "SM_Bld_Ruins_02", "SM_Bld_Ruins_03", "SM_Bld_Ruins_01");
      yield return Sized(DressKind.Furniture, 1f, "SM_Prop_CardboardBox_01", "SM_Prop_CardboardBox_02");
      // 엄폐 소품: 컨테이너·방벽.
      yield return Sized(DressKind.Container, 6.2f, "SM_Prop_Container_01", "SM_Prop_Container_Small_01");
      yield return Sized(DressKind.Barrier, 2.6f, "SM_Prop_Barrier_05", "SM_Prop_Barrier_06");
      yield return Sized(DressKind.BaseWall, 4.8f, "SM_Env_Fence_01");
      // 해안 옹벽 모듈(10~12m마다)과 점령 거점 소품. 팩 모델 그대로, 배율은 칸 데이터에만 둔다.
      yield return Sized(DressKind.QuayPillar, 1.8f, "SM_Bld_Base_Pillar_Half_01");
      yield return Sized(DressKind.QuayRail, 4.4f, "SM_Bld_Railing_01");
      yield return Sized(DressKind.QuayBollard, 1.1f, "SM_Env_Port_Boat_Tie_01");
      yield return Sized(DressKind.OutpostTower, 3.2f, "SM_Prop_GuardTower_01");
      yield return Sized(DressKind.OutpostWall, 2.8f, "SM_Prop_Barricade_01");
      yield return Scaled(DressKind.LumberYard, 1f, "SM_Bld_Warehouse_01");
      // 길가: 가로등·소화전·쓰레기통·고깔.
      yield return Scaled(DressKind.RoadProp, 1f, "SM_Prop_LightPole_Base_01", "SM_Prop_LightPole_Base_01", "SM_Prop_Hydrant_01", "SM_Prop_TrashCan_01", "SM_Prop_Cone_01");
      // 도시 건물(칸 크기에 맞춰 늘어난다: Fit). 세로는 칸 데이터의 목표 높이 쪽으로 0.8~1.3배.
      yield return Scaled(DressKind.CitySmall, 1f, "SM_Bld_Shop_01", "SM_Bld_Shop_02", "SM_Bld_Shop_04", "SM_Bld_Shop_05", "SM_Bld_Shop_06", "SM_Bld_Apartment_01", "SM_Bld_Apartment_02", "SM_Bld_Apartment_03");
      yield return Scaled(DressKind.CityMedium, 1f, "SM_Bld_Shop_03", "SM_Bld_Apartment_Stack_01", "SM_Bld_Apartment_Stack_02", "SM_Bld_Shop_Corner_01", "SM_Bld_Shop_Corner_02");
      yield return Scaled(DressKind.CityIndustrial, 1f, "SM_Bld_GasStation_01", "SM_Bld_Shop_03", "SM_Bld_Apartment_Stack_03");
      yield return Scaled(DressKind.CityTower, 1f, "SM_Bld_OfficeOld_Small_02", "SM_Bld_OfficeOld_Large_02");
      yield return Scaled(DressKind.CityGarrison, 1f, "SM_Bld_Station_01", "SM_Bld_Station_02", "SM_Bld_Shop_Corner_01");
    }

    [MenuItem("Tools/Factory Wars/Assets/6. Assign Urban Dressing")]
    public static void Menu() {
      Debug.Log($"[FW-Urban] 칸 {Assign()}개 연결");
    }

    public static void Run() {
      // 프로필 값(바닥 색·빛)도 같이 다시 쓴다: 시가지 톤은 VisualPresets에 있다.
      FactoryWarsSetup.BuildVisualProfiles();
      Debug.Log($"[FW-Urban] 칸 {Assign()}개 연결");
    }

    static Dictionary<string, GameObject> _byName;

    static GameObject Find(string name) {
      if (_byName == null) {
        _byName = new Dictionary<string, GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { SyntyAssign.PackRoot })) {
          var path = AssetDatabase.GUIDToAssetPath(guid);
          var key = Path.GetFileNameWithoutExtension(path);
          if (_byName.ContainsKey(key) == false) {
            _byName[key] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
          }
        }
      }
      return _byName.TryGetValue(name, out var found) ? found : null;
    }

    public static int Assign() {
      if (AssetDatabase.IsValidFolder(SyntyAssign.PackRoot + "/PolygonCity") == false) {
        Debug.LogWarning("[FW-Urban] City 팩 없음: Tools/import_synty.py로 먼저 들여오세요.");
        return 0;
      }
      _byName = null;
      var catalog = VisualCatalogTools.BuildSlots();
      AssetDatabase.DeleteAsset(SyntyAssign.OutRoot + "/Urban");
      Directory.CreateDirectory(Path.Combine(Application.dataPath, "FactoryWarsArt", "Synty", "Urban"));
      AssetDatabase.Refresh();
      var blocks = new[] { "SM_Bld_Shop_01", "SM_Bld_Shop_02", "SM_Bld_Shop_04", "SM_Bld_Shop_05", "SM_Bld_Shop_06", "SM_Bld_Apartment_01", "SM_Bld_Apartment_02", "SM_Bld_Apartment_03" }.Select(UnitBlock).Where(b => b != null).ToList();
      var csv = new StringBuilder("DressKind,Scale,Models,PackFiles\n");
      var count = 0;
      foreach (var row in Rows()) {
        var slot = catalog.Get(VisualCatalog.DressId(row.Kind));
        if (slot == null) {
          slot = new VisualSlot { Id = VisualCatalog.DressId(row.Kind), Category = VisualCatalogTools.DressCategory(row.Kind) };
          catalog.Slots.Add(slot);
        }
        var prefabs = row.Names.Select(Find).Where(p => p != null).ToList();
        if (prefabs.Count == 0) {
          Debug.LogWarning($"[FW-Urban] {row.Kind}: 팩에 모델이 없어 건너뜀 ({string.Join(",", row.Names)})");
          continue;
        }
        var scale = row.Scale;
        if (row.Meters > 0f) {
          scale = row.Meters / Mathf.Max(0.05f, prefabs.Average(p => { var s = VisualCatalog.LocalBounds(p).size; return Mathf.Max(s.x, s.z); }));
        }
        slot.Prefabs = prefabs;
        slot.Scale = scale;
        slot.AutoFit = false;
        slot.TeamColor = TeamColorMode.None;
        slot.RealWorld = RealWorldCheck.Clear;
        slot.Notes = $"Synty 팩 시가지 {row.Kind}";
        var triangles = prefabs.Average(p => p.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(i => (int)f.sharedMesh.GetIndexCount(i)) / 3));
        csv.AppendLine(string.Join(",", row.Kind, scale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), prefabs.Count, string.Join("+", prefabs.Select(p => p.name + ":" + p.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(i => (int)f.sharedMesh.GetIndexCount(i)) / 3))), triangles.ToString("0")));
        count++;
      }
      var cliff = catalog.Get(VisualCatalog.DressId(DressKind.Cliff));
      if (cliff != null && blocks.Count > 0) {
        cliff.Prefabs = blocks;
        cliff.Scale = 1f;
        cliff.AutoFit = false;
        cliff.TeamColor = TeamColorMode.None;
        cliff.RealWorld = RealWorldCheck.Clear;
        cliff.Notes = "Synty 팩 시가지 Cliff(상자형 건물 블록)";
        csv.AppendLine($"Cliff,1,{blocks.Count},{string.Join("+", blocks.Select(b => b.name))}");
        count++;
      }
      // 효과 칸: 가진 팩의 FX를 그대로 연결(포구 섬광은 발사마다 생겨 모바일 부하가 커서 절차 효과를 유지한다).
      foreach (var (fx, names, life, scale) in new[] {
        ("impact", new[] { "Explosion_Small_FX", "FX_Grenade_Explosive_01" }, 2.5f, 0.8f),
        ("explosion", new[] { "FX_Explosion_Large_Dark_01", "Explosion_Large_FX" }, 4f, 1f),
        ("flame", new[] { "FX_Fire_Small_01", "Fire_01_FX" }, 2f, 0.8f),
        ("smoke", new[] { "FX_Smoke_Medium_01", "FX_Smoke_Large_01" }, 6f, 1f),
        ("weather_sand", new[] { "FX_Dust_Blowing_Soft_Large_01", "FX_Dust_Blowing_Soft_01" }, 0f, 1f),
        ("weather_rain", new[] { "FX_Rain_01", "FX_Rain" }, 0f, 1f),
        ("weather_fog", new[] { "FX_Fog_01" }, 0f, 1f),
      }) {
        var fxSlot = catalog.Get(VisualCatalog.FxId(fx));
        if (fxSlot == null) {
          fxSlot = new VisualSlot { Id = VisualCatalog.FxId(fx), Category = VisualCategory.Effect };
          catalog.Slots.Add(fxSlot);
        }
        var fxPrefabs = names.Select(Find).Where(p => p != null).Take(1).ToList();
        if (fxPrefabs.Count == 0) {
          continue;
        }
        fxSlot.Prefabs = fxPrefabs;
        fxSlot.Scale = scale;
        fxSlot.Lifetime = life > 0f ? life : 2f;
        fxSlot.AutoFit = false;
        fxSlot.RealWorld = RealWorldCheck.Clear;
        fxSlot.Notes = "Synty 팩 FX " + fxPrefabs[0].name;
        csv.AppendLine($"fx/{fx},{scale},1,{fxPrefabs[0].name}");
        count++;
      }
      var gate = BuildWaterGate(csv);
      var gateSlot = catalog.Get(VisualCatalog.DressId(DressKind.WaterGate));
      if (gateSlot == null) {
        gateSlot = new VisualSlot { Id = VisualCatalog.DressId(DressKind.WaterGate), Category = VisualCategory.Terrain };
        catalog.Slots.Add(gateSlot);
      }
      if (gate != null) {
        gateSlot.Prefabs = new List<GameObject> { gate };
        gateSlot.Scale = 1f;
        gateSlot.AutoFit = false;
        gateSlot.TeamColor = TeamColorMode.None;
        gateSlot.RealWorld = RealWorldCheck.Clear;
        gateSlot.Notes = "Synty 팩 부품 조립 수문(우리 프리팹 water_gate)";
        count++;
      }
      catalog.Invalidate();
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
      var logs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "assets", "urban"));
      Directory.CreateDirectory(logs);
      File.WriteAllText(Path.Combine(logs, "dress-slots.csv"), csv.ToString(), new UTF8Encoding(true));
      return count;
    }
  }
}
