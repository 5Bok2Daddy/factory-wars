namespace FactoryWars.Tests {
  using System.Collections;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using FactoryWars.View;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 30 온라인. 앞 4개는 오프라인(일반 테스트), Online_Live_*는 실제 Photon 서버에 붙어서(-testCategory Online) 돈다.
  /// 끊김 시험은 신호 함수를 직접 부르지 않고, 로컬 세션에서 플레이어를 실제로 빼고 넣어 Quantum PlayerConnectedSystem 경로로 확인한다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    [Test]
    public void Online_RoomCodeGenerationAndValidation() {
      for (var seed = 0; seed < 10; seed++) {
        var code = OnlineMatchManager.GenerateRoomCode(seed);
        Assert.AreEqual(6, code.Length, "방 코드는 정확히 6자리");
        Assert.IsTrue(OnlineMatchManager.ValidateRoomCode(code), $"유효한 방 코드: {code}");
        Assert.IsFalse(code.Contains("0") || code.Contains("O") || code.Contains("1") || code.Contains("I"), "가독성을 위해 0/O, 1/I 제외");
      }
      Assert.IsFalse(OnlineMatchManager.ValidateRoomCode(""), "빈 코드는 유효하지 않음");
      Assert.IsFalse(OnlineMatchManager.ValidateRoomCode("123"), "6자리 미만은 유효하지 않음");
      Assert.IsFalse(OnlineMatchManager.ValidateRoomCode("ABCDEFGH"), "6자리 초과는 유효하지 않음");
      Assert.IsFalse(OnlineMatchManager.ValidateRoomCode("AB#4CD"), "특수문자 포함 코드는 유효하지 않음");
    }

    /// <summary>버전은 하드코딩 문자열이 아니라 시뮬레이션 코드·데이터 해시다. 파일 한 글자가 바뀌면 해시가 바뀐다.</summary>
    [Test]
    public void Online_VersionHashFromSimulationAndData() {
      BuildVersion.Reset();
      var current = BuildVersion.Current;
      StringAssert.StartsWith(BuildVersion.Prefix, current);
      Assert.IsTrue(OnlineMatchManager.CheckVersion(current), "같은 해시는 입장 허용");
      Assert.IsFalse(OnlineMatchManager.CheckVersion("FW-1.0.0"), "예전 고정 문자열은 거부");

      var files = BuildVersion.Files(Application.dataPath);
      Assert.IsTrue(files.Any(f => f.EndsWith("FactoryWars.qtn")), "qtn 포함");
      Assert.IsTrue(files.Any(f => f.EndsWith("game_rules.asset")), "규칙 데이터 포함");
      Assert.Contains("QuantumUser/Resources/Map4P.asset", files, "베이크된 맵 포함");
      Assert.Contains("QuantumUser/Resources/Map4P_WaterNavMesh_data.asset", files, "수상 NavMesh 포함");
      Assert.IsFalse(files.Any(f => f.Contains("/Art/") || f.Contains("/Visual/")), "그래픽 에셋은 제외(화면만 바뀌면 같이 놀 수 있음)");

      // 임시 복사본에서 규칙 데이터 한 줄을 바꾸면 해시가 달라진다.
      var temp = Path.Combine(Path.GetTempPath(), "fw-version-" + System.Guid.NewGuid().ToString("N"));
      try {
        foreach (var file in files) {
          var target = Path.Combine(temp, file);
          Directory.CreateDirectory(Path.GetDirectoryName(target));
          File.Copy(Path.Combine(Application.dataPath, file), target);
        }
        Assert.AreEqual(current, BuildVersion.Compute(temp), "복사본은 같은 해시");
        var nav = Path.Combine(temp, "QuantumUser/Resources/Map4P_NavMesh_data.asset");
        File.AppendAllText(nav, "\n# changed navmesh\n");
        Assert.AreNotEqual(current, BuildVersion.Compute(temp), "재베이크한 NavMesh는 다른 해시");
        File.Copy(Path.Combine(Application.dataPath, "QuantumUser/Resources/Map4P_NavMesh_data.asset"), nav, true);
        var rules = Path.Combine(temp, files.First(f => f.EndsWith("game_rules.asset")));
        File.AppendAllText(rules, "\n# changed\n");
        Assert.AreNotEqual(current, BuildVersion.Compute(temp), "데이터가 바뀌면 다른 해시");
      } finally {
        Directory.Delete(temp, true);
      }
    }

    static PlayerState StatePtr(int player) => *Verified.Global->Players.GetPointer(player);

    /// <summary>
    /// 끊김 → 봇 대체 → 재접속 → 다시 끊김 → 60초 뒤 기권. 사람 둘인 로컬 세션에서 플레이어 1을 실제로 빼고 넣는다.
    /// </summary>
    [UnityTest]
    public IEnumerator Online_DisconnectBotTakeoverReconnectAndForfeit() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      var rules = Verified.FindAsset(Verified.RuntimeConfig.Rules);
      Assert.AreEqual(60, rules.DisconnectForfeitSeconds, "기권 시간은 규칙 데이터");
      yield return WaitFrames(Fps);
      Assert.IsFalse(StatePtr(1).IsBot, "처음엔 사람");

      Game.RemovePlayer(1);
      yield return WaitFor(() => StatePtr(1).DisconnectedFrame > 0, 20f, "끊김 감지(PlayerConnectedSystem)");
      Assert.IsTrue(StatePtr(1).IsBot, "끊기면 봇이 조종");
      Assert.AreEqual(rules.DisconnectBotDifficulty, StatePtr(1).Difficulty, "대체 봇 난이도는 규칙 데이터");
      var producedBefore = StatePtr(1).UnitsProduced;
      yield return WaitFrames(30 * Fps, 120f);
      Assert.Greater(StatePtr(1).UnitsProduced, producedBefore, "대체 봇이 실제로 생산한다");
      Assert.IsFalse(StatePtr(1).Forfeited, "60초 전에는 기권 아님");

      Game.AddPlayer(1, new RuntimePlayer { PlayerNickname = "back", Faction = StatePtr(1).Faction });
      yield return WaitFor(() => StatePtr(1).DisconnectedFrame == 0, 20f, "재접속 감지");
      Assert.IsFalse(StatePtr(1).IsBot, "돌아오면 다시 사람");
      Assert.IsTrue(StatePtr(1).Active, "자리·건물 그대로");

      Game.RemovePlayer(1);
      yield return WaitFor(() => StatePtr(1).DisconnectedFrame > 0, 20f, "두 번째 끊김");
      yield return WaitFor(() => GameEnded(), 120f, "60초 뒤 기권으로 경기 종료");
      Assert.IsTrue(StatePtr(1).Forfeited && StatePtr(1).Defeated, "끊긴 쪽 기권 패");
      Assert.AreEqual(1, WinnerMask(), "남은 사람이 승리");
    }

    [UnityTest]
    public IEnumerator Online_SurrenderCommandEndsDuel() {
      yield return StartMatch(Map4PScene, 2, bots: new[] { BotDifficulty.Normal }, truce: 0);
      yield return Send(0, new SurrenderCommand());
      yield return WaitFor(() => GameEnded(), 20f, "기권 후 종료");
      Assert.IsTrue(StatePtr(0).Defeated, "기권한 쪽 패배");
      Assert.AreEqual(2, WinnerMask(), "상대 승리");
    }

    // ---------- 실제 Photon 서버 (명시 실행: -testCategory Online) ----------

    static IEnumerator Await(Task task, float seconds, string label) {
      var end = Time.realtimeSinceStartup + seconds;
      while (task.IsCompleted == false) {
        if (Time.realtimeSinceStartup > end) {
          Assert.Fail($"시간 초과: {label}");
        }
        yield return null;
      }
      if (task.IsFaulted) {
        Assert.Fail($"{label}: {task.Exception?.GetBaseException().Message}");
      }
    }

    static void OnlineLog(string line) {
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "online"));
      Directory.CreateDirectory(folder);
      File.AppendAllLines(Path.Combine(folder, "online-tests.log"), new[] { $"{System.DateTime.Now:HH:mm:ss} {line}" });
      Debug.Log("[FW-Online-Test] " + line);
    }

    /// <summary>방 만들기 → 코드로 참가 → 대기실 속성 → 없는 코드 거부. 두 클라이언트를 한 프로세스에 띄운다.</summary>
    [UnityTest, Explicit, Category("Online"), Timeout(300000)]
    public IEnumerator Online_Live_CreateJoinLobby() {
      Time.timeScale = 1f;
      // 없는 방 참가 시험에서 Photon이 남기는 오류 로그는 예상된 것이다.
      LogAssert.ignoreFailingMessages = true;
      var host = OnlineRoom.CreateExtra("HostRoom");
      var guest = OnlineRoom.CreateExtra("GuestRoom");
      try {
        var create = host.Create(new MatchSettings { FactionIndex = 0 });
        yield return Await(create, 60f, "방 만들기");
        Assert.IsTrue(((Task<bool>)create).Result, $"방 만들기 실패: {host.Error}");
        OnlineLog($"방 생성 {host.Code} 지역 {host.Client.CurrentRegion} 핑 {host.Ping}ms 버전 {BuildVersion.Current}");
        Assert.IsTrue(host.IsHost);

        var missing = guest.Join("ZZZZZ9", new MatchSettings());
        yield return Await(missing, 60f, "없는 방");
        Assert.IsFalse(((Task<bool>)missing).Result, "없는 코드는 실패");
        OnlineLog($"없는 코드 거부: {guest.Error}");

        var join = guest.Join(host.Code.ToLowerInvariant(), new MatchSettings { FactionIndex = 1 });
        yield return Await(join, 60f, "참가");
        Assert.IsTrue(((Task<bool>)join).Result, $"참가 실패: {guest.Error}");
        Assert.IsFalse(guest.IsHost);
        yield return WaitForReal(() => host.Entries().Count == 2 && guest.Entries().Count == 2, 20f, "서로 보임");
        yield return WaitForReal(() => host.Entries().Any(e => e.IsLocal == false && e.Faction == 1), 20f, "상대 진영 속성 전달");
        Assert.AreEqual("모두 준비해야 합니다", host.StartBlocker(), "참가자가 준비 전이면 시작 불가");
        guest.SetMine(1, 1, true);
        yield return WaitForReal(() => host.StartBlocker() == null, 20f, "준비 전달");
        host.HostSet(MatchMode.Duel, 0, new int[0]);
        yield return WaitForReal(() => guest.TruceMinutes == 0, 20f, "방 설정 전달");
        OnlineLog($"참가·대기실 속성 확인. 손님 핑 {guest.Ping}ms");
      } finally {
        var a = host.Leave();
        var b = guest.Leave();
        Object.Destroy(host.gameObject);
        Object.Destroy(guest.gameObject);
      }
    }

    static bool BothActive(Frame frame) => frame.Global->Players.GetPointer(0)->Active && frame.Global->Players.GetPointer(1)->Active;

    static IEnumerator WaitForReal(System.Func<bool> condition, float seconds, string label) {
      var end = Time.realtimeSinceStartup + seconds;
      while (condition() == false) {
        if (Time.realtimeSinceStartup > end) {
          Assert.Fail($"시간 초과: {label}");
        }
        yield return null;
      }
    }

    /// <summary>
    /// 두 클라이언트가 실제 Quantum 온라인 세션을 열고(1대1, 휴전 0), 같은 프레임의 체크섬이 같은지 본다.
    /// 건설 명령부터 공병의 자동 배치 및 첫 자원 반영까지 비교하고 입력 지연·핑을 기록한다.
    /// </summary>
    [UnityTest, Explicit, Category("Online"), Timeout(600000)]
    public IEnumerator Online_Live_SessionChecksumsMatch() {
      Time.timeScale = 1f;
      LogAssert.ignoreFailingMessages = true;
#if UNITY_EDITOR
      yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(TestScene, new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
      var local = Object.FindAnyObjectByType<QuantumRunnerLocalDebug>();
      local.enabled = false;
      QuantumRunner.ShutdownAll();
      var factions = local.LocalPlayers.Take(2).Select(p => p.Faction).ToArray();
      var template = local.RuntimeConfig;
      template.Map = Object.FindAnyObjectByType<QuantumMapData>().AssetRef;
      template.OverrideStartingResources = true;
      template.StartingResources = Stock(5000, 5000, 5000, 500);
      template.StartingTier = 3;

      var host = OnlineRoom.CreateExtra("HostRoom");
      var guest = OnlineRoom.CreateExtra("GuestRoom");
      host.KeepScenario = true;
      guest.KeepScenario = true;
      var errors = 0;
      var subscription = QuantumCallback.SubscribeManual<CallbackChecksumError>(e => errors++);
      try {
        var create = host.Create(new MatchSettings { FactionIndex = 0 });
        yield return Await(create, 60f, "방 만들기");
        Assert.IsTrue(((Task<bool>)create).Result, host.Error);
        var join = guest.Join(host.Code, new MatchSettings { FactionIndex = 1 });
        yield return Await(join, 60f, "참가");
        Assert.IsTrue(((Task<bool>)join).Result, guest.Error);
        guest.SetMine(1, 1, true);
        host.HostSet(MatchMode.Duel, 0, new int[0]);
        yield return WaitForReal(() => host.StartBlocker() == null && host.TruceMinutes == 0, 20f, "시작 가능");
        Assert.IsTrue(host.HostStart());
        yield return WaitForReal(() => host.StartReceived && guest.StartReceived, 20f, "시작 신호 전달");

        var a = host.StartSession(template, factions);
        var b = guest.StartSession(template, factions);
        yield return Await(Task.WhenAll(a, b), 90f, "세션 시작");
        Assert.IsTrue(a.Result && b.Result, $"세션 실패: {host.Error} / {guest.Error}");
        var gameA = host.Runner.Game;
        var gameB = guest.Runner.Game;
        yield return WaitForReal(() => gameA.Frames.Verified != null && gameB.Frames.Verified != null
          && BothActive(gameA.Frames.Verified) && gameA.GetLocalPlayers().Count > 0, 60f, "두 사람 합류");
        var frame = gameA.Frames.Verified;
        var player = gameA.GetLocalPlayers()[0];
        var engineer = EntityRef.None;
        foreach (var pair in frame.GetComponentIterator<Unit>()) {
          if (pair.Component.Owner != player || frame.Has<Worker>(pair.Entity) == false) continue;
          engineer = pair.Entity;
          break;
        }
        Assert.AreNotEqual(EntityRef.None, engineer, "온라인 시작 공병");
        var origin = frame.Get<Transform3D>(engineer).Position;
        var extractor = frame.FindAsset(frame.GetPlayerData(player).Faction).Buildings
          .First(reference => frame.FindAsset(reference).Role == BuildingRole.IronExtractor);
        var spec = frame.FindAsset(extractor);
        var node = EntityRef.None;
        var center = default(FPVector3);
        var nearestDistance = FP.MaxValue;
        foreach (var pair in frame.GetComponentIterator<ResourceNode>()) {
          if (pair.Component.Kind != ResourceKind.Iron) continue;
          var position = frame.Get<Transform3D>(pair.Entity).Position;
          var distance = FactoryWarsUtil.FlatDistanceSqr(origin, position);
          if (distance >= nearestDistance || ProductionSystem.CanPlace(frame, spec, position, player) == false) continue;
          node = pair.Entity;
          center = position;
          nearestDistance = distance;
        }
        Assert.AreNotEqual(EntityRef.None, node, "온라인 철 노드에 건설 가능");
        var initialRemaining = frame.Get<ResourceNode>(node).Remaining;
        gameA.SendCommand(0, new BuildCommand { Engineer = engineer, Building = extractor, Position = center });

        var startReal = Time.realtimeSinceStartup;
        var checks = 0;
        var lastSharedFrame = -1;
        var productionFrame = 0;
        // 생산이 시작된 뒤에도 동기화 비교를 계속한다.
        while (productionFrame == 0 || gameA.Frames.Verified.Number < productionFrame + 5 * Fps) {
          var fa = gameA.Frames.Verified;
          var fb = gameB.Frames.Verified;
          if (fa.Number == fb.Number && fa.Number != lastSharedFrame) {
            Assert.AreEqual(fa.CalculateChecksum(), fb.CalculateChecksum(), $"프레임 {fa.Number} 체크섬");
            lastSharedFrame = fa.Number;
            checks++;
            if (productionFrame == 0 && fa.Get<ResourceNode>(node).Remaining < initialRemaining) {
              var site = fa.Get<Unit>(engineer).Garrison;
              Assert.AreNotEqual(EntityRef.None, site, "별도 진입 명령 없이 공병 배치");
              Assert.AreEqual(node, fb.Get<Building>(site).ExtractNode);
              Assert.AreEqual(WorkerTask.Station, fb.Get<Worker>(engineer).Task);
              Assert.IsTrue(StateIn(fb, player).Earned.Iron > FP._0, "상대 클라이언트에서 소유자 철 자원 증가");
              productionFrame = fa.Number;
            }
          }
          if (Time.realtimeSinceStartup - startReal > 120f) {
            Assert.Fail("온라인 채굴 생산까지 진행하지 않음");
          }
          yield return null;
        }
        var delay = gameA.Frames.Predicted.Number - gameA.Frames.Verified.Number;
        OnlineLog($"세션 체크섬 일치 {checks}회, 디싱크 {errors}, 핑 {host.Ping}/{guest.Ping}ms, 예측-확정 {delay}프레임, 입력 지연 설정 {gameA.Session.SessionConfig.InputDelayMin}~{gameA.Session.SessionConfig.InputDelayMax}틱");
        Assert.Greater(checks, 10, "비교 횟수");
        Assert.AreEqual(0, errors, "체크섬 오류(디싱크) 없음");
      } finally {
        subscription.Dispose();
        var a = host.Leave();
        var b = guest.Leave();
        Object.Destroy(host.gameObject);
        Object.Destroy(guest.gameObject);
      }
    }
  }
}
