namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using FactoryWars.View;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>항목 20의 전장 규칙. 점령지·고지대·도로는 시험 맵, 바리케이드·상자는 실제 맵으로 잰다.</summary>
  public unsafe partial class LocalMatchTests {
    const int SiteX = -35;
    const int SiteZ = -38;

    static CaptureSite Site(int index) {
      foreach (var pair in Verified.GetComponentIterator<CaptureSite>()) {
        if (pair.Component.Index == index) {
          return pair.Component;
        }
      }
      Assert.Fail($"점령지 {index} 없음");
      return default;
    }

    // 점령

    [UnityTest]
    public IEnumerator Capture_SingleUnitTakesSiteInTenSecondsAndPays() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", SiteX, SiteZ) });
      var steel = Steel(0);
      yield return WaitFrames(9 * Fps);
      Assert.IsFalse(Site(0).Owner.IsValid, "10초 전에는 아직");
      Assert.Greater(Site(0).Progress.AsFloat, 8f);
      yield return WaitFor(() => Site(0).Owner.IsValid, 30f, "점령");
      Assert.AreEqual(0, (int)Site(0).Owner);
      var after = Steel(0);
      Assert.AreEqual(steel, after, 0.01f, "점령 전에는 보상 없음");
      yield return WaitFrames(5 * Fps);
      Assert.AreEqual(after + 5f, Steel(0), 0.2f, "초당 강철 1");
    }

    [UnityTest]
    public IEnumerator Capture_ContestStopsProgress() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", SiteX, SiteZ), (1, "cf_dragonscale_tank", SiteX + 2, SiteZ) });
      yield return WaitFrames(2 * Fps);
      Assert.AreEqual(0f, Site(0).Progress.AsFloat, "두 편이 있으면 멈춤");
      Assert.IsTrue(Site(0).Contested);
    }

    [UnityTest]
    public IEnumerator Capture_EngineerDoesNotCount() {
      yield return StartMatch(units: new[] { (0, "ff_engineer", SiteX, SiteZ) });
      yield return WaitFrames(2 * Fps);
      Assert.AreEqual(0f, Site(0).Progress.AsFloat);
    }

    [UnityTest]
    public IEnumerator Capture_NotDuringTruce() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", SiteX, SiteZ) }, truce: 300);
      yield return WaitFrames(2 * Fps);
      Assert.AreEqual(0f, Site(0).Progress.AsFloat);
    }

    [UnityTest]
    public IEnumerator Capture_ProgressFallsBackWhenEveryoneLeaves() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", SiteX, SiteZ) });
      yield return WaitFrames(4 * Fps);
      var built = Site(0).Progress.AsFloat;
      Assert.Greater(built, 3f);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { UnitsNamed(0, "ff_eagle_tank").First(e => Verified.Get<Transform3D>(e).Position.Z < -30) }, Target = V(-35, -20) });
      yield return WaitFor(() => Site(0).Progress.AsFloat < built - 0.5f, 30f, "진행도 감소");
      Assert.IsFalse(Site(0).Owner.IsValid);
    }

    [UnityTest]
    public IEnumerator Capture_EnemyNeutralizesThenTakes() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", SiteX, SiteZ), (1, "cf_dragonscale_tank", -35, -20) });
      yield return WaitFor(() => Site(0).Owner.IsValid, 30f, "먼저 점령");
      var tank = UnitsNamed(0, "ff_eagle_tank").First(e => Verified.Get<Transform3D>(e).Position.Z < -30);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { tank }, Target = V(10, -20) });
      yield return WaitFor(() => (Verified.Get<Transform3D>(tank).Position - V(SiteX, SiteZ)).Magnitude > 12, 30f, "떠남");
      var enemy = UnitsNamed(1, "cf_dragonscale_tank").First(e => Verified.Get<Transform3D>(e).Position.Z < -10);
      yield return Send(1, new MoveUnitsCommand { Units = new[] { enemy }, Target = V(SiteX, SiteZ) });
      yield return WaitFor(() => Site(0).Owner.IsValid == false, 40f, "중립화");
      yield return WaitFor(() => Site(0).Owner.IsValid, 40f, "탈환");
      Assert.AreEqual(1, (int)Site(0).Owner);
    }

    [UnityTest]
    public IEnumerator Capture_AlliesTakeSiteTogether() {
      yield return StartMatch(players: 3, units: new[] { (0, "ff_eagle_tank", SiteX, SiteZ), (1, "cf_dragonscale_tank", SiteX + 2, SiteZ) });
      yield return Ally(0, 1);
      yield return WaitFor(() => Site(0).Owner.IsValid, 30f, "공동 점령");
      Assert.IsFalse(Site(0).Contested);
    }

    // 바리케이드

    static List<(EntityRef Entity, T Component)> All<T>() where T : unmanaged, IComponent {
      var list = new List<(EntityRef, T)>();
      foreach (var pair in Verified.GetComponentIterator<T>()) {
        list.Add((pair.Entity, pair.Component));
      }
      return list;
    }

    static bool PathOpen(FPVector3 from, FPVector3 to) => Verified.Navigation.LineOfSight(from, to, *Verified.NavMeshRegionMask, MapNavMesh());

    static bool DropAnnounced() => Verified.Global->DropAnnounced;

    static int DropGroup() => Verified.Global->DropGroup;

    static float BarricadeHealth(EntityRef entity) => Verified.TryGet<Barricade>(entity, out var value) ? value.Health.AsFloat : 0f;

    [UnityTest]
    public IEnumerator Barricade_BlocksUntilDestroyedAndIsNotAutoTargeted() {
      var near = new[] { (0, "ff_eagle_tank", -38, 24), (0, "ff_eagle_tank", -36, 22), (0, "ff_eagle_tank", -34, 24), (0, "ff_eagle_tank", -38, 20) };
      yield return StartMatch(Map2PScene, 2, units: near, truce: 0);
      var destroyed = new List<int>();
      QuantumEvent.Subscribe<EventBarricadeDestroyed>(_listener, e => destroyed.Add(e.Index));
      var entity = All<Barricade>().OrderBy(p => (p.Component.Shape.Center - V(-44, 31)).SqrMagnitude.AsFloat).First().Entity;
      var from = V(-38, 24);
      var to = V(-50, 38);
      yield return WaitFrames(3 * Fps);
      Assert.AreEqual(1500f, BarricadeHealth(entity), "자동 조준 대상이 아님");
      Assert.IsFalse(PathOpen(from, to), "막혀 있음");

      var tanks = UnitsNamed(0, "ff_eagle_tank").Where(e => Verified.Get<Transform3D>(e).Position.X > -50).ToArray();
      yield return Send(0, new AttackTargetCommand { Units = tanks, Target = entity });
      Time.timeScale = 12f;
      yield return WaitFor(() => Verified.Exists(entity) == false, 60f, "파괴");
      Time.timeScale = TimeScale;
      yield return WaitFrames(2);
      Assert.AreEqual(1, destroyed.Count, "이벤트 한 번");
      Assert.IsTrue(PathOpen(from, to), "길이 열림");
    }

    [UnityTest]
    public IEnumerator Barricade_TakesSplashDamage() {
      var units = new[] { (0, "cf_longwall_mlrs", -30, 20), (1, "ff_ranger_squad", -41, 27) };
      yield return StartMatch(Map2PScene, 2, units: units, truce: 0);
      var entity = All<Barricade>().OrderBy(p => (p.Component.Shape.Center - V(-44, 31)).SqrMagnitude.AsFloat).First().Entity;
      yield return WaitFor(() => BarricadeHealth(entity) < 1500f, 30f, "범위 피해");
    }

    // 상자

    static int CrateCount(System.Func<SupplyCrate, bool> match) => All<SupplyCrate>().Count(p => match(p.Component));

    [UnityTest]
    public IEnumerator Crate_OwnBaseCrateIsPickedDuringTruce() {
      var spot = new[] { (0, "ff_eagle_tank", -70, -90), (1, "cf_dragonscale_tank", -88, -72) };
      yield return StartMatch(Map2PScene, 2, units: spot, truce: 300);
      yield return WaitFrames(Fps);
      Assert.AreEqual(1, CrateCount(c => c.Seat == 0), "자기 상자 하나만 주웠다");
      Assert.AreEqual(600, Funds(0), "시작 철 500 + 상자 100");
      Assert.AreEqual(300, Wood(0), "목재 상자는 그대로");
      Assert.AreEqual(3, CrateCount(c => c.Seat >= 0), "상대 유닛은 남의 상자를 못 줍는다");
    }

    [UnityTest]
    public IEnumerator Crate_DropsComeInSymmetricPairsAfterWarning() {
      yield return StartMatch(Map2PScene, 2, truce: 0);
      var warned = new List<int>();
      QuantumEvent.Subscribe<EventSupplyDropIncoming>(_listener, e => warned.Add(e.Frame));
      Time.timeScale = 30f;
      yield return WaitFor(() => CrateCount(c => c.Seat < 0) > 0, 60f, "투하");
      Time.timeScale = TimeScale;
      var frame = Verified.Number;
      Assert.AreEqual(150 * Fps, frame, 40, "150초에 투하");
      Assert.AreEqual(1, warned.Count, "예고 한 번");
      var positions = All<SupplyCrate>().Where(p => p.Component.Seat < 0).Select(p => Verified.Get<Transform3D>(p.Entity).Position).ToArray();
      Assert.AreEqual(2, positions.Length, "대칭 쌍");
      Assert.AreEqual(Rotated(positions[0], 2), positions[1]);
    }

    [UnityTest]
    public IEnumerator Crate_DropChoiceIsDeterministic() {
      var groups = new int[2];
      for (var run = 0; run < 2; run++) {
        yield return StartMatch(Map2PScene, 2, truce: 0, seed: 555);
        Time.timeScale = 30f;
        yield return WaitFor(DropAnnounced, 60f, "예고");
        Time.timeScale = TimeScale;
        groups[run] = DropGroup();
        TearDown();
        yield return null;
      }
      Assert.AreEqual(groups[0], groups[1], "같은 시드면 같은 후보지");
    }

    // 고지대

    [UnityTest]
    public IEnumerator HighGround_ExtendsRangeOnlyTowardsLowGround() {
      yield return StartMatch();
      var up = V(42, 24);
      Assert.AreEqual(11.5f, CombatSystem.RangeTo(Verified, 10, up, V(22, 24)).AsFloat, 0.01f, "위에서 아래로");
      Assert.AreEqual(10f, CombatSystem.RangeTo(Verified, 10, V(22, 24), up).AsFloat, 0.01f, "아래에서 위로");
      Assert.AreEqual(10f, CombatSystem.RangeTo(Verified, 10, up, V(43, 25)).AsFloat, 0.01f, "같은 고지대");
    }

    [UnityTest]
    public IEnumerator HighGround_HiddenUntilRevealed() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", 42, 24), (1, "cf_dragonscale_tank", 22, 24) });
      var hidden = UnitsNamed(0, "ff_eagle_tank").First(e => Verified.Get<Transform3D>(e).Position.X > 30);
      Assert.IsFalse(FactoryWarsUtil.IsVisibleTo(Verified, 1, hidden), "멀면 안 보임");
      Assert.IsTrue(FactoryWarsUtil.IsVisibleTo(Verified, 0, hidden), "주인은 봄");
      var enemy = UnitsNamed(1, "cf_dragonscale_tank").First(e => Verified.Get<Transform3D>(e).Position.Z > 20);
      yield return Send(1, new MoveUnitsCommand { Units = new[] { enemy }, Target = V(37, 24) });
      yield return WaitFor(() => FactoryWarsUtil.IsVisibleTo(Verified, 1, hidden), 20f, "6m 안에서 보임");
      Assert.LessOrEqual((Verified.Get<Transform3D>(enemy).Position - V(42, 24)).Magnitude.AsFloat, 12f);
    }

    [UnityTest]
    public IEnumerator HighGround_AirRevealsAtTwelveMeters() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", 42, 24), (1, "ff_hawk_gunship", 31, 24) });
      var hidden = UnitsNamed(0, "ff_eagle_tank").First(e => Verified.Get<Transform3D>(e).Position.X > 30);
      Assert.IsTrue(FactoryWarsUtil.IsVisibleTo(Verified, 1, hidden), "공중은 12m 안에서 봄");
    }

    [UnityTest]
    public IEnumerator HighGround_HiddenUnitIsNotAutoTargetedUntilItFires() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", 42, 24), (1, "cf_dragonscale_tank", 35, 29) }, truce: 4);
      var hidden = UnitsNamed(0, "ff_eagle_tank").First(e => Verified.Get<Transform3D>(e).Position.X > 30);
      var enemy = UnitsNamed(1, "cf_dragonscale_tank").First(e => Verified.Get<Transform3D>(e).Position.Z > 20);
      yield return Send(0, new MoveUnitsCommand { Units = new[] { hidden }, Target = V(42, 24) });
      Assert.IsFalse(FactoryWarsUtil.IsVisibleTo(Verified, 1, hidden), "6m 밖");
      yield return WaitFor(() => Verified.Get<Unit>(hidden).LastFiredFrame > 0, 30f, "휴전 뒤 사격");
      Assert.IsTrue(FactoryWarsUtil.IsVisibleTo(Verified, 1, hidden), "사격하면 3초간 보임");
    }

    // 도로·여울

    [UnityTest]
    public IEnumerator Ground_RoadIsFasterAndFordIsSlower() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", 10, -44), (0, "ff_eagle_tank", 10, -36), (0, "ff_eagle_tank", 10, -30) });
      yield return WaitFrames(3);
      float Speed(int z) {
        var tank = UnitsNamed(0, "ff_eagle_tank").First(e => Mathf.Abs(Verified.Get<Transform3D>(e).Position.Z.AsFloat - z) < 1f);
        return Verified.Get<NavMeshSteeringAgent>(tank).MaxSpeed.AsFloat;
      }
      Assert.AreEqual(5.5f, Speed(-44), 0.01f, "도로 +10%");
      Assert.AreEqual(3.75f, Speed(-36), 0.01f, "여울 -25%");
      Assert.AreEqual(5f, Speed(-30), 0.01f, "일반");
    }

    // AI

    [UnityTest]
    public IEnumerator Bot_CollectsItsBaseCratesDuringTruce() {
      yield return StartMatch(Map2PScene, 2, bots: new[] { BotDifficulty.Easy }, seed: 21, truce: 300);
      Time.timeScale = 12f;
      yield return WaitFor(() => CrateCount(c => c.Seat == 1) == 0, 40f, "쉬움도 상자를 줍는다");
      Time.timeScale = TimeScale;
    }

    // 화면

    [UnityTest]
    public IEnumerator Battlefield_ViewShowsSitesCratesAndBarricades() {
      yield return StartMatch(Map2PScene, 2, truce: 300);
      yield return WaitForBattle();
      yield return null;
      var field = Battle().Field;
      Assert.AreEqual(3, field.SiteCount, "점령지");
      Assert.AreEqual(2, field.WallCount, "바리케이드");
      Assert.AreEqual(4, field.CrateCount, "본진 상자");
    }

    [UnityTest]
    public IEnumerator Battlefield_HudAnnouncesCaptureAndCrate() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", SiteX, SiteZ) });
      yield return WaitForHud();
      yield return WaitFor(() => Site(0).Owner.IsValid, 30f, "점령");
      yield return null;
      Assert.AreEqual(Texts.Captured(CaptureKind.Comms), Controller().Hud.AlertText);
      Controller().Hud.FloatText(Vector3.zero, Texts.Reward(new ResourceCost { Iron = 100 }));
      Assert.AreEqual(1, Controller().Hud.FloatCount);
      Assert.AreEqual("+100 철", Texts.Reward(new ResourceCost { Iron = 100 }));
    }
  }
}
