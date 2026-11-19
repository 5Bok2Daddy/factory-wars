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
  /// 항목 33 궁극 전력. 시험 맵에서 자리 0은 (-30,0), 자리 1은 (30,0)이다. 전용 건물·연구소·발전소 3채는 시작 때 놓고(시나리오 건물),
  /// 궁극 전력 연구와 준비는 실제 명령으로 한다. 준비·연구·경고 시간은 시험이 메모리에서만 줄인다(TearDown이 되돌린다).
  /// </summary>
  public unsafe partial class LocalMatchTests {
    readonly List<(int Player, UltimateStage Stage, UltimateKind Kind, FPVector3 Position, int Value)> _notices = new List<(int, UltimateStage, UltimateKind, FPVector3, int)>();

    static UltimateSpec UltimateOf(int player) => UltimateSystem.SpecOf(Verified, player);

    static UltimatePhase PhaseOf(int player) => StateOf(player).UltimatePhase;

    /// <summary>자리 0(자유 연방) 기지: 발전소 3(전기 한도 1100), 연구소, 전용 건물.</summary>
    static (int, string, int, int)[] UltimateBase(int player, string prefix, string site, bool withSite = true) {
      var x = player == 0 ? -1 : 1;
      var list = new List<(int, string, int, int)> {
        (player, prefix + "_power_plant", 46 * x, -10), (player, prefix + "_power_plant", 46 * x, -18), (player, prefix + "_power_plant", 46 * x, -26),
        (player, prefix + "_research_lab", 38 * x, -24),
      };
      if (withSite) {
        list.Add((player, site, 18 * x, 14));
      }
      return list.ToArray();
    }

    /// <summary>시험 시간을 줄인다: 준비 prepare초, 연구 2초, 재사용 대기 4초, 미사일 경고 warning초, 드론 재생산 2초.</summary>
    static void ShortenUltimates(int prepare = 8, int warning = 4) {
      foreach (var spec in Resources.LoadAll<UltimateSpec>("FactoryWars")) {
        var target = spec;
        var before = (target.PrepareSeconds, target.RearmSeconds, target.CooldownSeconds, target.WarningSeconds, target.DroneBuildSeconds);
        target.PrepareSeconds = prepare;
        target.RearmSeconds = prepare;
        target.CooldownSeconds = 4;
        target.WarningSeconds = target.WarningSeconds > FP._0 ? warning : FP._0;
        target.DroneBuildSeconds = target.DroneBuildSeconds > FP._0 ? 2 : FP._0;
        Restores.Add(() => {
          target.PrepareSeconds = before.Item1;
          target.RearmSeconds = before.Item2;
          target.CooldownSeconds = before.Item3;
          target.WarningSeconds = before.Item4;
          target.DroneBuildSeconds = before.Item5;
        });
      }
      foreach (var research in Resources.LoadAll<ResearchSpec>("FactoryWars").Where(r => r.Kind == ResearchKind.Ultimate)) {
        var target = research;
        var before = target.Seconds;
        target.Seconds = 2;
        Restores.Add(() => target.Seconds = before);
      }
    }

    void ListenNotices() {
      _notices.Clear();
      QuantumEvent.Subscribe<EventUltimateNotice>(_listener, e => _notices.Add(((int)e.Player, e.Stage, e.Kind, e.Position, e.Value)));
    }

    bool Noticed(int player, UltimateStage stage) => _notices.Any(n => n.Player == player && n.Stage == stage);

    IEnumerator ResearchUltimate(int player, string prefix) {
      var lab = OwnBuilding(player, prefix + "_research_lab");
      var spec = UltimateOf(player);
      yield return Send(player, new ResearchCommand { Building = lab, Index = TechSystem.IndexOf(Verified, player, spec.Research) });
      yield return WaitFor(() => UltimateSystem.HasResearch(Verified, player, UltimateOf(player)), 30f, "궁극 전력 연구 완료");
    }

    IEnumerator PrepareUltimate(int player, string prefix, UltimatePhase until) {
      yield return ResearchUltimate(player, prefix);
      yield return Send(player, new StartUltimateCommand());
      Assert.AreEqual(UltimatePhase.Preparing, PhaseOf(player), "준비 시작");
      yield return WaitFor(() => PhaseOf(player) == until, 60f, $"궁극 전력 {until}");
    }

    /// <summary>재사용 대기가 끝나고 다시 준비할 자원(전기 포함)이 모일 때까지 기다린 뒤 준비를 시작한다.</summary>
    IEnumerator Rearm(int player, UltimatePhase until = UltimatePhase.Preparing) {
      yield return WaitFor(() => UltimateSystem.Locks(Verified, player) == UltimateLock.None, 60f, "재준비 가능(대기 종료·자원)");
      yield return Send(player, new StartUltimateCommand());
      Assert.AreNotEqual(UltimatePhase.None, PhaseOf(player), "다시 준비 시작");
      if (until != UltimatePhase.Preparing) {
        yield return WaitFor(() => PhaseOf(player) == until, 60f, $"재준비 {until}");
      }
    }

    static int CountComponent<T>(int owner) where T : unmanaged, IComponent {
      var count = 0;
      foreach (var pair in Verified.GetComponentIterator<T>()) {
        count += Verified.Get<Unit>(pair.Entity).Owner == owner ? 1 : 0;
      }
      return count;
    }

    static (EntityRef Entity, T Component) FirstOf<T>() where T : unmanaged, IComponent {
      foreach (var pair in Verified.GetComponentIterator<T>()) {
        return (pair.Entity, pair.Component);
      }
      Assert.Fail($"{typeof(T).Name} 없음");
      return default;
    }

    static EntityRef CarrierOf(int owner) {
      foreach (var pair in Verified.GetComponentIterator<Carrier>()) {
        if (Verified.Get<Unit>(pair.Entity).Owner == owner) {
          return pair.Entity;
        }
      }
      return EntityRef.None;
    }

    static FP Falloff(FPVector3 position, FPVector3 center, UltimateSpec spec) {
      var distance = FPMath.Sqrt(FactoryWarsUtil.FlatDistanceSqr(position, center));
      return FP._1 - (FP._1 - spec.BlastEdgeFraction) * FPMath.Clamp01(distance / spec.BlastRadius);
    }

    [Test]
    public void Ultimate_DataIsHardToBuildAndCounterable() {
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      var buildings = Resources.LoadAll<BuildingSpec>("FactoryWars");
      var researches = Resources.LoadAll<ResearchSpec>("FactoryWars");
      var units = Resources.LoadAll<UnitSpec>("FactoryWars");
      var kinds = new HashSet<UltimateKind>();
      foreach (var faction in Resources.LoadAll<FactionSpec>("FactoryWars")) {
        Assert.IsTrue(faction.Ultimate.IsValid, $"{faction.name}: 궁극 전력 연결");
        var spec = Resources.LoadAll<UltimateSpec>("FactoryWars").First(u => u.Guid == faction.Ultimate.Id);
        kinds.Add(spec.Kind);
        Assert.AreEqual(3, spec.RequiredTier, "기지 3단계");
        var site = buildings.First(b => b.Guid == spec.Site.Id);
        Assert.AreEqual(BuildingRole.UltimateSite, site.Role);
        Assert.AreEqual(3, site.RequiredTier, "전용 건물도 3단계");
        Assert.GreaterOrEqual(site.Prerequisites.Length, 2, "선행 건물 2개 이상");
        Assert.IsTrue(faction.Buildings.Any(b => b.Id == site.Guid), "전용 건물이 진영 건설 목록에 있음");
        Assert.AreEqual(UpkeepMode.WhileUltimate, site.Upkeep);
        Assert.GreaterOrEqual(site.PowerUpkeep.AsFloat, 3f, "큰 전기 유지비");
        var research = researches.First(r => r.Guid == spec.Research.Id);
        Assert.AreEqual(ResearchKind.Ultimate, research.Kind);
        Assert.IsTrue(faction.Researches.Any(r => r.Id == research.Guid), "궁극 전력 연구가 진영 연구 목록에 있음");
        Assert.GreaterOrEqual(research.Seconds.AsFloat, 60f, "연구 시간 큼");
        // 비용: 대형 유닛 3.5~11기 수준(고위험·고보상 슈퍼웨폰), 강철 비중 큼. 준비 60~240초.
        var large = faction.Units.Select(u => units.First(s => s.Guid == u.Id)).Where(u => u.Size == UnitSize.Large && u.IsNaval == false).ToArray();
        var average = large.Average(u => (u.Cost.Iron + u.Cost.Wood + u.Cost.Power + u.Cost.Steel * 3).AsFloat);
        var worth = (spec.Cost.Iron + spec.Cost.Wood + spec.Cost.Power + spec.Cost.Steel * 3).AsFloat;
        Assert.That(worth / average, Is.InRange(3.5f, 11.5f), $"{faction.name}: 대형 {worth / average:0.0}기 수준");
        Assert.GreaterOrEqual((spec.Cost.Steel * 3).AsFloat, spec.Cost.Iron.AsFloat, "강철 비중");
        Assert.That(spec.PrepareSeconds.AsFloat, Is.InRange(60f, 240f), "준비 60~240초");
        Assert.AreEqual(1, spec.OwnedLimit, "보유 상한 1");
        Assert.AreEqual(0.5f, spec.RefundFraction.AsFloat, 0.001f, "전용 건물 파괴 시 절반 환불");
        Assert.Greater(spec.CooldownSeconds.AsFloat, 0f);
      }
      Assert.AreEqual(3, kinds.Count, "항모·미사일·상륙 세 종류");
      var carrier = Resources.Load<UltimateSpec>("FactoryWars/ultimate_ff_carrier");
      Assert.AreEqual(6, carrier.DroneCount, "자율 6드론 윙");
      Assert.AreEqual(2800, carrier.Cost.Iron.AsInt, "항모 철 2800");
      Assert.AreEqual(2200, carrier.Cost.Steel.AsInt, "항모 강철 2200");
      Assert.AreEqual(80, carrier.PrepareSeconds.AsInt, "항모 지상 조립 80초");
      Assert.Greater(carrier.AntiAirTakenMultiplier.AsFloat, 1f, "항모는 대공에 취약");
      var missile = Resources.Load<UltimateSpec>("FactoryWars/ultimate_cf_missile");
      Assert.IsTrue(missile.FriendlyFire, "아군 피해 기본 켬");
      Assert.AreEqual(2500, missile.Cost.Iron.AsInt, "미사일 철 2500");
      Assert.AreEqual(2000, missile.Cost.Steel.AsInt, "미사일 강철 2000");
      Assert.AreEqual(75, missile.PrepareSeconds.AsInt, "미사일 준비 75초");
      Assert.AreEqual(120, missile.CooldownSeconds.AsInt, "미사일 재충전 120초");
      Assert.AreEqual(300, missile.RearmCost.Steel.AsInt, "미사일 재장전 강철 300");
      Assert.AreEqual(12, missile.BlastRadius.AsInt, "폭발 반경 12m");
      Assert.AreEqual(5, missile.EmpDurationSeconds.AsInt, "EMP 마비 5초");
      Assert.AreEqual(15, missile.ScorchedDurationSeconds.AsInt, "초토화 구역 15초");
      Assert.GreaterOrEqual(missile.WarningSeconds.AsFloat, 6f, "비행 궤적 6~8초");
      Assert.Greater(missile.InterceptSteps.Length, 1, "요격 단계");
      // 메뉴·AI 대전에 나오는 두 진영은 항모와 미사일이고, 상륙은 시험 전용 진영에만 붙어 있다.
      Assert.AreEqual(UltimateKind.Carrier, Resources.LoadAll<UltimateSpec>("FactoryWars").First(u => u.Guid == Resources.Load<FactionSpec>("FactoryWars/faction_free_federation").Ultimate.Id).Kind);
      Assert.AreEqual(UltimateKind.StrategicMissile, Resources.LoadAll<UltimateSpec>("FactoryWars").First(u => u.Guid == Resources.Load<FactionSpec>("FactoryWars/faction_continental_federation").Ultimate.Id).Kind);
      Assert.IsFalse(rules.EasyAi.Id == rules.NormalAi.Id);
      Assert.IsFalse(Resources.Load<AiSpec>("FactoryWars/ai_easy").UseUltimate, "쉬움은 궁극 전력을 쓰지 않음");
      Assert.IsTrue(Resources.Load<AiSpec>("FactoryWars/ai_normal").UseUltimate, "보통은 씀");
    }

    [UnityTest]
    public IEnumerator Ultimate_MaxCountOneSiteAndMissileThreeSecondDelay() {
      // 1) 미사일 격납고(UltimateSite)는 안전 본진 배치 불가(BlockedLocation), 암시장·점령 거점 18m 이내 건설 시 즉시 경고 방송 & 1채 상한(Limit).
      var bases = UltimateBase(0, "cf", null, withSite: false)
        .Concat(new[] { (0, "cf_heavy_works", -18, -30), (0, "cf_radar", -18, -20), (0, "cf_power_plant", -46, -2) }).ToArray();
      yield return StartMatch(swapFactions: true, buildings: bases, stock: Stock(30000, 30000, 30000, 1400));
      ShortenUltimates(prepare: 2, warning: 7);
      ListenNotices();
      yield return WaitForHud();

      // 본진 내 배치 시도 -> 중립 거점·암시장 18m 제한에 걸려 BlockedLocation 거부.
      _rejected.Clear();
      yield return Build(0, "_missile_silo", V(-18, 14));
      AssertRejected(0, ActionKind.Build, RejectReason.BlockedLocation);

      // 암시장(40, 20) 반경 18m 이내(30, 14) 건설 -> 허용 + 즉시 전장 경고 방송.
      var validSpot = V(30, 14);
      yield return Build(0, "_missile_silo", validSpot);
      yield return WaitFrames(CommandFrames + 2);
      Assert.AreEqual(1, ProductionSystem.CountRole(Verified, 0, BuildingRole.UltimateSite), "격납고 1채 건설 중");
      Assert.IsTrue(_notices.Any(n => n.Stage == UltimateStage.Started && n.Value < 0), "격납고 착공 즉시 전장 경고 방송");

      // 2번째 미사일 격납고 건설 시도 -> Limit 거부.
      _rejected.Clear();
      yield return Build(0, "_missile_silo", V(28, 24));
      AssertRejected(0, ActionKind.Build, RejectReason.Limit);

      // 기존 격납고가 파괴되면 다시 지을 수 있다.
      var firstSilo = BuildingAt(validSpot);
      Verified.Destroy(firstSilo);
      yield return WaitFrames(2);
      Assert.AreEqual(0, ProductionSystem.CountRole(Verified, 0, BuildingRole.UltimateSite), "파괴 후 0채");
      ProductionSystem.CreateFinishedBuilding(Verified, 0, BuildingOf(0, "_missile_silo"), validSpot);
      yield return WaitFrames(2);

      // 2) 전략 미사일 비행 궤적 7초(WarningSeconds = 7) 및 비행 체력 300 HP 검증.
      var spec = UltimateOf(0);
      Assert.AreEqual(7, spec.WarningSeconds.AsInt, "7초 포물선 비행 궤적");
      Assert.AreEqual(300, spec.MissileHealth.AsInt, "비행 중 요격 체력 300 HP");
      var beforePrep = spec.PrepareSeconds;
      spec.PrepareSeconds = 2;
      Restores.Add(() => spec.PrepareSeconds = beforePrep);
      yield return ResearchUltimate(0, "cf");
      yield return Send(0, new StartUltimateCommand());
      yield return WaitFor(() => PhaseOf(0) == UltimatePhase.Ready, 15f, "미사일 준비 완료");
      _notices.Clear();
      yield return Send(0, new LaunchUltimateCommand { Target = V(0, 20) });
      var launched = _notices.Single(n => n.Stage == UltimateStage.Launched);
      Assert.AreEqual(7, launched.Value, "7초 비행 알림");
      var strike = FirstOf<MissileStrike>().Component;
      Assert.AreEqual(strike.LaunchFrame + 7 * Fps, strike.ImpactFrame, "정확히 7초(140프레임) 뒤 착탄");
      Assert.AreEqual(300, strike.Health.AsInt, "초기 비행 체력 300");
    }

    [UnityTest]
    public IEnumerator Ultimate_RequiresTier3BuildingsAndResearch() {
      ShortenUltimates();
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      var tiers = rules.BaseTiers;
      var tierSeconds = tiers[1].Seconds;
      tiers[1].Seconds = 3;
      Restores.Add(() => tiers[1].Seconds = tierSeconds);
      // 2단계로 시작: 연구소·중공업 기지·발전소는 있고, 비행장과 전용 건물은 없다.
      // 비행장의 선행 건물(군수공장·가공소)도 둔다.
      var buildings = UltimateBase(0, "ff", null, withSite: false).Concat(new[] { (0, "ff_heavy_works", -18, -30), (0, "ff_vehicle_factory", -46, 6), (0, "ff_processor", -46, 16) }).ToArray();
      yield return StartMatch(startingTier: 2, buildings: buildings, stock: Stock(20000, 20000, 20000, 1000));
      var spec = UltimateOf(0);
      Assert.AreEqual(UltimateKind.Carrier, spec.Kind);
      var locks = UltimateSystem.Locks(Verified, 0);
      Assert.AreEqual(UltimateLock.Tier | UltimateLock.Site | UltimateLock.Research, locks, "2단계: 단계·전용 건물·연구가 모두 모자람");
      yield return Send(0, new StartUltimateCommand());
      AssertRejected(0, ActionKind.Ultimate, RejectReason.TierLocked);
      // 전용 건물은 3단계 건물이라 지을 수 없다.
      yield return Build(0, "_carrier_yard", V(-18, 14));
      AssertRejected(0, ActionKind.Build, RejectReason.TierLocked);
      // 궁극 전력 연구도 3단계가 필요하다.
      _rejected.Clear();
      yield return Send(0, new ResearchCommand { Building = OwnBuilding(0, "ff_research_lab"), Index = TechSystem.IndexOf(Verified, 0, spec.Research) });
      AssertRejected(0, ActionKind.Research, RejectReason.TierLocked);

      yield return Send(0, new UpgradeBaseCommand { Building = Headquarters(0) });
      yield return WaitFor(() => StateOf(0).BaseTier == 3, 30f, "3단계 도달");
      Assert.AreEqual(UltimateLock.Site | UltimateLock.Research, UltimateSystem.Locks(Verified, 0), "3단계: 전용 건물·연구가 모자람");
      // 선행 건물(비행장)이 없으면 전용 건물을 못 짓는다.
      _rejected.Clear();
      yield return Build(0, "_carrier_yard", V(-18, 14));
      AssertRejected(0, ActionKind.Build, RejectReason.Locked);
      yield return BuildAndFinish(0, "_airfield", V(-10, 30));
      yield return BuildAndFinish(0, "_carrier_yard", V(-18, 14));
      Assert.AreEqual(UltimateLock.Research, UltimateSystem.Locks(Verified, 0), "전용 건물 완공: 연구만 모자람");
      _rejected.Clear();
      yield return Send(0, new StartUltimateCommand());
      AssertRejected(0, ActionKind.Ultimate, RejectReason.Locked);

      yield return ResearchUltimate(0, "ff");
      Assert.AreEqual(UltimateLock.None, UltimateSystem.Locks(Verified, 0), "모두 충족");
      var before = StateOf(0).Stock;
      yield return Send(0, new StartUltimateCommand());
      Assert.AreEqual(UltimatePhase.Preparing, PhaseOf(0), "준비 시작");
      // 가공소가 그사이 철 몇 개를 강철로 바꾼다.
      Assert.AreEqual((before.Iron - spec.Cost.Iron).AsFloat, StateOf(0).Stock.Iron.AsFloat, 6f, "비용(철) 지불");
      Assert.AreEqual((before.Steel - spec.Cost.Steel).AsFloat, StateOf(0).Stock.Steel.AsFloat, 3f, "비용(강철) 지불");
      // 준비 중에는 다시 시작할 수 없다(보유 상한 1).
      _rejected.Clear();
      yield return Send(0, new StartUltimateCommand());
      AssertRejected(0, ActionKind.Ultimate, RejectReason.Busy);
      // 준비 중 전용 건물이 전기를 쓴다.
      var site = Verified.Get<Building>(OwnBuilding(0, "ff_carrier_yard"));
      Assert.AreEqual(UpkeepMode.WhileUltimate, Verified.FindAsset(site.Spec).Upkeep);
      Assert.Less(StateOf(0).PowerRate.AsFloat, 15f - 3.9f, "준비 중 전기 유지비(발전소 3채 15/초에서 4 이상 빠짐)");
    }

    [UnityTest]
    public IEnumerator Ultimate_RejectsWhenResourcesShort() {
      ShortenUltimates();
      // 비용(철 1600)보다 적게 준다.
      yield return StartMatch(buildings: UltimateBase(0, "ff", "ff_carrier_yard"), stock: Stock(1500, 5000, 5000, 1000));
      yield return ResearchUltimate(0, "ff");
      Assert.AreEqual(UltimateLock.Resources, UltimateSystem.Locks(Verified, 0));
      _rejected.Clear();
      yield return Send(0, new StartUltimateCommand());
      AssertRejected(0, ActionKind.Ultimate, RejectReason.InsufficientResources, MissingResource.Iron);
      Assert.AreEqual(UltimatePhase.None, PhaseOf(0));
    }

    [UnityTest]
    public IEnumerator Ultimate_AllPlayersNotifiedAtStartHalfReady() {
      ShortenUltimates(prepare: 12);
      yield return StartMatch(players: 3, buildings: UltimateBase(1, "cf", "cf_missile_silo"), stock: Stock(20000, 20000, 20000, 1000));
      ListenNotices();
      yield return WaitForHud();
      var hud = Controller().Hud;
      var sitePosition = Verified.Get<Transform3D>(OwnBuilding(1, "cf_missile_silo")).Position;
      yield return ResearchUltimate(1, "cf");
      yield return Send(1, new StartUltimateCommand());
      // 시작 알림: 누가(진영), 어디서(전용 건물 위치), 남은 시간.
      var started = _notices.Single(n => n.Stage == UltimateStage.Started);
      Assert.AreEqual(1, started.Player);
      Assert.AreEqual(UltimateKind.StrategicMissile, started.Kind);
      Assert.AreEqual(sitePosition, started.Position, "전용 건물 위치");
      Assert.AreEqual(12, started.Value, "남은 시간(초)");
      // 준비하지 않은 플레이어(자리 0)의 화면에도 알림·진행 막대·미니맵 표시가 뜬다.
      StringAssert.Contains("전략 미사일 준비 시작", hud.AlertText, "다른 플레이어 화면의 알림");
      StringAssert.Contains("대륙 연방", hud.AlertText, "진영 표시");
      hud.Refresh(Verified);
      Assert.AreEqual(1, hud.UltimateBars, "상단 진행 막대(적 준비)");
      StringAssert.Contains("전략 미사일", hud.UltimateBarText(0));
      Assert.AreEqual(1, hud.MinimapUltimateMarks, "미니맵에 전용 건물 표시");
      Assert.IsFalse(Noticed(1, UltimateStage.Half), "아직 절반 전");

      yield return WaitFor(() => Noticed(1, UltimateStage.Half), 30f, "절반 알림");
      var half = _notices.Single(n => n.Stage == UltimateStage.Half);
      Assert.That(half.Value, Is.InRange(5, 6), "절반 시점 남은 시간");
      Assert.AreEqual(sitePosition, half.Position);
      StringAssert.Contains("준비 절반", hud.AlertText);
      Assert.IsFalse(Noticed(1, UltimateStage.Ready));

      yield return WaitFor(() => Noticed(1, UltimateStage.Ready), 30f, "완료 알림");
      Assert.AreEqual(UltimatePhase.Ready, PhaseOf(1));
      StringAssert.Contains("준비 완료", hud.AlertText);
      Assert.AreEqual(new[] { UltimateStage.Started, UltimateStage.Half, UltimateStage.Ready }, _notices.Select(n => n.Stage).ToArray(), "시작·절반·완료 순서로 한 번씩");
      Assert.Greater(StateOf(1).UltimateStartFrame, 0, "측정용 시작 프레임");
      Assert.Greater(StateOf(1).UltimateReadyFrame, StateOf(1).UltimateStartFrame, "측정용 완료 프레임");
    }

    [UnityTest]
    public IEnumerator Ultimate_DedicatedBuildingDestroyedCancelsAndRefundsHalf() {
      ShortenUltimates(prepare: 60);
      var tanks = Enumerable.Range(0, 8).Select(i => (1, "cf_dragonscale_tank", -24 + 3 * (i % 4), 24 + 3 * (i / 4))).ToArray();
      yield return StartMatch(buildings: UltimateBase(0, "ff", "ff_carrier_yard"), units: tanks, stock: Stock(20000, 20000, 20000, 1000));
      ListenNotices();
      yield return ResearchUltimate(0, "ff");
      var spec = UltimateOf(0);
      var before = StateOf(0).Stock;
      yield return Send(0, new StartUltimateCommand());
      Assert.AreEqual(UltimatePhase.Preparing, PhaseOf(0));
      Assert.AreEqual((before.Iron - spec.Cost.Iron).AsFloat, StateOf(0).Stock.Iron.AsFloat, 0.5f);

      var site = OwnBuilding(0, "ff_carrier_yard");
      yield return Send(1, new AttackTargetCommand { Units = UnitsNamed(1, "cf_dragonscale_tank").ToArray(), Target = site });
      yield return WaitFor(() => Verified.Exists(site) == false, 60f, "전용 건물 파괴");
      yield return WaitFrames(3);
      Assert.AreEqual(UltimatePhase.None, PhaseOf(0), "준비 취소");
      Assert.AreEqual((before.Iron - spec.Cost.Iron / 2).AsFloat, StateOf(0).Stock.Iron.AsFloat, 0.5f, "철 절반 환불");
      Assert.AreEqual((before.Steel - spec.Cost.Steel / 2).AsFloat, StateOf(0).Stock.Steel.AsFloat, 0.5f, "강철 절반 환불");
      Assert.AreEqual(1, StateOf(0).UltimateSitesLost, "전용 건물 파괴 기록");
      Assert.IsTrue(_notices.Any(n => n.Player == 0 && n.Stage == UltimateStage.Cancelled && n.Value == 1), "취소 알림(파괴)");
      Assert.AreEqual(0, CountComponent<Carrier>(0), "항모는 나오지 않음");
    }

    [UnityTest]
    public IEnumerator Ultimate_PlayerCancelRefundsPartAndCooldownAllowsRearm() {
      ShortenUltimates(prepare: 60);
      yield return StartMatch(buildings: UltimateBase(0, "ff", "ff_carrier_yard"), stock: Stock(20000, 20000, 20000, 1000));
      yield return ResearchUltimate(0, "ff");
      var spec = UltimateOf(0);
      var before = StateOf(0).Stock;
      yield return Send(0, new StartUltimateCommand());
      yield return Send(0, new CancelUltimateCommand());
      Assert.AreEqual(UltimatePhase.None, PhaseOf(0));
      Assert.AreEqual((before.Iron - spec.Cost.Iron * (FP._1 - spec.CancelRefundFraction)).AsFloat, StateOf(0).Stock.Iron.AsFloat, 0.5f, "직접 취소는 CancelRefundFraction만큼 환불");
      // 취소 뒤 다시 시작할 수 있다.
      yield return Send(0, new StartUltimateCommand());
      Assert.AreEqual(UltimatePhase.Preparing, PhaseOf(0));
    }

    [UnityTest]
    public IEnumerator Ultimate_CarrierLaunchesRecoversAndRebuildsDrones() {
      ShortenUltimates();
      // 적: 대공차 1 + 전차 2(지상 대장갑 무기는 공중을 못 맞힌다). 항모가 (0,30)까지 다가가야 드론 반경(16m)에 든다. 대공차 사거리(10m)는 항모에 닿지 않는다.
      var enemies = new[] { (1, "cf_thunder_aa", 12, 40), (1, "cf_dragonscale_tank", 8, 40), (1, "cf_dragonscale_tank", 10, 36) };
      yield return StartMatch(buildings: UltimateBase(0, "ff", "ff_carrier_yard"), units: enemies, stock: Stock(20000, 20000, 20000, 1000));
      ListenNotices();
      yield return PrepareUltimate(0, "ff", UltimatePhase.Active);
      var spec = UltimateOf(0);
      var carrier = CarrierOf(0);
      Assert.AreNotEqual(EntityRef.None, carrier, "준비가 끝나면 항모가 조립창 위에 뜬다");
      Assert.AreEqual(1, CountComponent<Carrier>(0), "맵에 1기");
      Assert.IsTrue(Verified.FindAsset(Verified.Get<Unit>(carrier).Spec).IsAir, "공중 유닛");
      Assert.AreEqual(spec.DroneCount, Verified.Get<Carrier>(carrier).Docked, "드론 8기 격납");
      Assert.AreEqual(0, CountComponent<Drone>(0), "적이 없으면 발진하지 않음");
      Assert.IsTrue(Noticed(0, UltimateStage.Ready));
      Assert.AreEqual(1, StateOf(0).UltimateUses);
      // 보유 상한: 항모가 살아 있으면 다시 준비할 수 없다.
      _rejected.Clear();
      yield return Send(0, new StartUltimateCommand());
      AssertRejected(0, ActionKind.Ultimate, RejectReason.Busy);

      var iron = StateOf(0).Stock.Iron;
      yield return Send(0, new MoveUnitsCommand { Units = new[] { carrier }, Target = V(0, 30) });
      yield return WaitFor(() => CountComponent<Drone>(0) > 0, 60f, "적이 반경에 들면 드론 발진");
      // 드론은 플레이어 명령을 받지 않는다.
      var drone = FirstOf<Drone>().Entity;
      yield return Send(0, new MoveUnitsCommand { Units = new[] { drone }, Target = V(-40, -40) });
      Assert.AreNotEqual(V(-40, -40), Verified.Get<Unit>(drone).TargetPosition, "드론은 이동 명령 무시");
      // 교전 중 드론을 잃는다(대공차). 잃은 수를 기록한다.
      var lost = 0;
      var maxFlying = 0;
      yield return WaitFor(() => {
        if (Verified.Exists(carrier) == false) {
          return true;
        }
        var flying = CountComponent<Drone>(0);
        maxFlying = Mathf.Max(maxFlying, flying);
        lost = Mathf.Max(lost, spec.DroneCount - flying - Verified.Get<Carrier>(carrier).Docked);
        return Army(1, true).Count(u => Verified.Get<Transform3D>(u).Position.Z > 30) == 0;
      }, 180f, "적 전멸");
      Assert.IsTrue(Verified.Exists(carrier), "항모 생존");
      Assert.Greater(maxFlying, 1, "여러 기가 떠서 싸움");
      Assert.Greater(lost, 0, "대공차에 드론을 잃음");
      // 적이 없으면 불러들이고, 잃은 드론을 다시 만든다(비용은 재생산 때 낸다).
      yield return WaitFor(() => CountComponent<Drone>(0) == 0 && Verified.Get<Carrier>(carrier).Docked == spec.DroneCount, 60f, "회수와 재생산");
      var spent = (iron - StateOf(0).Stock.Iron).AsFloat;
      Assert.Greater(spent, 0f, "재생산 비용 지불");
      Assert.AreEqual(0f, spent % spec.DroneCost.Iron.AsFloat, 0.01f, "드론 한 기 비용의 배수");
      Assert.GreaterOrEqual(spent, lost * spec.DroneCost.Iron.AsFloat - 0.01f, "잃은 만큼 다시 만듦");
    }

    [UnityTest]
    public IEnumerator Ultimate_CarrierDronesFallWhenCarrierDies() {
      ShortenUltimates();
      // 항모 체력을 낮춰 드론이 떠 있는 동안 격추되게 한다.
      var carrierSpec = Resources.Load<UnitSpec>("FactoryWars/ff_stormnest_carrier");
      var health = carrierSpec.MaxHealth;
      carrierSpec.MaxHealth = 450;
      Restores.Add(() => carrierSpec.MaxHealth = health);
      // 대공차 두 대가 조립창에서 9m(대공 사거리 10m 안). 항모가 뜨면 바로 쏜다. 다중 조준(3기)이라 드론도 같이 맞지만 늦게 뜬 드론은 살아 있다.
      yield return StartMatch(buildings: UltimateBase(0, "ff", "ff_carrier_yard"), units: new[] { (1, "cf_thunder_aa", -20, 23), (1, "cf_thunder_aa", -16, 23) }, stock: Stock(20000, 20000, 20000, 1000));
      ListenNotices();
      var destroyed = new List<string>();
      QuantumEvent.Subscribe<EventUnitDestroyed>(_listener, e => destroyed.Add(e.Game.Frames.Verified.FindAsset(e.Spec).name));
      yield return PrepareUltimate(0, "ff", UltimatePhase.Active);
      var carrier = CarrierOf(0);
      var flyingBefore = 0;
      var maxFlying = 0;
      yield return WaitFor(() => {
        if (Verified.Exists(carrier)) {
          flyingBefore = CountComponent<Drone>(0);
          maxFlying = Mathf.Max(maxFlying, flyingBefore);
          return false;
        }
        return true;
      }, 60f, "항모 격추");
      Assert.Greater(maxFlying, 0, "드론 발진");
      Assert.Greater(flyingBefore, 0, "격추 직전 드론이 떠 있었음");
      yield return WaitFrames(5);
      Assert.AreEqual(0, CountComponent<Drone>(0), "드론도 함께 추락");
      Assert.GreaterOrEqual(destroyed.Count(n => n == "ff_nest_drone"), flyingBefore, "드론 파괴 이벤트");
      Assert.AreEqual(1, StateOf(0).UltimateLost, "격추 기록");
      Assert.IsTrue(Noticed(0, UltimateStage.Lost), "격추 알림");
      Assert.AreEqual(UltimatePhase.Cooldown, PhaseOf(0), "재사용 대기");
      // 대기가 끝나면 다시 준비할 수 있다.
      yield return Rearm(0);
      Assert.AreEqual(UltimatePhase.Preparing, PhaseOf(0), "다시 준비");
    }

    [UnityTest]
    public IEnumerator Ultimate_CarrierTakesExtraDamageFromAntiAir() {
      yield return StartMatch(buildings: UltimateBase(0, "ff", "ff_carrier_yard"), stock: Stock(20000, 20000, 20000, 1000));
      var spec = UltimateOf(0);
      var rules = Verified.FindAsset(Verified.RuntimeConfig.Rules);
      var antiAir = rules.Matchups.First(m => m.Attack == AttackType.AntiAir).VsAir;
      Assert.Greater((antiAir * spec.AntiAirTakenMultiplier).AsFloat, 2f, "대공 무기는 항모에 2배 넘게 들어간다(상성 1.5 x 항모 보정 1.5)");
      // 지상 대전차·대보병 무기는 공중 항모를 맞히지 못한다(대공만 카운터).
      var carrier = Verified.FindAsset(spec.Carrier);
      Assert.IsTrue(carrier.IsAir);
      Assert.AreNotEqual(SpecialAbility.GroundArmorResist, carrier.Ability, "저공 비행이 아니라 지상 대전차 무기에 맞지 않음");
    }

    /// <summary>자리 0이 대륙 연방(진영 교대)이고 미사일이 준비된 상태까지 간다. 표적은 자리 1(자유 연방)의 것.</summary>
    IEnumerator MissileReady((int, string, int, int)[] units, (int, string, int, int)[] extraBuildings = null, int blast = 400) {
      ShortenUltimates();
      var missile = Resources.Load<UltimateSpec>("FactoryWars/ultimate_cf_missile");
      var damage = missile.BlastDamage;
      missile.BlastDamage = blast;
      Restores.Add(() => missile.BlastDamage = damage);
      var buildings = UltimateBase(0, "cf", "cf_missile_silo").Concat(extraBuildings ?? new (int, string, int, int)[0]).ToArray();
      yield return StartMatch(swapFactions: true, buildings: buildings, units: units, stock: Stock(20000, 20000, 20000, 1000));
      ListenNotices();
      yield return PrepareUltimate(0, "cf", UltimatePhase.Ready);
    }

    [UnityTest]
    public IEnumerator Ultimate_MissileCountdownThenRadialDamage() {
      // 표적(자유 연방): 전차가 중심, 7m, 13m, 반경(14m) 밖 16m. 공격헬기(공중)는 중심 근처 위. 내 민병대(대륙)는 12m(적 사거리 밖): 아군도 맞는다.
      var units = new[] {
        (1, "ff_eagle_tank", 20, 36), (1, "ff_eagle_tank", 27, 36), (1, "ff_eagle_tank", 33, 36), (1, "ff_eagle_tank", 36, 36),
        (1, "ff_hawk_gunship", 20, 40), (0, "cf_militia", 20, 24),
      };
      yield return MissileReady(units, new[] { (1, "ff_power_plant", 20, 44) });
      var spec = UltimateOf(0);
      var target = V(20, 36);
      var tanks = UnitsNamed(1, "ff_eagle_tank").Where(u => Verified.Get<Transform3D>(u).Position.Z > 30).OrderBy(u => Verified.Get<Transform3D>(u).Position.X.AsFloat).ToArray();
      var heli = UnitsNamed(1, "ff_hawk_gunship")[0];
      var militia = UnitsNamed(0, "cf_militia").First(u => Verified.Get<Transform3D>(u).Position.Z > 20);
      var plant = BuildingAt(V(20, 44));
      var tankMax = Verified.FindAsset(Verified.Get<Unit>(tanks[0]).Spec).MaxHealth;
      var heliHealth = Health(heli);
      var lostBefore = StateOf(0).UnitsLost;

      // 발사 전에는 목표 지정이 안 된 발사 명령(맵 밖)이 거부된다.
      yield return Send(0, new LaunchUltimateCommand { Target = V(500, 500) });
      AssertRejected(0, ActionKind.Ultimate, RejectReason.BlockedLocation);
      Assert.AreEqual(UltimatePhase.Ready, PhaseOf(0));

      yield return Send(0, new LaunchUltimateCommand { Target = target });
      // 발사 즉시: 모든 플레이어에게 착탄 지점·카운트다운 알림, 날아오는 미사일 상태.
      var launched = _notices.Single(n => n.Stage == UltimateStage.Launched);
      Assert.AreEqual(target, launched.Position, "착탄 지점");
      Assert.AreEqual(4, launched.Value, "카운트다운(초)");
      Assert.AreEqual(1, Verified.ComponentCount<MissileStrike>());
      var strike = FirstOf<MissileStrike>().Component;
      Assert.AreEqual(strike.LaunchFrame + 4 * Fps, strike.ImpactFrame, "경고 시간 뒤 착탄");
      Assert.AreEqual(UltimatePhase.Cooldown, PhaseOf(0), "발사하면 재사용 대기");
      Assert.Greater(StateOf(0).UltimateUseFrame, 0);
      // 카운트다운 동안은 피해가 없다.
      Assert.AreEqual(tankMax.AsFloat, Health(tanks[0]), 0.01f, "착탄 전 피해 없음");
      Assert.IsFalse(Noticed(0, UltimateStage.Impact));
      var positions = tanks.Select(t => Verified.Get<Transform3D>(t).Position).ToArray();
      var plantHealth = Verified.Get<Building>(plant).Health;

      yield return WaitFor(() => Noticed(0, UltimateStage.Impact), 30f, "착탄");
      Assert.AreEqual(0, Verified.ComponentCount<MissileStrike>(), "착탄 뒤 미사일 상태 삭제");
      // 중심 최대, 바깥으로 직선 감소(가장자리 35%), 반경 밖은 0.
      for (var i = 0; i < 3; i++) {
        var expected = tankMax - spec.BlastDamage * Falloff(positions[i], target, spec);
        Assert.AreEqual(expected.AsFloat, Health(tanks[i]), 1f, $"전차 {i} 피해({FPMath.Sqrt(FactoryWarsUtil.FlatDistanceSqr(positions[i], target)).AsFloat:0.0}m)");
      }
      Assert.Less(Health(tanks[0]), Health(tanks[1]), "중심이 가장 크게");
      Assert.Less(Health(tanks[1]), Health(tanks[2]), "바깥으로 갈수록 줄어듦");
      Assert.AreEqual(tankMax.AsFloat, Health(tanks[3]), 0.01f, "반경 밖은 피해 없음");
      Assert.AreEqual(heliHealth, Health(heli), 0.01f, "공중 유닛은 맞지 않음");
      // 건물에는 추가 피해.
      var plantExpected = plantHealth - spec.BlastDamage * Falloff(V(20, 44), target, spec) * spec.BlastBuildingMultiplier;
      Assert.AreEqual(plantExpected.AsFloat, Verified.Get<Building>(plant).Health.AsFloat, 1f, "건물 추가 피해");
      // 아군(자기 유닛)도 맞는다.
      Assert.IsFalse(Verified.Exists(militia), "자기 민병대도 폭발에 죽음(아군 피해)");
      Assert.Greater(StateOf(0).UnitsLost, lostBefore);

      // 재사용 대기 뒤 다시 준비(재준비 비용·시간).
      yield return WaitFor(() => UltimateSystem.Locks(Verified, 0) == UltimateLock.None, 60f, "재준비 가능");
      var before = StateOf(0).Stock;
      yield return Send(0, new StartUltimateCommand());
      Assert.AreEqual(UltimatePhase.Preparing, PhaseOf(0));
      Assert.AreEqual((before.Iron - spec.RearmCost.Iron).AsFloat, StateOf(0).Stock.Iron.AsFloat, 0.5f, "재준비 비용");
      Assert.Less(spec.RearmCost.Iron.AsFloat, spec.Cost.Iron.AsFloat, "재준비는 처음보다 쌈");
    }

    [UnityTest]
    public IEnumerator Ultimate_MissileIgnoresCoverAndCollapsesCity() {
      // 엄폐 중인 레인저(자리 0 자유 연방)를 자리 1(대륙 연방)의 미사일이 때린다. 엄폐 감소(모래주머니 25%, 광역 절반)가 붙지 않아야 한다.
      ShortenUltimates();
      var missile = Resources.Load<UltimateSpec>("FactoryWars/ultimate_cf_missile");
      var damage = missile.BlastDamage;
      missile.BlastDamage = 60;
      Restores.Add(() => missile.BlastDamage = damage);
      var buildings = SandbagSite.Concat(UltimateBase(1, "cf", "cf_missile_silo")).ToArray();
      // 레인저가 북쪽의 적 보급창(오래 버티는 표적)을 쏘며 모래주머니 북쪽 엄폐점에 들어간다.
      buildings = buildings.Concat(new[] { (1, "cf_supply_depot", 0, 42) }).ToArray();
      yield return StartMatch(buildings: buildings, units: new[] { (0, "ff_ranger_squad", 0, 24) }, stock: Stock(20000, 20000, 20000, 1000));
      ListenNotices();
      var ranger = UnitsNamed(0, "ff_ranger_squad").OrderBy(u => (Verified.Get<Transform3D>(u).Position - V(0, 24)).SqrMagnitude).First();
      yield return PrepareUltimate(1, "cf", UltimatePhase.Ready);
      yield return Send(0, new AttackTargetCommand { Units = new[] { ranger }, Target = OwnBuilding(1, "cf_supply_depot") });
      yield return WaitFor(() => InCover(ranger), 30f, "엄폐 도착");
      // 북쪽(엄폐가 막는 쪽)에 떨어뜨린다. 일반 광역 무기라면 12.5% 줄어든다.
      var target = V(0, 38);
      Assert.Less(CoverMultiplier(ranger, target, true).AsFloat, 0.9f, "이 방향의 광역 공격은 엄폐로 줄어드는 자리");
      var before = Health(ranger);
      var position = Verified.Get<Transform3D>(ranger).Position;
      yield return Send(1, new LaunchUltimateCommand { Target = target });
      yield return WaitFor(() => Noticed(1, UltimateStage.Impact), 30f, "착탄");
      Assert.IsTrue(InCover(ranger), "착탄 때도 엄폐 중");
      var expected = missile.BlastDamage * Falloff(position, target, missile);
      Assert.AreEqual(before - expected.AsFloat, Health(ranger), 0.6f, "엄폐 효과 무시(줄지 않은 피해)");

      // 도시 건물은 배율(2배)로 맞아 무너진다: 시험 맵 진지 건물(-10,-24), 체력 1500.
      missile.BlastDamage = 1000;
      yield return Rearm(1, UltimatePhase.Ready);
      var collapsed = false;
      QuantumEvent.Subscribe<EventCityBuildingCollapsed>(_listener, e => collapsed = true);
      yield return Send(1, new LaunchUltimateCommand { Target = V(-10, -24) });
      yield return WaitFor(() => collapsed, 30f, "도시 건물 붕괴");
    }

    [UnityTest]
    public IEnumerator Ultimate_MissileInterceptionReducesDamageDeterministically() {
      // 요격 반경을 6m로 줄여 세 무리를 따로 잰다: A(방공 없음), B(방공포대 1), C(방공포대 2 + 레이더).
      var missile = Resources.Load<UltimateSpec>("FactoryWars/ultimate_cf_missile");
      var radius = missile.InterceptRadius;
      var blastRadius = missile.BlastRadius;
      missile.InterceptRadius = 9;
      missile.BlastRadius = 5;
      Restores.Add(() => {
        missile.InterceptRadius = radius;
        missile.BlastRadius = blastRadius;
      });
      var a = V(-10, 40);
      var b = V(14, 40);
      var c = V(38, 40);
      var units = new[] { (1, "ff_eagle_tank", -10, 40), (1, "ff_eagle_tank", 14, 40), (1, "ff_eagle_tank", 38, 40) };
      var defenses = new[] {
        (1, "ff_air_defense", 14, 47),
        (1, "ff_air_defense", 38, 47), (1, "ff_air_defense", 45, 40), (1, "ff_radar", 38, 33),
        // 발사자 자신의 방공포대는 요격하지 않는다.
        (0, "cf_air_defense", -10, 47),
        // 방어 쪽 발전소(정전이면 요격하지 못한다).
        (1, "ff_power_plant", 46, 10), (1, "ff_power_plant", 46, 18),
      };
      yield return MissileReady(units, defenses);
      var spec = UltimateOf(0);
      Assert.AreEqual(0, UltimateSystem.InterceptScore(Verified, 0, a, spec), "A: 자기 방공포대는 세지 않음");
      Assert.AreEqual(2, UltimateSystem.InterceptScore(Verified, 0, b, spec), "B: 방공포대 1 = 2점");
      Assert.AreEqual(6, UltimateSystem.InterceptScore(Verified, 0, c, spec), "C: 방공포대 2 x 2 + 레이더 2 = 6점");
      Assert.AreEqual(1f, UltimateSystem.InterceptMultiplier(spec, 0, out var step0).AsFloat, 0.001f);
      Assert.AreEqual(0, step0);
      Assert.AreEqual(0.7f, UltimateSystem.InterceptMultiplier(spec, 2, out var step1).AsFloat, 0.001f, "2점 70%");
      Assert.AreEqual(1, step1);
      Assert.AreEqual(0.7f, UltimateSystem.InterceptMultiplier(spec, 3, out _).AsFloat, 0.001f, "3점도 1단계");
      Assert.AreEqual(0.45f, UltimateSystem.InterceptMultiplier(spec, 4, out _).AsFloat, 0.001f, "4점 45%");
      Assert.AreEqual(0.25f, UltimateSystem.InterceptMultiplier(spec, 6, out var step3).AsFloat, 0.001f, "6점 25%");
      Assert.AreEqual(3, step3);
      Assert.AreEqual(0.25f, UltimateSystem.InterceptMultiplier(spec, 99, out _).AsFloat, 0.001f, "그 이상은 가장 높은 단계");

      var tanks = UnitsNamed(1, "ff_eagle_tank").Where(u => Verified.Get<Transform3D>(u).Position.Z > 30).OrderBy(u => Verified.Get<Transform3D>(u).Position.X.AsFloat).ToArray();
      var max = Verified.FindAsset(Verified.Get<Unit>(tanks[0]).Spec).MaxHealth.AsFloat;
      var points = new[] { c, b, a };
      var order = new[] { 2, 1, 0 };
      var multipliers = new[] { 0.25f, 0.7f, 1f };
      var steps = new[] { 3, 1, 0 };
      for (var shot = 0; shot < 3; shot++) {
        if (shot > 0) {
          yield return Rearm(0, UltimatePhase.Ready);
        }
        _notices.Clear();
        var tank = tanks[order[shot]];
        var position = Verified.Get<Transform3D>(tank).Position;
        yield return Send(0, new LaunchUltimateCommand { Target = points[shot] });
        yield return WaitFor(() => Noticed(0, UltimateStage.Impact), 30f, "착탄");
        var expected = max - spec.BlastDamage.AsFloat * multipliers[shot] * Falloff(position, points[shot], spec).AsFloat;
        Assert.AreEqual(expected, Health(tank), 1f, $"무리 {"CBA"[shot]}: 피해 배율 {multipliers[shot]}");
        Assert.AreEqual(steps[shot], _notices.Single(n => n.Stage == UltimateStage.Impact).Value, "착탄 알림의 요격 단계");
      }
      Assert.AreEqual(2, StateOf(0).UltimateIntercepted, "요격당한 횟수(C·B)");
      Assert.AreEqual(3, StateOf(0).UltimateUses);
    }

    [UnityTest]
    public IEnumerator Bunker_HoldsFourSquadsFiresFromInsideAndEjectsOnDestroy() {
      // 벙커(0,30)에 레인저 5분대를 보낸다. 넷만 들어간다. 북쪽 민병대와 전차가 벙커를 친다.
      var units = Enumerable.Range(0, 5).Select(i => (0, "ff_ranger_squad", -6 + 3 * i, 24))
        .Concat(Enumerable.Range(0, 6).Select(i => (1, "cf_dragonscale_tank", -8 + 3 * i, 46)))
        .Concat(new[] { (1, "cf_militia", 0, 50) }).ToArray();
      yield return StartMatch(buildings: new[] { (0, "ff_bunker", 0, 30) }, units: units);
      var bunker = OwnBuilding(0, "ff_bunker");
      var spec = Verified.FindAsset(Verified.Get<Building>(bunker).Spec);
      Assert.AreEqual(4, spec.GarrisonCapacity, "수용 4분대");
      Assert.AreEqual(0, spec.CoverSpots, "둘레 엄폐점 대신 실제 수용");
      var rangers = UnitsNamed(0, "ff_ranger_squad").Where(u => Verified.Get<Transform3D>(u).Position.Z > 20).ToArray();
      Assert.AreEqual(5, rangers.Length);
      // 전차는 벙커에 못 들어간다(보병만).
      yield return Send(0, new EnterGarrisonCommand { Units = UnitsNamed(0, "ff_eagle_tank").ToArray(), Building = bunker });
      yield return Send(0, new EnterGarrisonCommand { Units = rangers, Building = bunker });
      yield return WaitFor(() => CitySystem.Occupants(Verified, bunker) == 4, 30f, "네 분대 입장");
      yield return WaitFrames(2 * Fps);
      Assert.AreEqual(4, CitySystem.Occupants(Verified, bunker), "다섯째는 못 들어감");
      Assert.IsFalse(CitySystem.CanEnter(Verified, 0, bunker), "꽉 참");
      var inside = rangers.Where(u => Verified.Get<Unit>(u).Garrison == bunker).ToArray();
      var outside = rangers.Single(u => Verified.Get<Unit>(u).Garrison != bunker);
      Assert.IsFalse(Verified.Has<NavMeshPathfinder>(inside[0]), "안에서는 걷지 않음");
      var healthInside = inside.Sum(Health);

      // 적이 벙커를 친다. 안의 병력은 맞지 않고 벙커 체력만 깎인다. 안에서 사격한다.
      yield return Send(0, new MoveUnitsCommand { Units = new[] { outside }, Target = V(-40, 0) });
      var enemies = Army(1, true).Where(u => Verified.Get<Transform3D>(u).Position.Z > 40).ToArray();
      var enemyHealth = enemies.Sum(Health);
      yield return Send(1, new AttackTargetCommand { Units = enemies, Target = bunker });
      yield return WaitFor(() => Verified.Exists(bunker) == false || Verified.Get<Building>(bunker).Health < spec.MaxHealth * FP._0_50, 60f, "벙커 피해");
      Assert.AreEqual(healthInside, inside.Sum(Health), 0.01f, "안의 병력은 피해 없음(벙커가 대신 맞음)");
      Assert.IsTrue(inside.Any(u => Verified.Get<Unit>(u).LastFiredFrame > 0), "안에서 사격");
      Assert.Less(enemies.Sum(Health), enemyHealth, "밖의 적이 피해를 입음");

      yield return WaitFor(() => Verified.Exists(bunker) == false, 120f, "벙커 파괴");
      yield return WaitFrames(2);
      var rules = Verified.FindAsset(Verified.RuntimeConfig.Rules);
      var alive = inside.Where(u => Verified.Exists(u)).ToArray();
      Assert.Greater(alive.Length, 0, "튕겨 나온 병력");
      foreach (var unit in alive) {
        Assert.AreEqual(EntityRef.None, Verified.Get<Unit>(unit).Garrison, "밖으로 나옴");
        Assert.IsTrue(Verified.Has<NavMeshPathfinder>(unit), "다시 걸을 수 있음");
        var max = TechSystem.MaxHealth(Verified, 0, Verified.Get<Unit>(unit).Spec);
        Assert.LessOrEqual(Health(unit), (max * (FP._1 - rules.GarrisonEjectFraction)).AsFloat + 0.01f, "튕겨 나오며 피해(최대 체력의 25%)");
      }
    }

    [UnityTest]
    public IEnumerator Bunker_ExitCommandAndAiFillsOwnBunker() {
      yield return StartMatch(buildings: new[] { (0, "ff_bunker", 0, 30) }, units: new[] { (0, "ff_ranger_squad", 0, 24), (0, "ff_ranger_squad", 3, 24) });
      var bunker = OwnBuilding(0, "ff_bunker");
      var rangers = UnitsNamed(0, "ff_ranger_squad").Where(u => Verified.Get<Transform3D>(u).Position.Z > 20).ToArray();
      yield return Send(0, new EnterGarrisonCommand { Units = rangers, Building = bunker });
      yield return WaitFor(() => CitySystem.Occupants(Verified, bunker) == 2, 30f, "입장");
      var health = rangers.Sum(Health);
      yield return Send(0, new ExitGarrisonCommand { Building = bunker });
      Assert.AreEqual(0, CitySystem.Occupants(Verified, bunker), "모두 나옴");
      Assert.IsTrue(CitySystem.CanEnter(Verified, 0, bunker), "주인은 들어갈 수 있음");
      Assert.IsFalse(CitySystem.CanEnter(Verified, 1, bunker), "적은 못 들어감");
      Assert.AreEqual(health, rangers.Sum(Health), 0.01f, "스스로 나오면 피해 없음");
      TearDown();
      yield return null;

      // AI는 자기 벙커에 보병 분대를 BunkerSquads(2)까지 넣는다.
      yield return StartMatch(players: 2, bots: new[] { BotDifficulty.Normal }, buildings: new[] { (1, "cf_bunker", 30, 14) },
        units: new[] { (1, "cf_militia", 30, 22), (1, "cf_militia", 33, 22), (1, "cf_flame_squad", 27, 22) });
      var ai = Verified.FindAsset(Verified.FindAsset(Verified.RuntimeConfig.Rules).NormalAi);
      Assert.AreEqual(2, ai.BunkerSquads);
      var own = OwnBuilding(1, "cf_bunker");
      yield return WaitFor(() => CitySystem.Occupants(Verified, own) >= 2, 60f, "AI가 벙커를 채움");
      Assert.AreEqual(2, CitySystem.Occupants(Verified, own), "BunkerSquads까지만");
    }
  }
}
