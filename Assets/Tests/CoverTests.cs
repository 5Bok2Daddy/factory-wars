namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 32 보병 엄폐와 지원 건물. 모래주머니 진지(0,30)의 북쪽 엄폐점(0,33.7, 북쪽을 막음)을 쓰고,
  /// 북쪽(0,39)에 움직이지 않는 적 공병을 둔다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static readonly (int, string, int, int)[] SandbagSite = { (0, "ff_sandbags", 0, 30) };

    static EntityRef CoverOf(EntityRef unit) => Verified.Get<Unit>(unit).Cover;

    static bool InCover(EntityRef unit) => Verified.Exists(unit) && Verified.Get<Unit>(unit).InCover;

    static FP CoverMultiplier(EntityRef entity, FPVector3 origin, bool area) {
      var unit = Verified.Unsafe.GetPointer<Unit>(entity);
      return CoverSystem.DamageMultiplier(Verified, unit, origin, area);
    }

    IEnumerator RangerIntoCover(EntityRef[] ranger) {
      yield return StartMatch(buildings: SandbagSite, units: new[] { (0, "ff_ranger_squad", 0, 24), (1, "cf_engineer", 0, 39) });
      ranger[0] = UnitsNamed(0, "ff_ranger_squad").OrderBy(u => (Verified.Get<Transform3D>(u).Position - V(0, 24)).SqrMagnitude).First();
      var target = UnitsNamed(1, "cf_engineer").OrderBy(u => (Verified.Get<Transform3D>(u).Position - V(0, 39)).SqrMagnitude).First();
      ranger[1] = target;
      yield return Send(0, new AttackTargetCommand { Units = new[] { ranger[0] }, Target = target });
      yield return WaitFor(() => InCover(ranger[0]), 30f, "엄폐 도착");
    }

    [UnityTest]
    public IEnumerator Cover_InfantryMovesToCoverOnEngage() {
      var pair = new EntityRef[2];
      yield return RangerIntoCover(pair);
      var spot = Verified.Get<CoverSpot>(CoverOf(pair[0]));
      Assert.AreEqual(pair[0], spot.Occupant, "점 점유");
      Assert.Greater(spot.Facing.Y.AsFloat, 0.9f, "북쪽을 막는 점");
      Assert.Less((Verified.Get<Transform3D>(pair[0]).Position - spot.Position).Magnitude.AsFloat, 2f, "점에 서 있음");
      Assert.Greater(StateOf(0).CoverEntries, 0, "엄폐 횟수 기록");
      // 모래주머니 진지에는 엄폐점 4개.
      var spots = 0;
      foreach (var pairSpot in Verified.GetComponentIterator<CoverSpot>()) {
        spots += pairSpot.Component.Source == OwnBuilding(0, "ff_sandbags") ? 1 : 0;
      }
      Assert.AreEqual(4, spots);
    }

    [UnityTest]
    public IEnumerator Cover_DirectionalReductionApplies() {
      var pair = new EntityRef[2];
      yield return RangerIntoCover(pair);
      var spot = Verified.Get<CoverSpot>(CoverOf(pair[0]));
      var reduction = spot.Reduction.AsFloat;
      var rules = Verified.FindAsset(Verified.RuntimeConfig.Rules);
      Assert.AreEqual(1f - reduction, CoverMultiplier(pair[0], V(0, 45), false).AsFloat, 0.001f, "막는 쪽(북) 사격은 줄어든다");
      Assert.AreEqual(1f, CoverMultiplier(pair[0], V(0, 15), false).AsFloat, 0.001f, "등 뒤(남) 사격은 그대로");
      Assert.AreEqual(1f, CoverMultiplier(pair[0], V(20, 34), false).AsFloat, 0.001f, "옆(동, 90도)은 그대로");
      Assert.AreEqual(1f - reduction * rules.CoverAreaFactor.AsFloat, CoverMultiplier(pair[0], V(0, 45), true).AsFloat, 0.001f, "광역은 절반 효과");
    }

    [UnityTest]
    public IEnumerator Cover_PlayerMoveOverridesCover() {
      var pair = new EntityRef[2];
      yield return RangerIntoCover(pair);
      var spot = CoverOf(pair[0]);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { pair[0] }, Target = V(-10, 20) });
      Assert.AreEqual(EntityRef.None, CoverOf(pair[0]), "이동 명령이 엄폐를 푼다");
      Assert.AreEqual(EntityRef.None, Verified.Get<CoverSpot>(spot).Occupant, "점이 비었다");
      yield return WaitFrames(5 * Fps);
      Assert.AreEqual(EntityRef.None, CoverOf(pair[0]), "이동 중에는 다시 엄폐하지 않는다");
      // 위치 고수도 엄폐를 찾지 않는다.
      yield return Send(0, new HoldUnitsCommand { Units = new[] { pair[0] } });
      yield return WaitFrames(3 * Fps);
      Assert.AreEqual(EntityRef.None, CoverOf(pair[0]));
    }

    [UnityTest]
    public IEnumerator Cover_DestroyedCoverReleasesUnits() {
      var pair = new EntityRef[2];
      yield return RangerIntoCover(pair);
      var sandbags = OwnBuilding(0, "ff_sandbags");
      // 대륙 연방 전차 셋이 모래주머니를 부순다.
      yield return Send(1, new MoveUnitsCommand { Units = UnitsNamed(1, "cf_dragonscale_tank").ToArray(), Target = V(8, 22) });
      yield return Send(1, new AttackTargetCommand { Units = UnitsNamed(1, "cf_dragonscale_tank").ToArray(), Target = sandbags });
      yield return WaitFor(() => Verified.Exists(sandbags) == false, 60f, "모래주머니 파괴");
      yield return WaitFrames(2);
      Assert.IsTrue(Verified.Exists(pair[0]) == false || CoverOf(pair[0]) == EntityRef.None, "엄폐물이 사라지면 풀린다");
      foreach (var spot in Verified.GetComponentIterator<CoverSpot>()) {
        Assert.AreNotEqual(sandbags, spot.Component.Source, "점도 사라진다");
      }
    }

    [UnityTest, Timeout(240000)]
    public IEnumerator Cover_DeterministicAcrossRuns() {
      var checksums = new ulong[2];
      FixedStep = true;
      Restores.Add(() => FixedStep = false);
      for (var run = 0; run < 2; run++) {
        FixedStep = true;
        yield return StartMatch(seed: 4242, buildings: SandbagSite, units: new[] { (0, "ff_ranger_squad", 0, 24), (0, "ff_ranger_squad", 3, 24), (1, "cf_militia", 0, 40), (1, "cf_militia", 4, 40) });
        yield return Send(0, new MoveUnitsCommand { Units = UnitsNamed(0, "ff_ranger_squad").ToArray(), Target = V(0, 34), AttackMove = true });
        var stop = 40 * Fps;
        var sum = 0UL;
        QuantumCallback.Subscribe<CallbackSimulateFinished>(_listener, e => {
          // 로컬 경기는 되감기가 없어 예측 프레임과 확정 프레임이 같다. 첫 값만 쓴다.
          if (e.Frame.Number == stop && sum == 0UL) {
            sum = e.Frame.CalculateChecksum();
          }
        });
        yield return WaitFor(() => sum != 0UL, 90f, "40초 시점 체크섬");
        checksums[run] = sum;
        Debug.Log($"[FW-TEST] cover run {run}: checksum {checksums[run]} cover {StateOf(0).CoverEntries}");
        Assert.Greater(StateOf(0).CoverEntries, 0, "이 장면에서 엄폐를 쓴다");
        TearDown();
        yield return null;
      }
      Assert.AreEqual(checksums[0], checksums[1], "같은 시드·같은 명령이면 같은 결과");
    }
  }
}
