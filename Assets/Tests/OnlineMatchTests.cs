namespace FactoryWars.Tests {
  using System.Collections;
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
  /// 항목 28 온라인 6자리 방 코드 대전 테스트.
  /// 1. 6자리 방 코드 생성·검증
  /// 2. 버전 호환성 해시 검사
  /// 3. 연결 끊김 시 봇 대체 (Bot Takeover) 및 재접속
  /// </summary>
  public unsafe partial class LocalMatchTests {
    [Test]
    public void Online_RoomCodeGenerationAndValidation() {
      // 6자리 방 코드 생성 및 형식 검증
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

    [Test]
    public void Online_VersionHashVerification() {
      Assert.IsTrue(OnlineMatchManager.CheckVersion("FW-1.0.0"), "버전이 같으면 입장 허용");
      Assert.IsFalse(OnlineMatchManager.CheckVersion("FW-0.9.0"), "버전이 다르면 입장 거부");
      Assert.IsFalse(OnlineMatchManager.CheckVersion("FW-2.0.0"), "버전이 다르면 입장 거부");
    }

    static unsafe bool IsPlayerBot(Frame f, int player) => f.Global->Players.GetPointer(player)->IsBot;
    static unsafe BotDifficulty GetPlayerDifficulty(Frame f, int player) => f.Global->Players.GetPointer(player)->Difficulty;
    static unsafe FPVector3 GetBotHome(Frame f, int player) => f.Global->Bots.GetPointer(player)->Home;

    [UnityTest]
    public IEnumerator Online_PlayerDisconnectBotTakeoverAndReconnect() {
      yield return StartMatch(Map4PScene, 2, bots: new[] { BotDifficulty.Normal }, truce: 0);
      var f = Verified;
      var botSystem = new BotSystem();

      // 플레이어 0(사람)의 초기 상태 확인
      Assert.IsFalse(IsPlayerBot(f, 0), "플레이어 0은 초기 사람(호스트)");

      // 1. 플레이어 0 연결 끊김 신호 발생 -> 봇 대체 동작 검증
      botSystem.OnPlayerDisconnected(f, 0);
      Assert.IsTrue(IsPlayerBot(f, 0), "연결 끊김 시 즉시 봇으로 전환");
      Assert.AreEqual(BotDifficulty.Normal, GetPlayerDifficulty(f, 0), "대체 봇 난이도는 보통으로 설정");

      Assert.AreNotEqual(FPVector3.Zero, GetBotHome(f, 0), "봇 본진 좌표가 유효하게 설정됨");

      // 2. 봇 대체 후 프레임 진행 확인 (시뮬레이션 예외 없이 정상 연산)
      yield return WaitFrames(Fps);

      // 3. 플레이어 0 재접속 신호 발생 -> 사람 복귀
      botSystem.OnPlayerConnected(f, 0);
      Assert.IsFalse(IsPlayerBot(f, 0), "재접속 시 다시 사람으로 복귀");
    }
  }
}
