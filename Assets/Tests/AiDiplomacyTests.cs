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

  /// <summary>AI 외교: 전원 동맹 금지, 관계 점수, 수락·파기 조건.</summary>
  public unsafe partial class LocalMatchTests {
    static BotDifficulty[] Bots(int count, BotDifficulty difficulty = BotDifficulty.Normal) => Enumerable.Repeat(difficulty, count).ToArray();

    static int AlliedPairs(int players) {
      var pairs = 0;
      for (var a = 0; a < players; a++) {
        for (var b = a + 1; b < players; b++) {
          if (DiplomacySystem.GetRelation(Verified, a, b) == Relation.Allied) {
            pairs++;
          }
        }
      }
      return pairs;
    }

    static bool EveryoneAllied(int players) {
      var alive = Enumerable.Range(0, players).Where(i => StateOf(i).Defeated == false).ToArray();
      return alive.Length > 1 && alive.All(a => alive.All(b => a == b || DiplomacySystem.GetRelation(Verified, a, b) == Relation.Allied));
    }

    [UnityTest, Timeout(300000)]
    public IEnumerator AiDiplomacy_FourBotsAllyPartlyButNeverEveryone() {
      yield return StartMatch(Map4PScene, 4, bots: Bots(4), seed: 11, truce: -1);
      Time.timeScale = 30f;
      var most = 0;
      var target = 420 * Fps;
      yield return WaitFor(() => {
        Assert.IsFalse(EveryoneAllied(4), "AI끼리 전원 동맹 금지");
        most = Mathf.Max(most, AlliedPairs(4));
        return Verified.Number >= target;
      }, 120f, "휴전 끝까지");
      Debug.Log($"[FW-TEST] 4 bots truce: most allied pairs {most}");
      Assert.Greater(most, 0, "일부 동맹은 성립");
      Assert.IsFalse(GameEnded(), "공동 승리 없음");
    }

    /// <summary>AI 2가 사람보다 강해야 AI 1이 사람의 제안을 받는다.</summary>
    static readonly (int, string, int, int)[] StrongThirdPlayer =
      Enumerable.Range(0, 4).Select(i => (2, "ff_eagle_tank", -6 + i * 4, 44)).ToArray();

    [UnityTest]
    public IEnumerator AiDiplomacy_NeverCompletesFullAllianceWithHuman() {
      // 사람과 AI 둘. 사람이 AI 하나와 동맹이면 나머지 AI는 전원 동맹을 만들 수 없다.
      yield return StartMatch(players: 3, units: StrongThirdPlayer, bots: Bots(2));
      yield return FriendlyNotes(0, 1);
      yield return Diplomacy(0, DiplomacyAction.Propose, 1);
      yield return Sim(5);
      Assert.AreEqual(Relation.Allied, DiplomacySystem.GetRelation(Verified, 0, 1), "공동의 적이 남으니 수락");

      yield return FriendlyNotes(0, 2);
      yield return Diplomacy(0, DiplomacyAction.Propose, 2);
      yield return Sim(5);
      Assert.AreNotEqual(Relation.Allied, DiplomacySystem.GetRelation(Verified, 0, 2), "AI 2는 공동의 적이 없어 거절");
    }

    IEnumerator FriendlyNotes(int from, int to) {
      for (var i = 0; i < 3; i++) {
        yield return Send(from, new SendNoteCommand { PhraseId = 0, TargetMode = NoteTarget.Player, Receiver = to });
        yield return WaitFrames(5 * Fps);
      }
    }

    [UnityTest]
    public IEnumerator AiDiplomacy_OpinionRisesWithFriendlyNoteOnly() {
      yield return StartMatch(players: 3, bots: Bots(2));
      var before = DiplomacySystem.GetOpinion(Verified, 1, 0);
      yield return Send(0, new SendNoteCommand { PhraseId = 1, TargetMode = NoteTarget.Player, Receiver = 1 });
      Assert.LessOrEqual(DiplomacySystem.GetOpinion(Verified, 1, 0), before + 2, "우호 문구가 아니면 쪽지 효과 없음");
      yield return WaitFrames(5 * Fps);
      before = DiplomacySystem.GetOpinion(Verified, 1, 0);
      yield return Send(0, new SendNoteCommand { PhraseId = 0, TargetMode = NoteTarget.Player, Receiver = 1 });
      Assert.GreaterOrEqual(DiplomacySystem.GetOpinion(Verified, 1, 0), before + 13, "우호 문구 +15");
    }

    [UnityTest]
    public IEnumerator AiDiplomacy_NoAttackIsNotCountedAtMatchStart() {
      yield return StartMatch(players: 3, units: StrongThirdPlayer, bots: Bots(2));
      yield return Sim(25);
      Assert.GreaterOrEqual(DiplomacySystem.GetOpinion(Verified, 1, 0), 0, "공격한 적이 없으면 점수가 내려가지 않음");
    }

    [UnityTest]
    public IEnumerator AiDiplomacy_RejectsWithoutOpinion() {
      yield return StartMatch(players: 3, bots: Bots(2));
      yield return Diplomacy(0, DiplomacyAction.Propose, 1);
      yield return Sim(5);
      Assert.AreEqual(Relation.Enemy, DiplomacySystem.GetRelation(Verified, 0, 1), "점수가 모자라면 거절");
      Assert.IsFalse(DiplomacySystem.HasProposal(Verified, 0, 1));
    }

    [UnityTest]
    public IEnumerator AiDiplomacy_AttackLowersOpinion() {
      yield return StartMatch(players: 3, units: new[] { (0, "ff_eagle_tank", 26, 8), (0, "ff_eagle_tank", 26, -8) }, bots: Bots(2));
      var before = DiplomacySystem.GetOpinion(Verified, 1, 0);
      yield return WaitFor(() => DiplomacySystem.GetOpinion(Verified, 1, 0) < before - 10, 60f, "공격받아 점수 하락");
    }

    [Test]
    public void AiDiplomacy_OverpoweredNeedsRatio() {
      var ai = new AiSpec { BreakStrengthRatio = FP._1 + FP._0_50 };
      Assert.IsTrue(BotSystem.IsOverpowered(150, 100, ai), "2위의 1.5배");
      Assert.IsFalse(BotSystem.IsOverpowered(149, 100, ai));
      Assert.IsFalse(BotSystem.IsOverpowered(500, 0, ai), "비교할 상대가 없으면 파기하지 않음");
    }

    [UnityTest, Timeout(300000)]
    public IEnumerator AiDiplomacy_BreaksAfterCommonEnemyFalls() {
      // AI 1의 대군이 공동의 적인 AI 2를 무너뜨린다. 사람은 동맹으로 남아 있다.
      var army = Enumerable.Range(0, 24).Select(i => (1, "cf_dragonscale_tank", -20 + (i % 12) * 3, 14 + i / 12 * 3)).ToArray();
      yield return StartMatch(players: 3, units: army.Concat(StrongThirdPlayer).ToArray(), bots: Bots(2));
      Time.timeScale = 20f;
      yield return FriendlyNotes(0, 1);
      yield return Diplomacy(0, DiplomacyAction.Propose, 1);
      yield return Sim(5);
      Assert.AreEqual(Relation.Allied, DiplomacySystem.GetRelation(Verified, 0, 1), "동맹 성립");

      yield return WaitFor(() => Defeated(2), 200f, "공동의 적 패배");
      var fallen = Verified.Number;
      yield return WaitFor(() => DiplomacySystem.GetRelation(Verified, 0, 1) != Relation.Allied, 60f, "일정 시간 뒤 파기");
      Assert.GreaterOrEqual((Verified.Number - fallen) / (float)Fps, 25f, "바로 파기하지 않음");
      Assert.IsFalse(GameEnded(), "AI가 끼면 공동 승리 없음");
    }

    [UnityTest, Timeout(300000)]
    public IEnumerator AiDiplomacy_SameSeedSameRelations() {
      var results = new string[2];
      for (var run = 0; run < 2; run++) {
        yield return StartMatch(Map4PScene, 4, bots: Bots(4), seed: 77, truce: -1);
        Time.timeScale = 30f;
        var target = 200 * Fps;
        yield return WaitFor(() => Verified.Number >= target, 60f, "200초 진행");
        results[run] = string.Join(",", Enumerable.Range(0, 16).Select(c => DiplomacySystem.GetOpinion(Verified, c / 4, c % 4))) + "|" + AlliedPairs(4);
        TearDown();
        yield return null;
      }
      Assert.AreEqual(results[0], results[1], "같은 시드면 같은 관계");
    }
  }
}
