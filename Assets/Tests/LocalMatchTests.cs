namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.SceneManagement;
  using UnityEngine.TestTools;

  /// <summary>테스트 씬을 Quantum 로컬 모드로 돌려 이동·전투·외교를 확인한다.</summary>
  public unsafe class LocalMatchTests {
    const string ScenePath = "Assets/QuantumUser/Scenes/QuantumGameScene.unity";
    const float TimeScale = 8f;

    static QuantumGame Game => QuantumRunner.Default?.Game;
    static Frame Verified => Game.Frames.Verified;

    IEnumerator StartMatch(bool jointVictory = true) {
      Time.timeScale = TimeScale;
      // 씬 로드 직후, 러너의 Start 전에 규칙을 바꾼다.
      UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (scene, mode) =>
        Object.FindAnyObjectByType<QuantumRunnerLocalDebug>().RuntimeConfig.JointVictory = jointVictory;
      SceneManager.sceneLoaded += onLoaded;
#if UNITY_EDITOR
      yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
      yield return null;
#endif
      SceneManager.sceneLoaded -= onLoaded;
      yield return WaitFor(() => Game?.Frames?.Verified != null && Verified.ComponentCount<Building>() >= 2, 30f, "두 플레이어 사령부 생성");
      Debug.Log($"[FW-TEST] 시작 프레임 {Verified.Number}, 로컬 슬롯 {string.Join(",", Game.GetLocalPlayerSlots())}");
    }

    static IEnumerator WaitFor(System.Func<bool> condition, float realSeconds, string label) {
      var end = Time.realtimeSinceStartup + realSeconds;
      while (condition() == false) {
        if (Time.realtimeSinceStartup > end) {
          Assert.Fail($"시간 초과: {label}");
        }
        yield return null;
      }
    }

    static IEnumerator WaitFrames(int frames, float realSeconds = 60f) {
      var target = Verified.Number + frames;
      yield return WaitFor(() => Verified.Number >= target, realSeconds, $"{frames}프레임 경과");
    }

    static List<EntityRef> Army(PlayerRef owner, bool combatOnly) {
      var result = new List<EntityRef>();
      var f = Verified;
      foreach (var pair in f.GetComponentIterator<Unit>()) {
        if (pair.Component.Owner == owner && (combatOnly == false || f.FindAsset(pair.Component.Spec).Attack != AttackType.None)) {
          result.Add(pair.Entity);
        }
      }
      return result;
    }

    static FP TotalHealth(PlayerRef owner) {
      var total = FP._0;
      foreach (var pair in Verified.GetComponentIterator<Unit>()) {
        if (pair.Component.Owner == owner) {
          total += pair.Component.Health;
        }
      }
      return total;
    }

    static int Funds(Frame f, int player) => f.Global->Players.GetPointer(player)->Funds.AsInt;

    static bool GameEnded(Frame f) => f.Global->GameEnded;

    [TearDown]
    public void TearDown() {
      Time.timeScale = 1f;
      QuantumRunner.ShutdownAll();
    }

    [UnityTest]
    public IEnumerator Bootstrap_SetsUpBothPlayers() {
      yield return StartMatch();
      var f = Verified;
      Assert.IsTrue(f.SessionConfig.LockstepSimulation, "Lockstep");
      Assert.AreEqual(20, f.SessionConfig.UpdateFPS);
      Assert.AreEqual(2, f.ComponentCount<Building>(), "사령부 수");
      // 공병 1 + 시작 유닛. 민병대는 SpawnUnit 한 번이 1기다.
      Assert.AreEqual(4, Army(0, false).Count);
      Assert.AreEqual(4, Army(1, false).Count);
      Assert.AreEqual(5000, Funds(f, 0));
    }

    [UnityTest]
    public IEnumerator AttackMove_ArmiesMeetAndFight() {
      yield return StartMatch();
      var start = Verified.Get<Transform3D>(Army(0, true)[0]).Position;
      var healthBefore = TotalHealth(0) + TotalHealth(1);

      Game.SendCommand(0, new MoveUnitsCommand { Units = Army(0, true).ToArray(), Target = FPVector3.Zero, AttackMove = true });
      Game.SendCommand(1, new MoveUnitsCommand { Units = Army(1, true).ToArray(), Target = FPVector3.Zero, AttackMove = true });

      yield return WaitFrames(20 * 20);
      var units = Army(0, true);
      Assert.Greater(units.Count, 0);
      var moved = (Verified.Get<Transform3D>(units[0]).Position - start).Magnitude;
      Debug.Log($"[FW-TEST] 이동 거리 {moved}, 체력 합계 {healthBefore} -> {TotalHealth(0) + TotalHealth(1)}");
      Assert.Greater(moved.AsFloat, 5f, "NavMesh 이동");
      Assert.Less((TotalHealth(0) + TotalHealth(1)).AsFloat, healthBefore.AsFloat, "자동 공격 피해");
    }

    [UnityTest]
    public IEnumerator JointVictory_AllRemainingAlliedEndsGame() {
      yield return StartMatch();
      Game.SendCommand(0, new DiplomacyCommand { Action = DiplomacyAction.Propose, Target = 1 });
      yield return WaitFrames(15);
      Game.SendCommand(1, new DiplomacyCommand { Action = DiplomacyAction.Accept, Target = 0 });
      yield return WaitFrames(15);
      Assert.IsTrue(GameEnded(Verified), "공동 승리");
    }

    [UnityTest]
    public IEnumerator Alliance_BlocksCombatAndBreakHasGrace() {
      yield return StartMatch(jointVictory: false);
      Game.SendCommand(0, new DiplomacyCommand { Action = DiplomacyAction.Propose, Target = 1 });
      yield return WaitFrames(15);
      Game.SendCommand(1, new DiplomacyCommand { Action = DiplomacyAction.Accept, Target = 0 });
      yield return WaitFrames(15);
      Assert.AreEqual(Relation.Allied, DiplomacySystem.GetRelation(Verified, 0, 1));
      Assert.IsFalse(DiplomacySystem.CanAttack(Verified, 0, 1));

      Game.SendCommand(0, new DiplomacyCommand { Action = DiplomacyAction.Break, Target = 1 });
      yield return WaitFrames(15);
      Assert.AreEqual(Relation.BreakPending, DiplomacySystem.GetRelation(Verified, 0, 1));
      Assert.IsFalse(DiplomacySystem.CanAttack(Verified, 0, 1), "유예 중 공격 불가");

      yield return WaitFrames(10 * 20);
      Assert.AreEqual(Relation.Enemy, DiplomacySystem.GetRelation(Verified, 0, 1));
      Assert.IsTrue(DiplomacySystem.CanAttack(Verified, 0, 1));

      // 재동맹 금지 60초 안에는 제안이 무시된다.
      Game.SendCommand(0, new DiplomacyCommand { Action = DiplomacyAction.Propose, Target = 1 });
      yield return WaitFrames(15);
      Game.SendCommand(1, new DiplomacyCommand { Action = DiplomacyAction.Accept, Target = 0 });
      yield return WaitFrames(15);
      Assert.AreEqual(Relation.Enemy, DiplomacySystem.GetRelation(Verified, 0, 1));
    }
  }
}
