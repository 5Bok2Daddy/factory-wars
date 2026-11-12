namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using NUnit.Framework;
  using Quantum;
  using UnityEngine;
  using UnityEngine.Profiling;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 30-3-6 안정성: 20분(경기 시간) AI 4인 대전을 화면과 함께 돌리며 메모리·GC·프레임 시간 기록.
  /// 에디터 측정이라 실기기 수치가 아니다(경향만 본다). -testCategory Stability, 결과 Game-logs/stability/.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    [UnityTest, Explicit, Category("Stability"), Timeout(3000000)]
    public IEnumerator Stability_TwentyMinuteMemoryAndGc() {
      var bots = Enumerable.Repeat(BotDifficulty.Normal, 3).ToArray();
      yield return StartMatch(Map4PScene, 4, bots: bots, seed: 9100, truce: -1);
      Time.timeScale = 4f;
      var rows = new List<string> { "minute,mono_mb,total_alloc_mb,gc_count,max_frame_ms,avg_frame_ms,units" };
      var gcStart = System.GC.CollectionCount(0);
      var nextMinute = 60 * Fps;
      var frameTimes = new List<float>();
      float firstMono = 0f;
      while (Verified.Number < 20 * 60 * Fps && GameEnded() == false) {
        frameTimes.Add(Time.unscaledDeltaTime * 1000f);
        if (Verified.Number >= nextMinute) {
          nextMinute += 60 * Fps;
          var mono = Profiler.GetMonoUsedSizeLong() / 1048576f;
          if (firstMono == 0f) {
            firstMono = mono;
          }
          rows.Add($"{Verified.Number / (60 * Fps)},{mono:0.0},{Profiler.GetTotalAllocatedMemoryLong() / 1048576f:0.0},{System.GC.CollectionCount(0) - gcStart},{frameTimes.Max():0.0},{frameTimes.Average():0.0},{Verified.ComponentCount<Unit>()}");
          frameTimes.Clear();
        }
        yield return null;
      }
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "stability"));
      Directory.CreateDirectory(folder);
      File.WriteAllLines(Path.Combine(folder, "memory-20min.csv"), rows);
      Debug.Log("[FW-Stability]\n" + string.Join("\n", rows));
      Assert.Greater(rows.Count, 3, "몇 분 이상 기록");
    }
  }
}
