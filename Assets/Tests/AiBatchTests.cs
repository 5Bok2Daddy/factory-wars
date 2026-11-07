namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>
  /// AI 대 AI 자동 대전. 시간이 오래 걸려 일반 테스트에서 빠지고, -testCategory AiBatch로 따로 돌린다.
  /// 시뮬레이션은 건드리지 않고 프레임을 읽어 Game-logs/ai에 기록한다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    const float AiTimeScale = 40f;
    const int AiMaxSeconds = 30 * 60;
    const int AiCheckSeconds = 5;

    static readonly int[] AiRuns = Enumerable.Range(0, 10).ToArray();

    static string AiFolder() {
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "ai"));
      Directory.CreateDirectory(folder);
      return folder;
    }

    static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    [UnityTest, Explicit, Category("AiBatch"), Timeout(3000000)]
    public IEnumerator AiBatch_TwoPlayer([ValueSource(nameof(AiRuns))] int run) {
      yield return PlayAiMatch(Map2PScene, "2P", 2, run);
    }

    [UnityTest, Explicit, Category("AiBatch"), Timeout(3000000)]
    public IEnumerator AiBatch_FourPlayer([ValueSource(nameof(AiRuns))] int run) {
      yield return PlayAiMatch(Map4PScene, "4P", 4, run);
    }

    IEnumerator PlayAiMatch(string scene, string map, int players, int run) {
      var bots = Enumerable.Repeat(BotDifficulty.Normal, players).ToArray();
      yield return StartMatch(scene, players, bots: bots, seed: 7000 + run, swapFactions: run % 2 == 1, truce: -1);
      Time.timeScale = AiTimeScale;
      var view = Object.FindAnyObjectByType<FactoryWars.View.BattleView>();
      if (view != null) {
        view.enabled = false;
      }

      var firsts = new Dictionary<string, float>();
      var minutes = new List<string>();
      var nextCheck = 0;
      var nextMinute = 60 * Fps;
      var realStart = Time.realtimeSinceStartup;
      while (GameEnded() == false && Verified.Number < AiMaxSeconds * Fps) {
        var frame = Verified.Number;
        if (frame >= nextCheck) {
          nextCheck = frame + AiCheckSeconds * Fps;
          RecordFirsts(firsts, players, frame / (float)Fps);
        }
        if (frame >= nextMinute) {
          nextMinute += 60 * Fps;
          for (var i = 0; i < players; i++) {
            minutes.Add(MinuteRow(map, run, frame / (60 * Fps), i));
          }
        }
        yield return null;
      }

      var seconds = (GameEnded() ? EndFrame() : Verified.Number) / (float)Fps;
      var winners = GameEnded() ? string.Join("+", Enumerable.Range(0, players).Where(i => (WinnerMask() & (1 << i)) != 0).Select(i => FactionOf(i).FactionId)) : "timeout";
      var alive = Enumerable.Range(0, players).Count(i => StateOf(i).Defeated == false);
      var seats = string.Join("+", Enumerable.Range(0, players).Select(i => FactionOf(i).FactionId));
      var firstText = string.Join(";", firsts.OrderBy(p => p.Value).Select(p => $"{p.Key}={F(p.Value)}"));
      var endText = string.Join(" ", Enumerable.Range(0, players).Select(EndState));
      var row = $"{map},{run},{F(seconds)},{winners},{alive},{seats},{firstText},{endText}";
      File.AppendAllLines(Path.Combine(AiFolder(), "matches.csv"), new[] { row });
      File.AppendAllLines(Path.Combine(AiFolder(), "minutes.csv"), minutes);
      Debug.Log($"[FW-AI] {row} real {F(Time.realtimeSinceStartup - realStart)}s");
      Assert.Greater(seconds, 0f);
    }

    /// <summary>끝났을 때 자리별 상태. 안 끝난 경기의 원인을 찾는 데 쓴다.</summary>
    static string EndState(int player) {
      var s = StateOf(player);
      var buildings = 0;
      foreach (var pair in Verified.GetComponentIterator<Building>()) {
        buildings += pair.Component.Owner == player ? 1 : 0;
      }
      return $"p{player}:hq{(Headquarters(player) != EntityRef.None ? 1 : 0)}/b{buildings}/u{Army(player, true).Count}/e{Engineers(player).Count}/i{s.Stock.Iron.AsInt}/w{s.Stock.Wood.AsInt}/s{s.Stock.Steel.AsInt}";
    }

    static string MinuteRow(string map, int run, int minute, int player) {
      var s = StateOf(player);
      return string.Join(",", map, run.ToString(), minute.ToString(), player.ToString(), FactionOf(player).FactionId,
        F(s.Earned.Iron.AsFloat), F(s.Earned.Wood.AsFloat), F(s.Earned.Steel.AsFloat), F(s.Earned.Power.AsFloat),
        F(s.PowerRate.AsFloat), F(s.OutageSeconds.AsFloat), s.OutageCount.ToString(),
        F(s.PopulationUsed.AsFloat), s.UnitsProduced.ToString(), s.UnitsLost.ToString(), s.BuildingsLost.ToString(), (s.Defeated ? "1" : "0"));
    }

    /// <summary>주요 건물과 유닛이 처음 나타난 시각을 진영별로 남긴다.</summary>
    static void RecordFirsts(Dictionary<string, float> firsts, int players, float seconds) {
      void Mark(string key) {
        if (firsts.ContainsKey(key) == false) {
          firsts[key] = seconds;
        }
      }
      foreach (var pair in Verified.GetComponentIterator<Building>()) {
        if (pair.Component.ConstructionRemaining <= Photon.Deterministic.FP._0) {
          var role = Verified.FindAsset(pair.Component.Spec).Role;
          if (role != BuildingRole.Headquarters) {
            Mark($"{FactionOf(pair.Component.Owner).FactionId[0]}:{role}");
          }
        }
      }
      foreach (var pair in Verified.GetComponentIterator<Unit>()) {
        var spec = Verified.FindAsset(pair.Component.Spec);
        if (spec.Size >= UnitSize.Medium) {
          Mark($"{FactionOf(pair.Component.Owner).FactionId[0]}:{spec.Size}{(spec.IsAir ? "Air" : "")}");
        }
      }
    }
  }
}
