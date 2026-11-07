namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>AI 행동. 사람 자리는 아무 명령도 내리지 않는다.</summary>
  public unsafe partial class LocalMatchTests {
    static PlayerState StateOf(int player) => *Verified.Global->Players.GetPointer(player);

    static int WinnerMask() => Verified.Global->WinnerMask;

    static int EndFrame() => Verified.Global->EndFrame;

    static int BuildingCount(int player, BuildingRole role, bool finishedOnly = true) {
      var count = 0;
      foreach (var pair in Verified.GetComponentIterator<Building>()) {
        if (pair.Component.Owner == player && Verified.FindAsset(pair.Component.Spec).Role == role
            && (finishedOnly == false || pair.Component.ConstructionRemaining <= FP._0)) {
          count++;
        }
      }
      return count;
    }

    IEnumerator Sim(int seconds) {
      yield return WaitFrames(seconds * Fps, 120f);
    }

    [UnityTest]
    public IEnumerator Bot_BuildsEconomyInOrder() {
      yield return StartMatch(Map2PScene, 2, bots: new[] { BotDifficulty.Normal });
      for (var minute = 1; minute <= 5; minute++) {
        yield return Sim(60);
        var state = StateOf(1);
        Debug.Log($"[FW-TEST] normal bot {minute}m: iron {state.Stock.Iron} wood {state.Stock.Wood} steel {state.Stock.Steel} power {state.Stock.Power}/{state.PowerCap} rate {state.PowerRate} pop {state.PopulationUsed}/{state.PopulationCap} eng {Engineers(1).Count} depot {BuildingCount(1, BuildingRole.SupplyDepot)} barracks {BuildingCount(1, BuildingRole.Barracks)} plant {BuildingCount(1, BuildingRole.PowerPlant)} proc {BuildingCount(1, BuildingRole.Processor)} factory {BuildingCount(1, BuildingRole.VehicleFactory)} units {state.UnitsProduced}");
      }
      Assert.GreaterOrEqual(BuildingCount(1, BuildingRole.SupplyDepot), 1, "보급창");
      Assert.GreaterOrEqual(BuildingCount(1, BuildingRole.Barracks), 1, "병영");
      Assert.GreaterOrEqual(BuildingCount(1, BuildingRole.PowerPlant), 1, "발전소");
      Assert.GreaterOrEqual(BuildingCount(1, BuildingRole.Processor), 1, "가공소");
      Assert.GreaterOrEqual(BuildingCount(1, BuildingRole.VehicleFactory), 1, "군수공장");
      Assert.Greater(StateOf(1).Earned.Iron.AsFloat, 0f, "철 수입");
      Assert.Greater(StateOf(1).Earned.Wood.AsFloat, 0f, "목재 수입");
      Assert.Greater(StateOf(1).Earned.Steel.AsFloat, 0f, "강철 생산");
      Assert.GreaterOrEqual(Engineers(1).Count, 6, "공병 생산");
    }

    [UnityTest]
    public IEnumerator Bot_ProducesArmy() {
      yield return StartMatch(Map2PScene, 2, bots: new[] { BotDifficulty.Normal });
      yield return Sim(240);
      Assert.Greater(StateOf(1).UnitsProduced, 5, "유닛 생산");
      Assert.GreaterOrEqual(BuildingCount(1, BuildingRole.VehicleFactory), 1, "군수공장");
      Debug.Log($"[FW-TEST] normal bot 240s: produced {StateOf(1).UnitsProduced}, army {Army(1, true).Count}, earned {StateOf(1).Earned.Iron}");
      Debug.Log($"[FW-TEST] normal bot 240s power: stock {StateOf(1).Stock.Power}/{StateOf(1).PowerCap}, rate {StateOf(1).PowerRate}, generated {StateOf(1).Earned.Power}, outages {StateOf(1).OutageCount}, plants {BuildingCount(1, BuildingRole.PowerPlant)}, steel {StateOf(1).Stock.Steel}");
    }

    [UnityTest]
    public IEnumerator Bot_EasyIsSlowerThanNormal() {
      yield return StartMatch(Map2PScene, 2, bots: new[] { BotDifficulty.Easy, BotDifficulty.Normal });
      yield return Sim(300);
      Debug.Log($"[FW-TEST] 300s easy earned {StateOf(0).Earned.Iron} produced {StateOf(0).UnitsProduced}; normal earned {StateOf(1).Earned.Iron} produced {StateOf(1).UnitsProduced}");
      Assert.Greater((StateOf(1).Earned.Iron + StateOf(1).Earned.Wood).AsFloat, (StateOf(0).Earned.Iron + StateOf(0).Earned.Wood).AsFloat, "보통이 더 번다");
      Assert.Greater(StateOf(1).UnitsProduced, StateOf(0).UnitsProduced, "보통이 더 만든다");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Bot_DefeatsIdlePlayer() {
      yield return StartMatch(Map2PScene, 2, bots: new[] { BotDifficulty.Normal });
      yield return WaitFor(() => GameEnded(), 500f, "AI 승리");
      Assert.IsTrue(Defeated(0), "가만히 있는 사람이 패배");
      Assert.AreEqual(1 << 1, WinnerMask());
      Debug.Log($"[FW-TEST] idle player defeated at {EndFrame() / (float)Fps}s");
    }

    [UnityTest]
    public IEnumerator Bot_EasyAnswersProposalWithEitherResult() {
      yield return StartMatch(Map4PScene, 4, bots: new[] { BotDifficulty.Easy, BotDifficulty.Easy, BotDifficulty.Easy });
      yield return Diplomacy(0, DiplomacyAction.Propose, 1);
      yield return Sim(10);
      var answered = DiplomacySystem.HasProposal(Verified, 0, 1) == false;
      Assert.IsTrue(answered, "쉬움 AI가 제안에 답함");
      var allied = DiplomacySystem.GetRelation(Verified, 0, 1) == Relation.Allied;
      Assert.AreEqual(allied ? 200 : 350, Funds(0), "수락이면 환급 없음, 거절이면 150 환급");
    }

    [UnityTest]
    public IEnumerator Bot_NormalRejectsStrongestAcceptsOthers() {
      // 자리 0이 가장 강하다. 보통 AI는 0의 제안을 거절한다.
      var army = Enumerable.Range(0, 6).Select(i => (0, "ff_eagle_tank", -86 + i * 2, -100)).ToArray();
      yield return StartMatch(Map4PScene, 4, units: army, bots: new[] { BotDifficulty.Normal, BotDifficulty.Normal, BotDifficulty.Normal });
      yield return Diplomacy(0, DiplomacyAction.Propose, 1);
      yield return Sim(5);
      Assert.IsFalse(DiplomacySystem.HasProposal(Verified, 0, 1));
      Assert.AreEqual(Relation.Enemy, DiplomacySystem.GetRelation(Verified, 0, 1), "가장 강한 쪽은 거절");
    }

    [UnityTest]
    public IEnumerator Bot_DiplomacyDisabledInDuel() {
      yield return StartMatch(Map2PScene, 2, bots: new[] { BotDifficulty.Normal, BotDifficulty.Normal });
      yield return Sim(100);
      Assert.AreEqual(Relation.Enemy, DiplomacySystem.GetRelation(Verified, 0, 1));
    }

    [UnityTest]
    public IEnumerator Bot_SameSeedGivesSameMatch() {
      var checksums = new long[2];
      for (var run = 0; run < 2; run++) {
        yield return StartMatch(Map2PScene, 2, bots: new[] { BotDifficulty.Normal, BotDifficulty.Normal }, seed: 12345);
        var target = 90 * Fps;
        yield return WaitFor(() => Verified.Number >= target, 60f, "90초 진행");
        checksums[run] = Fingerprint();
        TearDown();
        yield return null;
      }
      Assert.AreEqual(checksums[0], checksums[1], "같은 시드면 같은 결과");
    }

    /// <summary>프레임 번호가 달라도 비교되도록 경기 진행값으로 지문을 만든다.</summary>
    static long Fingerprint() {
      long value = 0;
      for (var i = 0; i < 2; i++) {
        value = value * 31 + StateOf(i).Earned.Iron.RawValue;
        value = value * 31 + StateOf(i).Earned.Wood.RawValue;
        value = value * 31 + StateOf(i).Earned.Steel.RawValue;
        value = value * 31 + StateOf(i).UnitsProduced;
      }
      return value;
    }
  }
}
