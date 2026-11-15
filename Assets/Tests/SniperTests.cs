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
  /// 항목 34-C 주둔 저격수 시험. 진지 건물(도시 진지 0번, 테스트 맵 (-10, -24))에 올라가야 쏘고, 올라간 뒤 5초(올라가기 3 + 자리 잡기 2)는 못 쏜다.
  /// 은신은 적 근접·탐지로 풀리고, 교란 장치는 노드 채집 속도를 줄이며 탐지된 뒤 가까이 온 적이 풀어 낸다.
  /// 연막 시선 차단·가건물/감시탑 건물은 이 항목에서 만들지 않았으므로 시험도 없다(보고서 참조).
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static EntityRef Sniper(int owner, string name) => UnitsNamed(owner, name).First();

    static bool Concealed(EntityRef sniper, int viewer) => FactoryWarsUtil.IsVisibleTo(Verified, viewer, sniper) == false;

    static int Stat(int player, string name) {
      var state = Verified.Global->Players.GetPointer(player);
      switch (name) {
        case "snipeInfantry": return state->SnipeKillsInfantry;
        case "detections": return state->Detections;
        case "placed": return state->JammersPlaced;
        case "destroyed": return state->JammersDestroyed;
        default: return -1;
      }
    }

    static int JammerCount() => Verified.ComponentCount<Jammer>();

    /// <summary>지상에서는 사거리 안의 적이 있어도 쏘지 않고, 공격 명령은 "건물에 올라가야 사격 가능"으로 거부된다.</summary>
    [UnityTest]
    public IEnumerator Sniper_CannotFireOnGround() {
      yield return StartMatch(units: new[] { (0, "ff_sniper", -10, -15), (1, "cf_flame_squad", -10, -30) });
      var sniper = Sniper(0, "ff_sniper");
      var enemy = UnitsNamed(1, "cf_flame_squad").First(e => At(e).Z.AsFloat < -20f);
      var health = Health(enemy);
      _rejected.Clear();
      yield return Send(0, new AttackTargetCommand { Units = new[] { sniper }, Target = enemy });
      AssertRejected(0, ActionKind.Attack, RejectReason.NeedGarrison);
      yield return WaitFrames(10 * Fps);
      Assert.AreEqual(health, Health(enemy), 0.01f, "지상에서는 사격하지 않는다");
    }

    /// <summary>주둔한 뒤 5초(올라가기 3 + 자리 잡기 2)는 못 쏘고, 그 뒤에는 보병을 한 방에 잡는다. 차량에는 30만 들어간다.</summary>
    [UnityTest]
    public IEnumerator Sniper_FiresOnlyAfterSetupInGarrison() {
      yield return StartMatch(units: new[] { (0, "ff_sniper", -10, -15), (1, "cf_dragonscale_tank", -10, -33), (1, "cf_flame_squad", 6, -33) });
      var building = CityAt(0);
      var sniper = Sniper(0, "ff_sniper");
      var tank = UnitsNamed(1, "cf_dragonscale_tank").First(e => At(e).Z.AsFloat < -20f);
      var health = Health(tank);
      yield return Send(0, new EnterGarrisonCommand { Units = new[] { sniper }, Building = building });
      yield return WaitFor(() => GarrisonOf(sniper) == building, 20f, "진입");
      var setupUntil = Verified.Get<Unit>(sniper).SetupUntilFrame;
      Assert.Greater(setupUntil, Verified.Number, "자리 잡기 시간이 걸린다");
      Assert.AreEqual(5 * Fps, setupUntil - Verified.Number, Fps / 2, "올라가기 3초 + 자리 잡기 2초");
      yield return WaitFrames(Fps);
      Assert.AreEqual(health, Health(tank), 0.01f, "자리 잡는 동안은 못 쏜다");
      yield return WaitFor(() => Health(tank) < health, 30f, "사격");
      Assert.GreaterOrEqual(Verified.Number, setupUntil, "자리 잡은 뒤에 쏘았다");
      Assert.AreEqual(30f, health - Health(tank), 1.5f, "차량에는 30");
      Assert.Less(Verified.Number - setupUntil, 3 * Fps, "자리 잡자마자 쏜다");
    }

    /// <summary>보병 한 방(140): 체력 130 화염 분대가 한 발에 죽고 저격 처치로 센다. 쏜 건물은 피격 진영에게 4초 보인다.</summary>
    [UnityTest]
    public IEnumerator Sniper_MuzzleRevealsBuilding() {
      yield return StartMatch(units: new[] { (0, "ff_sniper", -10, -15), (1, "cf_flame_squad", -10, -33) });
      var building = CityAt(0);
      var sniper = Sniper(0, "ff_sniper");
      var enemy = UnitsNamed(1, "cf_flame_squad").First(e => At(e).Z.AsFloat < -20f);
      yield return Send(0, new EnterGarrisonCommand { Units = new[] { sniper }, Building = building });
      yield return WaitFor(() => GarrisonOf(sniper) == building, 20f, "진입");
      Assert.AreEqual(0, Verified.Get<Unit>(sniper).MuzzleRevealUntilFrame, "쏘기 전에는 노출 없음");
      yield return WaitFor(() => Verified.Exists(enemy) == false, 30f, "한 방에 처치");
      var unit = Verified.Get<Unit>(sniper);
      Assert.Greater(unit.MuzzleRevealUntilFrame, Verified.Number - 1, "사격 직후 건물 노출");
      Assert.AreNotEqual(0, unit.MuzzleVictimMask & (1 << 1), "피격 진영 비트");
      Assert.AreEqual(1, Stat(0, "snipeInfantry"), "저격 처치 집계");
      Assert.AreEqual(4f, (unit.MuzzleRevealUntilFrame - unit.LastFiredFrame) / (float)Fps, 0.2f, "노출 4초");
    }

    /// <summary>은신하면 적에게 안 보이고, 탐지 유닛이 10m 안이면 보이며(은신은 유지), 적이 4m 안에 오면 은신이 풀린다. 동맹·자기에게는 늘 보인다.</summary>
    [UnityTest]
    public IEnumerator Sniper_StealthBrokenByDetectorAndProximity() {
      yield return StartMatch(units: new[] { (0, "ff_sniper", -30, 8), (1, "cf_scout_bike", -30, 30), (1, "cf_militia", 0, 30) });
      var sniper = Sniper(0, "ff_sniper");
      var bike = UnitsNamed(1, "cf_scout_bike").First();
      var militia = UnitsNamed(1, "cf_militia").First();
      Assert.IsFalse(Concealed(sniper, 1), "은신 전에는 보임");
      yield return Send(0, new StealthCommand { Units = new[] { sniper } });
      Assert.Greater(Verified.Get<Unit>(sniper).StealthUntilFrame, Verified.Number, "은신 시작");
      Assert.IsTrue(Concealed(sniper, 1), "은신: 적에게 안 보임");
      Assert.IsFalse(Concealed(sniper, 0), "자기에게는 보임");
      // 은신은 20초.
      Assert.AreEqual(20f, (Verified.Get<Unit>(sniper).StealthUntilFrame - Verified.Number) / (float)Fps, 0.5f);
      // 탐지: 척후 오토바이를 8m까지 보내면(4m 밖) 드러난다.
      yield return Send(1, new MoveUnitsCommand { Units = new[] { bike }, Target = V(-30, 16) });
      yield return WaitFor(() => Concealed(sniper, 1) == false, 20f, "탐지로 드러남");
      Assert.Greater(Verified.Get<Unit>(sniper).StealthUntilFrame, Verified.Number, "탐지돼도 은신 시간은 남는다");
      Assert.GreaterOrEqual(Stat(1, "detections"), 1, "탐지 횟수");
      // 근접: 적 보병이 4m 안에 오면 은신 자체가 풀린다.
      yield return Send(1, new MoveUnitsCommand { Units = new[] { bike }, Target = V(-30, 30) });
      yield return WaitFor(() => Concealed(sniper, 1) && Verified.Get<Unit>(sniper).StealthUntilFrame > Verified.Number, 20f, "탐지 반경 밖으로 가면 다시 숨음");
      yield return Send(1, new MoveUnitsCommand { Units = new[] { militia }, Target = V(-30, 10) });
      yield return WaitFor(() => Verified.Get<Unit>(sniper).StealthUntilFrame <= Verified.Number, 30f, "접근으로 은신 해제");
      Assert.IsFalse(Concealed(sniper, 1), "노출");
      // 재사용 대기: 시작 후 60초가 지나야 다시 쓸 수 있다.
      _rejected.Clear();
      yield return Send(0, new StealthCommand { Units = new[] { sniper } });
      AssertRejected(0, ActionKind.Attack, RejectReason.Cooldown);
    }

    /// <summary>교란 장치: 4초 설치 뒤 노드 채집 속도 ×0.6, 1인당 동시 1개. 탐지로 드러난 뒤 적이 가까이 오면 풀려 사라진다.</summary>
    [UnityTest]
    public IEnumerator Sniper_JammerSlowsGatherAndIsDetectable() {
      yield return StartMatch(truce: 900, units: new[] { (0, "ff_sniper", -30, 8), (1, "cf_scout_bike", -30, 22) });
      var sniper = Sniper(0, "ff_sniper");
      var bike = UnitsNamed(1, "cf_scout_bike").First();
      var node = NodeAt(V(-30, 14));
      Assert.AreNotEqual(EntityRef.None, node, "테스트 맵 철광산");
      _rejected.Clear();
      yield return Send(0, new PlaceJammerCommand { Sniper = sniper, Node = node });
      AssertRejected(0, ActionKind.Attack, RejectReason.Invalid);
      yield return Send(0, new StealthCommand { Units = new[] { sniper } });
      yield return Send(0, new PlaceJammerCommand { Sniper = sniper, Node = node });
      Assert.AreEqual(0, JammerCount(), "설치는 4초 걸린다");
      yield return WaitFor(() => JammerCount() == 1, 10f, "장치 설치");
      Assert.AreEqual(0.6f, SniperSystem.GatherMultiplier(Verified, node).AsFloat, 0.01f, "채집 속도 -40%");
      Assert.AreEqual(1, Stat(0, "placed"));
      _rejected.Clear();
      yield return Send(0, new PlaceJammerCommand { Sniper = sniper, Node = node });
      AssertRejected(0, ActionKind.Attack, RejectReason.Limit);
      // 적 척후가 다가오면 탐지(10m)로 보이고, 4m 안에서 풀어 낸다.
      yield return Send(1, new MoveUnitsCommand { Units = new[] { bike }, Target = V(-30, 9) });
      yield return WaitFor(() => JammerCount() == 0, 40f, "탐지된 장치를 풀어 냄");
      Assert.AreEqual(1f, SniperSystem.GatherMultiplier(Verified, node).AsFloat, 0.01f, "장치가 사라지면 원래 속도");
      Assert.AreEqual(1, Stat(1, "destroyed"));
    }

    /// <summary>플레이어당 3기까지만 가질 수 있다(대기열 포함). 4번째는 Limit으로 거부된다.</summary>
    [UnityTest]
    public IEnumerator Sniper_OwnedCapIsThree() {
      yield return StartMatch(units: new[] { (0, "ff_sniper", -30, 8), (0, "ff_sniper", -28, 8), (0, "ff_sniper", -26, 8) });
      var spec = Verified.FindAsset(Verified.Get<Unit>(Sniper(0, "ff_sniper")).Spec);
      Assert.IsTrue(spec.GarrisonFireOnly);
      Assert.AreEqual(3, spec.MaxOwned);
      Assert.AreEqual(3, SniperSystem.CountOwnedAndQueued(Verified, 0, spec), "가진 수");
      Assert.IsFalse(TechSystem.CanProduce(Verified, 0, spec, out var reason));
      Assert.AreEqual(RejectReason.Limit, reason);
      Assert.AreEqual(2, spec.RequiredTier, "기지 2단계");
      Assert.AreEqual(2, (int)spec.Population, "인구 2");
    }
  }
}
