namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>중앙 암시장(NPC 무기상): 거래, 공격하면 영구 적대·거래 잠금·경비 반격, 파괴하면 현상금·영구 폐쇄. 테스트 맵 무기상은 (40, 20).</summary>
  public unsafe partial class LocalMatchTests {
    static EntityRef TraderEntity() {
      foreach (var pair in Verified.GetComponentIterator<NpcTrader>()) {
        return pair.Entity;
      }
      return EntityRef.None;
    }
    static NpcTrader TraderState() => Verified.Get<NpcTrader>(TraderEntity());
    static int TradesOf(int player) => Verified.Global->Players.GetPointer(player)->TradesMade;

    [UnityTest]
    public IEnumerator Trader_MercenariesCostAndSpawn() {
      yield return StartMatch(stock: SortieStock);
      yield return WaitFrames(Fps);
      Assert.AreEqual(1, Verified.ComponentCount<NpcTrader>(), "무기상 1");
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      var before = UnitsNamed(0, "ff_ranger_squad").Count;
      var iron = ExactFunds(0);
      yield return Send(0, new TradeCommand { Offer = NpcTraderSystem.OfferMercenaries });
      Assert.AreEqual(before + rules.TraderMercCount, UnitsNamed(0, "ff_ranger_squad").Count, "용병 투입");
      Assert.AreEqual(rules.TraderMercCost.Iron.AsFloat, iron - ExactFunds(0), 1f, "비용");
      Assert.AreEqual(1, TradesOf(0));
      _rejected.Clear();
      yield return Send(0, new TradeCommand { Offer = NpcTraderSystem.OfferMercenaries });
      AssertRejected(0, ActionKind.Produce, RejectReason.Cooldown);
    }

    [UnityTest]
    public IEnumerator Trader_AttackMakesHostileLocksTradeAndGuardsFire() {
      yield return StartMatch(stock: SortieStock, units: new[] { (0, "ff_eagle_tank", 40, 6), (1, "cf_militia", 40, 40) });
      yield return WaitFrames(Fps);
      var tank = UnitsNamed(0, "ff_eagle_tank").First(e => At(e).Z.AsFloat < 12f);
      var health = Health(tank);
      yield return Send(0, new AttackTargetCommand { Units = new[] { tank }, Target = TraderEntity() });
      yield return WaitFor(() => (TraderState().HostileMask & 1) != 0, 30f, "적대 전환");
      Assert.AreEqual(0, TraderState().HostileMask & 2, "다른 플레이어는 평화");
      _rejected.Clear();
      yield return Send(0, new TradeCommand { Offer = NpcTraderSystem.OfferRefuel });
      AssertRejected(0, ActionKind.Produce, RejectReason.Hostile);
      yield return Send(1, new TradeCommand { Offer = NpcTraderSystem.OfferRefuel });
      Assert.AreEqual(1, TradesOf(1), "적대가 아닌 플레이어는 거래 가능");
      yield return WaitFor(() => Verified.Exists(tank) == false || Health(tank) < health, 30f, "경비 반격");
    }

    [UnityTest]
    public IEnumerator Trader_DestroyedPaysBountyAndCloses() {
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      var before = rules.TraderHealth;
      Restores.Add(() => rules.TraderHealth = before);
      rules.TraderHealth = 60;
      yield return StartMatch(stock: SortieStock, units: new[] { (0, "ff_eagle_tank", 40, 6), (0, "ff_eagle_tank", 44, 6) });
      yield return WaitFrames(Fps);
      var tanks = UnitsNamed(0, "ff_eagle_tank").Where(e => At(e).Z.AsFloat < 12f).ToArray();
      var iron = ExactFunds(0);
      yield return Send(0, new AttackTargetCommand { Units = tanks, Target = TraderEntity() });
      yield return WaitFor(() => TraderState().Destroyed, 30f, "파괴");
      Assert.AreEqual(0, (int)TraderState().Killer, "파괴한 플레이어");
      Assert.GreaterOrEqual(ExactFunds(0) - iron, rules.TraderBounty.Iron.AsFloat - 1f, "현상금");
      _rejected.Clear();
      yield return Send(1, new TradeCommand { Offer = NpcTraderSystem.OfferRefuel });
      AssertRejected(1, ActionKind.Produce, RejectReason.Closed);
    }
  }
}
