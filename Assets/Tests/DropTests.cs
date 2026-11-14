namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 34-B 보급품 사전 알람 시험: 단계별 공개(35초 경고 원 → 10초 좁은 원 → 착지), 신호탄(모두에게 20초), 쟁탈 시 획득 정지, 보상 표.
  /// 첫 낙하를 앞당기려고 GameRulesSpec.DropIntervalSeconds를 잠시 줄인다(TearDown이 되돌린다).
  /// </summary>
  public unsafe partial class LocalMatchTests {
    void FastDrops(float interval) {
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      var before = rules.DropIntervalSeconds;
      rules.DropIntervalSeconds = FP.FromFloat_UNSAFE(interval);
      Restores.Add(() => rules.DropIntervalSeconds = before);
    }

    static int OpenDropCrates() {
      var count = 0;
      var crates = Verified.Filter<SupplyCrate>();
      while (crates.NextUnsafe(out _, out var crate)) {
        if (crate->Seat < 0) {
          count++;
        }
      }
      return count;
    }

    /// <summary>낙하 상자들의 (진행, 쟁탈 중인 상자 수, 최대 진행).</summary>
    static (int Contested, float Progress) DropCrateState() {
      var contested = 0;
      var progress = 0f;
      var crates = Verified.Filter<SupplyCrate>();
      while (crates.NextUnsafe(out _, out var crate)) {
        if (crate->Seat < 0) {
          contested += crate->Contested ? 1 : 0;
          progress = Mathf.Max(progress, crate->Progress.AsFloat);
        }
      }
      return (contested, progress);
    }

    /// <summary>맵의 낙하 자리마다 두 유닛(0번·1번 플레이어)을 겹쳐 놓는다(쟁탈 시험). 한 자리만이면 1번은 빼도 된다.</summary>
    static (int Owner, string Unit, int X, int Z)[] UnitsAtDropSpots(MapSpec map, bool both) {
      var list = new List<(int, string, int, int)>();
      foreach (var spot in map.CrateSpots.Where(s => s.Seat < 0)) {
        var x = Mathf.RoundToInt(spot.Position.X.AsFloat);
        var z = Mathf.RoundToInt(spot.Position.Z.AsFloat);
        // 공병은 무기가 없어 서로 쏘지 않고 낙하 상자를 얻을 수 있다(저격수만 못 얻는다).
        list.Add((0, "ff_engineer", x, z));
        if (both) {
          list.Add((1, "cf_engineer", x + 1, z));
        }
      }
      return list.Select(t => (t.Item1, t.Item2, t.Item3, t.Item4)).ToArray();
    }

    /// <summary>35초 전에 반경 30m 원, 10초 전에 반경 10m 원, 착지 때 상자가 생긴다. 원의 중심은 정확한 낙하 지점이 아니고 상자는 착지 전에 없다.</summary>
    [UnityTest, Timeout(600000)]
    public IEnumerator Drop_ZoneAnnouncedBeforeExactPoint() {
      FastDrops(45f);
      var stages = new List<(DropPhase Phase, FPVector3 Position, FP Radius, int Tick, int Group)>();
      yield return StartMatch(Map4PScene, 2, truce: 0);
      QuantumEvent.Subscribe<EventSupplyDropStage>(_listener, e => stages.Add((e.Phase, e.Position, e.Radius, e.Tick, e.Group)));
      yield return WaitFor(() => stages.Any(s => s.Phase == DropPhase.Landed), 120f, "착지");
      var zone = stages.First(s => s.Phase == DropPhase.Zone);
      var closing = stages.First(s => s.Phase == DropPhase.Closing);
      var landed = stages.First(s => s.Phase == DropPhase.Landed);
      Assert.Less(stages.IndexOf(zone), stages.IndexOf(closing), "구역 공개가 먼저");
      Assert.Less(stages.IndexOf(closing), stages.IndexOf(landed), "좁혀지고 나서 착지");
      Assert.AreEqual(30f, zone.Radius.AsFloat, 0.01f, "경고 원 반경 30m");
      Assert.AreEqual(10f, closing.Radius.AsFloat, 0.01f, "좁은 원 반경 10m");
      Assert.AreEqual(35f, (landed.Tick - zone.Tick) / (float)Fps, 1.5f, "35초 전에 공개");
      Assert.AreEqual(10f, (landed.Tick - closing.Tick) / (float)Fps, 1.5f, "10초 전에 좁아짐");
      var map = Map4P();
      var spots = map.CrateSpots.Where(s => s.Seat < 0 && s.Group == landed.Group).Select(s => s.Position).ToArray();
      Assert.IsTrue(spots.All(p => (p - zone.Position).Magnitude.AsFloat > 0.01f), "경고 원 중심은 정확한 낙하 자리가 아니다");
      Assert.IsTrue(spots.Any(p => (p - zone.Position).Magnitude.AsFloat <= zone.Radius.AsFloat), "정확한 자리는 경고 원 안에 있다");
      Assert.IsTrue(stages.Where(s => s.Phase == DropPhase.Zone).All(z => spots.Any(p => (p - z.Position).Magnitude.AsFloat <= z.Radius.AsFloat * 0.5f + 0.01f)), "원마다 정확한 자리가 반지름의 절반 안");
      Assert.Greater(OpenDropCrates(), 0, "착지하면 상자가 생긴다");
      var landings = stages.Count(s => s.Phase == DropPhase.Landed);
      Assert.Greater(landings, 0, "상자마다 착지 알림");
      Assert.AreEqual(landings, stages.Count(s => s.Phase == DropPhase.Zone), "떨어질 상자마다 경고 원");
      Assert.AreEqual(landings, stages.Count(s => s.Phase == DropPhase.Closing), "떨어질 상자마다 좁아진 원");
    }

    /// <summary>착지하면 신호탄이 모든 플레이어에게 상자 둘레를 20초 동안 보여 주고, 끝나면 다시 안 보인다(안개를 켠 경우).</summary>
    [UnityTest, Timeout(600000)]
    public IEnumerator Drop_FlareRevealsToAll() {
      UseFog();
      FastDrops(45f);
      var landedAt = FPVector3.Zero;
      var landedTick = 0;
      yield return StartMatch(Map4PScene, 2, truce: 0);
      QuantumEvent.Subscribe<EventSupplyDropStage>(_listener, e => {
        if (e.Phase == DropPhase.Landed) {
          landedAt = e.Position;
          landedTick = e.Tick;
        }
      });
      yield return WaitFor(() => landedTick > 0, 120f, "착지");
      yield return WaitFrames(Fps);
      Assert.IsTrue(FogSystem.IsVisible(Verified, 0, landedAt), "신호탄: 0번에게 보임");
      Assert.IsTrue(FogSystem.IsVisible(Verified, 1, landedAt), "신호탄: 1번에게도 보임");
      yield return WaitFor(() => FogSystem.IsVisible(Verified, 0, landedAt) == false && FogSystem.IsVisible(Verified, 1, landedAt) == false, 60f, "20초 뒤 신호탄이 꺼짐");
      Assert.GreaterOrEqual((Verified.Number - landedTick) / (float)Fps, 19f, "20초 동안은 보였다");
    }

    /// <summary>상자 3m 안에 서로 적인 유닛이 함께 있으면 획득 진행이 멈추고(쟁탈), 한쪽만 남으면 5초 뒤 얻는다.</summary>
    [UnityTest, Timeout(900000)]
    public IEnumerator Drop_ContestedPausesCapture() {
      FastDrops(45f);
      var captured = new List<(PlayerRef Player, int Kind)>();
      yield return StartMatch(Map4PScene, 2, truce: 0, units: UnitsAtDropSpots(Map4P(), both: true));
      QuantumEvent.Subscribe<EventSupplyDropStage>(_listener, e => {
        if (e.Phase == DropPhase.Captured) {
          captured.Add((e.Player, e.Kind));
        }
      });
      yield return WaitFor(() => OpenDropCrates() > 0, 120f, "착지");
      yield return WaitFrames(2);
      var state = DropCrateState();
      Assert.Greater(state.Contested, 0, "적이 함께 있어 쟁탈 중");
      Assert.AreEqual(0f, state.Progress, 0.001f, "쟁탈 중에는 진행이 멈춘다");
      // 1번 플레이어 공병을 상자에서 멀리 보내면 0번만 남아 5초 뒤 얻는다.
      yield return Send(1, new MoveUnitsCommand { Units = UnitsNamed(1, "cf_engineer").ToArray(), Target = Map4P().StartPositions[1] });
      yield return WaitFor(() => captured.Count > 0, 120f, "한쪽이 남으면 획득");
      var winner = (int)captured[0].Player;
      Assert.IsTrue(winner == 0 || winner == 1);
      Assert.GreaterOrEqual(PlayerOf(winner).DropCaptures, 1, "획득 횟수가 오른다");
      Assert.GreaterOrEqual(PlayerOf(0).DropContests + PlayerOf(1).DropContests, 1, "쟁탈 횟수가 오른다");
    }

    /// <summary>보상 표는 가중치대로 종류를 고르고, 같은 시드면 같은 보상이 나온다.</summary>
    [UnityTest, Timeout(900000)]
    public IEnumerator Drop_RewardTableDeterministic() {
      var weights = new FP[] { 5, 2, 2, 3 };
      Assert.AreEqual(0, BattlefieldSystem.PickRewardKind(weights, FP._0));
      Assert.AreEqual(0, BattlefieldSystem.PickRewardKind(weights, FP.FromFloat_UNSAFE(4.9f)));
      Assert.AreEqual(1, BattlefieldSystem.PickRewardKind(weights, 5));
      Assert.AreEqual(1, BattlefieldSystem.PickRewardKind(weights, FP.FromFloat_UNSAFE(6.9f)));
      Assert.AreEqual(2, BattlefieldSystem.PickRewardKind(weights, 7));
      Assert.AreEqual(2, BattlefieldSystem.PickRewardKind(weights, FP.FromFloat_UNSAFE(8.9f)));
      Assert.AreEqual(3, BattlefieldSystem.PickRewardKind(weights, 9));
      Assert.AreEqual(3, BattlefieldSystem.PickRewardKind(weights, FP.FromFloat_UNSAFE(11.99f)));
      var kinds = new int[2];
      for (var run = 0; run < 2; run++) {
        FastDrops(45f);
        var kind = -1;
        yield return StartMatch(Map4PScene, 2, truce: 0, seed: 3405, units: UnitsAtDropSpots(Map4P(), both: false));
        QuantumEvent.Subscribe<EventSupplyDropStage>(_listener, e => {
          if (e.Phase == DropPhase.Captured && kind < 0) {
            kind = e.Kind;
          }
        });
        yield return WaitFor(() => kind >= 0, 120f, "획득");
        kinds[run] = kind;
        TearDown();
        yield return null;
      }
      Assert.AreEqual(kinds[0], kinds[1], "같은 시드면 같은 보상 종류");
    }
  }
}
