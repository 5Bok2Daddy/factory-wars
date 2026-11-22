namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// Map4P v4 시범 블록(PROMPT-MAP4P-V4 4절). 사람이 검수할 수 있는 블록 프리팹을 보유 Synty 팩 조각으로 조립해
  /// Assets/FactoryWarsArt/Blocks/에 저장하고 VisualCatalog "block/..." 칸에 넣는다. 팩 원본은 건드리지 않는다.
  /// 블록 안의 배치는 이 파일의 표(블록 좌표)가 전부다. 맵에는 블록 단위로만 놓는다(GridMapView).
  /// - blk_base_pad: 자리 0(남서) 본진, 원점 = 본진 안쪽 모서리(서쪽·남쪽 옹벽 안쪽 꼭짓점), +X = 동, +Z = 북, Y 0 = 고지대 바닥.
  ///   다른 자리는 화면에서 90도씩 돌린다. 옹벽 위 방벽 띠는 칸 지도의 본진 둘레에서 만든다.
  /// - blk_city_mixed_2x2_a/b/c: 16×16m 도시 블록(칸 2×2), 원점 = 블록 가운데, Y 0 = 바닥.
  /// 배치: -executeMethod FactoryWars.EditorTools.BlockKit.BuildPilotBlocks
  /// </summary>
  public static class BlockKit {
    public const string OutFolder = "Assets/FactoryWarsArt/Blocks";
    const string PC = "Assets/ExternalAssets/Synty/PolygonCity/Prefabs/";
    const string PM = "Assets/ExternalAssets/Synty/PolygonMilitary/Prefabs/";

    /// <summary>본진 장식 한 덩어리(자리 0 본진 좌표). Half = 바닥 반 크기(건설 금지 구역 = 이 사각형 + 2m).</summary>
    public struct Decor {
      public string Name;
      public string Prefab;
      public float U, W, Yaw, Scale;
      public Vector2 Half;
    }

    /// <summary>
    /// 본진 장식 표. FactoryWarsGridMap이 같은 표로 시뮬레이션 건설 금지 구역을 만든다(장식 위에 건물을 짓지 않게).
    /// 시작(사령부) (44,92), 1단 자원 (14,128)·(128,14)·(14,60), 앞마당 (112,34), 2단 자원 (136,18)·(96,14), 경사로 (88,88)은 비운다.
    /// </summary>
    public static readonly Decor[] BaseDecor = {
      new Decor { Name = "helipad", Prefab = PM + "Environment/SM_Env_Helipad_01.prefab", U = 36, W = 32, Yaw = 45, Scale = 0.9f, Half = new Vector2(13.7f, 13f) },
      new Decor { Name = "helicopter", Prefab = PM + "Vehicles/SM_Veh_Helicopter_Transport_01.prefab", U = 36, W = 33, Yaw = 45, Scale = 1f, Half = new Vector2(0, 0) },
      new Decor { Name = "hangar", Prefab = PM + "Buildings/SM_Bld_Hangar_Open_01.prefab", U = 72, W = 20, Yaw = 0, Scale = 0.85f, Half = new Vector2(13f, 15.5f) },
      new Decor { Name = "control-tower", Prefab = PM + "Buildings/SM_Bld_ControlTower_01.prefab", U = 9, W = 9, Yaw = 45, Scale = 0.8f, Half = new Vector2(4f, 4f) },
      new Decor { Name = "barracks-a", Prefab = PM + "Buildings/SM_Bld_Barracks_01.prefab", U = 9, W = 82, Yaw = 90, Scale = 1f, Half = new Vector2(3.7f, 6.7f) },
      new Decor { Name = "barracks-b", Prefab = PM + "Buildings/SM_Bld_Barracks_01.prefab", U = 9, W = 104, Yaw = 90, Scale = 1f, Half = new Vector2(3.7f, 6.7f) },
      new Decor { Name = "tent", Prefab = PM + "Buildings/SM_Bld_Tent_01.prefab", U = 26, W = 112, Yaw = 0, Scale = 1f, Half = new Vector2(3.9f, 6f) },
      new Decor { Name = "fuel-a", Prefab = PM + "Props/SM_Prop_Gastank_01.prefab", U = 28, W = 5, Yaw = 0, Scale = 1f, Half = new Vector2(3.4f, 1.2f) },
      new Decor { Name = "fuel-b", Prefab = PM + "Props/SM_Prop_Gastank_01.prefab", U = 29, W = 9, Yaw = 0, Scale = 1f, Half = new Vector2(3.4f, 1.2f) },
      new Decor { Name = "fuel-bladder", Prefab = PM + "Props/Military/SM_Prop_Fuel_Bladder_01.prefab", U = 44, W = 9, Yaw = 90, Scale = 0.8f, Half = new Vector2(5.3f, 3.6f) },
      new Decor { Name = "truck", Prefab = PM + "Vehicles/SM_Veh_Truck_01.prefab", U = 60, W = 44, Yaw = 30, Scale = 1f, Half = new Vector2(1.6f, 3.4f) },
      new Decor { Name = "apc", Prefab = PM + "Vehicles/SM_Veh_APC_01.prefab", U = 66, W = 46, Yaw = 20, Scale = 1f, Half = new Vector2(1.7f, 2.7f) },
      new Decor { Name = "light-tank", Prefab = PM + "Vehicles/SM_Veh_Light_Tank_01.prefab", U = 73, W = 45, Yaw = 10, Scale = 1f, Half = new Vector2(1.7f, 3.3f) },
      new Decor { Name = "pickup", Prefab = PM + "Vehicles/SM_Veh_Pickup_Small_01.prefab", U = 80, W = 46, Yaw = 35, Scale = 1f, Half = new Vector2(1.2f, 2.3f) },
      new Decor { Name = "tower-west", Prefab = PM + "Buildings/SM_Bld_GuardTower_01.prefab", U = 4, W = 148, Yaw = 0, Scale = 1f, Half = new Vector2(1.5f, 2f) },
      new Decor { Name = "tower-south", Prefab = PM + "Buildings/SM_Bld_GuardTower_01.prefab", U = 148, W = 4, Yaw = 90, Scale = 1f, Half = new Vector2(1.5f, 2f) },
      new Decor { Name = "tower-ramp-a", Prefab = PM + "Buildings/SM_Bld_GuardTower_02.prefab", U = 60, W = 100, Yaw = 45, Scale = 1f, Half = new Vector2(2f, 3.3f) },
      new Decor { Name = "tower-ramp-b", Prefab = PM + "Buildings/SM_Bld_GuardTower_02.prefab", U = 100, W = 60, Yaw = 45, Scale = 1f, Half = new Vector2(2f, 3.3f) },
      new Decor { Name = "lamp-a", Prefab = PM + "Props/SM_Prop_LightPole_01.prefab", U = 18, W = 20, Yaw = 0, Scale = 1f, Half = new Vector2(1, 1) },
      new Decor { Name = "lamp-b", Prefab = PM + "Props/SM_Prop_LightPole_01.prefab", U = 54, W = 48, Yaw = 0, Scale = 1f, Half = new Vector2(1, 1) },
      new Decor { Name = "lamp-c", Prefab = PM + "Props/SM_Prop_LightPole_01.prefab", U = 30, W = 96, Yaw = 0, Scale = 1f, Half = new Vector2(1, 1) },
      new Decor { Name = "crates", Prefab = PM + "Props/SM_Prop_Crate_Wood_01.prefab", U = 20, W = 70, Yaw = 15, Scale = 2.2f, Half = new Vector2(1.5f, 1.5f) },
    };

    /// <summary>프리팹을 바닥 가운데가 (x, z), 바닥이 y에 오도록 놓는다(팩 프리팹마다 기준점이 모서리·가운데로 다르다).</summary>
    static GameObject Place(Transform parent, string path, Vector3 center, float yaw, float scale, string name = null) {
      var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
      if (source == null) {
        Debug.LogWarning($"[FW-Block] missing {path}");
        return null;
      }
      var item = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
      item.name = name ?? source.name;
      item.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
      item.transform.localScale = Vector3.one * scale;
      item.transform.localPosition = Vector3.zero;
      var bounds = WorldBounds(item);
      var offset = center - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) + parent.position;
      item.transform.position += offset;
      return item;
    }

    public static Bounds WorldBounds(GameObject item) {
      var renderers = item.GetComponentsInChildren<Renderer>();
      var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(item.transform.position, Vector3.zero);
      foreach (var r in renderers) bounds.Encapsulate(r.bounds);
      return bounds;
    }

    // ---------------------------------------------------------------- base pad

    static GameObject BuildBasePad(char[,] grid) {
      var root = new GameObject("blk_base_pad");
      foreach (var d in BaseDecor) {
        Place(root.transform, d.Prefab, new Vector3(d.U, 0f, d.W), d.Yaw, d.Scale, d.Name);
      }
      // 옹벽 위 방벽 띠: 자리 0 본진 칸의 바깥 변(경사로·경계 쪽 제외). 높은 방벽 묶음 4종을 해시로 섞고 끝을 맞대 잇는다.
      var cells = BaseCells(grid);
      var minC = cells.Min(c => c.Item2);
      var maxR = cells.Max(c => c.Item1);
      var cornerX = FactoryWarsSetup.CellX(minC) - FactoryWarsSetup.GridCell / 2f;
      var cornerZ = FactoryWarsSetup.CellZ(maxR) - FactoryWarsSetup.GridCell / 2f;
      var walls = new GameObject("PerimeterWall").transform;
      walls.SetParent(root.transform, false);
      var variants = new[] { "SM_Prop_Barrier_Tall_Group_01", "SM_Prop_Barrier_Tall_Group_02", "SM_Prop_Barrier_Tall_Group_03" };
      var dirs = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
      var index = 0;
      foreach (var (r, c) in cells.OrderBy(c => c.Item1).ThenBy(c => c.Item2)) {
        for (var d = 0; d < 4; d++) {
          var (dr, dc) = dirs[d];
          var nr = r + dr;
          var nc = c + dc;
          if (cells.Contains((nr, nc))) continue;
          var neighbour = nr >= 0 && nc >= 0 && nr < FactoryWarsSetup.GridCells && nc < FactoryWarsSetup.GridCells ? grid[nr, nc] : 'E';
          if (neighbour == 'P' || neighbour == 'E') continue;
          // 변 가운데(칸 가운데에서 바깥으로 4m, 안쪽으로 0.8m 들인다).
          var x = FactoryWarsSetup.CellX(c) + dc * (FactoryWarsSetup.GridCell / 2f - 0.8f) - cornerX;
          var z = FactoryWarsSetup.CellZ(r) - dr * (FactoryWarsSetup.GridCell / 2f - 0.8f) - cornerZ;
          var yaw = d < 2 ? 90f : 0f; // 묶음은 Z로 길다: 가로 변은 90도 돌린다.
          var hash = (r * 73856093) ^ (c * 19349663) ^ d;
          var variant = variants[Mathf.Abs(hash) % variants.Length];
          // 한 변(8m)에 5.2m 묶음 1개 + 남는 2.8m는 낮은 방벽 1개(키 섞기).
          var along = d < 2 ? Vector3.right : Vector3.forward;
          var center = new Vector3(x, 0f, z);
          Place(walls, $"{PM}Props/{variant}.prefab", center - along * 1.3f, yaw, 1f, $"Wall{index++}");
          Place(walls, $"{PM}Props/SM_Prop_Barrier_0{1 + Mathf.Abs(hash / 7) % 3}.prefab", center + along * 2.6f, yaw, 1.2f, $"Wall{index++}");
        }
      }
      return root;
    }

    public static HashSet<(int, int)> BaseCells(char[,] grid) {
      var set = new HashSet<(int, int)>();
      var queue = new Queue<(int, int)>();
      queue.Enqueue((FactoryWarsSetup.GridCells - 8, 8));
      while (queue.Count > 0) {
        var (r, c) = queue.Dequeue();
        if (r < 0 || c < 0 || r >= FactoryWarsSetup.GridCells || c >= FactoryWarsSetup.GridCells || set.Contains((r, c))) continue;
        var ch = grid[r, c];
        if (ch != 'B' && ch != 'p' && ch != 'v') continue;
        set.Add((r, c));
        queue.Enqueue((r + 1, c));
        queue.Enqueue((r - 1, c));
        queue.Enqueue((r, c + 1));
        queue.Enqueue((r, c - 1));
      }
      return set;
    }

    // ---------------------------------------------------------------- city block

    /// <summary>모듈 건물 한 채: 1층(상가) + 위층(아파트) n개 + 지붕. 모듈은 5×5m. 층마다 실제 높이를 재서 쌓는다.</summary>
    static void Stack(Transform parent, string name, Vector2 center, float yaw, string ground, string floor, int floors, string roof) {
      var building = new GameObject(name).transform;
      building.SetParent(parent, false);
      var y = 0f;
      var first = Place(building, PC + "Buildings/" + ground + ".prefab", new Vector3(center.x, 0f, center.y), yaw, 1f, "Ground");
      y += first != null ? WorldBounds(first).size.y : 3f;
      for (var i = 0; i < floors; i++) {
        var piece = Place(building, PC + "Buildings/" + floor + ".prefab", new Vector3(center.x, y, center.y), yaw, 1f, $"Floor{i + 1}");
        y += piece != null ? WorldBounds(piece).size.y : 3f;
      }
      if (string.IsNullOrEmpty(roof) == false) {
        Place(building, PC + "Buildings/" + roof + ".prefab", new Vector3(center.x, y, center.y), yaw, 1f, "Roof");
      }
    }

    static void Sidewalk(Transform parent, int variant) {
      // 둘레 인도(블록 가장자리 1.25m 띠): 가로등 2(팔은 변을 따라), 화분 2, 벤치 1, 소화전·우체통. 모두 블록 안(|x|,|z| ≤ 7.3)에 둔다.
      // 나무(지름 2.5m)는 띠보다 넓어 길로 삐져나오므로 두지 않는다(공원 칸 K 블록에서 쓴다).
      var props = new GameObject("Street").transform;
      props.SetParent(parent, false);
      var flip = variant % 2 == 0 ? 1f : -1f;
      Place(props, PC + "Props/SM_Prop_LightPole_Base_01.prefab", new Vector3(-6.9f * flip, 0f, -7.0f), 90f, 1f, "Lamp0");
      Place(props, PC + "Props/SM_Prop_LightPole_Base_01.prefab", new Vector3(6.9f * flip, 0f, 7.0f), 270f, 1f, "Lamp1");
      Place(props, PC + "Props/SM_Prop_Planter_01.prefab", new Vector3(0.2f, 0f, -7.05f), 0f, 1f, "Planter0");
      Place(props, PC + "Props/SM_Prop_Planter_01.prefab", new Vector3(7.05f * flip, 0f, 0.3f), 90f, 1f, "Planter1");
      Place(props, PC + "Props/SM_Prop_ParkBench_01.prefab", new Vector3(-7.05f * flip, 0f, 0.6f), 90f, 1f, "Bench0");
      Place(props, PC + "Props/SM_Prop_Hydrant_01.prefab", new Vector3(3.2f * flip, 0f, 7.1f), 0f, 1f, "Hydrant");
      Place(props, PC + "Props/SM_Prop_Mailbox_01.prefab", new Vector3(-2.6f * flip, 0f, 7.1f), 0f, 1f, "Mailbox");
    }

    /// <summary>주차 칸(사분면 6.5m): 주차선 + 승용차 2대(Z로 길게 나란히).</summary>
    static void ParkingCorner(Transform parent, Vector2 center) {
      var lot = new GameObject("Parking").transform;
      lot.SetParent(parent, false);
      Place(lot, PC + "Environments/SM_Env_Road_ParkingLines_01.prefab", new Vector3(center.x, 0.02f, center.y), 0f, 0.6f, "Lines");
      Place(lot, PC + "Vehicles/SM_Veh_Car_Sedan_01.prefab", new Vector3(center.x - 1.35f, 0f, center.y), 2f, 0.9f, "Car0");
      Place(lot, PC + "Vehicles/SM_Veh_Car_Van_01.prefab", new Vector3(center.x + 1.45f, 0f, center.y + 0.1f), -3f, 0.85f, "Car1");
    }

    /// <summary>16×16m 블록을 2×2 사분면(가운데 ±3.5, 한 변 6.5m)으로 나눠 건물·주차를 하나씩 넣는다. 사분면 사이 0.5m는 틈.</summary>
    static GameObject BuildCity(string id) {
      var root = new GameObject(id);
      var b = new GameObject("Buildings").transform;
      b.SetParent(root.transform, false);
      const float Q = 3.5f;
      switch (id) {
        case "blk_city_mixed_2x2_a":
          // 남서 4층 아파트(모서리) · 남동 2층 상가 · 북동 3층 상가주택 · 북서 주차.
          Stack(b, "Apartment4", new Vector2(-Q, -Q), 180f, "SM_Bld_Shop_Corner_01", "SM_Bld_Apartment_Corner_01", 3, null);
          Stack(b, "Shop2", new Vector2(Q, -Q), 180f, "SM_Bld_Shop_02", "SM_Bld_Apartment_02", 1, null);
          Stack(b, "Mixed3", new Vector2(Q, Q), 0f, "SM_Bld_Shop_05", "SM_Bld_Apartment_03", 2, null);
          ParkingCorner(root.transform, new Vector2(-Q, Q));
          Sidewalk(root.transform, 0);
          break;
        case "blk_city_mixed_2x2_b":
          // 남쪽 반 긴 1층 상가 · 북서 5층 아파트 · 북동 주차.
          Stack(b, "LongShop", new Vector2(0f, -Q), 180f, "SM_Bld_Shop_03", "SM_Bld_Apartment_01", 0, null);
          Stack(b, "Apartment5", new Vector2(-Q, Q), 0f, "SM_Bld_Shop_Corner_02", "SM_Bld_Apartment_Corner_02", 4, null);
          ParkingCorner(root.transform, new Vector2(Q, Q));
          Sidewalk(root.transform, 1);
          break;
        default:
          // 서쪽 반 큰 2층 상가건물(군용 팩 완성 건물) · 남동 3층 아파트 · 북동 주차.
          Place(b, PM + "Buildings/SM_Bld_Shop_09.prefab", new Vector3(-Q, 0f, 0f), 0f, 0.92f, "Townhouse");
          Stack(b, "Apartment3", new Vector2(Q, -Q), 90f, "SM_Bld_Shop_06", "SM_Bld_Apartment_03", 2, null);
          ParkingCorner(root.transform, new Vector2(Q, Q));
          Sidewalk(root.transform, 2);
          break;
      }
      return root;
    }

    public static readonly string[] CityIds = { "blk_city_mixed_2x2_a", "blk_city_mixed_2x2_b", "blk_city_mixed_2x2_c" };

    static void Register(VisualCatalog catalog, string id, GameObject prefab, Vector3 size) {
      var slotId = "block/" + id;
      var slot = catalog.Slots.FirstOrDefault(s => s != null && s.Id == slotId);
      if (slot == null) {
        slot = new VisualSlot { Id = slotId };
        catalog.Slots.Add(slot);
      }
      slot.Category = VisualCategory.Prop;
      slot.Prefabs = new List<GameObject> { prefab };
      slot.Scale = 1f;
      slot.AutoFit = false;
      slot.PivotOffset = Vector3.zero;
      slot.Size = size;
      slot.TeamColor = TeamColorMode.None;
      slot.RealWorld = RealWorldCheck.Clear;
      slot.OwnerRing = false;
      slot.Notes = "Map4P v4 pilot block (BlockKit.cs), Synty POLYGON City + Military pieces";
    }

    [MenuItem("Tools/Factory Wars/Map4P v4/Build Pilot Blocks")]
    public static void BuildPilotBlocks() {
      if (AssetDatabase.IsValidFolder("Assets/FactoryWarsArt") == false) AssetDatabase.CreateFolder("Assets", "FactoryWarsArt");
      if (AssetDatabase.IsValidFolder(OutFolder) == false) AssetDatabase.CreateFolder("Assets/FactoryWarsArt", "Blocks");
      var catalog = Resources.Load<VisualCatalog>(VisualCatalog.ResourcePath);
      var grid = FactoryWarsSetup.ReadGrid(FactoryWarsSetup.GridCsvPath);
      void Save(string id, GameObject scene) {
        var size = WorldBounds(scene).size;
        var renderers = scene.GetComponentsInChildren<Renderer>().Length;
        var prefab = PrefabUtility.SaveAsPrefabAsset(scene, $"{OutFolder}/{id}.prefab");
        Object.DestroyImmediate(scene);
        Register(catalog, id, prefab, size);
        Debug.Log($"[FW-Block] {id} size={size} renderers={renderers}");
      }
      Save("blk_base_pad", BuildBasePad(grid));
      foreach (var id in CityIds) {
        Save(id, BuildCity(id));
      }
      catalog.Invalidate();
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
    }
  }
}
