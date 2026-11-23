namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// Task 39 (fix 2, user review 13:14): the dense PMC camp inside the 96 m octagon, baked by DressKit into two blocks
  /// (MK_00 perimeter, MK_01 zones). Perimeter = tan concrete wall with razor wire on top along every compound cell edge (the simulation
  /// wall line) except the four 16 m gates, corner watchtowers, gate barrier and floodlights, firing lines (barriers + crates) at the gate.
  /// Zones: west half = vehicle display (filled at runtime by MercCampView from the shop stock), centre = 2x trading post with a counter,
  /// east half = barracks district (tents/huts/barracks in rows, camo-net shelters, two crate yards), north-west = logistics (two
  /// warehouses, ten dark crate/pallet stacks), one tidy container row, south-east = firing range. One loop road + the south gate road.
  /// Every piece is checked against the compound cells, the roads, the trading post and the simulation cover walls; a piece that does not
  /// fit is skipped and logged. Pack prefabs and materials are untouched: PMC black uses our darkened copies of pack materials
  /// (FactoryWarsArt/PMC) on our instances only.
  /// </summary>
  public static class CampKit {
    const string PC = "Assets/ExternalAssets/Synty/PolygonCity/Prefabs/";
    const string PM = "Assets/ExternalAssets/Synty/PolygonMilitary/Prefabs/";
    const string PK = "Assets/ExternalAssets/Synty/PolygonConstruction/Prefabs/";
    const string PB = "Assets/ExternalAssets/Synty/PolygonBattleRoyale/Prefabs/";
    const string PmcFolder = "Assets/FactoryWarsArt/PMC";
    const float GateReach = 28.8f;

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

    /// <summary>The loop road (rectangle ±LoopX × ±LoopZ, RoadWidth wide) and the road from the loop to the south gate.</summary>
    public static bool OnRoad(float x, float z) {
      var w = CampLayout.RoadWidth / 2f;
      var ax = Mathf.Abs(x);
      var az = Mathf.Abs(z);
      var loop = (ax < CampLayout.LoopX + w && az < CampLayout.LoopZ + w) && (ax > CampLayout.LoopX - w || az > CampLayout.LoopZ - w);
      var south = ax < CampLayout.GateRoadHalf && z < -CampLayout.LoopZ;
      return loop || south;
    }

    static bool InPost(float x, float z) => Mathf.Abs(x) < CampLayout.LoopX - CampLayout.RoadWidth / 2f && Mathf.Abs(z) < CampLayout.LoopZ - CampLayout.RoadWidth / 2f;

    /// <summary>A footprint (world bounds) fits: inside the wall with a margin, off the roads and the trading-post block, clear of the cover walls.</summary>
    public static bool Fits(Bounds b, float margin = 1.2f, bool allowRoad = false, bool allowPost = false) {
      for (var i = 0; i <= 4; i++) {
        for (var j = 0; j <= 4; j++) {
          var x = Mathf.Lerp(b.min.x, b.max.x, i / 4f);
          var z = Mathf.Lerp(b.min.z, b.max.z, j / 4f);
          if (!Inside(x + margin, z) || !Inside(x - margin, z) || !Inside(x, z + margin) || !Inside(x, z - margin)) return false;
          if (!allowRoad && OnRoad(x, z)) return false;
          if (!allowPost && InPost(x, z)) return false;
          if (Mathf.Abs(Mathf.Abs(x) - 24f) < 2.4f && Mathf.Abs(Mathf.Abs(z) - 24f) < 2.4f) return false;
        }
      }
      return true;
    }

    static string MP(string n) => PM + "Props/SM_Prop_" + n + ".prefab";
    static string MM(string n) => PM + "Props/Military/SM_Prop_" + n + ".prefab";
    static string MB(string n) => PM + "Buildings/SM_Bld_" + n + ".prefab";
    static string BR(string n) => PB + "Props/SM_Prop_" + n + ".prefab";
    static string BB(string n) => PB + "Buildings/SM_Bld_" + n + ".prefab";
    static string KP(string n) => PK + "Props/SM_Prop_" + n + ".prefab";

    static readonly List<string> Skipped = new List<string>();
    static readonly Dictionary<string, int> Placed = new Dictionary<string, int>();
    public static IEnumerable<string> Report => Skipped;

    /// <summary>Place a piece centred at (x, z) world (block origin = camp centre); skip it when it does not fit. Counted per zone.</summary>
    static GameObject Put(Transform root, string zone, string path, float x, float z, float yaw, float y = 0f, bool allowRoad = false, float margin = 1.2f, bool allowPost = false) {
      var g = BlockKit.Place(root, path, new Vector3(x, y, z), yaw, 1f);
      if (g == null) { Skipped.Add($"missing {path}"); return null; }
      var w = BlockKit.WorldBounds(g);
      w.center -= root.position;
      if (!Fits(w, margin, allowRoad, allowPost)) {
        Skipped.Add($"skip {System.IO.Path.GetFileNameWithoutExtension(path)} at ({x:0.#},{z:0.#})");
        Object.DestroyImmediate(g);
        return null;
      }
      Placed[zone] = (Placed.TryGetValue(zone, out var n) ? n : 0) + 1;
      return g;
    }

    // ------------------------------------------------------------ PMC black (our darkened copies of pack materials)

    static readonly Dictionary<(Material, bool), Material> DarkCache = new Dictionary<(Material, bool), Material>();

    /// <summary>A copy of a pack material with a dark base-colour tint, saved in FactoryWarsArt/PMC (the pack material is untouched).</summary>
    public static Material Dark(Material source, float tint = 0.24f, bool flat = false) {
      if (source == null) return null;
      if (DarkCache.TryGetValue((source, flat), out var cached) && cached != null) return cached;
      if (!AssetDatabase.IsValidFolder(PmcFolder)) AssetDatabase.CreateFolder("Assets/FactoryWarsArt", "PMC");
      var path = $"{PmcFolder}/PMC_Black_{source.name}{(flat ? "_Matte" : "")}.mat";
      var dark = AssetDatabase.LoadAssetAtPath<Material>(path);
      if (dark == null) {
        dark = new Material(source) { name = "PMC_Black_" + source.name };
        AssetDatabase.CreateAsset(dark, path);
      }
      dark.CopyPropertiesFromMaterial(source);
      var c = new Color(tint, tint, tint * 1.05f, 1f);
      if (dark.HasProperty("_BaseColor")) dark.SetColor("_BaseColor", c);
      if (dark.HasProperty("_Color")) dark.SetColor("_Color", c);
      if (dark.HasProperty("_Smoothness")) dark.SetFloat("_Smoothness", 0.1f);
      if (flat) {
        // Matte black without the camouflage texture (vehicles): the pack's camo pattern stays brown under any tint.
        if (dark.HasProperty("_BaseMap")) dark.SetTexture("_BaseMap", null);
        if (dark.HasProperty("_MainTex")) dark.SetTexture("_MainTex", null);
        if (dark.HasProperty("_Albedo")) dark.SetTexture("_Albedo", null);
      }
      dark.name = System.IO.Path.GetFileNameWithoutExtension(path);
      EditorUtility.SetDirty(dark);
      DarkCache[(source, flat)] = dark;
      return dark;
    }

    static void Blacken(GameObject g, float tint = 0.24f, bool flat = false) {
      if (g == null) return;
      foreach (var r in g.GetComponentsInChildren<Renderer>()) {
        var mats = r.sharedMaterials;
        for (var i = 0; i < mats.Length; i++) {
          if (mats[i] != null && mats[i].name.IndexOf("Glass") < 0) mats[i] = Dark(mats[i], tint, flat);
        }
        r.sharedMaterials = mats;
      }
    }

    static bool Gate(float x, float z) => (Mathf.Abs(x) < 8.5f && Mathf.Abs(z) > GateReach) || (Mathf.Abs(z) < 8.5f && Mathf.Abs(x) > GateReach);

    // ------------------------------------------------------------ display models and troops (runtime slots)

    /// <summary>
    /// Wrapper prefabs (our folder) around pack vehicles in PMC black: tank, APC, attack helicopter (one mesh each), the drone, the
    /// PMC trooper (Mercenary character, its 21 skinned parts merged into one skinned mesh, black material), the cargo truck.
    /// Registered as VisualCatalog slots camp/display_*, unit/pmc_* and unit/pmc_soldier.
    /// </summary>
    public static void RegisterDisplays(VisualCatalog catalog, string folder) {
      catalog.Slots.RemoveAll(s => s != null && s.Id != null && s.Id.StartsWith("camp/"));
      var models = new (string Id, string Path)[] {
        ("tank", PM + "Vehicles/SM_Veh_Light_Tank_01.prefab"),
        ("mrap", PM + "Vehicles/SM_Veh_APC_01.prefab"),
        ("heli", PM + "Vehicles/SM_Veh_Helicopter_Attack_02.prefab"),
      };
      foreach (var (id, path) in models) {
        var root = new GameObject("camp_display_" + id);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (source == null) { Debug.LogWarning($"[FW-Camp] missing {path}"); Object.DestroyImmediate(root); continue; }
        var item = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
        item.transform.localPosition = Vector3.zero;
        var b = BlockKit.WorldBounds(item);
        item.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
        Blacken(item, 0.13f, true);
        var prefab = SaveCombined(root, folder, $"camp_display_{id}");
        Object.DestroyImmediate(root);
        // Real scale (fix 3, infantry 1.8 m): MBT 9.5 m long, APC 7.5 m; the attack helicopter is replaced by ThirdPartyKit.
        var real = id == "tank" ? 9.5f : id == "mrap" ? 7.5f : Mathf.Max(b.size.x, b.size.z);
        Slot(catalog, "camp/display_" + id, prefab, b.size, real / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z)));
      }
      var drone = catalog.Slots.Find(s => s != null && s.Id == "unit/shared_scout_drone");
      if (drone != null && drone.Prefab != null) Slot(catalog, "camp/display_drone", drone.Prefab, Vector3.one * 2f, drone.Scale);
      catalog.Slots.RemoveAll(s => s != null && s.Id != null && (s.Id.StartsWith("unit/pmc_") || s.Id == "unit/merc_cargo_truck"));
      foreach (var (unit, display) in new[] { ("pmc_tank", "tank"), ("pmc_armored_suv", "mrap"), ("pmc_attack_heli", "heli"), ("pmc_attack_drone", "drone") }) {
        var slot = catalog.Slots.Find(s => s != null && s.Id == "camp/display_" + display);
        if (slot == null) continue;
        Slot(catalog, "unit/" + unit, slot.Prefab, slot.Size, slot.Scale);
        catalog.Slots.Find(s => s.Id == "unit/" + unit).AutoFit = false;
      }
      // PMC troopers: Mercenary male/female, merged into one skinned mesh with a black copy of the character material.
      var guard = catalog.Slots.Find(s => s != null && s.Id == "unit/trader_guard");
      var male = Trooper(PB + "Characters/Character_MercenaryMale_01.prefab", folder, "pmc_trooper_m");
      var female = Trooper(PB + "Characters/Character_MercenaryFemale_01.prefab", folder, "pmc_trooper_f");
      if (guard != null && male != null) {
        foreach (var (id, prefab) in new[] { ("unit/pmc_soldier", male), ("unit/pmc_soldier_f", female ?? male), ("unit/pmc_operator_squad", male), ("unit/pmc_marksman", female ?? male) }) {
          catalog.Slots.RemoveAll(s => s != null && s.Id == id);
          var copy = JsonUtility.FromJson<VisualSlot>(JsonUtility.ToJson(guard));
          copy.Id = id;
          copy.Prefabs = new List<GameObject> { prefab };
          copy.PivotOffset = Vector3.zero;
          copy.Scale = 1f;
          copy.Notes = "Task 39 PMC trooper (Synty BattleRoyale Mercenary, parts merged, PMC black copy of its material), trader_guard animator";
          catalog.Slots.Add(copy);
        }
      }
      var truckRoot = new GameObject("merc_cargo_truck");
      var truckSource = AssetDatabase.LoadAssetAtPath<GameObject>(PM + "Vehicles/SM_Veh_Truck_01.prefab");
      if (truckSource != null) {
        var truck = (GameObject)PrefabUtility.InstantiatePrefab(truckSource, truckRoot.transform);
        var tb = BlockKit.WorldBounds(truck);
        truck.transform.localPosition = new Vector3(-tb.center.x, -tb.min.y, -tb.center.z);
        var truckPrefab = SaveCombined(truckRoot, folder, "merc_cargo_truck");
        Slot(catalog, "unit/merc_cargo_truck", truckPrefab, tb.size);
        catalog.Slots.Find(s => s.Id == "unit/merc_cargo_truck").AutoFit = true;
      }
      Object.DestroyImmediate(truckRoot);
    }

    /// <summary>
    /// One skinned mesh per trooper: the Synty modular parts share one skeleton and bind poses, so their meshes concatenate with the
    /// same bone list. Falls back to the original prefab (with the black material) when the parts do not share bones.
    /// </summary>
    static GameObject Trooper(string path, string folder, string name) {
      var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
      if (source == null) { Debug.LogWarning($"[FW-Camp] missing {path}"); return null; }
      var item = (GameObject)PrefabUtility.InstantiatePrefab(source);
      PrefabUtility.UnpackPrefabInstance(item, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
      item.name = name;
      var parts = item.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy && r.sharedMesh != null).ToList();
      var keep = parts.OrderByDescending(r => r.sharedMesh.vertexCount).FirstOrDefault();
      var shared = keep != null && parts.All(p => p.bones.Length == keep.bones.Length && p.bones.SequenceEqual(keep.bones));
      if (shared && parts.Count > 1) {
        var instances = new List<CombineInstance>();
        var weights = new List<BoneWeight>();
        foreach (var p in parts) {
          for (var s = 0; s < p.sharedMesh.subMeshCount; s++) instances.Add(new CombineInstance { mesh = p.sharedMesh, subMeshIndex = s, transform = Matrix4x4.identity });
          weights.AddRange(p.sharedMesh.boneWeights);
        }
        var mesh = new Mesh { name = name + "_mesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.CombineMeshes(instances.ToArray(), true, false);
        mesh.boneWeights = weights.ToArray();
        mesh.bindposes = keep.sharedMesh.bindposes;
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{folder}/{name}_mesh.asset");
        keep.sharedMesh = mesh;
        foreach (var p in parts) if (p != keep) Object.DestroyImmediate(p.gameObject);
      }
      // One draw per trooper: merge submeshes that use the same material (the Synty character uses one atlas for every part).
      if (keep != null && keep.sharedMaterials.Length > 1 && keep.sharedMaterials.Distinct().Count() == 1) {
        var src = keep.sharedMesh;
        var merged = Object.Instantiate(src);
        merged.name = name + "_merged";
        var all = new List<int>();
        for (var s = 0; s < src.subMeshCount; s++) all.AddRange(src.GetTriangles(s));
        merged.subMeshCount = 1;
        merged.SetTriangles(all, 0);
        AssetDatabase.CreateAsset(merged, $"{folder}/{name}_merged.asset");
        keep.sharedMesh = merged;
        keep.sharedMaterials = new[] { keep.sharedMaterials[0] };
      }
      foreach (var r in item.GetComponentsInChildren<Renderer>(true)) {
        r.sharedMaterials = r.sharedMaterials.Select(m => Dark(m, 0.3f)).ToArray();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (r is SkinnedMeshRenderer sk) sk.updateWhenOffscreen = false;
      }
      var prefab = PrefabUtility.SaveAsPrefabAsset(item, $"{folder}/{name}.prefab");
      Object.DestroyImmediate(item);
      Debug.Log($"[FW-Camp] trooper {name}: {parts.Count} skinned parts -> {(shared ? 1 : parts.Count)}");
      return prefab;
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
      foreach (var mat in renderer.sharedMaterials) if (mat != null) mat.enableInstancing = mat.name.StartsWith("PMC_") ? true : mat.enableInstancing;
      var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{folder}/{name}.prefab");
      Object.DestroyImmediate(go);
      return prefab;
    }

    static void Slot(VisualCatalog catalog, string id, GameObject prefab, Vector3 size, float scale = 1f) {
      BlockKit.Register(catalog, "tmp_" + id.Replace('/', '_'), prefab, size);
      var slot = catalog.Slots.Find(s => s != null && s.Id == "block/tmp_" + id.Replace('/', '_'));
      slot.Id = id;
      slot.Scale = scale;
      slot.Notes = "Task 39 PMC camp model (CampKit.cs), PMC black copy of pack materials";
    }

    // ------------------------------------------------------------ MK_00 perimeter

    public static void Perimeter(Transform root) {
      Skipped.Clear();
      Placed.Clear();
      var size = FactoryWarsSetup.GridCell;
      var dirs = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
      var walls = new[] { MP("Barrier_Tall_Group_01"), MP("Barrier_Tall_Group_02") };
      var wire = MM("RazorWire_01");
      foreach (var (r, c) in Cells.OrderBy(t => t.Item1).ThenBy(t => t.Item2)) {
        var cx = FactoryWarsSetup.CellX(c);
        var cz = FactoryWarsSetup.CellZ(r);
        foreach (var (dr, dc) in dirs) {
          if (Cells.Contains((r + dr, c + dc))) continue;
          // Tan concrete wall on the cell boundary, 0.6 m inside (the simulation wall is 1.2 m thick, inward); razor wire on top.
          var horizontal = dr != 0;
          var ex = cx + dc * (size / 2f - 0.6f);
          var ez = cz - dr * (size / 2f - 0.6f);
          if (Gate(cx + dc * size / 2f, cz - dr * size / 2f)) continue;
          for (var k = 0; k < 2; k++) {
            var t = -2f + k * 4f;
            BlockKit.Place(root, walls[(r + c + k) % 2], new Vector3(horizontal ? cx + t : ex, 0f, horizontal ? ez : cz + t), horizontal ? 90f : 0f, 1f);
          }
          BlockKit.Place(root, wire, new Vector3(horizontal ? cx : ex, 3.25f, horizontal ? ez : cz), horizontal ? 0f : 90f, 1f);
          Placed["perimeter wall"] = (Placed.TryGetValue("perimeter wall", out var n) ? n : 0) + 1;
        }
      }
      // Corner watchtowers and the two towers flanking the south gate.
      foreach (var (x, z) in new[] { (-30f, 30f), (30f, 30f), (-30f, -30f), (30f, -30f) }) Put(root, "towers", MB("GuardTower_01"), x, z, z > 0 ? 180f : 0f, 0f, false, 0.4f);
      Put(root, "towers", MB("GuardTower_02"), -13.5f, -41.5f, 0f, 0f, false, 0.4f);
      Put(root, "towers", MB("GuardTower_02"), 13.5f, -41.5f, 0f, 0f, false, 0.4f);
      // South gate: barrier, guard hut, floodlights; firing lines of barriers and crates inside the gate; tank traps outside the line.
      Put(root, "gate", MM("Road_Barrier_01"), -6.5f, -43.5f, 0f, 0f, false, 0.2f);
      Put(root, "gate", MB("Plywood_Shed_Small_01"), 13f, -33.5f, 90f, 0f, false, 0.4f);
      foreach (var s in new[] { -1f, 1f }) Put(root, "gate", BR("Spotlight_01"), s * 5.5f, -46f, 0f, 0f, false, 0.2f);
      foreach (var s in new[] { -1f, 1f }) {
        Put(root, "firing line", MM("Barrier_Base_Row_02"), s * 8.5f, -40f, 0f, 0f, false, 0.4f);
        Put(root, "firing line", MM("Crate_Stack_Cover_01"), s * 5.2f, -39.8f, 0f, 0f, false, 0.3f);
        Put(root, "firing line", BR("TankTrap_01"), s * 5.5f, -35.5f, 30f, 0f, false, 0.3f);
      }
      foreach (var (gx, gz) in new[] { (0f, 40f), (40f, 0f), (-40f, 0f) }) {
        var along = gx == 0f;
        foreach (var s in new[] { -1f, 1f }) Put(root, "firing line", MM("Barrier_Base_Row_02"), gx + (along ? s * 8.5f : 0f), gz + (along ? 0f : s * 8.5f), along ? 0f : 90f, 0f, false, 0.4f);
      }
    }

    // ------------------------------------------------------------ MK_01 zones

    public static void Zones(Transform root) {
      // Centre: the Mercenary Trading Post, twice the old footprint (two tan office blocks side by side), counter canopy with crates.
      TradingPost(root);
      // North-west logistics: two warehouses facing the loop, ten dark crate/pallet stacks in two neat rows.
      Put(root, "logistics", MB("Plywood_Shed_01"), -9.5f, 23f, 180f);
      Put(root, "logistics", MB("Plywood_Shed_01"), -18.5f, 30.5f, 180f);
      var goods = new[] { KP("Crate_Stack_01"), MM("Crate_Stack_03"), KP("PalletStack_01"), KP("PalletStack_02") };
      for (var i = 0; i < 5; i++) Blacken(Put(root, "logistics", goods[i % goods.Length], -17f + i * 3.2f, 35.8f, 0f, 0f, false, 0.5f));
      for (var i = 0; i < 5; i++) Blacken(Put(root, "logistics", goods[(i + 2) % goods.Length], -15.5f + i * 3.2f, 39.3f, 0f, 0f, false, 0.5f));
      // One tidy container row (north).
      Put(root, "logistics", KP("Shipping_Container_01"), 9.5f, 43.5f, 90f, 0f, false, 0.5f);
      Put(root, "logistics", KP("Shipping_Container_01"), 16.5f, 43.5f, 90f, 0f, false, 0.5f);
      // East barracks district: two barracks (north), six tents in two columns, two camo-net shelters, two crate yards, paths between.
      Put(root, "barracks", MB("Barracks_01"), 11f, 23f, 90f, 0f, false, 0.6f);
      Put(root, "barracks", MB("Barracks_01"), 11f, 33f, 90f, 0f, false, 0.6f);
      foreach (var z in new[] { -12f, 12f }) {
        Put(root, "barracks", BB("Tent_01"), 25.5f, z, 0f, 0f, false, 0.6f);
        Put(root, "barracks", BB("Tent_02"), 40f, z, 0f, 0f, false, 0.6f);
      }
      Put(root, "barracks", BB("Tent_02"), 40f, 1f, 0f, 0f, false, 0.6f);
      Put(root, "barracks", BB("Tent_02"), 25.5f, 1f, 0f, 0f, false, 0.6f);
      Put(root, "barracks", MB("CamoNet_Tent_03"), 26.5f, 30.5f, 0f, 0f, false, 0.4f);
      Put(root, "barracks", MB("CamoNet_Tent_04"), 33f, -21f, 0f, 0f, false, 0.4f);
      var yard = new[] { MM("Crate_Stack_03"), KP("PalletStack_01"), MM("Crate_Stack_Cover_03") };
      for (var i = 0; i < 6; i++) Blacken(Put(root, "crate yards", yard[i % yard.Length], 19f + (i % 3) * 2.6f, 37f + (i / 3) * 2.6f, 0f, 0f, false, 0.4f));
      for (var i = 0; i < 6; i++) Blacken(Put(root, "crate yards", yard[(i + 1) % yard.Length], 36.5f + (i % 3) * 2.4f, 17.5f + (i / 3) * 2.4f, 0f, 0f, false, 0.4f));
      // South-east firing range: seven targets, Hesco backstop, shooting line barriers.
      for (var i = 0; i < 7; i++) Put(root, "range", BR("Target_01"), 10f + i * 3f, -36.5f, 0f, 0f, false, 0.4f);
      Put(root, "range", MM("Barrier_Base_Row_03"), 16f, -39.3f, 0f, 0f, false, 0.4f);
      for (var i = 0; i < 3; i++) Put(root, "range", MM("Barrier_Base_Single_01"), 10f + i * 6f, -26f, 0f, 0f, false, 0.4f);
      // Mechanics' corner by the display: generator and drums.

      foreach (var line in Skipped) Debug.Log($"[FW-Camp] {line}");
      Debug.Log("[FW-Camp] placed: " + string.Join(", ", Placed.Select(kv => $"{kv.Key} {kv.Value}")));
    }

    static void TradingPost(Transform root) {
      var basePath = PC + "Buildings/SM_Bld_OfficeOld_Small_Base_01.prefab";
      var yaw = DressKit.FrontOf(basePath);
      foreach (var x in new[] { -5.45f, 5.45f }) {
        var y = 0f;
        var piece = BlockKit.Place(root, basePath, new Vector3(x, y, 1.5f), yaw, 1f);
        y += piece != null ? BlockKit.WorldBounds(piece).size.y : 6f;
        BlockKit.Place(root, PC + "Buildings/SM_Bld_OfficeOld_Small_Roof_01.prefab", new Vector3(x, y, 1.5f), yaw, 1f);
        if (x > 0) BlockKit.Place(root, MP("Satellite_Dish_02"), new Vector3(x + 2f, y + 0.6f, 3f), 200f, 1f);
      }
      // Counter: a camo-net canopy over a line of crates (the vendor stands behind it, see CampLayout.Counter).
      BlockKit.Place(root, MB("CamoNet_Tent_06"), new Vector3(0f, 3.1f, -7.6f), 0f, 1f);
      foreach (var x in new[] { -2.8f, -1f, 0.8f, 2.6f }) BlockKit.Place(root, MM("Crate_Ammo_01"), new Vector3(x, 0f, -8.3f), 0f, 1f);
      BlockKit.Place(root, BR("RadioTower_01"), new Vector3(-9.5f, 0f, 6.5f), 0f, 1f);
      Placed["trading post"] = 2;
    }
  }
}
