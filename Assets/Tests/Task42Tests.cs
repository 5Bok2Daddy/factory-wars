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
  /// Task 42 (2026-11-24): Modern-warfare combined-arms roster expansion (17 units: 8 FF + 9 CF).
  /// Verifies specs, production buildings, VisualCatalog slots, loitering-munition self-destruct,
  /// faction drone operators, determinism, and captures showcase screenshots.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static readonly string[] Task42Ids = {
      "ff_striker_ifv", "cf_rampart_ifv",
      "ff_vanguard_mrap", "cf_ironhide_mrap",
      "ff_paladin_sph", "cf_bastion_sph",
      "ff_aegis_ew", "cf_shroud_ew",
      "cf_field_recovery",
      "ff_recon_drone", "cf_recon_drone",
      "ff_loiter_munition", "cf_loiter_munition",
      "ff_drone_operator", "cf_drone_operator",
      "ff_antidrone_gunner", "cf_antidrone_gunner",
    };

    static string T42Dir() =>
      Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "T42"));

    static FP UnitHealth(Frame frame, EntityRef entity) =>
      frame.Exists(entity) && frame.TryGet<Unit>(entity, out var u) ? u.Health : FP._0;

    [UnityTest, Timeout(600000)]
    public IEnumerator T42_All17UnitsExistProducibleAndSlotted() {
      yield return StartMatch(Map4PScene, 2, truce: 0, startingTier: 3);
      var catalog = Resources.Load<VisualCatalog>(VisualCatalog.ResourcePath);
      Assert.IsNotNull(catalog, "VisualCatalog loaded");
      var ffFaction = Resources.Load<FactionSpec>("FactoryWars/faction_free_federation");
      var cfFaction = Resources.Load<FactionSpec>("FactoryWars/faction_continental_federation");
      Assert.IsNotNull(ffFaction, "FF faction spec");
      Assert.IsNotNull(cfFaction, "CF faction spec");

      foreach (var id in Task42Ids) {
        var spec = Resources.Load<UnitSpec>($"FactoryWars/{id}");
        Assert.IsNotNull(spec, $"UnitSpec missing: {id}");
        var faction = id.StartsWith("ff_") ? ffFaction : cfFaction;
        Assert.IsTrue(faction.Units.Any(r => r.Id == spec.Guid), $"{id} must be listed in FactionSpec.Units");
        var slot = catalog.Slots.Find(s => s != null && s.Id == "unit/" + id);
        Assert.IsNotNull(slot, $"VisualCatalog slot missing: unit/{id}");
        Assert.IsNotNull(slot.Prefab, $"VisualCatalog prefab null: unit/{id}");
        Assert.AreNotEqual(id, Texts.NameOf(id), $"Korean display name missing in Texts.cs: {id}");
      }
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator T42_LoiteringMunition_SelfDestructsOnStrike() {
      yield return StartMatch(Map4PScene, 2, truce: 0, startingTier: 3);
      yield return WaitForBattle();
      var target = SpawnUnitByName(Verified, 1, "cf_bastion_sph", new FPVector3(12, 0, -64));
      var loiter = SpawnUnitByName(Verified, 0, "ff_loiter_munition", new FPVector3(0, 0, -64));
      var startHp = UnitHealth(Verified, target);
      yield return Send(0, new AttackTargetCommand { Units = new[] { loiter }, Target = target });
      Time.timeScale = 4f;
      yield return WaitFor(() => UnitHealth(Verified, target) < startHp && UnitHealth(Verified, loiter) <= FP._0,
        30f, "loitering munition damages target and self-destructs");
      Assert.Less(UnitHealth(Verified, target).AsFloat, startHp.AsFloat, "SPH took loitering munition strike damage");
      Assert.LessOrEqual(UnitHealth(Verified, loiter).AsFloat, 0f, "loitering munition self-destructed on impact");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator T42_FactionDroneOperator_LaunchesDrones() {
      yield return StartMatch(Map4PScene, 2, truce: 0, startingTier: 3);
      yield return WaitForBattle();
      var ffOp = SpawnUnitByName(Verified, 0, "ff_drone_operator", new FPVector3(-6, 0, -64));
      var cfOp = SpawnUnitByName(Verified, 1, "cf_drone_operator", new FPVector3(6, 0, -64));
      Time.timeScale = 4f;
      yield return WaitFor(() => OperatorDronesOf(Verified, ffOp).Count >= 1 && OperatorDronesOf(Verified, cfOp).Count >= 1,
        30f, "both FF and CF drone operators launch autonomous strike drones");
      Assert.GreaterOrEqual(OperatorDronesOf(Verified, ffOp).Count, 1);
      Assert.GreaterOrEqual(OperatorDronesOf(Verified, cfOp).Count, 1);
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator T42_CounterRoles_Deterministic() {
      var checksums = new ulong[2];
      for (var run = 0; run < 2; run++) {
        FixedStep = true;
        Restores.Add(() => FixedStep = false);
        yield return StartMatch(Map4PScene, 2, truce: 0, seed: 4242, startingTier: 3);
        yield return WaitForBattle();
        var ff = new[] {
          SpawnUnitByName(Verified, 0, "ff_striker_ifv", new FPVector3(-14, 0, -64)),
          SpawnUnitByName(Verified, 0, "ff_aegis_ew", new FPVector3(-12, 0, -60)),
          SpawnUnitByName(Verified, 0, "ff_antidrone_gunner", new FPVector3(-10, 0, -68)),
          SpawnUnitByName(Verified, 0, "ff_loiter_munition", new FPVector3(-8, 0, -64)),
        };
        var cf = new[] {
          SpawnUnitByName(Verified, 1, "cf_rampart_ifv", new FPVector3(14, 0, -64)),
          SpawnUnitByName(Verified, 1, "cf_shroud_ew", new FPVector3(12, 0, -60)),
          SpawnUnitByName(Verified, 1, "cf_bastion_sph", new FPVector3(16, 0, -68)),
          SpawnUnitByName(Verified, 1, "cf_drone_operator", new FPVector3(10, 0, -64)),
        };
        yield return Send(0, new AttackTargetCommand { Units = ff, Target = cf[0] });
        yield return Send(1, new AttackTargetCommand { Units = cf, Target = ff[0] });
        yield return WaitFrames(Fps * 6);
        checksums[run] = Verified.CalculateChecksum();
        TearDown();
        yield return null;
      }
      Assert.AreEqual(checksums[0], checksums[1], "Task 42 combined-arms skirmish must be 100% deterministic");
    }

    [UnityTest, Explicit, Timeout(1200000)]
    public IEnumerator T42_CaptureModernRosterShowcase() {
      var dir = T42Dir();
      Directory.CreateDirectory(dir);
      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 4299, startingTier: 3);
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      GrantAgreement(0);
      GrantAgreement(1);
      Time.timeScale = 1f;

      var camera = Camera.main;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;

      // 1. FF 8 new Task 42 units on wide south ring road (active march/hover).
      var ffOrigin = new FPVector3(0, 0, -64);
      var ffRow = new[] {
        SpawnUnitByName(Verified, 0, "ff_striker_ifv",      ffOrigin + new FPVector3(-12, 0, -3)),
        SpawnUnitByName(Verified, 0, "ff_vanguard_mrap",    ffOrigin + new FPVector3( -4, 0, -3)),
        SpawnUnitByName(Verified, 0, "ff_paladin_sph",      ffOrigin + new FPVector3(  4, 0, -3)),
        SpawnUnitByName(Verified, 0, "ff_aegis_ew",         ffOrigin + new FPVector3( 12, 0, -3)),
        SpawnUnitByName(Verified, 0, "ff_drone_operator",   ffOrigin + new FPVector3( -5, 0,  3)),
        SpawnUnitByName(Verified, 0, "ff_antidrone_gunner", ffOrigin + new FPVector3(  3, 0,  3)),
        SpawnUnitByName(Verified, 0, "ff_recon_drone",      ffOrigin + new FPVector3( -6, 0,  1)),
        SpawnUnitByName(Verified, 0, "ff_loiter_munition",  ffOrigin + new FPVector3(  6, 0,  1)),
      };
      yield return Send(0, new MoveUnitsCommand { Units = ffRow, Target = ffOrigin + new FPVector3(24, 0, 0) });
      yield return WaitFrames(Fps);
      var ffFocus = UnityPos(Verified, ffRow[2]);
      rts.Height = 32f;
      rts.MoveTo(new Vector2(ffFocus.x - 2f, ffFocus.z));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "t42_ff_combined_arms.png"), 1920, 1080);

      // 2. CF 9 new Task 42 units on wide north ring road (revealed by overhead FF recon drone).
      var cfOrigin = new FPVector3(0, 0, 64);
      SpawnUnitByName(Verified, 0, "ff_recon_drone", cfOrigin + new FPVector3(0, 0, -14)); // Reveals CF units through fog of war
      var cfRow = new[] {
        SpawnUnitByName(Verified, 1, "cf_rampart_ifv",      cfOrigin + new FPVector3(-16, 0, -3)),
        SpawnUnitByName(Verified, 1, "cf_ironhide_mrap",    cfOrigin + new FPVector3( -8, 0, -3)),
        SpawnUnitByName(Verified, 1, "cf_bastion_sph",      cfOrigin + new FPVector3(  0, 0, -3)),
        SpawnUnitByName(Verified, 1, "cf_shroud_ew",        cfOrigin + new FPVector3(  8, 0, -3)),
        SpawnUnitByName(Verified, 1, "cf_field_recovery",   cfOrigin + new FPVector3( 16, 0, -3)),
        SpawnUnitByName(Verified, 1, "cf_drone_operator",   cfOrigin + new FPVector3( -5, 0,  3)),
        SpawnUnitByName(Verified, 1, "cf_antidrone_gunner", cfOrigin + new FPVector3(  3, 0,  3)),
        SpawnUnitByName(Verified, 1, "cf_recon_drone",      cfOrigin + new FPVector3( -6, 0,  1)),
        SpawnUnitByName(Verified, 1, "cf_loiter_munition",  cfOrigin + new FPVector3(  6, 0,  1)),
      };
      yield return Send(1, new MoveUnitsCommand { Units = cfRow, Target = cfOrigin + new FPVector3(-24, 0, 0) });
      yield return WaitFrames(Fps);
      var cfFocus = UnityPos(Verified, cfRow[2]);
      rts.Height = 36f;
      rts.MoveTo(new Vector2(cfFocus.x, cfFocus.z));
      rts.Apply();
      yield return WaitFrames(6);
      CaptureCameraPng(camera, Path.Combine(dir, "t42_cf_combined_arms.png"), 1920, 1080);

      // 3. Combined-arms skirmish on west boulevard (clear of PMC camp & previous rows).
      var skCenter = new FPVector3(-64, 0, 0);
      var ffSk = new[] {
        SpawnUnitByName(Verified, 0, "ff_striker_ifv",      skCenter + new FPVector3(-3, 0, -14)),
        SpawnUnitByName(Verified, 0, "ff_paladin_sph",      skCenter + new FPVector3( 4, 0, -18)),
        SpawnUnitByName(Verified, 0, "ff_aegis_ew",         skCenter + new FPVector3(-4, 0, -10)),
        SpawnUnitByName(Verified, 0, "ff_drone_operator",   skCenter + new FPVector3( 2, 0,  -8)),
        SpawnUnitByName(Verified, 0, "ff_antidrone_gunner", skCenter + new FPVector3(-2, 0,  -8)),
        SpawnUnitByName(Verified, 0, "ff_loiter_munition",  skCenter + new FPVector3( 0, 0,  -5)),
      };
      var cfSk = new[] {
        SpawnUnitByName(Verified, 1, "cf_rampart_ifv",      skCenter + new FPVector3(-3, 0,  12)),
        SpawnUnitByName(Verified, 1, "cf_bastion_sph",      skCenter + new FPVector3( 4, 0,  16)),
        SpawnUnitByName(Verified, 1, "cf_shroud_ew",        skCenter + new FPVector3(-4, 0,   8)),
        SpawnUnitByName(Verified, 1, "cf_field_recovery",   skCenter + new FPVector3( 5, 0,  10)),
        SpawnUnitByName(Verified, 1, "cf_drone_operator",   skCenter + new FPVector3( 2, 0,   6)),
        SpawnUnitByName(Verified, 1, "cf_antidrone_gunner", skCenter + new FPVector3(-2, 0,   6)),
      };
      yield return Send(0, new AttackTargetCommand { Units = ffSk, Target = cfSk[0] });
      yield return Send(1, new AttackTargetCommand { Units = cfSk, Target = ffSk[0] });
      yield return WaitFrames(Fps);
      rts.Height = 40f;
      rts.MoveTo(new Vector2(skCenter.X.AsFloat, skCenter.Z.AsFloat));
      rts.Apply();
      yield return WaitFrames(4);
      CaptureCameraPng(camera, Path.Combine(dir, "t42_drone_ew_skirmish.png"), 1920, 1080);
    }
  }
}
