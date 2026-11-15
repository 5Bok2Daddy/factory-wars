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
  /// 항목 34-D 정찰 연동 비조작 항공 작전 시험. 비행장(ff_airfield)이 격납 칸에 작전기를 보관하고, OrderSortieCommand로 출격한다.
  /// 휴전(truce)을 길게 둬서 시험 밖 유닛이 끼어들지 못하게 한다(작전 피해는 휴전과 무관하게 적용되는지도 같이 본다).
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static readonly ResourceCost SortieStock = new ResourceCost { Iron = 6000, Wood = 3000, Steel = 3000, Power = 3000 };

    static int HangarState(EntityRef airfield, int slot) {
      var building = Verified.Get<Building>(airfield);
      return building.Hangar[slot].State;
    }

    static int HangarReady(EntityRef airfield, int slot) => Verified.Get<Building>(airfield).Hangar[slot].ReadyFrame;

    static int Counter(int player, string name) {
      var state = Verified.Global->Players.GetPointer(player);
      return name == "launched" ? state->SortiesLaunched : name == "completed" ? state->SortiesCompleted : state->SortiesShotDown;
    }

    IEnumerator StoreAircraft(EntityRef airfield, string unit) {
      _rejected.Clear();
      yield return Send(0, new ProduceCommand { Building = airfield, Unit = UnitRef(0, unit), Count = 1 });
      yield return WaitFrames(Fps);
      Assert.IsFalse(_rejected.Any(e => e.Kind == ActionKind.Produce), "생산 거부: " + string.Join(",", _rejected.Select(e => $"{e.Reason}/{(MissingResource)e.Missing}")));
      yield return WaitFrames(5 * Fps);
      var queued = Verified.Get<Building>(airfield);
      Debug.Log($"[SORTIE-DBG] {unit} queue={queued.QueueCount} progress={queued.QueueProgress.AsFloat} paused={queued.Paused} state={HangarState(airfield, 0)} ended={Ended()} frame={Verified.Number}");
      yield return WaitFrames(Fps);
      for (var i = 0; i < 8 && HangarState(airfield, 0) != 1; i++) {
        yield return WaitFrames(10 * Fps);
        var q = Verified.Get<Building>(airfield);
        Debug.Log($"[SORTIE-DBG2] {unit} queue={q.QueueCount} progress={q.QueueProgress.AsFloat} paused={q.Paused} pop={PopInfo()} ended={Ended()} frame={Verified.Number}");
      }
      yield return WaitFor(() => HangarState(airfield, 0) == 1, 90f, "작전기 격납");
    }

    /// <summary>휴전이 끝나야 작전 피해가 들어간다(CanAttack은 휴전 중 false).</summary>
    IEnumerator AfterTruce() {
      yield return WaitFor(() => FactoryWarsUtil.InTruce(Verified) == false, 120f, "휴전 종료");
    }

    static string PopInfo() => $"{Verified.Global->Players.GetPointer(0)->PopulationUsed}/{Verified.Global->Players.GetPointer(0)->PopulationCap}";

    static bool Ended() => Verified.Global->GameEnded;

    static FPVector2 East => new FPVector2(1, 0);

    [Test]
    public void Sortie_PathIsDeterministicFromStartFrame() {
      var spec = Resources.Load<UnitSpec>("FactoryWars/ff_sortie_fighter");
      Assert.AreEqual(SortieKind.Strafe, spec.SortieKind);
      var a = stackalloc FPVector3[SortieSystem.Points];
      var b = stackalloc FPVector3[SortieSystem.Points];
      var ca = stackalloc FP[SortieSystem.Points];
      var cb = stackalloc FP[SortieSystem.Points];
      var home = new FPVector3(-20, 0, 10);
      var target = new FPVector3(10, 0, 10);
      SortieSystem.BuildPath(home, target, East, spec, a, ca);
      SortieSystem.BuildPath(home, target, East, spec, b, cb);
      for (var i = 0; i < SortieSystem.Points; i++) {
        Assert.AreEqual(a[i], b[i]);
        Assert.AreEqual(ca[i].RawValue, cb[i].RawValue);
        if (i > 0) {
          Assert.Greater(ca[i].AsFloat, ca[i - 1].AsFloat, "누적 길이 증가");
        }
      }
      Assert.AreEqual((target.X - spec.SortieApproach).AsFloat, a[1].X.AsFloat, 0.01f, "진입점 E = T - d·Lin");
      Assert.AreEqual((target.X + spec.SortieExit).AsFloat, a[3].X.AsFloat, 0.01f, "이탈점 X = T + d·Lout");
      FPVector3 dir;
      Assert.AreEqual(a[0], SortieSystem.PositionAt(a, ca, FP._0, out dir), "시작은 비행장");
      Assert.AreEqual(target, SortieSystem.PositionAt(a, ca, ca[2], out dir), "T 도달");
      Assert.AreEqual(a[5], SortieSystem.PositionAt(a, ca, ca[5] + 100, out dir), "끝을 넘으면 비행장");
      // 같은 경과 프레임이면 같은 위치(출격 프레임 기준 누적 거리).
      Assert.AreEqual(SortieSystem.PositionAt(a, ca, FP._10, out dir), SortieSystem.PositionAt(b, cb, FP._10, out dir));
    }

    [UnityTest]
    public IEnumerator Sortie_RejectedOutsideVision() {
      UseFog();
      yield return StartMatch(truce: 900, stock: SortieStock, buildings: new[] { (0, "ff_airfield", -20, 10) });
      yield return WaitFrames(Fps);
      var airfield = OwnBuilding(0, "ff_airfield");
      yield return StoreAircraft(airfield, "ff_sortie_fighter");
      _rejected.Clear();
      yield return Send(0, new OrderSortieCommand { Airfield = airfield, Slot = 0, Target = V(40, -40), Heading = East });
      AssertRejected(0, ActionKind.Attack, RejectReason.NoVision);
      Assert.AreEqual(1, HangarState(airfield, 0), "출격하지 않음");
      yield return Send(0, new OrderSortieCommand { Airfield = airfield, Slot = 0, Target = V(-20, 10), Heading = East });
      Assert.AreEqual(2, HangarState(airfield, 0), "보이는 칸이면 출격");
    }

    [UnityTest]
    public IEnumerator Sortie_StrafeHitsCorridorOnly() {
      yield return StartMatch(truce: 40, stock: SortieStock, buildings: new[] { (0, "ff_airfield", -20, 10) },
        units: new[] { (1, "cf_flame_squad", 10, 10), (1, "cf_flame_squad", 10, 22) });
      var airfield = OwnBuilding(0, "ff_airfield");
      yield return StoreAircraft(airfield, "ff_sortie_fighter");
      var inside = UnitsNamed(1, "cf_flame_squad").First(e => At(e).Z.AsFloat < 16f && At(e).X.AsFloat > 5f);
      var outside = UnitsNamed(1, "cf_flame_squad").First(e => At(e).Z.AsFloat > 18f);
      var healthIn = Health(inside);
      var healthOut = Health(outside);
      yield return AfterTruce();
      yield return Send(0, new OrderSortieCommand { Airfield = airfield, Slot = 0, Target = V(10, 10), Heading = East });
      yield return WaitFor(() => Verified.ComponentCount<Sortie>() == 0, 60f, "작전 종료");
      Assert.Less(Health(inside), healthIn - 30f, "통로 안은 피해");
      Assert.AreEqual(healthOut, Health(outside), 0.01f, "통로 밖(12m 옆)은 무사");
      Assert.AreEqual(1, Counter(0, "completed"));
    }

    [UnityTest]
    public IEnumerator Sortie_BombDamagesBuildingDouble() {
      yield return StartMatch(truce: 80, stock: SortieStock, buildings: new[] { (0, "ff_airfield", -20, 10), (0, "ff_power_plant", -30, 0), (0, "ff_power_plant", -30, 20), (1, "cf_barracks", 10, 10), (1, "cf_headquarters", 40, 40) });
      var airfield = OwnBuilding(0, "ff_airfield");
      yield return StoreAircraft(airfield, "ff_sortie_striker");
      var barracks = OwnBuilding(1, "cf_barracks");
      var health = BuildingHealth(barracks).AsFloat;
      yield return AfterTruce();
      yield return Send(0, new OrderSortieCommand { Airfield = airfield, Slot = 0, Target = V(10, 10), Heading = East });
      yield return WaitFor(() => Verified.ComponentCount<Sortie>() == 0, 60f, "작전 종료");
      Assert.Less(BuildingHealth(barracks).AsFloat, health - 300f, "폭탄 4발, 건물 피해 2배");
    }

    [UnityTest]
    public IEnumerator Sortie_AirborneDropLandsCargo() {
      yield return StartMatch(truce: 900, stock: SortieStock, buildings: new[] { (0, "ff_airfield", -20, 10) });
      var airfield = OwnBuilding(0, "ff_airfield");
      yield return StoreAircraft(airfield, "ff_sortie_transport");
      var before = UnitsNamed(0, "ff_ranger_squad").Count;
      yield return Send(0, new OrderSortieCommand { Airfield = airfield, Slot = 0, Target = V(10, 10), Heading = East });
      yield return WaitFor(() => Verified.ComponentCount<Sortie>() == 0, 60f, "작전 종료");
      var landed = UnitsNamed(0, "ff_ranger_squad");
      Assert.AreEqual(before + 6, landed.Count, "병력 6 투하");
      Assert.AreEqual(6, landed.Count(e => (At(e) - V(10, 10)).Magnitude.AsFloat <= 8f), "목표 반경 안에 흩어짐");
    }

    [UnityTest]
    public IEnumerator Sortie_ShotDownLosesPayload() {
      yield return StartMatch(truce: 40, stock: SortieStock, buildings: new[] { (0, "ff_airfield", -20, 10) },
        units: new[] { (1, "cf_thunder_aa", -4, 12), (1, "cf_thunder_aa", 0, 8), (1, "cf_thunder_aa", 4, 12), (1, "cf_thunder_aa", 8, 8), (1, "cf_thunder_aa", 12, 12), (1, "cf_thunder_aa", 16, 8), (1, "cf_thunder_aa", -8, 8), (1, "cf_thunder_aa", 2, 4) });
      var airfield = OwnBuilding(0, "ff_airfield");
      yield return StoreAircraft(airfield, "ff_sortie_transport");
      var before = UnitsNamed(0, "ff_ranger_squad").Count;
      yield return AfterTruce();
      yield return Send(0, new OrderSortieCommand { Airfield = airfield, Slot = 0, Target = V(60, 10), Heading = East });
      yield return WaitFor(() => Verified.ComponentCount<Sortie>() == 0, 60f, "작전 종료");
      Assert.AreEqual(1, Counter(0, "shotdown"), "격추");
      Assert.AreEqual(0, HangarState(airfield, 0), "칸이 빈다(재생산 필요)");
      Assert.AreEqual(before, UnitsNamed(0, "ff_ranger_squad").Count, "탑재 병력은 사라진다");
    }

    [UnityTest]
    public IEnumerator Sortie_ReturnsAndRearms() {
      yield return StartMatch(truce: 900, stock: SortieStock, buildings: new[] { (0, "ff_airfield", -20, 10) });
      var airfield = OwnBuilding(0, "ff_airfield");
      yield return StoreAircraft(airfield, "ff_sortie_fighter");
      yield return Send(0, new OrderSortieCommand { Airfield = airfield, Slot = 0, Target = V(10, 10), Heading = East });
      yield return WaitFor(() => Verified.ComponentCount<Sortie>() == 0, 60f, "작전 종료");
      Assert.AreEqual(1, HangarState(airfield, 0), "귀환해 칸에 다시 대기");
      var rearm = HangarReady(airfield, 0) - Verified.Number;
      Assert.AreEqual(45 * Fps, rearm, 3 * Fps, "재정비 45초");
      _rejected.Clear();
      yield return Send(0, new OrderSortieCommand { Airfield = airfield, Slot = 0, Target = V(10, 10), Heading = East });
      AssertRejected(0, ActionKind.Attack, RejectReason.Cooldown);
      yield return WaitFor(() => Verified.Number >= HangarReady(airfield, 0), 90f, "재정비 끝");
      yield return Send(0, new OrderSortieCommand { Airfield = airfield, Slot = 0, Target = V(10, 10), Heading = East });
      Assert.AreEqual(2, HangarState(airfield, 0), "재정비 뒤 다시 출격");
      Assert.AreEqual(2, Counter(0, "launched"));
    }
  }
}
