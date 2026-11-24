namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// Task 39 card 4 (2026-11-23): the PMC camp look. Garrison visible, ambient troops, display line follows the shop stock, PMC goods
  /// are their own units (attack drone is a real air unit), black is reserved for the PMC (no team colour is dark).
  /// Captures: Camp_CaptureT39 (explicit) writes screenshots/t39/*_{FW_MAP_ITER}.png and a draw-call/triangle table.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static EntityRef SpawnFor(int owner, string id, FPVector3 p) => ProductionSystem.SpawnUnitAt(Verified, owner, SpecRef(id), p, null);

    static MercCampView CampView() => Object.FindAnyObjectByType<BattleView>()?.MercCamp;

    [Test]
    public void Pmc_IdentityKitApplied() {
      // Fix 5: Codex's PMC kit (art-staging/t39) is imported: shop icons + state overlays in UiCatalog, decals on the camp blocks.
      foreach (var id in new[] { "pmc_operator_squad", "pmc_marksman", "pmc_armoured_suv", "pmc_attack_drone", "pmc_tank", "pmc_supply_trade", "pmc_diplomacy", "pmc_cargo_truck", "pmc_state_locked", "pmc_state_sold_out", "pmc_state_cooldown" }) {
        Assert.IsNotNull(UiCatalog.Current.Get(id)?.Sprite, $"UiCatalog slot {id} has the PMC icon");
      }
      var mats = new System.Collections.Generic.List<string>();
      foreach (var id in new[] { GridDress.Prefix + "MK_00", GridDress.Prefix + "MK_01", "camp/display_tank", "camp/display_mrap" }) {
        Assert.IsTrue(VisualCatalog.TryFilled(id, out var slot), id);
        foreach (var r in slot.Prefab.GetComponentsInChildren<Renderer>()) mats.AddRange(r.sharedMaterials.Where(m => m != null).Select(m => m.name));
      }
      foreach (var decal in new[] { "PMC_GateBanner", "PMC_EmblemAmber", "PMC_SideStripe", "PMC_Amber" }) Assert.Contains(decal, mats, $"{decal} is used in the camp");
    }

    [Test]
    public void Palette_BlackReservedForPmc() {
      foreach (var c in Palette.Colors) {
        var luminance = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        Assert.Greater(luminance, 80f, $"team colour {Palette.Hex(c)} must not be black/charcoal (reserved for the PMC)");
      }
      foreach (var id in new[] { "camp/display_tank", "camp/display_mrap", "camp/display_heli" }) {
        Assert.IsTrue(VisualCatalog.TryFilled(id, out var slot), id);
        var names = slot.Prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).ToList();
        Assert.IsTrue(names.Count > 0 && names.All(n => n.StartsWith("PMC_") || n.Contains("Glass")), $"{id} uses our PMC black materials ({string.Join(",", names.Distinct())})");
      }
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Camp_ViewShowsGarrisonTroopsAndStock() {
      yield return StartMatch(Map4PScene, 2, truce: 0, stock: new ResourceCost { Iron = 5000, Wood = 5000, Steel = 2000, Power = 2000 });
      yield return WaitForHud();
      yield return WaitFor(() => CampView() != null && CampView().Built && CampView().GarrisonShown == MercSpec.GuardCount, 30f, "garrison soldiers shown");
      Assert.GreaterOrEqual(CampView().AmbientCount + CampView().GarrisonShown, 30, "at least 30 PMC figures (patrols, drill, range, guards, mechanics, counter, garrison)");
      Assert.GreaterOrEqual(CampView().TroopGap, MercCampView.MinTroopGap - 0.01f, $"camp troops must stay at least {MercCampView.MinTroopGap:0.0} m apart (min gap {CampView().TroopGap:0.00} m)");
      var shown = CampView().DisplayShown;
      Assert.AreEqual(16, shown, "display line full at start (6 tanks, 3 attack + 1 light helicopter at real scale, 4 APCs, 2 drones)");
      GrantAgreement(0);
      var tank = MercSpec.Offers.Select((o, i) => (o, i)).First(t => t.o.Unit.IsValid && Verified.FindAsset(t.o.Unit).name == "pmc_tank").i;
      yield return Send(0, new TradeCommand { Offer = MercenaryCamp.OfferShop + tank });
      Assert.AreEqual(1, UnitsNamed(0, "pmc_tank").Count, "bought PMC tank belongs to the buyer");
      yield return WaitFor(() => CampView().DisplayShown == shown - 1, 20f, "sold tank leaves the display line");
      var droneOp = MercSpec.Offers.Select((o, i) => (o, i)).First(t => t.o.Unit.IsValid && Verified.FindAsset(t.o.Unit).name == "pmc_drone_operator").i;
      yield return Send(0, new TradeCommand { Offer = MercenaryCamp.OfferShop + droneOp });
      var bought = UnitsNamed(0, "pmc_drone_operator");
      Assert.AreEqual(1, bought.Count, "drone operator bought");
      Assert.IsTrue(Verified.FindAsset(Verified.Get<Unit>(bought[0]).Spec).OperatorDrone.IsValid, "drone operator carries strike drone spec");
    }

    /// <summary>Task 39 fix 3: real-scale table (infantry 1.8 m reference) measured from the slots as shown in the camp / as units.</summary>
    static List<string> ScaleTable() {
      var rows = new List<string> { "scale | slot | shown length m | shown height m | real target m" };
      foreach (var (id, target) in new[] { ("unit/pmc_soldier", "1.8 tall"), ("camp/display_tank", "9-10"), ("camp/display_mrap", "7-8"), ("camp/display_heli", "15-17"), ("camp/display_scoutheli", "8-9"), ("camp/display_drone", "2-3"), ("unit/merc_cargo_truck", "7-8") }) {
        if (VisualCatalog.TryFilled(id, out var slot) == false) { rows.Add($"scale | {id} | missing"); continue; }
        var go = Object.Instantiate(slot.Prefab);
        go.transform.localScale = Vector3.one * (slot.Scale > 0f ? slot.Scale : 1f);
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        rows.Add($"scale | {id} | {Mathf.Max(b.size.x, b.size.z):0.0} | {b.size.y:0.0} | {target}");
        Object.Destroy(go);
      }
      return rows;
    }

    /// <summary>
    /// Fix 6 colour audit (replaces fix 4's "luminance < 0.30"): inside each display vehicle's screen box, the model pixels (darker than
    /// the light concrete) must have (a) mean luminance in the PMC dark band 0.10-0.38 and (b) internal contrast p90-p10 >= 0.08 so
    /// parts stay distinguishable (fix 5's flat black failed this).
    /// </summary>
    static List<string> ColourAudit(Camera camera, string png) {
      var rows = new List<string>();
      var tex = new Texture2D(2, 2);
      tex.LoadImage(File.ReadAllBytes(png));
      var pass = 0;
      var total = 0;
      foreach (var (view, kind) in CampView().Displays) {
        var lums = ModelPixels(camera, tex, view, 0.55f).Select(c => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b).OrderBy(l => l).ToList();
        if (lums.Count < 40) continue;
        var mean = lums.Average();
        var spread = lums[(int)(lums.Count * 0.9f)] - lums[(int)(lums.Count * 0.1f)];
        var ok = mean >= 0.10f && mean <= 0.38f && spread >= 0.08f;
        total++;
        if (ok) pass++;
        rows.Add($"  colour | {kind} at {view.position.x:0},{view.position.z:0} | mean {mean:0.00} spread {spread:0.00} {(ok ? "PASS" : "FAIL")}");
      }
      rows.Insert(0, $"colour audit (display vehicles, dark band 0.10-0.38 + contrast >= 0.08): {pass}/{total}");
      Object.Destroy(tex);
      return rows;
    }

    /// <summary>Pixels of the screenshot inside the projected bounds of <paramref name="view"/>, darker than <paramref name="maxLum"/>.</summary>
    static List<Color> ModelPixels(Camera camera, Texture2D tex, Transform view, float maxLum) {
      var list = new List<Color>();
      var rs = view.GetComponentsInChildren<Renderer>();
      if (rs.Length == 0) return list;
      var b = rs[0].bounds;
      foreach (var r in rs) b.Encapsulate(r.bounds);
      var min = new Vector2(float.MaxValue, float.MaxValue);
      var max = new Vector2(float.MinValue, float.MinValue);
      for (var k = 0; k < 8; k++) {
        var corner = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
        var sp = camera.WorldToViewportPoint(corner);
        if (sp.z <= 0f) return list;
        min = Vector2.Min(min, sp);
        max = Vector2.Max(max, sp);
      }
      // Skip vehicles clipped outside the viewport (otherwise the box samples mostly ground around an edge sliver).
      if (min.x < 0.01f || max.x > 0.99f || min.y < 0.01f || max.y > 0.99f) return list;
      // Inner 60 % of the box (the outer ring is mostly ground around the silhouette).
      var c0 = Vector2.Lerp(min, max, 0.2f);
      var c1 = Vector2.Lerp(min, max, 0.8f);
      int x0 = Mathf.Clamp((int)(c0.x * tex.width), 0, tex.width - 1), x1 = Mathf.Clamp((int)(c1.x * tex.width), 0, tex.width - 1);
      int y0 = Mathf.Clamp((int)(c0.y * tex.height), 0, tex.height - 1), y1 = Mathf.Clamp((int)(c1.y * tex.height), 0, tex.height - 1);
      for (var x = x0; x <= x1; x += 2) for (var y = y0; y <= y1; y += 2) {
        var c = tex.GetPixel(x, y);
        if (0.299f * c.r + 0.587f * c.g + 0.114f * c.b < maxLum) list.Add(c);
      }
      return list;
    }

    /// <summary>Fix 6 (c): skin-tone pixels on the troops in a close capture (faces must read as people, not shadows).</summary>
    static string SkinAudit(Camera camera, string png) {
      var tex = new Texture2D(2, 2);
      tex.LoadImage(File.ReadAllBytes(png));
      var troops = 0;
      var withSkin = 0;
      foreach (var t in CampView().Troops) {
        if (t == null || t.gameObject.activeInHierarchy == false) continue;
        var vp = camera.WorldToViewportPoint(t.position + Vector3.up);
        if (vp.z <= 0f || vp.x < 0.05f || vp.x > 0.95f || vp.y < 0.05f || vp.y > 0.95f) continue;
        troops++;
        var skin = ModelPixels(camera, tex, t, 0.95f).Count(c => {
          Color.RGBToHSV(c, out var h, out var sat, out var v);
          return h * 360f >= 8f && h * 360f <= 40f && sat > 0.18f && sat < 0.65f && v > 0.35f;
        });
        if (skin > 0) withSkin++;
      }
      Object.Destroy(tex);
      return $"skin audit (troops in the close capture with skin-tone pixels): {withSkin}/{troops} {(troops > 0 && withSkin * 2 >= troops ? "PASS" : "FAIL")}";
    }

    static int VisibleTroops(Camera camera) {
      var planes = GeometryUtility.CalculateFrustumPlanes(camera);
      var n = 0;
      foreach (var t in CampView().Troops) {
        if (t == null || t.gameObject.activeInHierarchy == false) continue;
        if (GeometryUtility.TestPlanesAABB(planes, new Bounds(t.position + Vector3.up, Vector3.one))) n++;
      }
      return n;
    }

    static GameObject ScaleLineup(Vector3 origin) {
      var root = new GameObject("ScaleLineup");
      var x = origin.x;
      foreach (var id in new[] { "camp/display_tank", "camp/display_mrap", "camp/display_heli", "camp/display_scoutheli", "camp/display_drone", "unit/merc_cargo_truck" }) {
        if (VisualCatalog.TryFilled(id, out var slot) == false) continue;
        var go = Object.Instantiate(slot.Prefab, root.transform);
        go.transform.localScale = Vector3.one * (slot.Scale > 0f ? slot.Scale : 1f);
        go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        go.transform.position += new Vector3(x - b.min.x, origin.y - b.min.y, origin.z - b.center.z);
        x += b.size.x + 1f;
        if (VisualCatalog.TryFilled("unit/pmc_soldier", out var man)) {
          var trooper = Object.Instantiate(man.Prefab, root.transform);
          trooper.transform.position = new Vector3(x, origin.y, origin.z - 1.5f);
          trooper.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        }
        x += 2.5f;
      }
      return root;
    }

    static string T39Root() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "t39"));

    [UnityTest, Explicit, Timeout(900000)]
    public IEnumerator Camp_CaptureT39() {
      var iteration = System.Environment.GetEnvironmentVariable("FW_MAP_ITER");
      iteration = string.IsNullOrEmpty(iteration) ? "r1" : iteration;
      var dir = T39Root();
      Directory.CreateDirectory(dir);
      yield return StartMatch(Map4PScene, 2, truce: 0, stock: new ResourceCost { Iron = 5000, Wood = 5000, Steel = 2000, Power = 2000 });
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      Time.timeScale = 1f;
      yield return WaitFrames(2 * Fps);
      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;
      var report = new List<string> { $"Task 39 camp captures {iteration}", "view | height | renderers in view | draw calls (renderer x material + shadow casters) | triangles" };
      IEnumerator Shot(string name, Vector2 focus, float height) {
        rts.MoveTo(focus);
        rts.Height = height;
        rts.Apply();
        yield return WaitFrames(Fps / 2);
        CaptureCameraPng(camera, Path.Combine(dir, $"{name}_{iteration}.png"), 1920, 1080);
        var planes = GeometryUtility.CalculateFrustumPlanes(camera);
        var inView = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
          .Where(r => r.enabled && r.gameObject.activeInHierarchy && (r is MeshRenderer || r is SkinnedMeshRenderer) && GeometryUtility.TestPlanesAABB(planes, r.bounds))
          .Where(r => {
            // Same LOD test as the Map4P audit: a renderer whose LOD 0 is below its transition height is not drawn.
            var group = r.GetComponentInParent<LODGroup>();
            if (group == null) return true;
            var tan = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var relative = group.size * group.transform.lossyScale.x / (2f * Vector3.Distance(camera.transform.position, r.bounds.center) * tan) * QualitySettings.lodBias;
            return relative >= group.GetLODs()[0].screenRelativeTransitionHeight;
          }).ToList();
        var draws = inView.Sum(r => r.sharedMaterials.Length) + inView.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off);
        long tris = 0;
        foreach (var r in inView) {
          var mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
          if (mesh == null) continue;
          for (var i = 0; i < mesh.subMeshCount; i++) tris += mesh.GetIndexCount(i) / 3;
        }
        report.Add($"{name} | {height:0}m | {inView.Count} | {draws} | {tris}");
        string Group(Renderer r) {
          var t = r.transform;
          while (t.parent != null && t.parent.parent != null && t.parent.name != "GridDress" && t.parent.name != "MercCamp" && t.parent.name != "Drawbridges") t = t.parent;
          var n = t.parent != null ? t.parent.name + "/" : "";
          var cut = t.name.IndexOfAny(new[] { '_', '@', '(' });
          return n + (cut > 0 ? t.name.Substring(0, cut) : t.name);
        }
        long Tris(Renderer r) {
          var m = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
          long n = 0;
          for (var i = 0; m != null && i < m.subMeshCount; i++) n += m.GetIndexCount(i) / 3;
          return n;
        }
        foreach (var g in inView.GroupBy(Group).Select(g => (g.Key, Draws: g.Sum(r => r.sharedMaterials.Length) + g.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off), Tris: g.Sum(Tris))).OrderByDescending(g => g.Tris).Take(12)) {
          report.Add($"  {g.Key}: {g.Draws} draws, {g.Tris} tris");
        }
      }
      yield return Shot("camp_wide", new Vector2(0f, -5f), 132f);
      report.Add($"troops visible in the wide view: {VisibleTroops(camera)}");
      yield return Shot("camp_display", new Vector2(-30f, 0f), 58f);
      report.AddRange(ColourAudit(camera, Path.Combine(dir, $"camp_display_{iteration}.png")));
      // Fix 6 close-ups: tanks, helicopter (canopy/rotor), APC row, troops at the counter (faces), crate stacks.
      yield return Shot("close_tanks", new Vector2(-40f, 3f), 22f);
      report.AddRange(ColourAudit(camera, Path.Combine(dir, $"close_tanks_{iteration}.png")));
      yield return Shot("close_heli", new Vector2(-27f, -2f), 20f);
      yield return Shot("close_apc", new Vector2(-17f, -31f), 18f);
      report.AddRange(ColourAudit(camera, Path.Combine(dir, $"close_apc_{iteration}.png")));
      yield return Shot("close_troops", new Vector2(0f, -10f), 11f);
      report.Add(SkinAudit(camera, Path.Combine(dir, $"close_troops_{iteration}.png")));
      yield return Shot("close_crates", new Vector2(10.5f, -21f), 14f);
      report.Add($"troop overlap audit: min gap {CampView().TroopGap:0.00} m (limit {MercCampView.MinTroopGap:0.0} m) {(CampView().TroopGap >= MercCampView.MinTroopGap - 0.01f ? "PASS" : "FAIL")}, separated {CampView().TroopOverlaps}");
      yield return Shot("camp_gate", new Vector2(0f, -38f), 34f);
      // Scale line-up: one PMC trooper next to each vehicle at real scale on the open north ring road.
      // On the main east-west road (row 24, z = 124), clear of buildings.
      var lineup = ScaleLineup(new Vector3(104f, 0f, 124f));
      Object.Destroy(lineup); // fix 6: line-up shot dropped (fix 5 accepted it); the close-ups above replace it
      report.Add($"camp view: garrison {CampView()?.GarrisonShown} ambient {CampView()?.AmbientCount} display {CampView()?.DisplayShown}");
      report.AddRange(ScaleTable());
      File.WriteAllLines(Path.Combine(dir, $"perf_{iteration}.txt"), report);
      Debug.Log(string.Join("\n", report));
    }

    [UnityTest, Explicit, Timeout(600000)]
    public IEnumerator Bridge_CapturePassUnder() {
      var dir = T39Root();
      Directory.CreateDirectory(dir);
      // Old drawbridge 0 position (row-24 bridge, centre -16, 124): an artillery ship sails under, two tanks drive across.
      yield return StartMatch(Map4PScene, 2, truce: 0, units: new[] { (0, "ff_artillery_ship", -16, 146), (0, "ff_eagle_tank", -46, 124), (0, "ff_eagle_tank", -50, 124) });
      yield return WaitForHud();
      yield return WaitForBattle();
      Assert.Greater(Object.FindAnyObjectByType<BattleView>().ArchBridges.Count, 0, "arch bridges built");
      var ship = UnitsNamed(0, "ff_artillery_ship").First();
      var tanks = UnitsNamed(0, "ff_eagle_tank").Where(u => Verified.Get<Transform3D>(u).Position.Z.AsFloat > 100f).ToArray();
      yield return Send(0, new MoveUnitsCommand { Units = new[] { ship }, Target = Pos(-16f, 100f) });
      yield return Send(0, new MoveUnitsCommand { Units = tanks, Target = Pos(18f, 124f) });
      Time.timeScale = 1f;
      yield return WaitFor(() => Mathf.Abs(Verified.Get<Transform3D>(ship).Position.Z.AsFloat - 124f) < 3f, 60f, "ship under the deck");
      var rts = Controller().Camera;
      Camera.main.aspect = 16f / 9f;
      foreach (var (name, focus, height) in new[] { ("bridge_pass_under", new Vector2(-16f, 112f), 30f), ("bridge_top", new Vector2(-16f, 124f), 62f) }) {
        rts.MoveTo(focus);
        rts.Height = height;
        rts.Apply();
        yield return WaitFrames(Fps / 4);
        CaptureCameraPng(Camera.main, Path.Combine(dir, name + ".png"), 1920, 1080);
      }
      yield return WaitFor(() => tanks.Any(t => Mathf.Abs(Verified.Get<Transform3D>(t).Position.X.AsFloat + 16f) < 8f), 60f, "tanks on the deck");
      rts.MoveTo(new Vector2(-16f, 118f));
      rts.Height = 40f;
      rts.Apply();
      yield return WaitFrames(Fps / 4);
      CaptureCameraPng(Camera.main, Path.Combine(dir, "bridge_vehicles.png"), 1920, 1080);
    }
  }
}
