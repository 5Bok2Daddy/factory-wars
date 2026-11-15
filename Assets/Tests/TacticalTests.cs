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

  /// <summary>항목 37(일부) 시험: 베테랑 승급, 전술 도크린(선택·잠금·효과·AI·기본값), 맵별 기상(시야 감소·일반 출격 제한).</summary>
  public unsafe partial class LocalMatchTests {
    static int DoctrineOf(int player) => TacticalSystem.DoctrineOf(Verified, player);

    static bool DoctrineLockedOf(int player) => Verified.Global->Players.GetPointer(player)->DoctrineLocked;

    static int PreferredOf(int player) => Verified.FindAsset(Verified.Global->Players.GetPointer(player)->Faction).PreferredDoctrine;

    static bool WeatherNow() => Verified.Global->WeatherOn;

    static int Promotions(int player) => Verified.Global->Players.GetPointer(player)->Promotions;

    /// <summary>저격수가 보병 3기를 잡으면 계급 1(★)로 승급하고 최대 체력 +10%만큼 체력이 오른다.</summary>
    [UnityTest]
    public IEnumerator Vet_PromotesAtKillThresholds() {
      yield return StartMatch(units: new[] { (0, "ff_sniper", -10, -15), (1, "cf_flame_squad", -10, -33), (1, "cf_flame_squad", -8, -33), (1, "cf_flame_squad", -12, -33) });
      var building = CityAt(0);
      var sniper = Sniper(0, "ff_sniper");
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      Assert.AreEqual(3, rules.VetKillsRank1);
      var maxHealth = Verified.FindAsset(Verified.Get<Unit>(sniper).Spec).MaxHealth.AsFloat;
      yield return Send(0, new EnterGarrisonCommand { Units = new[] { sniper }, Building = building });
      yield return WaitFor(() => GarrisonOf(sniper) == building, 20f, "진입");
      Assert.AreEqual(0, Verified.Get<Unit>(sniper).VetRank);
      yield return WaitFor(() => Verified.Get<Unit>(sniper).Kills >= 3, 90f, "킬 3");
      var unit = Verified.Get<Unit>(sniper);
      Assert.AreEqual(1, unit.VetRank, "★ 승급");
      Assert.AreEqual(1, Promotions(0));
      Assert.AreEqual(maxHealth * (1f + rules.VetHealthBonus[0].AsFloat), unit.Health.AsFloat, 0.6f, "체력 보너스");
      Assert.AreEqual(1.08f, 1f + rules.VetDamageBonus[0].AsFloat, 0.001f);
    }

    /// <summary>도크린은 한 번만 고를 수 있고, 기갑 요새화 군단은 전차 생산비를 15% 깎는다.</summary>
    [UnityTest]
    public IEnumerator Doctrine_ChoiceLocksAndArmorDiscount() {
      yield return StartMatch(stock: SortieStock, buildings: new[] { (0, "ff_vehicle_factory", -20, 10), (0, "ff_power_plant", -30, 0), (0, "ff_power_plant", -30, 20) });
      Assert.IsFalse(DoctrineLockedOf(0));
      Assert.AreEqual(0, DoctrineOf(0));
      yield return Send(0, new ChooseDoctrineCommand { Doctrine = 3 });
      Assert.IsTrue(DoctrineLockedOf(0));
      Assert.AreEqual(3, DoctrineOf(0));
      _rejected.Clear();
      yield return Send(0, new ChooseDoctrineCommand { Doctrine = 1 });
      AssertRejected(0, ActionKind.Research, RejectReason.Doctrine);
      Assert.AreEqual(3, DoctrineOf(0), "바뀌지 않는다");
      var factory = OwnBuilding(0, "ff_vehicle_factory");
      var before = ExactFunds(0);
      yield return Send(0, new ProduceCommand { Building = factory, Unit = UnitRef(0, "ff_eagle_tank"), Count = 1 });
      Assert.AreEqual(250f * 0.85f, before - ExactFunds(0), 1f, "전차 철 250 → 212.5");
    }

    /// <summary>그림자 특수전단: 저격수 보유 한도가 3 → 4. 도크린 없이는 3기에서 막힌다.</summary>
    [UnityTest]
    public IEnumerator Doctrine_ShadowRaisesSniperCap() {
      yield return StartMatch(units: new[] { (0, "ff_sniper", -30, 8), (0, "ff_sniper", -28, 8), (0, "ff_sniper", -26, 8) });
      var spec = Verified.FindAsset(Verified.Get<Unit>(Sniper(0, "ff_sniper")).Spec);
      Assert.IsFalse(TechSystem.CanProduce(Verified, 0, spec, out _), "도크린 없음: 한도 3");
      yield return Send(0, new ChooseDoctrineCommand { Doctrine = 2 });
      Assert.IsTrue(TechSystem.CanProduce(Verified, 0, spec, out _), "그림자 특수전단: 한도 4");
    }

    /// <summary>AI는 시작 직후 진영 성향대로 고르고(자유 1, 대륙 3), 사람이 안 고르면 선택 시간 뒤 기본값으로 정해진다.</summary>
    [UnityTest]
    public IEnumerator Doctrine_BotsChooseByFactionAndDefaultAtDeadline() {
      yield return StartMatch(players: 2, bots: new[] { BotDifficulty.Normal }, seed: 5);
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      yield return WaitFrames(Fps);
      Assert.IsTrue(DoctrineLockedOf(1), "AI는 바로 고른다");
      Assert.AreEqual(PreferredOf(1), DoctrineOf(1), "진영 성향");
      Assert.IsFalse(DoctrineLockedOf(0), "사람은 아직");
      yield return WaitFor(() => DoctrineLockedOf(0), 60f, "선택 시간 종료");
      Assert.AreEqual(rules.DefaultDoctrine, DoctrineOf(0), "기본값");
      Assert.GreaterOrEqual(Verified.Number, rules.DoctrineSelectSeconds * Fps - 2);
    }

    /// <summary>사막 모래폭풍: 기상 중 기본 시야가 줄고, 신호탄·정찰 사진 밖의 일반 출격은 거부된다. 끝나면 돌아온다.</summary>
    [UnityTest]
    public IEnumerator Weather_ReducesSightAndBlocksSortie() {
      UseFog();
      var desert = MapNamed("map_MapDesert");
      var first = desert.WeatherFirstSeconds;
      var duration = desert.WeatherDurationSeconds;
      var period = desert.WeatherPeriodSeconds;
      Restores.Add(() => { desert.WeatherFirstSeconds = first; desert.WeatherDurationSeconds = duration; desert.WeatherPeriodSeconds = period; });
      desert.WeatherFirstSeconds = 40;
      desert.WeatherDurationSeconds = 30;
      desert.WeatherPeriodSeconds = 200;
      yield return StartMatch(DesertScene, 2, bots: new[] { BotDifficulty.Normal }, seed: 5, startingTier: 3, stock: SortieStock, buildings: new[] { (0, "ff_airfield", -20, 10), (0, "ff_power_plant", -30, 0), (0, "ff_power_plant", -30, 20) },
        units: new[] { (0, "ff_ranger_squad", -30, 6) });
      var ranger = UnitsNamed(0, "ff_ranger_squad").First(e => At(e).Z.AsFloat < 8f);
      var airfield = OwnBuilding(0, "ff_airfield");
      yield return StoreAircraft(airfield, "ff_sortie_fighter");
      yield return WaitFrames(Fps);
      Assert.IsFalse(WeatherNow(), "아직 맑음");
      var probe = At(ranger) + new FPVector3(8, 0, 0);
      Assert.IsTrue(FogSystem.IsVisible(Verified, 0, probe), "맑을 때 8m 보임(시야 10)");
      yield return WaitFor(WeatherNow, 120f, "모래폭풍 시작");
      yield return WaitFrames(Fps);
      Assert.IsFalse(FogSystem.IsVisible(Verified, 0, probe), "기상 중 시야 -35%: 8m 안 보임");
      Assert.IsTrue(FogSystem.IsVisible(Verified, 0, At(ranger) + new FPVector3(5, 0, 0)), "5m는 보임");
      _rejected.Clear();
      yield return Send(0, new OrderSortieCommand { Airfield = airfield, Slot = 0, Target = V(-20, 10), Heading = East });
      AssertRejected(0, ActionKind.Attack, RejectReason.Weather);
      Assert.AreEqual(1, HangarState(airfield, 0), "출격하지 않음");
      yield return WaitFor(() => WeatherNow() == false, 120f, "모래폭풍 끝");
      yield return WaitFrames(Fps);
      Assert.IsTrue(FogSystem.IsVisible(Verified, 0, probe), "다시 8m 보임");
      yield return Send(0, new OrderSortieCommand { Airfield = airfield, Slot = 0, Target = V(-20, 10), Heading = East });
      Assert.AreEqual(2, HangarState(airfield, 0), "기상이 끝나면 출격");
    }
  }
}
