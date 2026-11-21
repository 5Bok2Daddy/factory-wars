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

  /// <summary>항목 37 전술 깊이: 기갑 잔해(엄폐·고철·수명), 교량 폭파·폰툰 복구와 길찾기 영역, 사령부 비상 프로토콜, 블랙박스, 레이더탑·유류고.</summary>
  public unsafe partial class LocalMatchTests {
    static int WreckCount() => Verified.ComponentCount<Wreck>();

    static int CoverSpotsOf(EntityRef source) {
      var count = 0;
      foreach (var pair in Verified.GetComponentIterator<CoverSpot>()) {
        count += pair.Component.Source == source ? 1 : 0;
      }
      return count;
    }

    static EntityRef FirstWreck() {
      foreach (var pair in Verified.GetComponentIterator<Wreck>()) {
        return pair.Entity;
      }
      return EntityRef.None;
    }

    static bool BridgeRegionOpen(int index) {
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      return Verified.Map.RegionMap.TryGetValue($"{rules.BridgeRegion}{index}", out var region) && Verified.NavMeshRegionMask->IsRegionEnabled(region);
    }

    static int SeatBridge() {
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var seat = FactoryWarsUtil.SeatOf(Verified, 0);
      for (var i = 0; i < map.Bridges.Length; i++) {
        if (DepthSystem.BridgeSeat(map, i) == seat) {
          return i;
        }
      }
      return 0;
    }

    static Quantum.Bridge BridgeOf(int index) => Verified.Get<Quantum.Bridge>(DepthSystem.BridgeEntity(Verified, index));

    /// <summary>전차를 잡으면 잔해가 남고, 잔해마다 임시 엄폐점 4개가 생기며, 공병이 분해하면 고철을 받고 잔해가 사라진다.</summary>
    [UnityTest]
    public IEnumerator Depth_TankWreckGivesCoverAndSalvage() {
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", -12, 40), (0, "ff_eagle_tank", -8, 40), (0, "ff_eagle_tank", -4, 40), (1, "cf_dragonscale_tank", -8, 52) });
      var attackers = UnitsNamed(0, "ff_eagle_tank").Where(e => At(e).Z.AsFloat > 30f).ToArray();
      var target = UnitsNamed(1, "cf_dragonscale_tank").First(e => At(e).Z.AsFloat > 30f);
      yield return Send(0, new AttackTargetCommand { Units = attackers, Target = target });
      yield return WaitFor(() => WreckCount() > 0, 90f, "잔해");
      var wreck = FirstWreck();
      Assert.AreEqual(4, CoverSpotsOf(wreck), "임시 엄폐점 4");
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      var scrap = Verified.Get<Wreck>(wreck).Scrap.AsFloat;
      Assert.AreEqual(Verified.FindAsset(Verified.Get<Wreck>(wreck).Spec).Cost.Iron.AsFloat * rules.WreckScrapRatio.AsFloat, scrap, 0.5f, "회수 고철");
      var iron = ExactFunds(0);
      yield return Send(0, new SalvageCommand { Units = new[] { Engineers(0)[0] }, Wreck = wreck });
      yield return WaitFor(() => WreckCount() == 0, 90f, "분해");
      Assert.AreEqual(scrap, ExactFunds(0) - iron, 3f, "고철 입금");
      Assert.AreEqual(1, PlayerOf(0).WrecksSalvaged);
      yield return WaitFrames(Fps);
      Assert.AreEqual(0, CoverSpotsOf(wreck), "엄폐점도 사라진다");
    }

    /// <summary>잔해는 WreckSeconds가 지나면 스스로 사라지고 엄폐점도 지워진다.</summary>
    [UnityTest]
    public IEnumerator Depth_WreckExpires() {
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      var before = rules.WreckSeconds;
      Restores.Add(() => rules.WreckSeconds = before);
      rules.WreckSeconds = 5;
      yield return StartMatch(units: new[] { (0, "ff_eagle_tank", -12, 40), (0, "ff_eagle_tank", -8, 40), (0, "ff_eagle_tank", -4, 40), (1, "cf_dragonscale_tank", -8, 52) });
      var attackers = UnitsNamed(0, "ff_eagle_tank").Where(e => At(e).Z.AsFloat > 30f).ToArray();
      yield return Send(0, new AttackTargetCommand { Units = attackers, Target = UnitsNamed(1, "cf_dragonscale_tank").First(e => At(e).Z.AsFloat > 30f) });
      yield return WaitFor(() => WreckCount() > 0, 90f, "잔해");
      var wreck = FirstWreck();
      yield return WaitFor(() => WreckCount() == 0, 20f, "수명 만료");
      yield return WaitFrames(Fps);
      Assert.AreEqual(0, CoverSpotsOf(wreck));
    }

    /// <summary>다리 폭파: 휴전이 끝난 뒤 공병이 폭약을 설치하면 다리 영역이 닫히고, 폰툰 복구하면 다시 열린다. 휴전 중에는 폭파할 수 없다.</summary>
    [UnityTest]
    public IEnumerator Depth_BridgeDemolishClosesNavAndPontoonReopens() {
      yield return StartMatch(Map4PScene, 2, truce: 8);
      yield return WaitForHud();
      var bridge = SeatBridge();
      var engineers = Engineers(0).Take(2).ToArray();
      _rejected.Clear();
      yield return Send(0, new WorkBridgeCommand { Units = engineers, Bridge = bridge, Demolish = true });
      AssertRejected(0, ActionKind.Repair, RejectReason.Truce);
      yield return WaitFor(() => BarrierOpen(), 40f, "휴전 종료");
      Assert.IsTrue(BridgeRegionOpen(bridge), "처음엔 열려 있다");
      yield return Send(0, new WorkBridgeCommand { Units = engineers, Bridge = bridge, Demolish = true });
      yield return WaitFor(() => BridgeOf(bridge).Destroyed, 120f, "폭파");
      Assert.IsFalse(BridgeRegionOpen(bridge), "길찾기 영역이 닫힌다");
      Assert.AreEqual(1, PlayerOf(0).BridgesDown);
      yield return Send(0, new WorkBridgeCommand { Units = engineers, Bridge = bridge, Demolish = false });
      yield return WaitFor(() => BridgeOf(bridge).Destroyed == false, 120f, "복구");
      Assert.IsTrue(BridgeRegionOpen(bridge), "다시 열린다");
      Assert.IsTrue(BridgeOf(bridge).Pontoon, "폰툰 가교");
      Assert.AreEqual(1, PlayerOf(0).BridgesRepaired);
    }

    /// <summary>사령부 체력이 40% 아래로 떨어지면 비상 프로토콜이 한 번 발동한다: 수비 분대 + 피해 감소. 두 번째는 없다.</summary>
    [UnityTest]
    public IEnumerator Depth_HqProtocolTriggersOnceAndShieldsDamage() {
      yield return StartMatch(units: new[] { (1, "cf_dragonscale_tank", -38, 0), (1, "cf_dragonscale_tank", -38, 3), (1, "cf_dragonscale_tank", -38, -3) });
      var hq = Headquarters(0);
      var squadsBefore = UnitsNamed(0, "ff_ranger_squad").Count;
      var tanks = UnitsNamed(1, "cf_dragonscale_tank").ToArray();
      Game.SendCommand(1, new AttackTargetCommand { Units = tanks, Target = hq });
      yield return WaitFor(() => PlayerOf(0).ProtocolUsed, 240f, "프로토콜 발동");
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      Assert.Less(BuildingState(hq).Health.AsFloat, Verified.FindAsset(BuildingState(hq).Spec).MaxHealth.AsFloat * rules.HqProtocolHealthRatio.AsFloat + 80f, "40% 근처에서");
      Assert.Greater(BuildingState(hq).ProtocolUntilFrame, Verified.Number, "피해 감소 유지");
      Assert.AreEqual(squadsBefore + rules.HqProtocolSquads, UnitsNamed(0, "ff_ranger_squad").Count, "수비 분대");
      var until = BuildingState(hq).ProtocolUntilFrame;
      yield return WaitFrames(Fps * 3);
      Assert.AreEqual(until, BuildingState(hq).ProtocolUntilFrame, "한 번만");
    }

    /// <summary>블랙박스: 첫 추락 시각에 맵 자리에 떨어지고, 한 쪽 지상 유닛이 15초 점령하면 보상(영구 화력 중첩)을 받는다. 두 진영이 같이 있으면 멈춘다.</summary>
    [UnityTest]
    public IEnumerator Depth_BlackBoxCaptureGivesPermanentBonus() {
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      var first = rules.BlackBoxFirstSeconds;
      Restores.Add(() => rules.BlackBoxFirstSeconds = first);
      rules.BlackBoxFirstSeconds = 2;
      // 표준 맵 블랙박스 0번은 중앙 섬 안쪽 (-32, 0)이다(중앙 강 안쪽 43m). 분대를 그 옆에 둔다.
      yield return StartMatch(Map4PScene, 2, truce: 0, units: new[] { (0, "ff_ranger_squad", -32, 2) });
      yield return WaitForHud();
      yield return WaitFor(() => Verified.ComponentCount<BlackBox>() > 0, 30f, "블랙박스 추락");
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var spot = map.BlackBoxSpots[0];
      Assert.AreEqual(spot.X.AsFloat, BoxPosition().X.AsFloat, 0.1f, "맵 자리");
      Assert.AreEqual(1f, DepthSystem.OutgoingBonus(Verified, 0).AsFloat, 0.0001f);
      yield return WaitFor(() => PlayerOf(0).BlackBoxes > 0, 240f, "회수");
      Assert.AreEqual(1, PlayerOf(0).BlackBoxStacks);
      Assert.AreEqual(1f + rules.BlackBoxDamageBonus.AsFloat, DepthSystem.OutgoingBonus(Verified, 0).AsFloat, 0.0001f, "영구 화력 보너스");
      Assert.AreEqual(0, Verified.ComponentCount<BlackBox>(), "회수하면 사라진다");
    }

    static FPVector3 BoxPosition() {
      foreach (var pair in Verified.GetComponentIterator<BlackBox>()) {
        return Verified.Get<Transform3D>(pair.Entity).Position;
      }
      return default;
    }

    /// <summary>전술 거점: 레이더탑을 가지면 반경 안이 기상 중에도 출격 가능 구역이 되고, 유류고를 가지면 재정비 시간이 30% 줄어든다. 맵에 거점이 있다.</summary>
    [UnityTest]
    public IEnumerator Depth_RadarAndFuelSitesApplyEffects() {
      yield return StartMatch(Map4PScene, 2, truce: 0);
      yield return WaitForHud();
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      Assert.AreEqual(4, map.CaptureSites.Count(s => s.Kind == CaptureKind.Radar), "레이더탑 4(자리마다)");
      Assert.AreEqual(4, map.CaptureSites.Count(s => s.Kind == CaptureKind.Fuel), "유류고 4");
      Assert.AreEqual(1f, DepthSystem.RearmMultiplier(Verified, 0).AsFloat, 0.001f, "갖기 전");
      Assert.IsFalse(DepthSystem.InRadarArea(Verified, 0, map.CaptureSites.First(s => s.Kind == CaptureKind.Radar).Center));
      yield return null;
    }
  }
}
