namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// Task 39 card 4: the mercenary (PMC) camp inside the 96 m octagon, baked by DressKit into two blocks (MK_00 perimeter, MK_01 zones).
  /// Perimeter = chain-link fence with razor wire on every compound cell edge (the simulation wall line) except the four 16 m gates,
  /// corner watchtowers with searchlights, gate floodlights + guard hut, radio masts, firing lines inside each gate.
  /// Zones (no random clutter, nothing outside the fence): NW logistics sheds + aligned pallet/crate/drum rows, NE barracks tents and huts,
  /// SE training yard (firing range, tyre course, climbing wall), E workshop + one tidy container row. The SW vehicle display line, the
  /// helipad and drone pads are painted by BlockKit's compound trim and filled at runtime by MercCampView (they follow the shop stock).
  /// Every piece is checked against the compound cells, the road loop/axis roads, the HQ hub and the simulation cover walls; a piece that
  /// does not fit is skipped and logged. Pack prefabs are only placed; dark vehicles use the pack's own Land_Vehicles_01 variant.
  /// </summary>
  public static class CampKit {
    const string PC = "Assets/ExternalAssets/Synty/PolygonCity/Prefabs/";
    const string PM = "Assets/ExternalAssets/Synty/PolygonMilitary/Prefabs/";
    const string PK = "Assets/ExternalAssets/Synty/PolygonConstruction/Prefabs/";
    const string PB = "Assets/ExternalAssets/Synty/PolygonBattleRoyale/Prefabs/";
    public const string DarkVehicles = "Assets/ExternalAssets/Synty/PolygonMilitary/Materials/Vehicles/Land_Vehicles_01.mat";

    /// <summary>Road loop around the HQ hub: square centre line at ±RingMid, RingWidth wide; axis roads |x| or |z| &lt; AxisHalf to the gates.</summary>
    public const float RingMid = 17f, RingWidth = 6f, AxisHalf = 3.5f, HubHalf = 12.5f, GateReach = 28.8f;

    static HashSet<(int, int)> _cells;
    public static HashSet<(int, int)> Cells {
      get {
        if (_cells != null) return _cells;
        var g = FactoryWarsSetup.ReadGrid(FactoryWarsSetup.GridCsvPath);
        _cells = new HashSet<(int, int)>();
        for (var r = 0; r < FactoryWarsSetup.GridCells; r++)
          for (var c = 0; c < FactoryWarsSetup.GridCells; c++)
            if ("CYp".IndexOf(g[r, c]) >= 0 && Mathf.Abs(r - 39.5f) < 8 && Mathf.Abs(c - 39.5f) < 8) _cells.Add((r, c));
        return _cells;
      }
    }

    static bool Inside(float x, float z) => Cells.Contains((Mathf.FloorToInt((FactoryWarsSetup.GridHalf - z) / FactoryWarsSetup.GridCell), Mathf.FloorToInt((x + FactoryWarsSetup.GridHalf) / FactoryWarsSetup.GridCell)));

    public static bool OnRoad(float x, float z) {
      var ax = Mathf.Abs(x);
      var az = Mathf.Abs(z);
      var ring = Mathf.Max(ax, az) > RingMid - RingWidth / 2f && Mathf.Max(ax, az) < RingMid + RingWidth / 2f;
      var axis = (ax < AxisHalf && az > RingMid) || (az < AxisHalf && ax > RingMid);
      return ring || axis;
    }

    /// <summary>A footprint (world bounds) fits: inside the fence with a margin, off the roads and the hub, clear of the cover walls.</summary>
    public static bool Fits(Bounds b, float margin = 1.2f, bool allowRoad = false) {
      for (var i = 0; i <= 4; i++) {
        for (var j = 0; j <= 4; j++) {
          var x = Mathf.Lerp(b.min.x, b.max.x, i / 4f);
          var z = Mathf.Lerp(b.min.z, b.max.z, j / 4f);
          if (!Inside(x + margin, z) || !Inside(x - margin, z) || !Inside(x, z + margin) || !Inside(x, z - margin)) return false;
          if (!allowRoad && OnRoad(x, z)) return false;
          if (Mathf.Abs(x) < HubHalf && Mathf.Abs(z) < HubHalf) return false;
          if (Mathf.Abs(Mathf.Abs(x) - 24f) < 2.4f && Mathf.Abs(Mathf.Abs(z) - 24f) < 2.4f) return false;
        }
      }
      return true;
    }

    static string MP(string n) => PM + "Props/SM_Prop_" + n + ".prefab";
    static string MM(string n) => PM + "Props/Military/SM_Prop_" + n + ".prefab";
    static string MB(string n) => PM + "Buildings/SM_Bld_" + n + ".prefab";
    static string BR(string n) => PB + "Props/SM_Prop_" + n + ".prefab";
    static string KP(string n) => PK + "Props/SM_Prop_" + n + ".prefab";

    static readonly List<string> Skipped = new List<string>();
    public static IEnumerable<string> Report => Skipped;

    /// <summary>Place a piece centred at (x, z) world (block origin = camp centre); skip it when it does not fit.</summary>
    static GameObject Put(Transform root, string path, float x, float z, float yaw, float y = 0f, bool allowRoad = false, float margin = 1.2f) {
      var g = BlockKit.Place(root, path, new Vector3(x, y, z), yaw, 1f);
      if (g == null) { Skipped.Add($"missing {path}"); return null; }
      var w = BlockKit.WorldBounds(g);
      w.center -= root.position;
      if (!Fits(w, margin, allowRoad)) {
        Skipped.Add($"skip {System.IO.Path.GetFileNameWithoutExtension(path)} at ({x:0.#},{z:0.#})");
        Object.DestroyImmediate(g);
        return null;
      }
      return g;
    }

    static void Dark(GameObject g) {
      if (g == null) return;
      var dark = AssetDatabase.LoadAssetAtPath<Material>(DarkVehicles);
      foreach (var r in g.GetComponentsInChildren<Renderer>()) {
        var mats = r.sharedMaterials;
        for (var i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name.StartsWith("Land_Vehicles")) mats[i] = dark;
        r.sharedMaterials = mats;
      }
    }

    static bool Gate(float x, float z) => (Mathf.Abs(x) < 8.5f && Mathf.Abs(z) > GateReach) || (Mathf.Abs(z) < 8.5f && Mathf.Abs(x) > GateReach);

    // ------------------------------------------------------------ display models (runtime slots camp/display_*)

    public const string HeliDark = "Assets/ExternalAssets/Synty/PolygonMilitary/Materials/Vehicles/Helicopters_01_B.mat";

    /// <summary>
    /// Wrapper prefabs (our folder) around pack vehicles with the pack's dark colour variants: tank, MRAP, attack helicopter, drone.
    /// Registered as VisualCatalog slots camp/display_* (MercCampView) and unit/pmc_* (bought PMC units keep the dark look).
    /// </summary>
    public static void RegisterDisplays(VisualCatalog catalog, string folder) {
      catalog.Slots.RemoveAll(s => s != null && s.Id != null && s.Id.StartsWith("camp/"));
      var heli = AssetDatabase.LoadAssetAtPath<Material>(HeliDark);
      var models = new (string Id, string Path, Material Swap, string Prefix)[] {
        ("tank", PM + "Vehicles/SM_Veh_Light_Tank_01.prefab", null, "Land_Vehicles"),
        ("mrap", PM + "Vehicles/SM_Veh_Light_Armored_Car_01.prefab", null, "Land_Vehicles"),
        ("heli", PM + "Vehicles/SM_Veh_Helicopter_Attack_02.prefab", heli, "Helicopters"),
      };
      foreach (var (id, path, swap, prefix) in models) {
        var root = new GameObject("camp_display_" + id);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (source == null) { Debug.LogWarning($"[FW-Camp] missing {path}"); Object.DestroyImmediate(root); continue; }
        var item = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
        item.transform.localPosition = Vector3.zero;
        var b = BlockKit.WorldBounds(item);
        item.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
        var mat = swap != null ? swap : AssetDatabase.LoadAssetAtPath<Material>(DarkVehicles);
        foreach (var r in item.GetComponentsInChildren<Renderer>()) {
          var mats = r.sharedMaterials;
          for (var i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name.StartsWith(prefix)) mats[i] = mat;
          r.sharedMaterials = mats;
          r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        var prefab = SaveCombined(root, folder, $"camp_display_{id}");
        Object.DestroyImmediate(root);
        Slot(catalog, "camp/display_" + id, prefab, b.size);
      }
      // Drone: the shared scout drone model already used by the trader patrol (user CC BY model).
      var drone = catalog.Slots.Find(s => s != null && s.Id == "unit/shared_scout_drone");
      if (drone != null && drone.Prefab != null) Slot(catalog, "camp/display_drone", drone.Prefab, Vector3.one * 2f, drone.Scale);
      // Bought PMC units keep the dark camp models (auto-fit to the donor unit's ring); the players' cargo truck is the pack truck.
      catalog.Slots.RemoveAll(s => s != null && s.Id != null && (s.Id.StartsWith("unit/pmc_") || s.Id == "unit/merc_cargo_truck"));
      foreach (var (unit, display) in new[] { ("pmc_tank", "tank"), ("pmc_armored_suv", "mrap"), ("pmc_attack_heli", "heli"), ("pmc_attack_drone", "drone") }) {
        var slot = catalog.Slots.Find(s => s != null && s.Id == "camp/display_" + display);
        if (slot == null) continue;
        Slot(catalog, "unit/" + unit, slot.Prefab, slot.Size, slot.Scale);
        catalog.Slots.Find(s => s.Id == "unit/" + unit).AutoFit = true;
      }
      var guard = catalog.Slots.Find(s => s != null && s.Id == "unit/trader_guard");
      var contractor = AssetDatabase.LoadAssetAtPath<GameObject>(PM + "Characters/SM_Chr_Contractor_Male_01.prefab");
      if (guard != null && contractor != null) {
        foreach (var id in new[] { "unit/pmc_soldier", "unit/pmc_operator_squad", "unit/pmc_marksman" }) {
          catalog.Slots.RemoveAll(s => s != null && s.Id == id);
          var copy = JsonUtility.FromJson<VisualSlot>(JsonUtility.ToJson(guard));
          copy.Id = id;
          copy.Prefabs = new List<GameObject> { contractor };
          copy.PivotOffset = Vector3.zero;
          copy.Notes = "Task 39 PMC contractor (Synty Military SM_Chr_Contractor_Male_01, dark outfit), trader_guard animator";
          catalog.Slots.Add(copy);
        }
      }
      var truckRoot = new GameObject("merc_cargo_truck");
      var truckSource = AssetDatabase.LoadAssetAtPath<GameObject>(PM + "Vehicles/SM_Veh_Truck_01.prefab");
      if (truckSource != null) {
        var truck = (GameObject)PrefabUtility.InstantiatePrefab(truckSource, truckRoot.transform);
        var tb = BlockKit.WorldBounds(truck);
        truck.transform.localPosition = new Vector3(-tb.center.x, -tb.min.y, -tb.center.z);
        var truckPrefab = PrefabUtility.SaveAsPrefabAsset(truckRoot, $"{folder}/merc_cargo_truck.prefab");
        Slot(catalog, "unit/merc_cargo_truck", truckPrefab, tb.size);
        catalog.Slots.Find(s => s.Id == "unit/merc_cargo_truck").AutoFit = true;
      }
      Object.DestroyImmediate(truckRoot);
    }

    /// <summary>One mesh per display vehicle (a submesh per material), no shadows: pack vehicles are many part renderers.</summary>
    static GameObject SaveCombined(GameObject root, string folder, string name) {
      var byMaterial = new Dictionary<Material, List<CombineInstance>>();
      var world = root.transform.worldToLocalMatrix;
      foreach (var f in root.GetComponentsInChildren<MeshFilter>()) {
        var r = f.GetComponent<MeshRenderer>();
        if (r == null || r.enabled == false || f.sharedMesh == null) continue;
        var lod = f.GetComponentInParent<LODGroup>();
        if (lod != null && lod.GetLODs().Length > 1 && System.Array.IndexOf(lod.GetLODs()[0].renderers, r) < 0) continue;
        var mats = r.sharedMaterials;
        for (var s = 0; s < f.sharedMesh.subMeshCount; s++) {
          var mat = s < mats.Length ? mats[s] : mats[mats.Length - 1];
          if (mat == null) continue;
          if (!byMaterial.TryGetValue(mat, out var list)) byMaterial[mat] = list = new List<CombineInstance>();
          list.Add(new CombineInstance { mesh = f.sharedMesh, subMeshIndex = s, transform = world * f.transform.localToWorldMatrix });
        }
      }
      var parts = new List<CombineInstance>();
      foreach (var kv in byMaterial) {
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.CombineMeshes(kv.Value.ToArray(), true, true);
        parts.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity });
      }
      var mesh = new Mesh { name = name + "_mesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
      mesh.CombineMeshes(parts.ToArray(), false, false);
      mesh.RecalculateBounds();
      AssetDatabase.CreateAsset(mesh, $"{folder}/{name}_mesh.asset");
      var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      go.GetComponent<MeshFilter>().sharedMesh = mesh;
      var renderer = go.GetComponent<MeshRenderer>();
      renderer.sharedMaterials = byMaterial.Keys.ToArray();
      renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{folder}/{name}.prefab");
      Object.DestroyImmediate(go);
      return prefab;
    }

    static void Slot(VisualCatalog catalog, string id, GameObject prefab, Vector3 size, float scale = 1f) {
      BlockKit.Register(catalog, "tmp_" + id.Replace('/', '_'), prefab, size);
      var slot = catalog.Slots.Find(s => s != null && s.Id == "block/tmp_" + id.Replace('/', '_'));
      slot.Id = id;
      slot.Scale = scale;
      slot.Notes = "Task 39 PMC camp display model (CampKit.cs), pack dark colour variant";
    }

    // ------------------------------------------------------------ MK_00 perimeter

    public static void Perimeter(Transform root) {
      Skipped.Clear();
      var size = FactoryWarsSetup.GridCell;
      var dirs = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
      // Military chain-link panels (3 m, 360 tris) - three per 8 m edge; the 2 m BattleRoyale panels cost 656 tris each.
      var fences = new[] { MP("Fence_01"), MP("Fence_02"), MP("Fence_03") };
      var wire = MM("RazorWire_01");
      foreach (var (r, c) in Cells.OrderBy(t => t.Item1).ThenBy(t => t.Item2)) {
        var cx = FactoryWarsSetup.CellX(c);
        var cz = FactoryWarsSetup.CellZ(r);
        foreach (var (dr, dc) in dirs) {
          if (Cells.Contains((r + dr, c + dc))) continue;
          // Edge on the cell boundary, fence 0.6 m inside (the simulation wall is 1.2 m thick, inward).
          var horizontal = dr != 0;
          var ex = cx + dc * (size / 2f - 0.6f);
          var ez = cz - dr * (size / 2f - 0.6f);
          if (Gate(cx + dc * size / 2f, cz - dr * size / 2f)) continue;
          for (var k = 0; k < 3; k++) {
            var t = -2.67f + k * 2.67f;
            var x = horizontal ? cx + t : ex;
            var z = horizontal ? ez : cz + t;
            BlockKit.Place(root, fences[(r + c + k) % 3], new Vector3(x, 0f, z), horizontal ? 0f : 90f, 1f);
          }
          for (var k = 0; k < 2; k++) {
            var t = -2f + k * 4f;
            BlockKit.Place(root, wire, new Vector3(horizontal ? cx + t : ex, 3.05f, horizontal ? ez : cz + t), horizontal ? 0f : 90f, 1f);
          }
        }
      }
      // Corner watchtowers with searchlights (inside the diagonal steps), two towers flanking the main (south) gate.
      foreach (var (x, z) in new[] { (-30f, 30f), (30f, 30f), (-30f, -30f), (30f, -30f) }) {
        Put(root, MB("GuardTower_01"), x, z, z > 0 ? 180f : 0f, 0f, false, 0.4f);
        Put(root, BR("Spotlight_01"), x - Mathf.Sign(x) * 3f, z - Mathf.Sign(z) * 1.5f, Mathf.Atan2(x, z) * Mathf.Rad2Deg, 0f, false, 0.3f);
      }
      Put(root, MB("GuardTower_02"), -13.5f, -41.5f, 0f, 0f, false, 0.4f);
      Put(root, MB("GuardTower_02"), 13.5f, -41.5f, 0f, 0f, false, 0.4f);
      // Main gate (south, camera side): guard hut, floodlights on both posts; other gates: floodlights.
      Put(root, MB("Plywood_Shed_Small_01"), 13f, -33.5f, 90f, 0f, false, 0.4f);
      foreach (var (gx, gz, yaw) in new[] { (0f, -46f, 0f), (0f, 46f, 180f), (46f, 0f, -90f), (-46f, 0f, 90f) }) {
        var side = gx == 0f ? new Vector2(1, 0) : new Vector2(0, 1);
        foreach (var s in new[] { -1f, 1f }) Put(root, BR("Spotlight_01"), gx + side.x * s * 6.4f, gz + side.y * s * 6.4f, yaw, 0f, false, 0.2f);
      }
      // Radio masts and a mobile radar in the east/west bands.
      Put(root, BR("RadioTower_01"), -29f, 36f, 0f, 0f, false, 0.6f);
      Put(root, BR("RadioTower_01"), 42f, -18f, 0f, 0f, false, 0.6f);
      // Firing lines (Hesco rows) inside each gate, facing the approach.
      foreach (var (gx, gz) in new[] { (0f, -40f), (0f, 40f), (40f, 0f), (-40f, 0f) }) {
        var along = gx == 0f;
        foreach (var s in new[] { -1f, 1f }) {
          Put(root, MM("Barrier_Base_Row_02"), gx + (along ? s * 8.5f : 0f), gz + (along ? 0f : s * 8.5f), along ? 0f : 90f, 0f, false, 0.4f);
        }
      }
    }

    // ------------------------------------------------------------ MK_01 zones

    public static void Zones(Transform root) {
      // NW logistics: two sheds facing the road loop, a covered store, neatly aligned rows of crates, pallets and drums on a pad.
      Put(root, MB("Plywood_Shed_01"), -9.5f, 29f, 180f);
      Put(root, MB("Plywood_Shed_01"), -20f, 32f, 180f);

      var goods = new[] { MM("Crate_Stack_02"), MM("Crate_Stack_01"), MM("Barrel_Stack_01"), MM("Crate_Stack_03") };
      for (var i = 0; i < 5; i++) Put(root, goods[i % goods.Length], -19f + i * 3f, 38f, 0f, 0f, false, 0.6f);
      for (var i = 0; i < 5; i++) Put(root, i % 2 == 0 ? MP("Pallet_02") : MM("Crate_Ammo_01"), -21f + i * 3f, 41f, 0f, 0f, false, 0.4f);
      // Centre: the two-storey mercenary exchange (the shop) in the HQ hub, facing the main gate.
      Exchange(root);
      // NE barracks: three tents in a row, two huts behind.
      Put(root, MB("Tent_01"), 12f, 30f, 90f);
      Put(root, MB("Tent_01"), 12f, 39f, 90f);
      Put(root, MB("Plywood_Shed_Small_01"), 26f, 17f, 180f, 0f, false, 0.6f);
      // SE training yard: firing range (shooting line + seven targets + Hesco backstop), tyre course, climbing wall.
      for (var i = 0; i < 7; i++) Put(root, BR("Target_01"), 9f + i * 3.2f, -36.5f, 0f, 0f, false, 0.4f);
      Put(root, MM("Barrier_Base_Row_03"), 16f, -39.3f, 0f, 0f, false, 0.4f);
      for (var i = 0; i < 4; i++) Put(root, MM("Barrier_Base_Single_01"), 10f + i * 6f, -26f, 0f, 0f, false, 0.4f);
      for (var i = 0; i < 6; i++) Put(root, MP("Tire_01"), 30f + (i % 3) * 2.2f, -25.5f - (i / 3) * 2.2f, 0f, 0f, false, 0.4f);
      Put(root, BR("Barrier_04"), 37.5f, -27f, 90f, 0f, false, 0.4f);
      // East band: workshop, generator and drums; one tidy row of two containers.
      Put(root, MB("Plywood_Shed_01"), 37f, -12f, -90f);
      Put(root, MP("Generator_Large_01"), 29f, -14f, 0f, 0f, false, 0.6f);
      Put(root, MM("Barrel_Stack_01"), 29f, -8f, 0f, 0f, false, 0.6f);
      Put(root, KP("Shipping_Container_01"), 40.5f, 9f, 0f, 0f, false, 0.6f);
      Put(root, KP("Shipping_Container_01"), 40.5f, 16f, 0f, 0f, false, 0.6f);
      Put(root, KP("Shipping_Container_01"), 37f, 9f, 0f, 0f, false, 0.6f);
      // A dark logistics truck parked by the sheds (pack's own dark vehicle variant).
      Dark(Put(root, PM + "Vehicles/SM_Veh_Truck_01.prefab", -26f, 38f, 90f, 0f, false, 0.6f));
      foreach (var line in Skipped) Debug.Log($"[FW-Camp] {line}");
    }

    static void Exchange(Transform root) {
      var yaw = DressKit.FrontOf(PC + "Buildings/SM_Bld_OfficeOld_Small_Base_01.prefab");
      var y = 0f;
      var piece = BlockKit.Place(root, PC + "Buildings/SM_Bld_OfficeOld_Small_Base_01.prefab", new Vector3(0f, y, 0.5f), yaw, 1f);
      y += piece != null ? BlockKit.WorldBounds(piece).size.y : 6f;
      piece = BlockKit.Place(root, PC + "Buildings/SM_Bld_OfficeOld_Small_Floor_01.prefab", new Vector3(0f, y, 0.5f), yaw, 1f);
      y += piece != null ? BlockKit.WorldBounds(piece).size.y : 3.2f;
      BlockKit.Place(root, PC + "Buildings/SM_Bld_OfficeOld_Small_Roof_01.prefab", new Vector3(0f, y, 0.5f), yaw, 1f);
      BlockKit.Place(root, MP("Satellite_Dish_02"), new Vector3(2.2f, y + 0.6f, 1.5f), 200f, 1f);
      BlockKit.Place(root, BR("RadioTower_01"), new Vector3(-8.5f, 0f, 8.5f), 0f, 1f);
      // Trading tables and crates in front of the entrance.
      BlockKit.Place(root, MM("Crate_Stack_Cover_01"), new Vector3(-6.5f, 0f, -8.5f), 0f, 1f);
      BlockKit.Place(root, MM("Crate_Stack_Cover_02"), new Vector3(6.5f, 0f, -8.5f), 0f, 1f);
    }
  }
}
