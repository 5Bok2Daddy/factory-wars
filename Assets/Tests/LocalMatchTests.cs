namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.SceneManagement;
  using UnityEngine.TestTools;

  /// <summary>Quantum 로컬 모드로 씬을 돌려 이동·전투·외교·비용·맵을 확인한다.</summary>
  public unsafe partial class LocalMatchTests {
    const string TestScene = "Assets/QuantumUser/Scenes/QuantumGameScene.unity";
    const string Map2PScene = "Assets/QuantumUser/Scenes/Map2P.unity";
    const string Map4PScene = "Assets/QuantumUser/Scenes/Map4P.unity";
    const float TimeScale = 8f;
    const int Fps = 20;
    // 커맨드가 입력 지연(4틱)을 지나 실행될 때까지 기다리는 프레임.
    const int CommandFrames = 15;

    readonly List<EventActionRejected> _rejected = new List<EventActionRejected>();
    static AssetRef<FactionSpec>[] _sceneFactions;
    GameObject _listener;

    static QuantumGame Game => QuantumRunner.Default?.Game;
    static Frame Verified => Game.Frames.Verified;

    IEnumerator StartMatch(string scene = TestScene, int players = 2, bool jointVictory = true, bool teamMode = false, (int Owner, string Unit, int X, int Z)[] units = null, (int Owner, string Building, int X, int Z)[] buildings = null, BotDifficulty[] bots = null, int? seed = null, ResourceCost? stock = null) {
      Time.timeScale = TimeScale;
      // 씬 로드 직후, 러너의 Start 전에 규칙을 바꾼다.
      UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (loaded, mode) => {
        var runner = Object.FindAnyObjectByType<QuantumRunnerLocalDebug>();
        runner.RuntimeConfig.JointVictory = jointVictory;
        runner.RuntimeConfig.TeamMode = teamMode;
        runner.RuntimeConfig.OverrideStartingResources = stock.HasValue;
        runner.RuntimeConfig.StartingResources = stock ?? default;
        var list = runner.LocalPlayers.ToList();
        while (list.Count < players) {
          list.Add(new RuntimePlayer { PlayerNickname = $"Extra{list.Count}", Faction = list[0].Faction, Team = list.Count });
        }
        for (var i = 0; teamMode && i < list.Count; i++) {
          list[i].Team = i / 2;
        }
        // 뒤쪽 자리를 AI에게 준다.
        var humans = players - (bots?.Length ?? 0);
        runner.RuntimeConfig.Bots = bots?.Select((difficulty, i) => new BotSlot {
          Player = humans + i, Faction = list[humans + i].Faction, Team = humans + i, Difficulty = difficulty,
        }).ToArray();
        if (seed.HasValue) {
          runner.RuntimeConfig.Seed = seed.Value;
        }
        var allFactions = list;
        _sceneFactions = allFactions.Select(p => p.Faction).ToArray();
        runner.LocalPlayers = list.Take(humans).ToArray();
        runner.MaxPlayerCount = players;
        runner.RuntimeConfig.ScenarioUnits = units?.Select(u => new ScenarioUnit {
          Player = u.Owner, Unit = FindUnit(runner, u.Unit), Position = new FPVector3(u.X, 0, u.Z),
        }).ToArray();
        runner.RuntimeConfig.ScenarioBuildings = buildings?.Select(b => new ScenarioBuilding {
          Player = b.Owner, Building = FindBuilding(runner, b.Building), Position = new FPVector3(b.X, 0, b.Z),
        }).ToArray();
      };
      SceneManager.sceneLoaded += onLoaded;
#if UNITY_EDITOR
      yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scene, new LoadSceneParameters(LoadSceneMode.Single));
#else
      yield return null;
#endif
      SceneManager.sceneLoaded -= onLoaded;
      yield return WaitFor(() => Game?.Frames?.Verified != null && Verified.ComponentCount<Building>() >= players, 30f, "플레이어 사령부 생성");

      // AI 건물이 먼저 있어서 사람 자리가 확정될 때까지 더 기다린다.
      if (bots != null && players > bots.Length) {
        yield return WaitFrames(CommandFrames);
      }
      _rejected.Clear();
      _listener = new GameObject("RejectedListener");
      QuantumEvent.Subscribe<EventActionRejected>(_listener, e => _rejected.Add(e));
    }

    static AssetRef<UnitSpec> FindUnit(QuantumRunnerLocalDebug runner, string name) {
      return _sceneFactions
        .SelectMany(p => QuantumUnityDB.GetGlobalAsset<FactionSpec>(p.Id).Units)
        .First(u => QuantumUnityDB.GetGlobalAsset<UnitSpec>(u.Id).name == name);
    }

    static AssetRef<BuildingSpec> FindBuilding(QuantumRunnerLocalDebug runner, string name) {
      return _sceneFactions
        .SelectMany(p => QuantumUnityDB.GetGlobalAsset<FactionSpec>(p.Id).Buildings)
        .First(b => QuantumUnityDB.GetGlobalAsset<BuildingSpec>(b.Id).name == name);
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

    static IEnumerator Send(int player, DeterministicCommand command) {
      Game.SendCommand(player, command);
      yield return WaitFrames(CommandFrames);
    }

    static IEnumerator Diplomacy(int from, DiplomacyAction action, int target) {
      yield return Send(from, new DiplomacyCommand { Action = action, Target = target });
    }

    static IEnumerator Ally(int a, int b) {
      yield return Diplomacy(a, DiplomacyAction.Propose, b);
      yield return Diplomacy(b, DiplomacyAction.Accept, a);
    }

    /// <summary>건물·공병을 사서 자금을 limit 아래로 줄인다. 건물은 부지만 잡아도 비용이 나간다.</summary>
    IEnumerator SpendBelow(int player, int limit) {
      var faction = FactionOf(player);
      var engineer = Engineers(player)[0];
      var site = 0;
      while (Funds(player) >= limit) {
        // 처음부터 지을 수 있는 건물만 쓴다.
        var building = faction.Buildings.First(b => Verified.FindAsset(b).name.EndsWith("_power_plant"));
        if (Verified.FindAsset(building).Cost.Iron <= Funds(player)) {
          var position = V(-20 + 8 * (site % 6), -44 + 8 * (site / 6));
          site++;
          yield return Send(player, new BuildCommand { Engineer = engineer, Building = building, Position = position });
        } else {
          yield return Send(player, new ProduceCommand { Building = Headquarters(player), Unit = faction.Engineer, Count = 1 });
        }
      }
    }

    static FactionSpec FactionOf(int player) => Verified.FindAsset(Verified.Global->Players.GetPointer(player)->Faction);

    static EntityRef Headquarters(PlayerRef owner) {
      var f = Verified;
      var spec = FactionOf(owner).Headquarters;
      foreach (var pair in f.GetComponentIterator<Building>()) {
        if (pair.Component.Owner == owner && pair.Component.Spec == spec) {
          return pair.Entity;
        }
      }
      return EntityRef.None;
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

    static int Funds(int player) => Verified.Global->Players.GetPointer(player)->Stock.Iron.AsInt;

    static bool GameEnded() => Verified.Global->GameEnded;

    static int JointVictoryFrame() => Verified.Unsafe.GetPointerSingleton<Quantum.Diplomacy>()->JointVictoryFrame;

    void AssertRejected(int player, ActionKind kind, RejectReason reason) {
      Assert.IsTrue(_rejected.Any(e => e.Player == player && e.Kind == kind && e.Reason == reason),
        $"ActionRejected({player}, {kind}, {reason}) 없음. 받은 이벤트: {string.Join(", ", _rejected.Select(e => $"{e.Player}/{e.Kind}/{e.Reason}"))}");
    }

    [TearDown]
    public void TearDown() {
      Time.timeScale = 1f;
      if (_listener != null) {
        QuantumEvent.UnsubscribeListener(_listener);
        Object.Destroy(_listener);
      }
      QuantumRunner.ShutdownAll();
    }

    [UnityTest]
    public IEnumerator Bootstrap_SetsUpBothPlayers() {
      yield return StartMatch();
      var f = Verified;
      Assert.IsFalse(f.SessionConfig.LockstepSimulation, "soft lockstep");
      Assert.AreEqual(20, f.SessionConfig.UpdateFPS);
      Assert.AreEqual(4, f.SessionConfig.InputDelayMin, "입력 지연 200ms");
      Assert.AreEqual(2, f.ComponentCount<Building>(), "사령부 수");
      // 공병 1 + 시작 유닛. 민병대는 SpawnUnit 한 번이 1기다.
      Assert.AreEqual(4, Army(0, false).Count);
      Assert.AreEqual(4, Army(1, false).Count);
      Assert.AreEqual(5000, Funds(0));
    }

    [UnityTest]
    public IEnumerator AttackMove_ArmiesMeetAndFight() {
      yield return StartMatch();
      var start = Verified.Get<Transform3D>(Army(0, true)[0]).Position;
      var healthBefore = TotalHealth(0) + TotalHealth(1);

      Game.SendCommand(0, new MoveUnitsCommand { Units = Army(0, true).ToArray(), Target = FPVector3.Zero, AttackMove = true });
      Game.SendCommand(1, new MoveUnitsCommand { Units = Army(1, true).ToArray(), Target = FPVector3.Zero, AttackMove = true });

      yield return WaitFrames(20 * Fps);
      var units = Army(0, true);
      Assert.Greater(units.Count, 0);
      var moved = (Verified.Get<Transform3D>(units[0]).Position - start).Magnitude;
      Debug.Log($"[FW-TEST] move {moved}, health {healthBefore} -> {TotalHealth(0) + TotalHealth(1)}");
      Assert.Greater(moved.AsFloat, 5f, "NavMesh 이동");
      Assert.Less((TotalHealth(0) + TotalHealth(1)).AsFloat, healthBefore.AsFloat, "자동 공격 피해");
    }

    [UnityTest]
    public IEnumerator JointVictory_EndsAfterSixtySecondsOfFullAlliance() {
      yield return StartMatch(players: 3);
      yield return Ally(0, 1);
      yield return Ally(0, 2);
      yield return Ally(1, 2);

      var deadline = JointVictoryFrame();
      Assert.Greater(deadline, 0, "공동 승리 타이머 시작");
      Assert.Greater(deadline, Verified.Number);

      var second59 = deadline - Fps;
      yield return WaitFor(() => Verified.Number >= second59, 60f, "59초 경과");
      Assert.IsFalse(GameEnded(), "59초에는 경기 계속");
      yield return WaitFor(() => Verified.Number > deadline, 60f, "60초 경과");
      Assert.IsTrue(GameEnded(), "60초에 공동 승리");
    }

    [UnityTest]
    public IEnumerator JointVictory_BreakResetsTimer() {
      yield return StartMatch(players: 3);
      yield return Ally(0, 1);
      yield return Ally(0, 2);
      yield return Ally(1, 2);
      var deadline = JointVictoryFrame();
      Assert.Greater(deadline, 0);

      yield return Diplomacy(0, DiplomacyAction.Break, 1);
      Assert.AreEqual(0, JointVictoryFrame(), "파기하면 타이머 초기화");

      yield return WaitFor(() => Verified.Number > deadline + Fps, 60f, "원래 종료 시각 경과");
      Assert.IsFalse(GameEnded(), "파기 후 경기 계속");
    }

    [UnityTest]
    public IEnumerator JointVictory_Off_NeverEnds() {
      yield return StartMatch(players: 3, jointVictory: false);
      yield return Ally(0, 1);
      yield return Ally(0, 2);
      yield return Ally(1, 2);
      Assert.AreEqual(0, JointVictoryFrame());
    }

    [UnityTest]
    public IEnumerator Alliance_BlocksCombatAndBreakHasGrace() {
      yield return StartMatch(players: 3, jointVictory: false);
      yield return Ally(0, 1);
      Assert.AreEqual(Relation.Allied, DiplomacySystem.GetRelation(Verified, 0, 1));
      Assert.IsFalse(DiplomacySystem.CanAttack(Verified, 0, 1));

      yield return Diplomacy(0, DiplomacyAction.Break, 1);
      Assert.AreEqual(Relation.BreakPending, DiplomacySystem.GetRelation(Verified, 0, 1));
      Assert.IsFalse(DiplomacySystem.CanAttack(Verified, 0, 1), "유예 중 공격 불가");

      yield return WaitFrames(10 * Fps);
      Assert.AreEqual(Relation.Enemy, DiplomacySystem.GetRelation(Verified, 0, 1));
      Assert.IsTrue(DiplomacySystem.CanAttack(Verified, 0, 1));

      // 재동맹 금지 60초 안에는 제안이 거부된다.
      var funds = Funds(0);
      yield return Diplomacy(0, DiplomacyAction.Propose, 1);
      AssertRejected(0, ActionKind.Propose, RejectReason.Cooldown);
      Assert.AreEqual(funds, Funds(0), "거부된 제안은 비용 없음");
      yield return Diplomacy(1, DiplomacyAction.Accept, 0);
      Assert.AreEqual(Relation.Enemy, DiplomacySystem.GetRelation(Verified, 0, 1));
    }

    [UnityTest]
    public IEnumerator Proposal_Charges300_RejectRefunds150() {
      yield return StartMatch(players: 3);
      yield return Diplomacy(0, DiplomacyAction.Propose, 1);
      Assert.AreEqual(4700, Funds(0), "제안 300 차감");

      // 같은 상대에게 대기 중 제안은 1개만.
      yield return Diplomacy(0, DiplomacyAction.Propose, 1);
      AssertRejected(0, ActionKind.Propose, RejectReason.Invalid);
      Assert.AreEqual(4700, Funds(0));

      yield return Diplomacy(1, DiplomacyAction.Reject, 0);
      Assert.AreEqual(4850, Funds(0), "거절 시 150 환급");
      Assert.AreEqual(5000, Funds(1), "거절은 무료");
    }

    [UnityTest]
    public IEnumerator Proposal_ExpiresAfterThirtySeconds_Refunds150() {
      yield return StartMatch(players: 3);
      yield return Diplomacy(0, DiplomacyAction.Propose, 1);
      Assert.AreEqual(4700, Funds(0));

      yield return WaitFrames(28 * Fps);
      Assert.AreEqual(4700, Funds(0), "만료 전");
      yield return WaitFrames(3 * Fps);
      Assert.AreEqual(4850, Funds(0), "만료 시 150 환급");

      yield return Diplomacy(1, DiplomacyAction.Accept, 0);
      AssertRejected(1, ActionKind.Accept, RejectReason.Invalid);
      Assert.AreEqual(Relation.Enemy, DiplomacySystem.GetRelation(Verified, 0, 1));
    }

    [UnityTest]
    public IEnumerator Accept_Charges150AndFormsAlliance() {
      yield return StartMatch(players: 3);
      yield return Ally(0, 1);
      Assert.AreEqual(Relation.Allied, DiplomacySystem.GetRelation(Verified, 0, 1));
      Assert.AreEqual(4700, Funds(0), "수락되면 환급 없음");
      Assert.AreEqual(4850, Funds(1), "수락 150 차감");
    }

    [UnityTest]
    public IEnumerator Accept_InsufficientFunds_KeepsProposalPending() {
      yield return StartMatch(players: 3);
      yield return SpendBelow(1, 150);
      var funds = Funds(1);
      yield return Diplomacy(0, DiplomacyAction.Propose, 1);
      yield return Diplomacy(1, DiplomacyAction.Accept, 0);

      AssertRejected(1, ActionKind.Accept, RejectReason.InsufficientFunds);
      Assert.AreEqual(funds, Funds(1));
      Assert.AreEqual(Relation.Enemy, DiplomacySystem.GetRelation(Verified, 0, 1));

      // 제안은 계속 대기 중이라 거절하면 환급된다.
      yield return Diplomacy(1, DiplomacyAction.Reject, 0);
      Assert.AreEqual(4850, Funds(0));
    }

    [UnityTest]
    public IEnumerator Break_Charges500() {
      yield return StartMatch(players: 3, jointVictory: false);
      yield return Ally(0, 1);
      yield return Diplomacy(1, DiplomacyAction.Break, 0);
      Assert.AreEqual(Relation.BreakPending, DiplomacySystem.GetRelation(Verified, 0, 1));
      Assert.AreEqual(4350, Funds(1), "수락 150 + 파기 500");
      Assert.AreEqual(4700, Funds(0), "파기 비용은 파기하는 쪽만");
    }

    [UnityTest]
    public IEnumerator Break_InsufficientFunds_Fails() {
      yield return StartMatch(players: 3, jointVictory: false);
      yield return Ally(0, 1);
      yield return SpendBelow(1, 500);
      var funds = Funds(1);
      yield return Diplomacy(1, DiplomacyAction.Break, 0);
      AssertRejected(1, ActionKind.Break, RejectReason.InsufficientFunds);
      Assert.AreEqual(Relation.Allied, DiplomacySystem.GetRelation(Verified, 0, 1));
      Assert.AreEqual(funds, Funds(1));
    }

    [UnityTest]
    public IEnumerator Note_ChargesAndHasCooldown_InEveryMode() {
      yield return StartMatch();
      var received = new List<int>();
      QuantumEvent.Subscribe<EventNoteReceived>(_listener, e => received.Add(e.Receiver));

      yield return Send(0, new SendNoteCommand { PhraseId = 1, TargetMode = NoteTarget.Player, Receiver = 1 });
      Assert.AreEqual(4950, Funds(0), "1명 쪽지 50");
      Assert.AreEqual(1, received.Count);

      yield return Send(0, new SendNoteCommand { PhraseId = 1, TargetMode = NoteTarget.Player, Receiver = 1 });
      AssertRejected(0, ActionKind.Note, RejectReason.Cooldown);
      Assert.AreEqual(4950, Funds(0));
      Assert.AreEqual(1, received.Count);

      yield return WaitFrames(5 * Fps);
      yield return Send(0, new SendNoteCommand { PhraseId = 2, TargetMode = NoteTarget.Everyone });
      Assert.AreEqual(4850, Funds(0), "전체 쪽지 100");
      Assert.AreEqual(2, received.Count);

      // 받을 동맹이 없으면 돈을 받지 않는다.
      yield return WaitFrames(5 * Fps);
      yield return Send(0, new SendNoteCommand { PhraseId = 2, TargetMode = NoteTarget.Allies });
      AssertRejected(0, ActionKind.Note, RejectReason.Invalid);
      Assert.AreEqual(4850, Funds(0));
    }

    static bool Defeated(int player) => Verified.Global->Players.GetPointer(player)->Defeated;

    static FP BuildingHealth(EntityRef entity) => Verified.TryGet<Building>(entity, out var building) ? building.Health : FP._0;

    [UnityTest]
    public IEnumerator Proposal_TargetDefeated_Refunds150() {
      yield return StartMatch(players: 3, jointVictory: false);
      yield return Ally(0, 1);
      var headquarters = Headquarters(2);
      Game.SendCommand(0, new AttackTargetCommand { Units = Army(0, true).ToArray(), Target = headquarters });
      Game.SendCommand(1, new AttackTargetCommand { Units = Army(1, true).ToArray(), Target = headquarters });
      yield return WaitFor(() => BuildingHealth(headquarters) < 1000, 60f, "사령부 체력 감소");

      var proposedFrame = Verified.Number;
      yield return Diplomacy(0, DiplomacyAction.Propose, 2);
      Assert.AreEqual(4400, Funds(0), "제안 300 차감");

      yield return WaitFor(() => Defeated(2), 60f, "대상 패배");
      yield return WaitFrames(2);
      Assert.Less(Verified.Number - proposedFrame, 30 * Fps, "만료가 아니라 패배로 환급되어야 한다");
      Assert.AreEqual(4550, Funds(0), "대상 패배 시 150 환급");
    }

    [UnityTest]
    public IEnumerator TeamMode_RejectsDiplomacy_AllowsNotes() {
      yield return StartMatch(players: 4, teamMode: true);
      yield return WaitFrames(CommandFrames);
      Assert.AreEqual(Relation.Allied, DiplomacySystem.GetRelation(Verified, 0, 1), "같은 팀");
      Assert.AreEqual(Relation.Enemy, DiplomacySystem.GetRelation(Verified, 0, 2), "다른 팀");
      Assert.IsFalse(GameEnded(), "시작하자마자 끝나지 않는다");

      yield return Diplomacy(0, DiplomacyAction.Propose, 2);
      AssertRejected(0, ActionKind.Propose, RejectReason.ModeDisabled);
      Assert.AreEqual(5000, Funds(0));

      yield return Send(0, new SendNoteCommand { PhraseId = 0, TargetMode = NoteTarget.Allies });
      Assert.AreEqual(4900, Funds(0), "팀전에서도 쪽지 허용");
    }

    [UnityTest]
    public IEnumerator Duel_RejectsDiplomacy() {
      yield return StartMatch();
      yield return Diplomacy(0, DiplomacyAction.Propose, 1);
      AssertRejected(0, ActionKind.Propose, RejectReason.ModeDisabled);
      Assert.AreEqual(5000, Funds(0));
      Assert.AreEqual(Relation.Enemy, DiplomacySystem.GetRelation(Verified, 0, 1));
    }

    static void AssertRotationalSymmetry(MapSpec spec, int quarterTurns) {
      FPVector3 Turn(FPVector3 p) {
        for (var i = 0; i < quarterTurns; i++) {
          p = new FPVector3(-p.Z, p.Y, p.X);
        }
        return p;
      }
      foreach (var start in spec.StartPositions) {
        Assert.IsTrue(spec.StartPositions.Contains(Turn(start)), $"시작 위치 대칭 {start}");
      }
      foreach (var node in spec.ResourceNodes) {
        var turned = Turn(node.Shape.Center);
        Assert.IsTrue(spec.ResourceNodes.Any(n => n.Shape.Center == turned && n.Amount == node.Amount), $"노드 대칭 {node.Shape.Center}");
      }
    }

    static FP NodeFunds(FP amount) {
      var total = 0;
      foreach (var pair in Verified.GetComponentIterator<ResourceNode>()) {
        if (pair.Component.Remaining == amount) {
          total++;
        }
      }
      return total;
    }

    static void AssertNodesOnNavMesh(MapSpec spec) {
      var f = Verified;
      var navMesh = f.Map.GetNavMesh(f.FindAsset(f.RuntimeConfig.Rules).NavMeshName);
      var mask = NavMeshRegionMask.Default;
      foreach (var start in spec.StartPositions) {
        Assert.IsTrue(navMesh.Contains(start, mask, true), $"시작 위치 NavMesh 위 {start}");
      }
      foreach (var node in spec.ResourceNodes) {
        Assert.IsTrue(navMesh.Contains(node.Shape.Center, mask, true), $"노드 NavMesh 위 {node.Shape.Center}");
      }
    }

    /// <summary>from에서 to로 곧장 갈 수 없고, 주어진 경로들은 구간마다 막힘이 없어야 한다.</summary>
    static void AssertBlockedWithDetours(FPVector3 from, FPVector3 to, params FPVector3[][] detours) {
      var f = Verified;
      var navMesh = f.Map.GetNavMesh(f.FindAsset(f.RuntimeConfig.Rules).NavMeshName);
      var mask = NavMeshRegionMask.Default;
      Assert.IsFalse(f.Navigation.LineOfSight(from, to, mask, navMesh), $"직선 경로가 막혀야 한다 {from} -> {to}");
      Assert.GreaterOrEqual(detours.Length, 2, "우회로 2개 이상");
      foreach (var detour in detours) {
        var previous = from;
        foreach (var point in detour.Append(to)) {
          Assert.IsTrue(f.Navigation.LineOfSight(previous, point, mask, navMesh), $"우회로 구간 {previous} -> {point}");
          previous = point;
        }
      }
    }

    /// <summary>이동 속도 5 유닛을 보내 도착까지 걸린 시뮬레이션 초를 기록한다.</summary>
    IEnumerator CrossMap(int player, FPVector3 target, float maxSeconds, string label) {
      var unit = Army(player, true).First(e => Verified.FindAsset(Verified.Get<Unit>(e).Spec).MoveSpeed == 5);
      var start = Verified.Get<Transform3D>(unit).Position;
      var startFrame = Verified.Number;
      Game.SendCommand(player, new MoveUnitsCommand { Units = new[] { unit }, Target = target });

      var limit = startFrame + (int)(maxSeconds * Fps);
      yield return WaitFor(() => {
        Assert.Less(Verified.Number, limit, $"{label}: {maxSeconds}초 안에 도착 못 함");
        return Verified.Exists(unit) && (Verified.Get<Transform3D>(unit).Position - target).Magnitude < 3;
      }, 120f, label);

      var seconds = (Verified.Number - startFrame) / (float)Fps;
      Debug.Log($"[FW-TEST] {label}: straight {(target - start).Magnitude}, ideal {(target - start).Magnitude / 5}s, actual {seconds}s");
    }

    [UnityTest]
    public IEnumerator Map2P_LayoutAndCrossing() {
      yield return StartMatch(Map2PScene, 2, units: new[] { (0, "ff_eagle_tank", -50, -60) });
      var spec = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      Assert.AreEqual(2, spec.StartPositions.Length);
      Assert.AreEqual(new FPVector2(160, 160), spec.BoundsMax - spec.BoundsMin);
      Assert.AreEqual(9, Verified.ComponentCount<ResourceNode>());
      Assert.AreEqual(4, NodeFunds(5000).AsInt, "기지 노드");
      Assert.AreEqual(4, NodeFunds(4000).AsInt, "확장 노드");
      Assert.AreEqual(1, NodeFunds(8000).AsInt, "중앙 노드");
      AssertRotationalSymmetry(spec, 2);
      AssertNodesOnNavMesh(spec);
      AssertBlockedWithDetours(V(-55, -55), V(55, 55), new[] { V(-60, 60) }, new[] { V(60, -60) });
      yield return CrossMap(0, V(47, 47), 90f, "2P base to base");
    }

    [UnityTest]
    public IEnumerator Map4P_LayoutAndCrossing() {
      yield return StartMatch(Map4PScene, 4, units: new[] { (0, "ff_eagle_tank", -85, -95) });
      var spec = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      Assert.AreEqual(4, spec.StartPositions.Length);
      Assert.AreEqual(new FPVector2(240, 240), spec.BoundsMax - spec.BoundsMin);
      Assert.AreEqual(4, Verified.ComponentCount<Building>());
      Assert.AreEqual(17, Verified.ComponentCount<ResourceNode>());
      Assert.AreEqual(8, NodeFunds(5000).AsInt, "기지 노드");
      Assert.AreEqual(8, NodeFunds(4000).AsInt, "확장 노드");
      Assert.AreEqual(1, NodeFunds(12000).AsInt, "중앙 노드");
      AssertRotationalSymmetry(spec, 1);
      AssertNodesOnNavMesh(spec);
      // 이웃 기지. 90도 회전 대칭이라 나머지 세 쌍도 같다.
      AssertBlockedWithDetours(V(-90, -90), V(90, -90),
        new[] { V(-45, -114), V(45, -114) },
        new[] { V(-45, -66), V(45, -66) });
      // 맞은편 기지.
      AssertBlockedWithDetours(V(-90, -90), V(90, 90),
        new[] { V(-66, -40), V(-66, -12), V(1, 1), V(66, 12), V(66, 40) },
        new[] { V(-40, -66), V(-12, -66), V(1, 1), V(12, 66), V(40, 66) });
      yield return CrossMap(0, V(82, 82), 120f, "4P opposite base");
    }
  }
}
