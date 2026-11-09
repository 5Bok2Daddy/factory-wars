namespace FactoryWars.Tests {
  using System.Collections;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 27 K-2 전장 손상 지도. 화면 전용이고 synced 이벤트(확정 프레임에서 한 번)만 쓴다.
  /// 같은 시드 경기를 두 번 돌려 같은 프레임까지의 손상 지도가 같은지 본다(리플레이 결정론).
  /// 시뮬레이션 상태는 바꾸지 않으므로 체크섬에는 영향이 없다(이벤트는 프레임 상태가 아니다).
  /// </summary>
  public unsafe partial class LocalMatchTests {
    [UnityTest, Timeout(600000)]
    public IEnumerator BattleScars_SameSeedSameMap() {
      const int seconds = 600;
      var results = new (int Hash, int Damaged, int Events)[2];
      for (var run = 0; run < 2; run++) {
        yield return StartMatch(Map4PScene, 2, bots: new[] { BotDifficulty.Normal, BotDifficulty.Normal }, seed: LookdevSeed, truce: -1);
        yield return WaitForBattle();
        Assert.IsNotNull(BattleScars.Active, "손상 지도");
        BattleScars.Active.LastTick = seconds * Fps;
        Time.timeScale = 30f;
        yield return WaitFor(() => Verified.Number >= seconds * Fps + 10, 150f, $"{seconds}초 진행");
        yield return null;
        yield return null;
        var scars = BattleScars.Active;
        results[run] = (scars.Hash(), scars.Damaged, scars.Events);
        Debug.Log($"[FW-TEST] BattleScars run {run}: hash {results[run].Hash} damaged {results[run].Damaged} events {results[run].Events}");
        TearDown();
        yield return null;
      }
      Assert.Greater(results[0].Events, 0, "10분 안에 손상 이벤트가 있다");
      Assert.Greater(results[0].Damaged, 0, "손상된 칸이 있다");
      Assert.AreEqual(results[0], results[1], "같은 시드·같은 프레임까지면 같은 손상 지도");
    }
  }
}
