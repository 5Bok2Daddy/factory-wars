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
    public void Palette_BlackReservedForPmc() {
      foreach (var c in Palette.Colors) {
        var luminance = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        Assert.Greater(luminance, 80f, $"team colour {Palette.Hex(c)} must not be black/charcoal (reserved for the PMC)");
      }
      foreach (var id in new[] { "camp/display_tank", "camp/display_mrap", "camp/display_heli" }) {
        Assert.IsTrue(VisualCatalog.TryFilled(id, out var slot), id);
        var names = slot.Prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).ToList();
        Assert.IsTrue(names.Any(n => n == "Land_Vehicles_01" || n == "Helicopters_01_B"), $"{id} uses the pack's dark variant ({string.Join(",", names.Distinct())})");
      }
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Camp_ViewShowsGarrisonTroopsAndStock() {
      yield return StartMatch(Map4PScene, 2, truce: 0, stock: new ResourceCost { Iron = 5000, Wood = 5000, Steel = 2000, Power = 2000 });
      yield return WaitForHud();
      yield return WaitFor(() => CampView() != null && CampView().Built && CampView().GarrisonShown == MercSpec.GuardCount, 30f, "garrison soldiers shown");
      Assert.GreaterOrEqual(CampView().AmbientCount, 25, "ambient troops (patrols, joggers, range, sentries, mechanics, drill)");
      var shown = CampView().DisplayShown;
      Assert.AreEqual(12, shown, "display line full at start (4 tanks, 4 helicopters, 2 SUVs, 2 drones)");
      GrantAgreement(0);
      var tank = MercSpec.Offers.Select((o, i) => (o, i)).First(t => t.o.Unit.IsValid && Verified.FindAsset(t.o.Unit).name == "pmc_tank").i;
      yield return Send(0, new TradeCommand { Offer = MercenaryCamp.OfferShop + tank });
      Assert.AreEqual(1, UnitsNamed(0, "pmc_tank").Count, "bought PMC tank belongs to the buyer");
      yield return WaitFor(() => CampView().DisplayShown == shown - 1, 20f, "sold tank leaves the display line");
      var drone = MercSpec.Offers.Select((o, i) => (o, i)).First(t => t.o.Unit.IsValid && Verified.FindAsset(t.o.Unit).name == "pmc_attack_drone").i;
      yield return Send(0, new TradeCommand { Offer = MercenaryCamp.OfferShop + drone });
      var bought = UnitsNamed(0, "pmc_attack_drone");
      Assert.AreEqual(1, bought.Count, "attack drone bought");
      Assert.IsTrue(Verified.FindAsset(Verified.Get<Unit>(bought[0]).Spec).IsAir, "attack drone is a real air unit");
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
          .Where(r => r.enabled && r.gameObject.activeInHierarchy && (r is MeshRenderer || r is SkinnedMeshRenderer) && GeometryUtility.TestPlanesAABB(planes, r.bounds)).ToList();
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
        foreach (var g in inView.GroupBy(Group).Select(g => (g.Key, Draws: g.Sum(r => r.sharedMaterials.Length) + g.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off))).OrderByDescending(g => g.Draws).Take(10)) {
          report.Add($"  {g.Key}: {g.Draws}");
        }
      }
      yield return Shot("camp_wide", new Vector2(0f, -5f), 132f);
      yield return Shot("camp_gate", new Vector2(0f, -38f), 42f);
      yield return Shot("camp_display", new Vector2(-28f, 0f), 58f);
      yield return Shot("camp_training", new Vector2(22f, -6f), 58f);
      // Garrison sortie: an intruder without an agreement inside the fence.
      SpawnFor(1, "cf_dragonscale_tank", Pos(0f, 30f));
      yield return WaitFrames(3 * Fps);
      yield return Shot("camp_sortie", new Vector2(0f, 22f), 48f);
      report.Add($"camp view: garrison {CampView()?.GarrisonShown} ambient {CampView()?.AmbientCount} display {CampView()?.DisplayShown}");
      File.WriteAllLines(Path.Combine(dir, $"perf_{iteration}.txt"), report);
      Debug.Log(string.Join("\n", report));
    }

    [UnityTest, Explicit, Timeout(600000)]
    public IEnumerator Drawbridge_CaptureRaised() {
      var dir = T39Root();
      Directory.CreateDirectory(dir);
      yield return StartMatch(Map4PScene, 2, truce: 0, units: new[] { (0, "ff_artillery_ship", -16, 146) });
      yield return WaitForHud();
      yield return WaitForBattle();
      var index = Map4PSpec.Drawbridges[0];
      yield return WaitFor(() => BridgeState(index).Lift == BridgeTraffic.Up, 60f, "raised for the artillery ship");
      var ship = UnitsNamed(0, "ff_artillery_ship").First();
      yield return Send(0, new MoveUnitsCommand { Units = new[] { ship }, Target = Pos(-16f, 104f) });
      Time.timeScale = 1f;
      yield return WaitFrames(3 * Fps);
      var rts = Controller().Camera;
      Camera.main.aspect = 16f / 9f;
      rts.MoveTo(new Vector2(-16f, 118f));
      rts.Height = 46f;
      rts.Apply();
      yield return WaitFrames(Fps / 2);
      Assert.Greater(Object.FindAnyObjectByType<BattleView>().Drawbridges.LeafAngle(0), 40f, "leaves are up on screen");
      CaptureCameraPng(Camera.main, Path.Combine(dir, "drawbridge_raised.png"), 1920, 1080);
    }
  }
}
