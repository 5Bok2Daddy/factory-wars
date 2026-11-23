namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// Task 39 card 3 (2026-11-23): Map4P mercenary camp on top of NpcTraderSystem. Garrison sortie and return, entry blocked before an
  /// agreement, message + truck delivery makes the agreement, shop delivers units owned by the buyer with a shared stock,
  /// attacking revokes the agreement, deterministic with two bots negotiating.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static MercenarySpec MercSpec => Resources.Load<MercenarySpec>("FactoryWars/mercenary_camp");
    static NpcTrader Camp() => Verified.Get<NpcTrader>(TraderEntity());
    static List<(EntityRef Entity, MercGuard Guard, FPVector3 At)> Guards() {
      var list = new List<(EntityRef, MercGuard, FPVector3)>();
      foreach (var pair in Verified.GetComponentIterator<MercGuard>()) list.Add((pair.Entity, pair.Component, Verified.Get<Transform3D>(pair.Entity).Position));
      return list;
    }

    static void GrantAgreement(int player) {
      var trader = Verified.Unsafe.GetPointer<NpcTrader>(TraderEntity());
      trader->AgreementMask |= 1 << player;
      trader->ContactMask |= 1 << player;
    }

    static void Teleport(EntityRef entity, FPVector3 p) => SetEntityPosition(Verified, entity, p);
    static EntityRef CampTruck(int player) => Verified.Unsafe.GetPointer<NpcTrader>(TraderEntity())->Trucks[player];
    static int CampStock(int offer) => Verified.Unsafe.GetPointer<NpcTrader>(TraderEntity())->Stock[offer];
    static void ResetReady(int player, int offer) => Verified.Unsafe.GetPointer<NpcTrader>(TraderEntity())->ReadyFrames[player * MercenaryCamp.MaxOffers + offer] = 0;

    [UnityTest, Timeout(600000)]
    public IEnumerator Merc_GarrisonSortiesOnIntrusionAndReturns() {
      yield return StartMatch(Map4PScene, 2, truce: 0, units: new[] { (0, "ff_eagle_tank", 0, 30) });
      yield return WaitFrames(Fps);
      Assert.IsTrue(FactoryWarsUtil.MapInfo(Verified).MercenaryCamp, "Map4P trader is the mercenary camp");
      Assert.AreEqual(MercSpec.GuardCount, Guards().Count, "garrison at its posts");
      var tank = UnitsNamed(0, "ff_eagle_tank").OrderBy(u => At(u).Magnitude.AsFloat).First();
      var health = Health(tank);
      yield return WaitFor(() => (Camp().ThreatMask & 1) != 0, 10f, "intruder without agreement becomes a threat");
      Assert.AreEqual(0, Camp().ThreatMask & 2, "the other player is not a threat");
      yield return WaitFor(() => Guards().Any(g => g.Guard.Target == tank), 10f, "guards sortie against the intruder");
      yield return WaitFor(() => Verified.Exists(tank) == false || Health(tank) < health, 20f, "garrison fire");
      if (Verified.Exists(tank)) Teleport(tank, Pos(-200, -200));
      yield return WaitFor(() => Camp().ThreatMask == 0, 10f, "threat clears once outside the leash radius");
      yield return WaitFor(() => Guards().All(g => (g.At - g.Guard.Post).Magnitude.AsFloat < 0.5f), 40f, "guards return to their posts");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Merc_GarrisonSortiesOnAttack() {
      yield return StartMatch(Map4PScene, 2, truce: 0, units: new[] { (0, "ff_eagle_tank", 0, 66) });
      yield return WaitFrames(Fps);
      var tank = UnitsNamed(0, "ff_eagle_tank").OrderBy(u => (At(u) - Pos(0, 66)).Magnitude.AsFloat).First();
      var guard = Guards().OrderBy(g => (g.At - At(tank)).Magnitude.AsFloat).First();
      Assert.AreEqual(0, Camp().ThreatMask, "outside the fence: no threat yet");
      yield return Send(0, new AttackTargetCommand { Units = new[] { tank }, Target = guard.Entity });
      yield return WaitFor(() => (Camp().ThreatMask & 1) != 0, 30f, "attacking a guard makes the attacker a threat");
      yield return WaitFor(() => Guards().Any(g => g.Guard.Target == tank && (g.At - g.Guard.Post).Magnitude.AsFloat > 1f), 20f, "guards leave their posts to fight back");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Merc_EntryBlockedBeforeAgreement() {
      yield return StartMatch(Map4PScene, 2, truce: 0, units: new[] { (0, "ff_scout_buggy", 0, 90), (1, "cf_scout_bike", 0, -90) });
      yield return WaitFrames(Fps);
      var buggy = UnitsNamed(0, "ff_scout_buggy").OrderBy(u => (At(u) - Pos(0, 90)).Magnitude.AsFloat).First();
      var bike = UnitsNamed(1, "cf_scout_bike").OrderBy(u => (At(u) - Pos(0, -90)).Magnitude.AsFloat).First();
      GrantAgreement(1);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { buggy }, Target = Pos(0, 10) });
      yield return Send(1, new MoveUnitsCommand { Units = new[] { bike }, Target = Pos(0, -10) });
      Assert.GreaterOrEqual(Verified.Get<Unit>(buggy).TargetPosition.Magnitude.AsFloat, MercSpec.CampRadius.AsFloat, "order without agreement stops at the gate");
      yield return WaitFrames(25 * Fps);
      Assert.Greater(At(buggy).Magnitude.AsFloat, MercSpec.CampRadius.AsFloat - 2f, "no agreement: stays outside the camp");
      Assert.Less((At(bike) - Pos(0, -10)).Magnitude.AsFloat, 6f, "with an agreement the camp is open");
      Assert.AreEqual(0, Camp().ThreatMask & 2, "agreement holders are not threats");
    }

    [UnityTest, Timeout(900000)]
    public IEnumerator Merc_MessageTruckAgreementAndShop() {
      yield return StartMatch(Map4PScene, 2, truce: 0, stock: new ResourceCost { Iron = 5000, Wood = 5000, Steel = 2000, Power = 2000 });
      var events = new List<(int Player, int Stage)>();
      QuantumEvent.Subscribe<EventMercDiplomacy>(_listener, e => events.Add(((int)e.Player, e.Stage)));
      _rejected.Clear();
      yield return Send(0, new TradeCommand { Offer = MercenaryCamp.OfferShop });
      AssertRejected(0, ActionKind.Produce, RejectReason.Hostile);
      yield return Send(0, new TradeCommand { Offer = MercenaryCamp.OfferDispatch });
      AssertRejected(0, ActionKind.Produce, RejectReason.Locked);
      yield return Send(0, new TradeCommand { Offer = MercenaryCamp.OfferMessage });
      yield return WaitFor(() => (Camp().ContactMask & 1) != 0, 30f, "reply after the message delay");
      var iron = ExactFunds(0);
      yield return Send(0, new TradeCommand { Offer = MercenaryCamp.OfferDispatch });
      Assert.AreEqual(MercSpec.Cargo.Iron.AsFloat, iron - ExactFunds(0), 1f, "cargo escrowed");
      var truck = CampTruck(0);
      Assert.IsTrue(Verified.Exists(truck) && Verified.Has<MercCargo>(truck), "armoured truck spawned");
      Assert.AreEqual(0, (int)Verified.Get<Unit>(truck).Owner, "truck belongs to the sender");
      yield return WaitFor(() => (Camp().AgreementMask & 1) != 0, 240f, "truck delivers the cargo at the gate");
      CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, events.Where(e => e.Player == 0).Select(e => e.Stage).ToArray(), "message, reply, dispatch, agreement");
      // Shop: unit owned by the buyer at the gate; shared stock.
      var line = MercSpec.Offers[2];
      var unitName = Verified.FindAsset(line.Unit).name;
      var before = UnitsNamed(0, unitName).Count;
      var stock = CampStock(2);
      yield return Send(0, new TradeCommand { Offer = MercenaryCamp.OfferShop + 2 });
      Assert.AreEqual(before + line.Count, UnitsNamed(0, unitName).Count, "bought unit delivered");
      Assert.Less((At(UnitsNamed(0, unitName).Last()) - MercenaryCamp.GateToward(MercSpec, FPVector3.Zero, FactoryWarsUtil.StartPosition(Verified, 0))).Magnitude.AsFloat, 12f, "at the camp gate");
      Assert.AreEqual(stock - 1, CampStock(2), "stock taken");
      GrantAgreement(1);
      for (var i = 1; i < stock; i++) {
        yield return Send(1, new TradeCommand { Offer = MercenaryCamp.OfferShop + 2 });
      }
      Assert.AreEqual(0, CampStock(2), "player 1 bought the rest of the shared stock");
      _rejected.Clear();
      ResetReady(0, 2);
      yield return Send(0, new TradeCommand { Offer = MercenaryCamp.OfferShop + 2 });
      AssertRejected(0, ActionKind.Produce, RejectReason.Limit);
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Merc_AttackRevokesAgreement() {
      yield return StartMatch(Map4PScene, 2, truce: 0, units: new[] { (0, "ff_eagle_tank", 0, 66) });
      yield return WaitFrames(Fps);
      GrantAgreement(0);
      var tank = UnitsNamed(0, "ff_eagle_tank").OrderBy(u => (At(u) - Pos(0, 66)).Magnitude.AsFloat).First();
      var guard = Guards().OrderBy(g => (g.At - At(tank)).Magnitude.AsFloat).First();
      var revoked = 0;
      QuantumEvent.Subscribe<EventMercDiplomacy>(_listener, e => { if ((int)e.Player == 0 && e.Stage == 6) revoked++; });
      yield return Send(0, new AttackTargetCommand { Units = new[] { tank }, Target = guard.Entity });
      yield return WaitFor(() => (Camp().AgreementMask & 1) == 0, 30f, "agreement revoked by the attack");
      Assert.AreEqual(1, revoked, "revoke event");
      _rejected.Clear();
      yield return Send(0, new TradeCommand { Offer = MercenaryCamp.OfferShop });
      AssertRejected(0, ActionKind.Produce, RejectReason.Hostile);
    }

    [UnityTest, Timeout(900000)]
    public IEnumerator Merc_Deterministic() {
      var spec = MercSpec;
      var contact = spec.AiContactAfterSeconds;
      var delay = spec.MessageDelaySeconds;
      var checksums = new ulong[2];
      var masks = new int[2];
      for (var run = 0; run < 2; run++) {
        FixedStep = true;
        Restores.Add(() => FixedStep = false);
        // TearDown restores the data after each run, so set the short AI timings again for every run.
        Restores.Add(() => { spec.AiContactAfterSeconds = contact; spec.MessageDelaySeconds = delay; });
        spec.AiContactAfterSeconds = 4;
        spec.MessageDelaySeconds = 2;
        yield return StartMatch(Map4PScene, 2, truce: 0, seed: 3940, bots: new[] { BotDifficulty.Normal, BotDifficulty.Normal },
          stock: new ResourceCost { Iron = 3000, Wood = 3000, Steel = 800, Power = 800 });
        var stop = 70 * Fps;
        var sum = 0UL;
        var mask = 0;
        QuantumCallback.Subscribe<CallbackSimulateFinished>(_listener, e => {
          if (e.Frame.Number != stop || sum != 0UL) return;
          sum = e.Frame.CalculateChecksum();
          foreach (var pair in e.Frame.GetComponentIterator<NpcTrader>()) mask = pair.Component.ContactMask | pair.Component.AgreementMask << 4;
        });
        yield return WaitFor(() => sum != 0UL, 180f, "70 s checksum");
        checksums[run] = sum;
        masks[run] = mask;
        Debug.Log($"[FW-TEST] merc run {run}: checksum {sum} contact/agreement mask {mask}");
        Assert.AreEqual(3, mask & 3, "both bots contacted the camp");
        Assert.AreEqual(3, mask >> 4, "both bots delivered cargo and hold an agreement");
        TearDown();
        yield return null;
      }
      Assert.AreEqual(checksums[0], checksums[1], "two bots negotiating: same seed gives the same state");
      Assert.AreEqual(masks[0], masks[1]);
    }
  }
}
