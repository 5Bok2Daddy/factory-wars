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

    const string PmcMaterials = "Assets/FactoryWarsArt/Materials/PMC";
    static readonly Dictionary<(Material, bool), Material> DarkCache = new Dictionary<(Material, bool), Material>();
    static readonly Dictionary<(Texture, bool), Texture2D> DarkTextures = new Dictionary<(Texture, bool), Texture2D>();

    static void Folder(string path) {
      var parts = path.Split('/');
      for (var i = 1; i < parts.Length; i++) {
        var parent = string.Join("/", parts.Take(i));
        if (!AssetDatabase.IsValidFolder(parent + "/" + parts[i])) AssetDatabase.CreateFolder(parent, parts[i]);
      }
    }

    /// <summary>
    /// Root cause (fix 4): the Synty materials sample their colour from the atlas in the shader-graph property "_Albedo_Map"
    /// (Generic_Basic), so clearing _BaseMap/_MainTex or tinting did nothing and the camo atlas stayed visible. Fix: a charcoal copy of
    /// the atlas itself (detail kept, 22-30 % luminance, desaturated) saved in our folder and assigned to every albedo-like texture slot.
    /// </summary>
    static Texture2D DarkTexture(Texture source, bool muted = false) {
      if (source == null) return null;
      if (DarkTextures.TryGetValue((source, muted), out var cached) && cached != null) return cached;
      Folder(PmcMaterials + "/Textures");
      var path = $"{PmcMaterials}/Textures/{source.name}_{(muted ? "pmcm" : "pmc")}.png";
      var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
      Graphics.Blit(source, rt);
      var previous = RenderTexture.active;
      RenderTexture.active = rt;
      var read = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
      read.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
      read.Apply();
      RenderTexture.active = previous;
      RenderTexture.ReleaseTemporary(rt);
      var px = read.GetPixels32();
      // Fix 6: per-swatch remap instead of darkening everything (Synty atlases are swatch palettes). Every swatch keeps its own
      // value step; the class decides the PMC tone: glass -> tinted blue-grey, rubber -> near black, metal/greys -> gunmetal,
      // greens -> olive dark slate, warm camo/tan -> charcoal, saturated accents -> amber, skin -> unchanged (troops read as people).
      var vehicle = IsVehicleAtlas(source.name);
      for (var i = 0; i < px.Length; i++) px[i] = muted ? PmcMuted(px[i], vehicle) : PmcTone(px[i], vehicle);
      read.SetPixels32(px);
      System.IO.File.WriteAllBytes(path, read.EncodeToPNG());
      Object.DestroyImmediate(read);
      AssetDatabase.ImportAsset(path);
      if (AssetImporter.GetAtPath(path) is TextureImporter importer) {
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.maxTextureSize = 1024;
        importer.SaveAndReimport();
      }
      var dark = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
      DarkTextures[(source, muted)] = dark;
      return dark;
    }

    static bool IsVehicleAtlas(string name) {
      var n = name.ToLowerInvariant();
      return n.Contains("veh") || n.Contains("vehicle") || n.Contains("tank") || n.Contains("heli") || n.Contains("drone") || n.Contains("palette");
    }

    public static Color32 PmcTone(Color32 c, bool vehicle) {
      Color.RGBToHSV(c, out var h, out var s, out var v);
      var lum = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
      var hue = h * 360f;
      Color o;
      // Skin: light pinkish beige only (hue 10-28, low-mid saturation, bright). Wood, tan camo and khaki cloth sit at hue 30-40
      // or lower value and must not be kept (fix 6 round 1 kept them by mistake).
      if (vehicle == false && s > 0.15f && s < 0.42f && v > 0.7f && hue >= 10f && hue <= 28f) return c;                  // skin
      if (s > 0.55f && v > 0.5f && (hue < 55f || hue > 330f)) o = new Color(0.95f, 0.71f, 0.29f) * (0.55f + 0.45f * lum); // accents -> amber
      else if (vehicle && s > 0.2f && hue >= 170f && hue <= 250f) o = new Color(0.16f, 0.2f, 0.26f) + new Color(0.2f, 0.24f, 0.3f) * lum; // glass
      else if (lum < 0.12f) o = new Color(0.05f, 0.05f, 0.055f) + Color.white * (lum * 0.4f);                                // rubber, gaps
      else if (s < 0.12f) { var g = 0.12f + lum * 0.42f; o = new Color(g, g * 1.02f, g * 1.08f); }                          // metal, greys
      // Camo greens / tans: two tones (olive slate for darker swatches, gunmetal for lighter ones) with stretched value steps so
      // camo patches and panels stay distinguishable.
      else if (hue >= 60f && hue < 170f) o = Color.Lerp(new Color(0.2f, 0.23f, 0.19f), new Color(0.3f, 0.32f, 0.35f), Mathf.Clamp01((lum - 0.25f) * 3f)) * (0.35f + lum * 1.6f); // greens
      else o = Color.Lerp(new Color(0.17f, 0.18f, 0.2f), new Color(0.32f, 0.31f, 0.28f), Mathf.Clamp01((lum - 0.3f) * 2.5f)) * (0.4f + lum * 1.5f); // tan/brown/wood/blue cloth
      o.a = c.a / 255f;
      return o;
    }

    /// <summary>
    /// 2026-11-24 (AGY fix C): muted PMC tone for vehicles, camo nets and big camp pieces. The per-swatch remap kept the camo pattern
    /// (olive slate vs gunmetal with a wide value stretch), which read as green camo next to the gunmetal helicopters. Muted = a
    /// low-contrast olive-grey / gunmetal two-tone with a narrow value band; skin (troops), glass and rubber keep their classes, and
    /// saturated accents (red containers, red roofs) fall into the same two-tone (the amber PMC stripes are separate quads).
    /// </summary>
    public static Color32 PmcMuted(Color32 c, bool vehicle) {
      Color.RGBToHSV(c, out var h, out var s, out var v);
      var lum = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
      var hue = h * 360f;
      if (vehicle == false && s > 0.15f && s < 0.42f && v > 0.7f && hue >= 10f && hue <= 28f) return c;                   // skin
      Color o;
      if (vehicle && s > 0.2f && hue >= 170f && hue <= 250f) o = new Color(0.16f, 0.2f, 0.26f) + new Color(0.2f, 0.24f, 0.3f) * lum; // glass
      else if (lum < 0.12f) o = new Color(0.05f, 0.05f, 0.055f) + Color.white * (lum * 0.4f);                                 // rubber, gaps
      else {
        var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((lum - 0.20f) / 0.30f));
        o = Color.Lerp(new Color(0.15f, 0.165f, 0.165f), new Color(0.33f, 0.34f, 0.36f), t) * (0.88f + lum * 0.24f);           // charcoal / gunmetal two-tone
      }
      o.a = c.a / 255f;
      return o;
    }

    /// <summary>Our PMC material variant of a pack material (pack material untouched): every albedo slot gets the charcoal atlas.</summary>
    public static Material Dark(Material source, bool muted = false) {
      if (source == null) return null;
      if (source.name.StartsWith("PMC_") || source.name.StartsWith("PMCM_")) return source;
      if (DarkCache.TryGetValue((source, muted), out var cached) && cached != null) return cached;
      Folder(PmcMaterials);
      var path = $"{PmcMaterials}/{(muted ? "PMCM_" : "PMC_")}{source.name}.mat";
      var dark = AssetDatabase.LoadAssetAtPath<Material>(path);
      if (dark == null) {
        dark = new Material(source);
        AssetDatabase.CreateAsset(dark, path);
      }
      dark.shader = source.shader;
      dark.CopyPropertiesFromMaterial(source);
      var any = false;
      foreach (var name in source.GetTexturePropertyNames()) {
        var lower = name.ToLowerInvariant();
        if (!(lower.Contains("albedo") || lower.Contains("basemap") || lower.Contains("maintex") || lower.Contains("base_map"))) continue;
        var tex = source.GetTexture(name);
        if (tex == null) continue;
        dark.SetTexture(name, DarkTexture(tex, muted));
        any = true;
      }
      var tint = any ? Color.white : new Color(0.14f, 0.14f, 0.15f, 1f);
      foreach (var colour in new[] { "_BaseColor", "_Color" }) if (dark.HasProperty(colour)) dark.SetColor(colour, tint);
      if (dark.HasProperty("_Smoothness")) dark.SetFloat("_Smoothness", 0.12f);
      dark.enableInstancing = true;
      EditorUtility.SetDirty(dark);
      DarkCache[(source, muted)] = dark;
      return dark;
    }

    static void Blacken(GameObject g, bool muted = false) {
      if (g == null) return;
      foreach (var r in g.GetComponentsInChildren<Renderer>(true)) {
        var mats = r.sharedMaterials;
        for (var i = 0; i < mats.Length; i++) {
          if (mats[i] != null && mats[i].name.IndexOf("Glass") < 0) mats[i] = Dark(mats[i], muted);
        }
        r.sharedMaterials = mats;
      }
    }

    /// <summary>Third-party PMC models (helicopters, drone): black prefab variants in our folder replace the slot prefabs.</summary>
    public static void BlackenThirdParty(VisualCatalog catalog, string folder) {
      foreach (var id in new[] { "camp/display_heli", "camp/display_scoutheli", "camp/display_drone", "unit/pmc_attack_heli", "unit/pmc_scout_heli", "unit/pmc_attack_drone" }) {
        var slot = catalog.Slots.Find(s => s != null && s.Id == id);
        if (slot == null || slot.Prefab == null || slot.Prefab.name.StartsWith("pmc_black_")) continue;
        var item = (GameObject)PrefabUtility.InstantiatePrefab(slot.Prefab);
        PrefabUtility.UnpackPrefabInstance(item, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
        Blacken(item);
        var prefab = PrefabUtility.SaveAsPrefabAsset(item, $"{folder}/pmc_black_{slot.Prefab.name}.prefab");
        Object.DestroyImmediate(item);
        slot.Prefabs[0] = prefab;
        Debug.Log($"[FW-Camp] {id} -> {prefab.name}");
      }
      EditorUtility.SetDirty(catalog);
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
        // Fix 5: a main battle tank with turret and gun (BattleRoyale pack tank, 8.0k tris), loaded by GUID.
        ("tank", AssetDatabase.GUIDToAssetPath("c67acd4846ba1774589d2830ac38c5de")),
        ("mrap", PB + "Vehicles/SM_Veh_Armored_Car_01.prefab"),
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
        // Fix C: tank and APC in the muted two-tone (no green camo); the helicopter keeps the fix-6 tones that passed review.
        Blacken(item, id != "heli");
        if (id != "heli") {
          // PMC side stripe (Codex identity kit) on both flanks, about 45 % of the hull length, 4:1.
          var hull = BlockKit.WorldBounds(item);
          var w = hull.size.z * 0.45f;
          var stripe = PmcIdentityKit.Mat("SideStripe", "vehicle_side_stripe");
          foreach (var side in new[] { -1f, 1f }) {
            PmcIdentityKit.Quad(root.transform, "PmcStripe", stripe, new Vector3(side * (hull.extents.x + 0.03f), hull.min.y + hull.size.y * (id == "tank" ? 0.3f : 0.42f), hull.center.z), new Vector3(side, 0f, 0f), w, w / 4f);
          }
        }
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
        r.sharedMaterials = r.sharedMaterials.Select(m => Dark(m)).ToArray();
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

    /// <summary>Task 41: logistics depot/garage models (pack prefabs, unchanged) for LogisticsView.</summary>
    public static void RegisterLogistics(VisualCatalog catalog) {
      foreach (var (id, path) in new[] {
        ("logi/garage", MB("Plywood_Shed_01")),
        ("logi/warehouse_low", MB("Plywood_Shed_Small_01")),
        ("logi/warehouse_big", PB + "Buildings/SM_Bld_Warehouse_01.prefab"),
        ("logi/lumber_long", PK + "Props/SM_Prop_Plank_Long_Stack_01.prefab"),
        ("logi/lumber", PK + "Props/SM_Prop_Plank_Stack_01.prefab"),
        ("logi/container", KP("Shipping_Container_01")),
        ("logi/crane", PK + "Buildings/SM_Bld_Crane_01.prefab"),
        ("logi/tree_a", PM + "Environment/SM_Env_Tree_01.prefab"),
        ("logi/tree_b", PM + "Environment/SM_Env_Tree_02.prefab"),
      }) {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { Debug.LogWarning($"[FW-T41] missing {path}"); continue; }
        catalog.Slots.RemoveAll(s => s != null && s.Id == id);
        Slot(catalog, id, prefab, BlockKit.WorldBounds(prefab).size);
        var slot = catalog.Slots.Find(s => s.Id == id);
        slot.AutoFit = false;
        slot.Notes = "Task 41 logistics depot model (pack prefab, unchanged)";
      }
      EditorUtility.SetDirty(catalog);
    }

    static void Slot(VisualCatalog catalog, string id, GameObject prefab, Vector3 size, float scale = 1f) {
      BlockKit.Register(catalog, "tmp_" + id.Replace('/', '_'), prefab, size);
      var slot = catalog.Slots.Find(s => s != null && s.Id == "block/tmp_" + id.Replace('/', '_'));
      slot.Id = id;
      slot.Scale = scale;
      slot.Notes = "Task 39 PMC camp model (CampKit.cs), PMC black copy of pack materials";
    }

    // ------------------------------------------------------------ MK_00 perimeter

    public static void Perimeter(Transform root, bool low = false) {
      Skipped.Clear();
      Placed.Clear();
      var size = FactoryWarsSetup.GridCell;
      var dirs = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
      var wire = MM("RazorWire_01");
      ContinuousWall(root, wire, low);
      // Corner watchtowers and the two towers flanking the south gate.
      foreach (var (x, z) in new[] { (-30f, 30f), (30f, 30f), (-30f, -30f), (30f, -30f) }) Put(root, "towers", MB("GuardTower_01"), x, z, z > 0 ? 180f : 0f, 0f, false, 0.4f);
      // Fix C: the gate towers had a red roof; muted camp two-tone.
      Blacken(Put(root, "towers", MB("GuardTower_02"), -13.5f, -41.5f, 0f, 0f, false, 0.4f), true);
      Blacken(Put(root, "towers", MB("GuardTower_02"), 13.5f, -41.5f, 0f, 0f, false, 0.4f), true);
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


    /// <summary>
    /// Fix 5: one continuous 4.2 m concrete T-wall along the stepped octagon (no gaps at the stepped corners), built as panels of one
    /// mesh textured from the pack barrier's own atlas region (same PolygonMilitary material, so it batches with the pack props).
    /// ~7k triangles instead of 80 pack wall groups (71k). Razor wire (pack) runs along the whole top: two coils per 8 m edge.
    /// </summary>
    static void ContinuousWall(Transform root, string wire, bool low) {
      const float Inset = 0.6f, Height = 4.2f, Stem = 0.34f, BaseDepth = 0.95f, BaseHeight = 0.5f, Panel = 1.55f, Gap = 0.05f;
      var size = FactoryWarsSetup.GridCell;
      var half = size / 2f;
      var source = AssetDatabase.LoadAssetAtPath<GameObject>(MP("Barrier_Tall_Group_01"));
      var sourceFilter = source.GetComponentInChildren<MeshFilter>();
      var material = source.GetComponentInChildren<MeshRenderer>().sharedMaterial;
      var uvMin = new Vector2(0.5f, 0.5f);
      var uvMax = uvMin;
      {
        // UV box of the largest broad-face triangle of the pack barrier (its stained concrete).
        var m = sourceFilter.sharedMesh;
        var mv = m.vertices;
        var muv = m.uv;
        var mt = m.triangles;
        var best = -1f;
        for (var t = 0; t < mt.Length; t += 3) {
          var a = mv[mt[t]];
          var b = mv[mt[t + 1]];
          var c = mv[mt[t + 2]];
          var n = Vector3.Cross(b - a, c - a);
          var area = n.magnitude;
          if (area < 1e-4f || Mathf.Abs(n.normalized.x) < 0.8f || area <= best) continue;
          best = area;
          uvMin = Vector2.Min(Vector2.Min(muv[mt[t]], muv[mt[t + 1]]), muv[mt[t + 2]]);
          uvMax = Vector2.Max(Vector2.Max(muv[mt[t]], muv[mt[t + 1]]), muv[mt[t + 2]]);
        }
      }
      var verts = new List<Vector3>();
      var uvs = new List<Vector2>();
      var tris = new List<int>();
      void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) {
        var k = verts.Count;
        verts.AddRange(new[] { a, b, c, d });
        uvs.AddRange(new[] { new Vector2(uvMin.x, uvMin.y), new Vector2(uvMax.x, uvMin.y), new Vector2(uvMax.x, uvMax.y), new Vector2(uvMin.x, uvMax.y) });
        tris.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
      }
      // Box without a bottom face; centre p (ground), half-extents along the wall (u) and across it (w).
      void Box(Vector3 p, Vector3 u, Vector3 w, float y0, float y1) {
        Vector3 P(float su, float sw, float y) => p + u * su + w * sw + Vector3.up * y;
        Quad(P(-1, -1, y0), P(1, -1, y0), P(1, -1, y1), P(-1, -1, y1));
        Quad(P(1, 1, y0), P(-1, 1, y0), P(-1, 1, y1), P(1, 1, y1));
        Quad(P(1, -1, y0), P(1, 1, y0), P(1, 1, y1), P(1, -1, y1));
        Quad(P(-1, 1, y0), P(-1, -1, y0), P(-1, -1, y1), P(-1, 1, y1));
        Quad(P(-1, -1, y1), P(1, -1, y1), P(1, 1, y1), P(-1, 1, y1));
      }
      var dirs = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
      foreach (var (r, c) in Cells.OrderBy(t => t.Item1).ThenBy(t => t.Item2)) {
        var cx = FactoryWarsSetup.CellX(c);
        var cz = FactoryWarsSetup.CellZ(r);
        foreach (var (dr, dc) in dirs) {
          if (Cells.Contains((r + dr, c + dc))) continue;
          if (Gate(cx + dc * half, cz - dr * half)) continue;
          var horizontal = dr != 0;
          // End of the wall line at each side: convex corner -> stop at the inset corner, concave -> run on to meet the
          // perpendicular wall, straight -> to the cell edge.
          float End(int s) {
            var n = horizontal ? (r, c + s) : (r - s, c);
            var d = horizontal ? (r + dr, c + s) : (r - s, c + dc);
            if (Cells.Contains(n) == false) return half - Inset;
            return Cells.Contains(d) ? half + Inset : half;
          }
          var a0 = -End(-1);
          var a1 = End(1);
          var along = horizontal ? Vector3.right : Vector3.forward;
          var across = horizontal ? Vector3.forward : Vector3.right;
          var line = horizontal ? new Vector3(cx, 0f, cz - dr * (half - Inset)) : new Vector3(cx + dc * (half - Inset), 0f, cz);
          var length = a1 - a0;
          var count = Mathf.Max(1, Mathf.RoundToInt(length / Panel));
          var step = length / count;
          for (var k = 0; k < count; k++) {
            var p = line + along * (a0 + step * (k + 0.5f));
            var u = along * (step / 2f - Gap);
            Box(p, u, across * (BaseDepth / 2f), 0f, BaseHeight);
            Box(p, u, across * (Stem / 2f), BaseHeight, Height);
          }
          if (low == false) {
            foreach (var t in new[] { -2f, 2f }) BlockKit.Place(root, wire, line + along * t + Vector3.up * Height, horizontal ? 0f : 90f, 1f);
          }
          Placed["perimeter wall"] = (Placed.TryGetValue("perimeter wall", out var placed) ? placed : 0) + 1;
        }
      }
      var mesh = new Mesh { name = "pmc_perimeter_wall", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
      mesh.SetVertices(verts);
      mesh.SetUVs(0, uvs);
      mesh.SetTriangles(tris, 0);
      mesh.RecalculateNormals();
      mesh.RecalculateBounds();
      var go = new GameObject("PerimeterWall", typeof(MeshFilter), typeof(MeshRenderer));
      go.transform.SetParent(root, false);
      go.GetComponent<MeshFilter>().sharedMesh = mesh;
      go.GetComponent<MeshRenderer>().sharedMaterial = material;
      Debug.Log($"[FW-Camp] continuous wall: {tris.Count / 3} tris");
      GateIdentity(root);
    }

    /// <summary>Fix 5: gate gantry with the PMC banner (both faces) and a flag pole with the emblem flag inside the gate.</summary>
    static void GateIdentity(Transform root) {
      var charcoal = PmcIdentityKit.Mat("Charcoal", null, PmcIdentityKit.Charcoal);
      var banner = PmcIdentityKit.Mat("GateBanner", "gate_banner");
      var emblem = PmcIdentityKit.Mat("EmblemAmber", "emblem_amber");
      const float Z = -45.6f;
      foreach (var s in new[] { -1f, 1f }) PmcIdentityKit.Box(root, "GatePost", charcoal, new Vector3(s * 7.2f, 4.2f, Z), new Vector3(0.45f, 8.4f, 0.45f));
      PmcIdentityKit.Box(root, "GateBeam", charcoal, new Vector3(0f, 8.2f, Z), new Vector3(14.9f, 0.4f, 0.45f));
      PmcIdentityKit.Quad(root, "GateBanner", banner, new Vector3(0f, 6.1f, Z - 0.25f), Vector3.back, 7.6f, 3.8f);
      PmcIdentityKit.Quad(root, "GateBannerIn", banner, new Vector3(0f, 6.1f, Z + 0.25f), Vector3.forward, 7.6f, 3.8f);
      PmcIdentityKit.Box(root, "FlagPole", charcoal, new Vector3(-10.6f, 5f, -42.5f), new Vector3(0.18f, 10f, 0.18f));
      PmcIdentityKit.Box(root, "Flag", charcoal, new Vector3(-9.05f, 9f, -42.5f), new Vector3(2.9f, 1.8f, 0.04f));
      PmcIdentityKit.Quad(root, "FlagEmblem", emblem, new Vector3(-9.05f, 9f, -42.55f), Vector3.back, 1.6f, 1.6f);
      PmcIdentityKit.Quad(root, "FlagEmblemIn", emblem, new Vector3(-9.05f, 9f, -42.45f), Vector3.forward, 1.6f, 1.6f);
    }

    // ------------------------------------------------------------ MK_01 zones

    public static void Zones(Transform root, bool low = false) {
      // Centre: the Mercenary Trading Post, twice the old footprint (two tan office blocks side by side), counter canopy with crates.
      TradingPost(root);
      // North-west logistics: two warehouses facing the loop, ten dark crate/pallet stacks in two neat rows.
      Put(root, "logistics", MB("Plywood_Shed_01"), -9.5f, 23f, 180f);
      Put(root, "logistics", MB("Plywood_Shed_01"), -18.5f, 30.5f, 180f);
      var goods = new[] { MM("Crate_Stack_03"), MM("Crate_Stack_Cover_03"), MM("Crate_Stack_03"), BR("PalletStack_01") };
      for (var i = 0; i < 5 && low == false; i++) Blacken(Put(root, "logistics", goods[i % goods.Length], -17f + i * 3.2f, 35.8f, 0f, 0f, false, 0.5f));
      for (var i = 0; i < 5 && low == false; i++) Blacken(Put(root, "logistics", goods[(i + 2) % goods.Length], -15.5f + i * 3.2f, 39.3f, 0f, 0f, false, 0.5f));
      // One tidy container row (north).
      // Fix C: the two north containers were pack red; muted camp two-tone.
      Blacken(Put(root, "logistics", KP("Shipping_Container_01"), 9.5f, 43.5f, 90f, 0f, false, 0.5f), true);
      Blacken(Put(root, "logistics", KP("Shipping_Container_01"), 16.5f, 43.5f, 90f, 0f, false, 0.5f), true);
      // East barracks district: two barracks (north), six tents in two columns, two camo-net shelters, two crate yards, paths between.
      Put(root, "barracks", MB("Barracks_01"), 11f, 23f, 90f, 0f, false, 0.6f); // one barracks; the rest are tents (triangle budget)
      Put(root, "barracks", BB("Tent_02"), 11f, 33f, 90f, 0f, false, 0.6f); // fix 5: tan tents only (Tent_01 is green)
      foreach (var z in new[] { -12f, 12f }) {
        Put(root, "barracks", BB("Tent_02"), 25.5f, z, 0f, 0f, false, 0.6f);
        Put(root, "barracks", BB("Tent_02"), 40f, z, 0f, 0f, false, 0.6f);
      }
      Put(root, "barracks", BB("Tent_02"), 40f, 1f, 0f, 0f, false, 0.6f);
      Put(root, "barracks", BB("Tent_02"), 25.5f, 1f, 0f, 0f, false, 0.6f);
      Put(root, "barracks", MB("CamoNet_Tent_03"), 26.5f, 30.5f, 0f, 0f, false, 0.4f);
      Put(root, "barracks", MB("CamoNet_Tent_04"), 33f, -21f, 0f, 0f, false, 0.4f);
      var yard = new[] { MM("Crate_Stack_03"), MM("Crate_Stack_Cover_03"), MM("Crate_Stack_03") };
      for (var i = 0; i < 6 && low == false; i++) Blacken(Put(root, "crate yards", yard[i % yard.Length], 19f + (i % 3) * 2.6f, 37f + (i / 3) * 2.6f, 0f, 0f, false, 0.4f));
      for (var i = 0; i < 6 && low == false; i++) Blacken(Put(root, "crate yards", yard[(i + 1) % yard.Length], 36.5f + (i % 3) * 2.4f, 17.5f + (i / 3) * 2.4f, 0f, 0f, false, 0.4f));
      // South-east firing range: seven targets, Hesco backstop, shooting line barriers.
      for (var i = 0; i < 7; i++) Put(root, "range", BR("Target_01"), 10f + i * 3f, -36.5f, 0f, 0f, false, 0.4f);
      Put(root, "range", MM("Barrier_Base_Row_03"), 16f, -39.3f, 0f, 0f, false, 0.4f);
      for (var i = 0; i < 3; i++) Put(root, "range", MM("Barrier_Base_Single_01"), 10f + i * 6f, -26f, 0f, 0f, false, 0.4f);
      // Mechanics' corner by the display: generator and drums.

      if (low == false) Density(root);
      foreach (var line in Skipped) Debug.Log($"[FW-Camp] {line}");
      Debug.Log("[FW-Camp] placed: " + string.Join(", ", Placed.Select(kv => $"{kv.Key} {kv.Value}")));
    }

    /// <summary>Fix 4 density: maintenance yard, parked trucks, fuel drums, generator/antenna clusters, more crate rows.</summary>
    static void Density(Transform root) {
      // South yard maintenance area (between the loop and the firing range).
      Blacken(Put(root, "maintenance", PK + "Vehicles/SM_Veh_Truck_01.prefab", 23.5f, -19.5f, 90f, 0f, false, 0.4f));
      Put(root, "maintenance", MP("Generator_Large_01"), -6.2f, -20.5f, 0f, 0f, false, 0.3f);
      foreach (var (x, z) in new[] { (6f, -19f), (8.2f, -19f), (10.4f, -19.2f) }) Blacken(Put(root, "fuel drums", MM("Barrel_Stack_01"), x, z, 0f, 0f, false, 0.3f));
      foreach (var x in new[] { 6f, 9f, 12f, 15f }) Blacken(Put(root, "crate rows", MM("Crate_Stack_03"), x, -22.2f, 0f, 0f, false, 0.3f));
      // North-west antenna + generator cluster, north strip crate rows.
      Put(root, "antenna cluster", MP("Generator_Large_01"), -28.5f, 30f, 0f, 0f, false, 0.4f);
      Put(root, "antenna cluster", MP("Satellite_Dish_02"), -25.5f, 32.5f, 200f, 0f, false, 0.4f);
      Put(root, "antenna cluster", BR("RadioTower_01"), -26f, 36.5f, 0f, 0f, false, 0.4f);
      foreach (var x in new[] { -4.5f, -1.5f, 1.5f, 4.5f }) Blacken(Put(root, "crate rows", MM("Crate_Stack_03"), x, 18.6f, 0f, 0f, false, 0.3f));
      // East: drums by the tents, a second parked truck by the barracks.
      foreach (var (x, z) in new[] { (44f, -3f), (44f, 5f) }) Blacken(Put(root, "fuel drums", MM("Barrel_Stack_01"), x, z, 0f, 0f, false, 0.3f));
      // Fix 5: east lane between the tents and the wall - tidy supply rows and a generator.
      foreach (var z in new[] { -18f, -15f, 9f, 12f, 15f }) Blacken(Put(root, "east lane", MM("Crate_Stack_03"), 45f, z, 90f, 0f, false, 0.2f));
      Put(root, "east lane", MP("Generator_Large_01"), 45f, -8.5f, 90f, 0f, false, 0.2f);
      // Fix 5: north-west strip - supply rows behind the light helicopter and along the north wall.
      foreach (var (x, z) in new[] { (-42f, 27f), (-42f, 30f), (-39f, 30f), (-30f, 41.5f), (-27f, 41.5f), (-24f, 41.5f), (-21f, 43f),
        (-26f, 25.5f), (-23f, 25.5f), (-22.5f, 36f), (-25.5f, 39f), (-34.5f, 30.5f), (-37f, 31f), (-40f, 23f), (-44.5f, 22.5f) }) Blacken(Put(root, "nw strip", MM("Crate_Stack_03"), x, z, 0f, 0f, false, 0.2f));
      foreach (var (x, z) in new[] { (-36f, 33f), (-33.5f, 33f) }) Blacken(Put(root, "nw strip", MM("Barrel_Stack_01"), x, z, 0f, 0f, false, 0.2f));

    }

    static void TradingPost(Transform root) {
      var basePath = PC + "Buildings/SM_Bld_OfficeOld_Small_Base_01.prefab";
      var yaw = DressKit.FrontOf(basePath);
      var front = 1.5f;
      var height = 6f;
      foreach (var x in new[] { -5.45f, 5.45f }) {
        var y = 0f;
        var piece = BlockKit.Place(root, basePath, new Vector3(x, y, 1.5f), yaw, 1f);
        if (piece != null) {
          var fb = BlockKit.WorldBounds(piece);
          front = Mathf.Min(front, root.InverseTransformPoint(fb.min).z); // world bounds -> block-local (the block root is offset)
          height = fb.size.y;
        }
        y += piece != null ? BlockKit.WorldBounds(piece).size.y : 6f;
        BlockKit.Place(root, PC + "Buildings/SM_Bld_OfficeOld_Small_Roof_01.prefab", new Vector3(x, y, 1.5f), yaw, 1f);
        if (x > 0) BlockKit.Place(root, MP("Satellite_Dish_02"), new Vector3(x + 2f, y + 0.6f, 3f), 200f, 1f);
      }
      // Fix 5 front: PMC signboard (banner art) across the facade, black awnings with an amber valance, a counter of two tables
      // with the vendor behind it, crates being handed over.
      Debug.Log($"[FW-Camp] trading post front z {front:0.00}, ground floor height {height:0.00}");
      PmcIdentityKit.Quad(root, "PmcSign", PmcIdentityKit.Mat("GateBanner", "gate_banner"), new Vector3(0f, Mathf.Max(3.6f, height - 1.4f), front - 0.06f), Vector3.back, 6.4f, 3.2f);
      var amber = PmcIdentityKit.Mat("Amber", null, PmcIdentityKit.Amber);
      foreach (var x in new[] { -3.4f, 3.4f }) {
        var net = BlockKit.Place(root, MB("CamoNet_Tent_01"), new Vector3(x, 0f, -7.2f), 0f, 1f);
        // Fix C: the net's black/white blotches -> muted two-tone.
        Blacken(net, true);
        var nb = BlockKit.WorldBounds(net);
        PmcIdentityKit.Box(root, "AwningValance", amber, new Vector3(x, root.InverseTransformPoint(nb.max).y - 0.45f, root.InverseTransformPoint(nb.min).z - 0.02f), new Vector3(nb.size.x * 0.96f, 0.32f, 0.06f));
      }
      foreach (var x in new[] { -1.15f, 1.15f }) Blacken(BlockKit.Place(root, "Assets/ExternalAssets/Synty/PolygonGeneric/Prefabs/Props/SM_Gen_Prop_Table_01.prefab", new Vector3(x, 0f, -8.15f), 0f, 1f));
      Blacken(BlockKit.Place(root, BR("PalletStack_01"), new Vector3(-7.5f, 0f, -8.2f), 15f, 1f));
      Blacken(BlockKit.Place(root, MM("Crate_Stack_03"), new Vector3(7.5f, 0f, -8.2f), -10f, 1f));
      foreach (var (x, y, z) in new[] { (-1.6f, 0.72f, -8.15f), (0.9f, 0.72f, -8.15f), (-4.2f, 0f, -9.6f), (4.4f, 0f, -9.4f) }) Blacken(BlockKit.Place(root, MM("Crate_Ammo_01"), new Vector3(x, y, z), x * 7f, 1f));
      BlockKit.Place(root, BR("RadioTower_01"), new Vector3(-9.5f, 0f, 6.5f), 0f, 1f);
      Placed["trading post"] = 2;
    }
  }
}
