namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using NUnit.Framework;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 33 원인 규명용 측정(-testCategory Probe): 같은 인구의 1단계 군대끼리 빈 들판에서 공격 이동으로 맞붙였을 때 남는 인구.
  /// AI 대전에서 대륙 연방이 휴전 직후 첫 교전에서 크게 지는 까닭이 유닛 상성인지 본다. 결과는 Game-logs/probe/matchup.csv.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static readonly (string Name, (string Unit, int Count)[] Free, (string Unit, int Count)[] Continental)[] MatchupCases = {
      ("레인저 20 대 민병대 40(인구 20:20)", new[] { ("ff_ranger_squad", 20) }, new[] { ("cf_militia", 40) }),
      ("레인저 20 대 민병대 24+화염 8(인구 20:20)", new[] { ("ff_ranger_squad", 20) }, new[] { ("cf_militia", 24), ("cf_flame_squad", 8) }),
      ("레인저 14+버기 6 대 민병대 24+오토바이 8(인구 20:20)", new[] { ("ff_ranger_squad", 14), ("ff_scout_buggy", 6) }, new[] { ("cf_militia", 24), ("cf_scout_bike", 8) }),
      ("이글 전차 6 대 용린 전차 6(인구 18:18)", new[] { ("ff_eagle_tank", 6) }, new[] { ("cf_dragonscale_tank", 6) }),
      ("레인저 20 대 민병대 60(인구 20:30, 대륙 인구 한도 1.2배·값 비슷)", new[] { ("ff_ranger_squad", 20) }, new[] { ("cf_militia", 60) }),
    };

    static IEnumerable<int> MatchupIndexes() => Enumerable.Range(0, MatchupCases.Length);

    static float PopulationOf(int owner, float minZ) {
      var total = 0f;
      foreach (var pair in Verified.GetComponentIterator<Unit>()) {
        if (pair.Component.Owner == owner && Verified.Get<Transform3D>(pair.Entity).Position.Z.AsFloat > minZ) {
          total += Verified.FindAsset(pair.Component.Spec).Population.AsFloat;
        }
      }
      return total;
    }

    [UnityTest, Explicit, Category("Probe"), Timeout(600000)]
    public IEnumerator Probe_TierOneFieldBattle([ValueSource(nameof(MatchupIndexes))] int index) {
      var test = MatchupCases[index];
      var units = new List<(int, string, int, int)>();
      void Place(int owner, (string Unit, int Count)[] list, int x) {
        var slot = 0;
        foreach (var (unit, count) in list) {
          for (var i = 0; i < count; i++, slot++) {
            units.Add((owner, unit, x + (slot / 12) * 2 * (owner == 0 ? -1 : 1), 24 + (slot % 12) * 2));
          }
        }
      }
      Place(0, test.Free, -16);
      Place(1, test.Continental, 16);
      yield return StartMatch(units: units.ToArray());
      var before = (PopulationOf(0, 20), PopulationOf(1, 20));
      var free = Army(0, true).Where(u => Verified.Get<Transform3D>(u).Position.Z.AsFloat > 20).ToArray();
      var continental = Army(1, true).Where(u => Verified.Get<Transform3D>(u).Position.Z.AsFloat > 20).ToArray();
      Game.SendCommand(0, new MoveUnitsCommand { Units = free.Take(MoveUnitsCommand.MaxUnits).ToArray(), Target = V(0, 35), AttackMove = true });
      Game.SendCommand(1, new MoveUnitsCommand { Units = continental.Take(MoveUnitsCommand.MaxUnits).ToArray(), Target = V(0, 35), AttackMove = true });
      var start = Verified.Number;
      yield return WaitFor(() => PopulationOf(0, 20) <= 0f || PopulationOf(1, 20) <= 0f || Verified.Number > start + 180 * Fps, 120f, "교전 종료");
      var after = (PopulationOf(0, 20), PopulationOf(1, 20));
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "probe"));
      Directory.CreateDirectory(folder);
      var row = $"{test.Name},{before.Item1:0.#},{before.Item2:0.#},{after.Item1:0.#},{after.Item2:0.#},{(Verified.Number - start) / Fps}";
      File.AppendAllLines(Path.Combine(folder, "matchup.csv"), new[] { row });
      Debug.Log("[FW-Probe] " + row);
      Assert.Greater(before.Item1 + before.Item2, 0f);
    }
  }
}
