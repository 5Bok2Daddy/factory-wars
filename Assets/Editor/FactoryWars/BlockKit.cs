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
    public static readonly Vector2 Forecourt = new Vector2(112, 34);

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
      new Decor { Name = "hangar", Prefab = PM + "Buildings/SM_Bld_Hangar_Open_01.prefab", U = 67, W = 17, Yaw = 0, Scale = 0.8f, Half = new Vector2(12.5f, 14.7f) },
      new Decor { Name = "control-tower", Prefab = PM + "Buildings/SM_Bld_ControlTower_01.prefab", U = 9, W = 9, Yaw = 45, Scale = 0.8f, Half = new Vector2(4f, 4f) },
      new Decor { Name = "barracks-a", Prefab = PM + "Buildings/SM_Bld_Barracks_01.prefab", U = 11, W = 81, Yaw = 90, Scale = 1f, Half = new Vector2(3.7f, 6.7f) },
      new Decor { Name = "barracks-b", Prefab = PM + "Buildings/SM_Bld_Barracks_01.prefab", U = 9, W = 104, Yaw = 90, Scale = 1f, Half = new Vector2(3.7f, 6.7f) },
      new Decor { Name = "tent", Prefab = PM + "Buildings/SM_Bld_Tent_01.prefab", U = 36, W = 118, Yaw = 0, Scale = 1f, Half = new Vector2(3.9f, 6f) },
      new Decor { Name = "supply-pallet", Prefab = PM + "Props/SM_Prop_Pallet_01.prefab", U = 38, W = 107, Yaw = 12, Scale = 1f, Half = new Vector2(1.3f, 1.3f) },
      new Decor { Name = "supply-crate", Prefab = PM + "Props/SM_Prop_Crate_Wood_02.prefab", U = 41, W = 110, Yaw = 35, Scale = 1f, Half = new Vector2(1f, 1f) },
      new Decor { Name = "fuel-a", Prefab = PM + "Props/SM_Prop_Gastank_01.prefab", U = 28, W = 5, Yaw = 0, Scale = 1f, Half = new Vector2(3.4f, 1.2f) },
      new Decor { Name = "fuel-b", Prefab = PM + "Props/SM_Prop_Gastank_01.prefab", U = 29, W = 9, Yaw = 0, Scale = 1f, Half = new Vector2(3.4f, 1.2f) },
      new Decor { Name = "fuel-bladder", Prefab = PM + "Props/Military/SM_Prop_Fuel_Bladder_01.prefab", U = 44, W = 9, Yaw = 90, Scale = 0.8f, Half = new Vector2(5.3f, 3.6f) },
      new Decor { Name = "truck", Prefab = PM + "Vehicles/SM_Veh_Truck_01.prefab", U = 55, W = 39, Yaw = 12, Scale = 1f, Half = new Vector2(1.6f, 3.4f) },
      new Decor { Name = "apc", Prefab = PM + "Vehicles/SM_Veh_APC_01.prefab", U = 61, W = 40, Yaw = 7, Scale = 1f, Half = new Vector2(1.7f, 2.7f) },
      new Decor { Name = "light-tank", Prefab = PM + "Vehicles/SM_Veh_Light_Tank_01.prefab", U = 67, W = 39, Yaw = -5, Scale = 1f, Half = new Vector2(1.7f, 3.3f) },
      new Decor { Name = "pickup", Prefab = PM + "Vehicles/SM_Veh_Pickup_Small_01.prefab", U = 73, W = 38, Yaw = 8, Scale = 1f, Half = new Vector2(1.2f, 2.3f) },
      new Decor { Name = "tower-west", Prefab = PM + "Buildings/SM_Bld_GuardTower_01.prefab", U = 4, W = 148, Yaw = 0, Scale = 1f, Half = new Vector2(1.5f, 2f) },
      new Decor { Name = "tower-south", Prefab = PM + "Buildings/SM_Bld_GuardTower_01.prefab", U = 148, W = 4, Yaw = 90, Scale = 1f, Half = new Vector2(1.5f, 2f) },
      new Decor { Name = "tower-ramp-a", Prefab = PM + "Buildings/SM_Bld_GuardTower_02.prefab", U = 59, W = 93, Yaw = 45, Scale = 1f, Half = new Vector2(2f, 3.3f) },
      new Decor { Name = "tower-yard", Prefab = PM + "Buildings/SM_Bld_GuardTower_02.prefab", U = 112, W = 8, Yaw = 45, Scale = 1f, Half = new Vector2(2f, 3.3f) },
      new Decor { Name = "lamp-a", Prefab = PM + "Props/SM_Prop_LightPole_01.prefab", U = 18, W = 20, Yaw = 0, Scale = 1f, Half = new Vector2(1, 1) },
      new Decor { Name = "lamp-b", Prefab = PM + "Props/SM_Prop_LightPole_01.prefab", U = 54, W = 48, Yaw = 0, Scale = 1f, Half = new Vector2(1, 1) },
      new Decor { Name = "lamp-c", Prefab = PM + "Props/SM_Prop_LightPole_01.prefab", U = 30, W = 96, Yaw = 0, Scale = 1f, Half = new Vector2(1, 1) },
      new Decor { Name = "crates", Prefab = PM + "Props/SM_Prop_Crate_Wood_01.prefab", U = 23, W = 75, Yaw = 15, Scale = 1.3f, Half = new Vector2(1f, 1f) },
      new Decor { Name = "yard-divider", Prefab = PM + "Props/SM_Prop_Barrier_02.prefab", U = 107, W = 25, Yaw = 3, Scale = 1f, Half = new Vector2(2f, 1f) },
      new Decor { Name = "yard-pallet", Prefab = PM + "Props/SM_Prop_Pallet_02.prefab", U = 110, W = 20, Yaw = -16, Scale = 1f, Half = new Vector2(1f, 1f) },
      new Decor { Name = "yard-crate", Prefab = PM + "Props/SM_Prop_Crate_Wood_03.prefab", U = 115, W = 18, Yaw = 27, Scale = 1f, Half = new Vector2(1f, 1f) },
      new Decor { Name = "yard-planter", Prefab = PC + "Props/SM_Prop_Planter_02.prefab", U = 98, W = 32, Yaw = 12, Scale = 1f, Half = new Vector2(1.5f, 1f) },
      new Decor { Name = "stores-divider", Prefab = PM + "Props/SM_Prop_Barrier_01.prefab", U = 16, W = 114, Yaw = -4, Scale = 1f, Half = new Vector2(1.6f, 1f) },
      new Decor { Name = "stores-pallet", Prefab = PM + "Props/SM_Prop_Pallet_03.prefab", U = 10, W = 117, Yaw = 24, Scale = 1f, Half = new Vector2(1f, 1f) },
      new Decor { Name = "stores-crate", Prefab = PM + "Props/SM_Prop_Crate_Wood_02.prefab", U = 20, W = 109, Yaw = -9, Scale = 1f, Half = new Vector2(1f, 1f) },
      // 2026-11-22 [claude] 꾸미기: 서쪽 감시탑 앞 빈 구역(지역 (9,139), 14m)에 통신 설비 군집.
      new Decor { Name = "comms-mast", Prefab = PM + "Props/Military/SM_Prop_Radio_Control_Box_01.prefab", U = 11, W = 135, Yaw = 0, Scale = 1f, Half = new Vector2(2.2f, 4.6f) },
      new Decor { Name = "comms-dish", Prefab = PM + "Props/SM_Prop_Satellite_Dish_02.prefab", U = 16.5f, W = 141, Yaw = 200, Scale = 1f, Half = new Vector2(1f, 1f) },
      new Decor { Name = "comms-generator", Prefab = PM + "Props/SM_Prop_Generator_Large_01.prefab", U = 5.5f, W = 133, Yaw = 0, Scale = 1f, Half = new Vector2(1.2f, 2.3f) },
      new Decor { Name = "stores-barrels", Prefab = PM + "Props/Military/SM_Prop_Barrel_Stack_01.prefab", U = 21, W = 119, Yaw = 10, Scale = 1f, Half = new Vector2(1f, 1f) },
      new Decor { Name = "stores-crates-b", Prefab = PM + "Props/Military/SM_Prop_Crate_Stack_02.prefab", U = 25, W = 123, Yaw = -20, Scale = 1f, Half = new Vector2(1.2f, 1.2f) },
      new Decor { Name = "stores-cart", Prefab = PM + "Props/SM_Prop_Cart_Wood_01.prefab", U = 16, W = 124, Yaw = 75, Scale = 1f, Half = new Vector2(2f, 1.3f) },
      new Decor { Name = "yard-fuel", Prefab = PM + "Props/SM_Prop_Gastank_01.prefab", U = 140, W = 7, Yaw = 0, Scale = 1f, Half = new Vector2(3.4f, 1.3f) },
      new Decor { Name = "yard-barrels", Prefab = PM + "Props/Military/SM_Prop_Barrel_Stack_01.prefab", U = 146.5f, W = 11, Yaw = 30, Scale = 1f, Half = new Vector2(1f, 1f) },
      new Decor { Name = "stores-cover", Prefab = PM + "Props/Military/SM_Prop_Crate_Stack_Cover_01.prefab", U = 27, W = 104, Yaw = 15, Scale = 1f, Half = new Vector2(1f, 1f) },
      new Decor { Name = "hq-cover", Prefab = PM + "Props/Military/SM_Prop_Crate_Stack_Cover_02.prefab", U = 47, W = 107, Yaw = -10, Scale = 1f, Half = new Vector2(1.1f, 1.1f) },
      new Decor { Name = "yard-truck", Prefab = PM + "Vehicles/SM_Veh_Truck_01.prefab", U = 117, W = 14, Yaw = 90, Scale = 1f, Half = new Vector2(1.7f, 3.5f) },
      new Decor { Name = "yard-generator", Prefab = PM + "Props/SM_Prop_Generator_Large_01.prefab", U = 127.5f, W = 22.5f, Yaw = 8, Scale = 1f, Half = new Vector2(1.2f, 2.3f) },
      new Decor { Name = "yard-barrels-b", Prefab = PM + "Props/Military/SM_Prop_Barrel_Stack_01.prefab", U = 124.8f, W = 25.6f, Yaw = -15, Scale = 1f, Half = new Vector2(1f, 1f) },
      new Decor { Name = "comms-crates", Prefab = PM + "Props/Military/SM_Prop_Crate_Stack_03.prefab", U = 17, W = 131, Yaw = 25, Scale = 1f, Half = new Vector2(1f, 1f) },
    };

    public static readonly Decor[] CompoundCover = {
      new Decor { Name = "cover", Prefab = PM + "Props/SM_Prop_Barrier_02.prefab", U = 24, W = 24, Yaw = 45, Scale = 1f, Half = new Vector2(2.2f, 1f) },
    };

    /// <summary>프리팹을 바닥 가운데가 (x, z), 바닥이 y에 오도록 놓는다(팩 프리팹마다 기준점이 모서리·가운데로 다르다).</summary>
    internal static GameObject Place(Transform parent, string path, Vector3 center, float yaw, float scale, string name = null) {
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

    internal sealed class Paving {
      readonly List<Vector3> _vertices = new List<Vector3>();
      readonly List<Color> _colors = new List<Color>();
      readonly List<int> _indices = new List<int>();

      public void Strip(Vector2 a, Vector2 b, float width, float y, Color color) {
        var along = (b - a).normalized;
        var side = new Vector2(-along.y, along.x) * (width * 0.5f);
        Polygon(new[] { a - side, a + side, b + side, b - side }, y, color);
      }

      public void Polygon(IList<Vector2> points, float y, Color color) {
        var offset = _vertices.Count;
        foreach (var p in points) {
          _vertices.Add(new Vector3(p.x, y, p.y));
          _colors.Add(QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color);
        }
        for (var i = 1; i + 1 < points.Count; i++) _indices.AddRange(new[] { offset, offset + i, offset + i + 1 });
      }

      public void Save(GameObject root) {
        var path = $"{OutFolder}/{root.name}_paving.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null) {
          mesh = new Mesh { name = root.name + "_paving" };
          AssetDatabase.CreateAsset(mesh, path);
        }
        mesh.Clear();
        mesh.SetVertices(_vertices);
        mesh.SetColors(_colors);
        mesh.SetTriangles(_indices, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        var materialPath = $"{OutFolder}/PilotPaving.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) {
          material = new Material(WorldView.SharedGroundMaterial) { name = "PilotPaving" };
          material.SetFloat("_GridStrength", 0f);
          AssetDatabase.CreateAsset(material, materialPath);
        }
        var child = new GameObject("Paving", typeof(MeshRenderer), typeof(MeshFilter));
        child.transform.SetParent(root.transform, false);
        child.GetComponent<MeshFilter>().sharedMesh = mesh;
        child.GetComponent<MeshRenderer>().sharedMaterial = material;
      }
    }

    static void BasePaving(GameObject root, char[,] grid, HashSet<(int, int)> cells, int minC, int maxR) {
      var paving = new Paving();
      var concrete = new Color(0.59f, 0.59f, 0.56f);
      var joint = new Color(0.43f, 0.43f, 0.41f);
      var apron = new Color(0.38f, 0.41f, 0.41f);
      var marking = new Color(0.83f, 0.69f, 0.40f);
      const float tile = FactoryWarsSetup.GridCell / 2f;
      bool KeepCorner(int r, int c) => cells.Contains((r, c)) ||
        r < 0 || r >= FactoryWarsSetup.GridCells || c < 0 || c >= FactoryWarsSetup.GridCells || grid[r, c] == 'P' || grid[r, c] == 'E';
      var panelCorners = new[] { 0, 3, 1, 2 };
      foreach (var (r, c) in cells.OrderBy(cell => cell.Item1).ThenBy(cell => cell.Item2)) {
        var chamfers = GridMapView.ConvexCorners(r, c, KeepCorner);
        for (var i = 0; i < 4; i++) {
          var x = (c - minC) * FactoryWarsSetup.GridCell + i % 2 * tile;
          var z = (maxR - r) * FactoryWarsSetup.GridCell + i / 2 * tile;
          var shade = 0.96f + ((r * 31 + c * 17 + i * 23) % 11) * 0.007f;
          var mask = chamfers & (1 << panelCorners[i]);
          paving.Polygon(GridMapView.TileOutline(x, z, x + tile, z + tile, mask), 0.037f, joint);
          paving.Polygon(GridMapView.TileOutline(x + 0.035f, z + 0.035f, x + tile - 0.035f, z + tile - 0.035f, mask), 0.044f, concrete * shade);
        }
      }
      paving.Strip(new Vector2(49, 38), new Vector2(77, 38), 9.5f, 0.052f, apron);
      for (var x = 52; x <= 76; x += 6) {
        paving.Strip(new Vector2(x, 34), new Vector2(x, 42), 0.19f, 0.063f, marking);
      }
      var entry = new Vector2(78f, 78f);
      var bend = new Vector2(72, 69);
      var yard = new Vector2(48, 69);
      paving.Strip(bend, entry, 7.3f, 0.054f, apron);
      paving.Strip(yard, bend, 7.3f, 0.052f, apron);
      for (var i = 0; i < 5; i++) {
        paving.Strip(yard + Vector2.right * (i * 5), yard + Vector2.right * (i * 5 + 2), 0.23f, 0.064f, marking);
      }
      for (var i = 0; i < 4; i++) {
        var point = Vector2.Lerp(bend, entry, i / 4f) + new Vector2(1.2f, 1.2f);
        paving.Strip(point, point + new Vector2(1.45f, 1.45f), 0.23f, 0.064f, marking);
      }
      // Mark the apron and fuel depot without putting collision or scenery inside construction lanes.
      paving.Strip(new Vector2(20, 8), new Vector2(50, 8), 13f, 0.051f, new Color(0.49f, 0.46f, 0.39f));
      for (var x = 20; x < 49; x += 3) {
        paving.Strip(new Vector2(x, 14), new Vector2(x + 1.1f, 12.9f), 0.22f, 0.062f, marking);
      }
      paving.Strip(new Vector2(58, 29), new Vector2(75, 29), 0.25f, 0.062f, marking);
      paving.Strip(new Vector2(58, 31), new Vector2(75, 31), 0.25f, 0.062f, marking);
      paving.Strip(new Vector2(36, 106), new Vector2(36, 122), 10f, 0.053f, apron);
      paving.Strip(new Vector2(32, 106), new Vector2(40, 106), 0.24f, 0.063f, marking);
      // Dark drainage strips and a striped loading pocket alongside the existing vehicle apron.
      paving.Strip(new Vector2(91, 23), new Vector2(114, 23), 15f, 0.054f, apron);
      paving.Strip(new Vector2(91, 30), new Vector2(114, 30), 0.24f, 0.065f, joint);
      paving.Strip(new Vector2(49, 32.5f), new Vector2(77, 32.5f), 0.23f, 0.064f, joint);
      paving.Strip(new Vector2(21, 2), new Vector2(49, 2), 0.23f, 0.064f, joint);
      for (var x = 92; x < 113; x += 3) {
        paving.Strip(new Vector2(x, 28.9f), new Vector2(x + 1.1f, 27.8f), 0.21f, 0.063f, marking);
      }
      paving.Strip(Forecourt - Vector2.right * 8f, Forecourt + Vector2.right * 8f, 14f, .056f, new Color(.50f, .51f, .46f));
      for (var sx = -1; sx <= 1; sx += 2) {
        for (var sz = -1; sz <= 1; sz += 2) {
          var point = Forecourt + new Vector2(sx * 7.7f, sz * 6.7f);
          paving.Strip(point, point - Vector2.right * (sx * 2.4f), .24f, .065f, marking);
          paving.Strip(point, point - Vector2.up * (sz * 2.4f), .24f, .066f, marking);
        }
      }
      paving.Strip(new Vector2(6, 114), new Vector2(19, 114), 10f, .053f, apron);
      paving.Save(root);
    }

    // ---------------------------------------------------------------- base pad

    static GameObject BuildBasePad(char[,] grid) {
      var root = new GameObject("blk_base_pad");
      foreach (var d in BaseDecor) {
        Place(root.transform, d.Prefab, new Vector3(d.U, 0f, d.W), d.Yaw, d.Scale, d.Name);
      }
      // Seat 0 retaining-wall cap. Fit the measured barriers inside their edge cells.
      var cells = BaseCells(grid);
      var minC = cells.Min(c => c.Item2);
      var maxR = cells.Max(c => c.Item1);
      var cornerX = FactoryWarsSetup.CellX(minC) - FactoryWarsSetup.GridCell / 2f;
      var cornerZ = FactoryWarsSetup.CellZ(maxR) - FactoryWarsSetup.GridCell / 2f;
      var walls = new GameObject("PerimeterWall").transform;
      walls.SetParent(root.transform, false);
      var occupied = root.transform.Cast<Transform>().Where(t => t != walls).Select(t => WorldBounds(t.gameObject)).ToList();
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
          var x = FactoryWarsSetup.CellX(c) - cornerX;
          var z = FactoryWarsSetup.CellZ(r) - cornerZ;
          var hash = (uint)((r * 73856093) ^ (c * 19349663) ^ d);
          var along = d < 2 ? Vector3.right : Vector3.forward;
          var inward = new Vector3(-dc, 0, dr);
          var model = hash % 7 < 3 ? $"SM_Prop_Barrier_Tall_Group_0{1 + hash % 3}"
            : hash % 7 < 6 ? $"SM_Prop_Barrier_Tall_0{1 + hash % 6}" : $"SM_Prop_Barrier_0{1 + hash % 3}";
          var wall = Place(walls, $"{PM}Props/{model}.prefab", new Vector3(x, 0, z), d < 2 ? 0f : 90f, 1f, $"Wall{index++}");
          if (wall == null) continue;
          var dimensions = WorldBounds(wall).extents;
          var depth = Vector3.Dot(dimensions, new Vector3(Mathf.Abs(inward.x), 0, Mathf.Abs(inward.z)));
          var length = Vector3.Dot(dimensions, along);
          if (length < depth) {
            wall.transform.Rotate(0f, 90f, 0f);
            (length, depth) = (depth, length);
            var rotated = WorldBounds(wall).center;
            wall.transform.position += new Vector3(x - rotated.x, 0, z - rotated.z);
          }
          wall.transform.position -= inward * (3.85f - depth);
          wall.transform.position += along * Mathf.Clamp(3.85f - length, 0f, 1.8f) * ((hash / 13 % 17 - 8f) / 8f);
          var bounds = WorldBounds(wall);
          var margin = bounds;
          margin.Expand(0.14f);
          if (length > 3.4f || depth > 2f || occupied.Any(b => b.Intersects(margin))) Object.DestroyImmediate(wall);
          else occupied.Add(bounds);
        }
      }
      BasePaving(root, grid, cells, minC, maxR);
      return root;
    }

    static GameObject BuildCompoundTrim() {
      var root = new GameObject("blk_compound_pilot_trim");
      var street = new GameObject("Street").transform;
      street.SetParent(root.transform, false);
      var paving = new Paving();
      var asphalt = new Color(0.36f, 0.37f, 0.36f);
      var marking = new Color(0.78f, 0.68f, 0.43f);
      for (var side = 0; side < 4; side++) {
        var turn = Quaternion.Euler(0, side * -90f, 0);
        Vector2 Rotate(float x, float z) {
          var p = turn * new Vector3(x, 0, z);
          return new Vector2(p.x, p.z);
        }
        foreach (var prop in CompoundCover) {
          var p = Rotate(prop.U, prop.W);
          Place(street, prop.Prefab, new Vector3(p.x, 0, p.y), prop.Yaw - side * 90f, prop.Scale, $"{prop.Name}-{side}");
        }
      }
      // Task 39 fix 2: one 6 m loop road around the trading post (rectangle) and one road to the south gate, amber centre dashes.
      {
        var lx = FactoryWars.View.CampLayout.LoopX;
        var lz = FactoryWars.View.CampLayout.LoopZ;
        var w = FactoryWars.View.CampLayout.RoadWidth;
        paving.Strip(new Vector2(-lx - w / 2f, -lz), new Vector2(lx + w / 2f, -lz), w, 0.019f, asphalt);
        paving.Strip(new Vector2(-lx - w / 2f, lz), new Vector2(lx + w / 2f, lz), w, 0.019f, asphalt);
        paving.Strip(new Vector2(-lx, -lz), new Vector2(-lx, lz), w, 0.0185f, asphalt);
        paving.Strip(new Vector2(lx, -lz), new Vector2(lx, lz), w, 0.0185f, asphalt);
        paving.Strip(new Vector2(0f, -lz - w / 2f), new Vector2(0f, -48.5f), FactoryWars.View.CampLayout.GateRoadHalf * 2f, 0.018f, asphalt);
        for (var x = -lx + 2f; x < lx - 2f; x += 4f) { paving.Strip(new Vector2(x, -lz), new Vector2(x + 2f, -lz), 0.2f, 0.026f, marking); paving.Strip(new Vector2(x, lz), new Vector2(x + 2f, lz), 0.2f, 0.026f, marking); }
        for (var z = -lz + 2f; z < lz - 2f; z += 4f) { paving.Strip(new Vector2(-lx, z), new Vector2(-lx, z + 2f), 0.2f, 0.026f, marking); paving.Strip(new Vector2(lx, z), new Vector2(lx, z + 2f), 0.2f, 0.026f, marking); }
        for (var z = -lz - 5f; z > -46f; z -= 4f) paving.Strip(new Vector2(0f, z), new Vector2(0f, z - 2f), 0.2f, 0.026f, marking);
      }
      CampPaving(paving);
      paving.Save(root);
      return root;
    }

    /// <summary>Task 39 card 4: display bays (white lines), helipad, drone pads, firing range pad and the main gate stop line.</summary>
    static void CampPaving(Paving paving) {
      var white = new Color(0.86f, 0.86f, 0.82f);
      var amber = new Color(0.86f, 0.62f, 0.18f);
      var pad = new Color(0.42f, 0.43f, 0.42f);
      var d = FactoryWars.View.CampLayout.DisplayPad;
      paving.Polygon(new[] { new Vector2(d.x, d.y), new Vector2(d.z, d.y), new Vector2(d.z, d.w), new Vector2(d.x, d.w) }, 0.015f, new Color(0.17f, 0.17f, 0.18f));
      // Trading-post forecourt: light paving slabs in front of the counter (fix 4; it read as a sand patch).
      paving.Polygon(new[] { new Vector2(-9.5f, -10.2f), new Vector2(9.5f, -10.2f), new Vector2(9.5f, -4.3f), new Vector2(-9.5f, -4.3f) }, 0.016f, new Color(0.62f, 0.6f, 0.56f));
      for (var x = -8f; x <= 8f; x += 2f) paving.Strip(new Vector2(x, -10.2f), new Vector2(x, -4.3f), 0.06f, 0.0175f, new Color(0.5f, 0.48f, 0.45f));
      var e = FactoryWars.View.CampLayout.ApcPad;
      paving.Polygon(new[] { new Vector2(e.x, e.y), new Vector2(e.z, e.y), new Vector2(e.z, e.w), new Vector2(e.x, e.w) }, 0.015f, new Color(0.17f, 0.17f, 0.18f));
      foreach (var (p, yaw, kind) in FactoryWars.View.CampLayout.Bays) {
        var c = new Vector2(p.x, p.z);
        if (kind == FactoryWars.View.CampLayout.Bay.Heli || kind == FactoryWars.View.CampLayout.Bay.ScoutHeli) {
          var ring = new List<Vector2>();
          for (var i = 0; i < 24; i++) ring.Add(c + new Vector2(Mathf.Cos(i * Mathf.PI / 12f), Mathf.Sin(i * Mathf.PI / 12f)) * (kind == FactoryWars.View.CampLayout.Bay.Heli ? 6f : 4.6f));
          paving.Polygon(ring, 0.02f, pad);
          for (var i = 0; i < 24; i += 2) paving.Strip(ring[i] + (c - ring[i]) * 0.08f, ring[(i + 1) % 24] + (c - ring[(i + 1) % 24]) * 0.08f, 0.35f, 0.027f, amber);
          paving.Strip(c + new Vector2(-1.6f, -2.2f), c + new Vector2(-1.6f, 2.2f), 0.5f, 0.028f, white);
          paving.Strip(c + new Vector2(1.6f, -2.2f), c + new Vector2(1.6f, 2.2f), 0.5f, 0.028f, white);
          paving.Strip(c + new Vector2(-1.6f, 0f), c + new Vector2(1.6f, 0f), 0.5f, 0.028f, white);
          continue;
        }
        var half = kind == FactoryWars.View.CampLayout.Bay.Drone ? new Vector2(1.9f, 1.9f) : kind == FactoryWars.View.CampLayout.Bay.Tank ? new Vector2(5.3f, 2.65f) : new Vector2(2.7f, 4.3f);
        if (kind == FactoryWars.View.CampLayout.Bay.Drone) {
          paving.Polygon(new[] { c + new Vector2(-half.x, -half.y), c + new Vector2(half.x, -half.y), c + new Vector2(half.x, half.y), c + new Vector2(-half.x, half.y) }, 0.02f, pad);
          paving.Strip(c + new Vector2(-half.x, -half.y), c + new Vector2(half.x, -half.y), 0.2f, 0.027f, amber);
          paving.Strip(c + new Vector2(half.x, -half.y), c + new Vector2(half.x, half.y), 0.2f, 0.027f, amber);
          paving.Strip(c + new Vector2(half.x, half.y), c + new Vector2(-half.x, half.y), 0.2f, 0.027f, amber);
          paving.Strip(c + new Vector2(-half.x, half.y), c + new Vector2(-half.x, -half.y), 0.2f, 0.027f, amber);
          continue;
        }
        paving.Strip(c + new Vector2(-half.x, -half.y), c + new Vector2(-half.x, half.y), 0.18f, 0.027f, white);
        paving.Strip(c + new Vector2(half.x, -half.y), c + new Vector2(half.x, half.y), 0.18f, 0.027f, white);
        paving.Strip(c + new Vector2(-half.x, -half.y), c + new Vector2(half.x, -half.y), 0.18f, 0.027f, white);
      }
      // Firing range pad and the stop line at the main gate.
      paving.Polygon(new[] { new Vector2(7f, -40f), new Vector2(31f, -40f), new Vector2(31f, -23.2f), new Vector2(7f, -23.2f) }, 0.016f, new Color(0.52f, 0.5f, 0.45f));
      paving.Strip(new Vector2(-3.2f, -44f), new Vector2(3.2f, -44f), 0.4f, 0.028f, white);
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
      var props = new GameObject("Street").transform;
      props.SetParent(parent, false);
      var flip = variant % 2 == 0 ? 1f : -1f;
      Place(props, PC + "Props/SM_Prop_LightPole_Base_01.prefab", new Vector3((-3.9f + variant * 1.4f) * flip, 0.10f, -6.75f), 90f, 1f, "Lamp0");
      Place(props, PC + "Props/SM_Prop_Planter_01.prefab", new Vector3(1.8f * flip, 0.10f, -6.8f), 0f, 1f, "Planter0");
      Place(props, PC + "Props/SM_Prop_ParkBench_01.prefab", new Vector3(-7f * flip, 0.10f, 0.6f), 90f, 1f, "Bench0");
      if (variant != 1) Place(props, PC + "Props/SM_Prop_Hydrant_01.prefab", new Vector3(3.2f * flip, 0.10f, 7f), 0f, 1f, "Hydrant");
      if (variant != 0) Place(props, PC + "Props/SM_Prop_Mailbox_01.prefab", new Vector3(-2.6f * flip, 0.10f, 7f), 0f, 1f, "Mailbox");
    }

    /// <summary>주차 칸(사분면 6.5m): 주차선 + 승용차 2대(Z로 길게 나란히).</summary>
    static void ParkingCorner(Transform parent, Vector2 center) {
      var lot = new GameObject("Parking").transform;
      lot.SetParent(parent, false);
      Place(lot, PC + "Environments/SM_Env_Road_ParkingLines_01.prefab", new Vector3(center.x, 0.092f, center.y), 0f, 0.6f, "Lines");
      Place(lot, PC + "Vehicles/SM_Veh_Car_Sedan_01.prefab", new Vector3(center.x - 1.35f, 0.087f, center.y), 2f, 0.9f, "Car0");
      Place(lot, PC + "Vehicles/SM_Veh_Car_Van_01.prefab", new Vector3(center.x + 1.45f, 0.087f, center.y + 0.1f), -3f, 0.85f, "Car1");
    }

    static void CityPaving(GameObject root, float parkingX) {
      var paving = new Paving();
      var sidewalk = new Color(0.62f, 0.60f, 0.55f);
      var curb = new Color(0.78f, 0.74f, 0.66f);
      var lot = new Color(0.31f, 0.33f, 0.34f);
      const float edge = 7.98f;
      for (var i = 0; i < 8; i++) {
        var z = -7f + i * 2f;
        paving.Strip(new Vector2(-edge, z), new Vector2(edge, z), 1.96f, 0.071f, sidewalk * (i % 2 == 0 ? 0.98f : 1f));
      }
      paving.Strip(new Vector2(parkingX - 3f, 3.5f), new Vector2(parkingX + 3f, 3.5f), 5.75f, 0.086f, lot);
      var corners = new[] { new Vector2(-7.84f, -7.84f), new Vector2(7.84f, -7.84f), new Vector2(7.84f, 7.84f), new Vector2(-7.84f, 7.84f) };
      for (var i = 0; i < corners.Length; i++) paving.Strip(corners[i], corners[(i + 1) % corners.Length], 0.22f, 0.10f, curb);
      paving.Save(root);
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
          Stack(b, "Apartment4", new Vector2(-Q, -Q), 180f, "SM_Bld_Shop_Corner_01", "SM_Bld_Apartment_Corner_01", 3, "SM_Bld_Apartment_Roof_Corner_01");
          Stack(b, "Shop2", new Vector2(Q, -Q), 180f, "SM_Bld_Shop_02", "SM_Bld_Apartment_02", 1, "SM_Bld_Apartment_Roof_02");
          Stack(b, "Mixed3", new Vector2(Q, Q), 0f, "SM_Bld_Shop_05", "SM_Bld_Apartment_03", 2, "SM_Bld_Apartment_Roof_03");
          ParkingCorner(root.transform, new Vector2(-Q, Q));
          Sidewalk(root.transform, 0);
          break;
        case "blk_city_mixed_2x2_b":
          Stack(b, "Shop1", new Vector2(-Q, -Q), 180f, "SM_Bld_Shop_06", "SM_Bld_Apartment_01", 0, "SM_Bld_Apartment_Roof_01");
          Stack(b, "Mixed2", new Vector2(Q, -Q), 180f, "SM_Bld_Shop_05", "SM_Bld_Apartment_03", 1, "SM_Bld_Apartment_Roof_03");
          Stack(b, "Apartment5", new Vector2(-Q, Q), 0f, "SM_Bld_Shop_Corner_02", "SM_Bld_Apartment_Corner_02", 4, "SM_Bld_Apartment_Roof_Corner_02");
          ParkingCorner(root.transform, new Vector2(Q, Q));
          Sidewalk(root.transform, 1);
          break;
        default:
          Stack(b, "Shop2", new Vector2(-Q, -Q), 180f, "SM_Bld_Shop_02", "SM_Bld_Apartment_02", 1, "SM_Bld_Apartment_Roof_02");
          Stack(b, "Corner3", new Vector2(-Q, Q), 270f, "SM_Bld_Shop_Corner_01", "SM_Bld_Apartment_Corner_02", 2, "SM_Bld_Apartment_Roof_Corner_02");
          Stack(b, "Apartment3", new Vector2(Q, -Q), 90f, "SM_Bld_Shop_06", "SM_Bld_Apartment_03", 2, "SM_Bld_Apartment_Roof_03");
          ParkingCorner(root.transform, new Vector2(Q, Q));
          Sidewalk(root.transform, 2);
          break;
      }
      CityPaving(root, id == CityIds[0] ? -Q : Q);
      return root;
    }

    public static readonly string[] CityIds = { "blk_city_mixed_2x2_a", "blk_city_mixed_2x2_b", "blk_city_mixed_2x2_c" };

    internal static void Register(VisualCatalog catalog, string id, GameObject prefab, Vector3 size) {
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
      Save("blk_compound_pilot_trim", BuildCompoundTrim());
      foreach (var id in CityIds) {
        Save(id, BuildCity(id));
      }
      catalog.Invalidate();
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Factory Wars/Map4P v4/Build and Bake Pilot")]
    public static void BuildAndBakePilot() {
      BuildPilotBlocks();
      FactoryWarsSetup.BakeMap4P();
    }
  }
}
