namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using FactoryWars.View;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 33 궁극 전력 2: 상륙 강습(시험 전용 진영, 4인 맵의 수로), 결정론(같은 시드 2회 + 실제 온라인 2클라이언트), AI 사용·대응.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    /// <summary>자리 0(시험 전용 진영)이 운하 옆 강습 도크에서 상륙 강습을 준비한 상태까지 간다. goal은 맞은편(자리 2) 운하.</summary>
    IEnumerator AssaultReady(FPVector3[] goal) {
      ShortenUltimates();
      yield return StartMatch(Map4PScene, 2, truce: 0, seed: 0, factionNames: new[] { "faction_test_assault", null }, stock: Stock(20000, 20000, 20000, 200), startingTier: 3);
      ListenNotices();
      var f = Verified;
      Assert.AreEqual("test_assault", FactionOf(0).FactionId, "시험 전용 진영");
      Assert.AreEqual(UltimateKind.AmphibiousAssault, UltimateOf(0).Kind);
      var map = f.FindAsset<MapSpec>(f.Map.UserAsset);
      var start = map.StartPositions[FactoryWarsUtil.SeatOf(f, 0)];
      Assert.Less(start.X.AsFloat + start.Z.AsFloat, 0f, "자리 0은 남서");
      var canal = CanalPoint(map);
      goal[0] = new FPVector3(-canal.X, FP._0, -canal.Z);
      // 강습 도크는 조선소처럼 물가에만 지을 수 있다.
      var dockRef = BuildingOf(0, "_assault_dock");
      var dock = f.FindAsset(dockRef);
      Assert.IsTrue(dock.NeedsWater);
      Assert.IsFalse(ProductionSystem.CanPlace(f, dock, start + new FPVector3(10, 0, 10)), "물에서 먼 땅에는 못 지음");
      var site = FPVector3.Zero;
      var found = false;
      for (var dx = -20; dx <= 20 && found == false; dx += 2) {
        for (var dz = -20; dz <= 20 && found == false; dz += 2) {
          var candidate = canal + new FPVector3(dx, 0, dz);
          if (ProductionSystem.CanPlace(f, dock, candidate)) {
            site = candidate;
            found = true;
          }
        }
      }
      Assert.IsTrue(found, "운하 변에 강습 도크 자리");
      ProductionSystem.CreateFinishedBuilding(f, 0, dockRef, site);
      for (var i = 0; i < 3; i++) {
        ProductionSystem.CreateFinishedBuilding(f, 0, BuildingOf(0, "_power_plant"), start + new FPVector3(-16 + 8 * i, 0, 16));
      }
      ProductionSystem.CreateFinishedBuilding(f, 0, BuildingOf(0, "_research_lab"), start + new FPVector3(16, 0, -8));
      yield return WaitFrames(5);
      yield return WaitFor(() => StateOf(0).Stock.Power >= 300, 60f, "연구 전기");
      yield return ResearchUltimate(0, "ff");
      yield return WaitFor(() => UltimateSystem.Locks(Verified, 0) == UltimateLock.None, 120f, "준비 자원(전기)");
      yield return Send(0, new StartUltimateCommand());
      yield return WaitFor(() => PhaseOf(0) == UltimatePhase.Ready, 60f, "상륙 강습 준비 완료");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Ultimate_AmphibiousLandsCargoOnShore() {
      var goal = new FPVector3[1];
      yield return AssaultReady(goal);
      var spec = UltimateOf(0);
      var rules = FactoryWarsUtil.Rules(Verified);
      Assert.IsTrue(UltimateSystem.FindLanding(Verified, goal[0], spec, out var water, out var shore), "상륙 자리");
      var waterMesh = Verified.Map.GetNavMesh(rules.WaterNavMeshName);
      var groundMesh = Verified.Map.GetNavMesh(rules.NavMeshName);
      var all = NavMeshRegionMask.Default;
      Assert.IsTrue(waterMesh.Contains(water, all, true), "접안 지점은 물 위");
      Assert.IsTrue(groundMesh.Contains(shore, all, true), "하선 지점은 땅 위");
      // 물에서 먼 내륙은 목표로 잡을 수 없다.
      var enemyStart = FactoryWarsUtil.StartPosition(Verified, 1);
      yield return Send(0, new LaunchUltimateCommand { Target = enemyStart });
      AssertRejected(0, ActionKind.Ultimate, RejectReason.BlockedLocation);
      Assert.AreEqual(UltimatePhase.Ready, PhaseOf(0));

      var tanks = UnitsNamed(0, "ff_eagle_tank").Count;
      var squads = UnitsNamed(0, "ff_ranger_squad").Count;
      Time.timeScale = 20f;
      yield return Send(0, new LaunchUltimateCommand { Target = goal[0] });
      Assert.AreEqual(UltimatePhase.Active, PhaseOf(0), "출발");
      Assert.AreEqual(spec.CraftCount, CountComponent<LandingCraft>(0), "상륙정 2척");
      Assert.IsTrue(Noticed(0, UltimateStage.Launched), "출발 알림");
      Assert.AreEqual(tanks, UnitsNamed(0, "ff_eagle_tank").Count, "탑재 부대는 내리기 전까지 맵에 없음");
      var craft = FirstOf<LandingCraft>().Entity;
      Assert.IsTrue(waterMesh.Contains(Verified.Get<Transform3D>(craft).Position, all, true), "상륙정은 물 위에서 출발");
      Assert.AreEqual(WaterPathOk(Verified.Get<Transform3D>(craft).Position, water), true, "수로로 갈 수 있는 목표");
      // 상륙정은 플레이어 명령을 받지 않는다.
      yield return Send(0, new MoveUnitsCommand { Units = new[] { craft }, Target = Verified.Get<Transform3D>(craft).Position });
      Assert.AreEqual(UnitOrder.Move, Verified.Get<Unit>(craft).Order, "이동 명령 무시(계속 접안 지점으로)");

      yield return WaitFor(() => _notices.Count(n => n.Stage == UltimateStage.Landed) == spec.CraftCount, 240f, "두 척 모두 하선");
      Assert.AreEqual(tanks + 4, UnitsNamed(0, "ff_eagle_tank").Count, "중형 기갑 4");
      Assert.AreEqual(squads + 4, UnitsNamed(0, "ff_ranger_squad").Count, "소형 보병 4");
      var landed = new List<EntityRef>();
      foreach (var pair in Verified.GetComponentIterator<Unit>()) {
        if (pair.Component.Owner == 0 && pair.Component.AssaultUntilFrame > 0) {
          landed.Add(pair.Entity);
        }
      }
      Assert.AreEqual(8, landed.Count, "상륙 부대 8기");
      foreach (var unit in landed) {
        var position = Verified.Get<Transform3D>(unit).Position;
        Assert.Less(FPMath.Sqrt(FactoryWarsUtil.FlatDistanceSqr(position, shore)).AsFloat, 30f, "해안 근처에 내림");
        // 내린 뒤 목표(운하 쪽)로 공격 이동하며 물가에서 서로 밀릴 수 있어, 땅(NavMesh)에서 4m 안인지만 본다.
        Assert.IsTrue(FactoryWarsUtil.FindNavPointNear(Verified, rules.NavMeshName, position, 4, out _), "땅 위(물가)");
      }
      // 상륙 후 30초 공격력 보너스.
      var boosted = landed.First(u => Verified.Exists(u));
      Assert.AreEqual(spec.LandedDamageMultiplier.AsFloat, AssaultBonus(boosted).AsFloat, 0.001f, "상륙 직후 보너스");
      Assert.AreEqual(1.25f, Outgoing(boosted).AsFloat, 0.001f, "피해 배율에 반영");
      var until = Verified.Get<Unit>(boosted).AssaultUntilFrame;
      Assert.That(until - Verified.Number, Is.InRange(1, spec.LandedBonusSeconds.AsInt * Fps), "30초 안에 끝남");
      // 하선 뒤 상륙정은 해안에서 포격 지원(위치 고수)하다가 귀환한다.
      Assert.IsTrue(Verified.Exists(craft) == false || Verified.Get<LandingCraft>(craft).Stage >= 1, "지원 단계");
      yield return WaitFor(() => Verified.Number > until, 60f, "보너스 종료");
      if (Verified.Exists(boosted)) {
        Assert.AreEqual(1f, Outgoing(boosted).AsFloat, 0.001f, "보너스가 끝나면 평소 피해");
      }
      yield return WaitFor(() => CountComponent<LandingCraft>(0) == 0, 240f, "상륙정 귀환");
      yield return WaitFrames(3);
      Assert.AreNotEqual(UltimatePhase.Active, PhaseOf(0), "귀환하면 재사용 대기");
      Assert.AreEqual(0, StateOf(0).UltimateLost, "격침 없음");
    }

    static FP AssaultBonus(EntityRef entity) => UltimateSystem.AssaultMultiplier(Verified, Verified.Unsafe.GetPointer<Unit>(entity));

    static BotState BotOf(int player) => *Verified.Global->Bots.GetPointer(player);

    static PlayerState StateIn(Frame frame, PlayerRef player) => *frame.Global->Players.GetPointer(player);

    bool WaterPathOk(FPVector3 from, FPVector3 to) {
      var result = WaterPath(from, to);
      return result == Navigation.FindPathResult.Code.Success;
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Ultimate_AmphibiousSunkLosesCargo() {
      // 상륙정 체력을 낮춰 정찰선의 첫 사격에 가라앉게 한다.
      var lander = Resources.Load<UnitSpec>("FactoryWars/tf_wavecrest_lander");
      var health = lander.MaxHealth;
      lander.MaxHealth = 10;
      Restores.Add(() => lander.MaxHealth = health);
      var goal = new FPVector3[1];
      yield return AssaultReady(goal);
      var spec = UltimateOf(0);
      var tanks = UnitsNamed(0, "ff_eagle_tank").Count;
      var squads = UnitsNamed(0, "ff_ranger_squad").Count;
      yield return Send(0, new LaunchUltimateCommand { Target = goal[0] });
      Assert.AreEqual(spec.CraftCount, CountComponent<LandingCraft>(0));
      // 적 정찰선이 이동 중인 상륙정 옆에 나타난다(카운터: 정찰선·포격함·해안 방어탑).
      var boat = FactionOf(1).Units.First(u => Verified.FindAsset(u).name.EndsWith("_scout_boat"));
      foreach (var pair in Verified.GetComponentIterator<LandingCraft>()) {
        var position = Verified.Get<Transform3D>(pair.Entity).Position;
        SpawnUnitAtWater(Verified, 1, boat, position);
        SpawnUnitAtWater(Verified, 1, boat, position);
      }
      yield return WaitFor(() => CountComponent<LandingCraft>(0) == 0, 60f, "상륙정 격침");
      yield return WaitFrames(2 * Fps);
      Assert.AreEqual(spec.CraftCount, StateOf(0).UltimateLost, "격침 기록(척마다)");
      Assert.AreEqual(spec.CraftCount, _notices.Count(n => n.Stage == UltimateStage.Lost), "격침 알림");
      Assert.IsFalse(Noticed(0, UltimateStage.Landed), "하선하지 못함");
      Assert.AreEqual(tanks, UnitsNamed(0, "ff_eagle_tank").Count, "탑재 기갑도 함께 손실");
      Assert.AreEqual(squads, UnitsNamed(0, "ff_ranger_squad").Count, "탑재 보병도 함께 손실");
      Assert.AreNotEqual(UltimatePhase.Active, PhaseOf(0), "강습 종료");
    }

    /// <summary>AI 둘이 스스로 궁극 전력을 쓰게 하는 시험 맵 장면. 자리 0 자유 연방(항모), 자리 1 대륙 연방(미사일).</summary>
    static readonly (int, string, int, int)[] DuelBases = UltimateBase(0, "ff", "ff_carrier_yard").Concat(UltimateBase(1, "cf", "cf_missile_silo")).Concat(new[] {
      (0, "ff_barracks", -40, 12), (1, "cf_barracks", 40, 12), (1, "cf_vehicle_factory", 40, -40),
      // 인구 여유(대공차를 바로 뽑을 수 있게).
      (1, "cf_supply_depot", 26, 40), (1, "cf_supply_depot", 34, 40),
    }).ToArray();

    static readonly (int, string, int, int)[] DuelUnits = Enumerable.Range(0, 4).Select(i => (0, "ff_eagle_tank", -24 + 3 * i, -8))
      .Concat(Enumerable.Range(0, 4).Select(i => (1, "cf_dragonscale_tank", 24 - 3 * i, -8))).ToArray();

    /// <summary>AI가 시작부터 궁극 전력을 준비하고 일찍 공격하게 한다(메모리에서만 바꿈).</summary>
    static void EagerAi(int firstAttack = 5) {
      var ai = Resources.Load<AiSpec>("FactoryWars/ai_normal");
      var before = (ai.UltimateStartSeconds, ai.FirstAttackSeconds);
      ai.UltimateStartSeconds = 0;
      ai.FirstAttackSeconds = firstAttack;
      Restores.Add(() => {
        ai.UltimateStartSeconds = before.Item1;
        ai.FirstAttackSeconds = before.Item2;
      });
    }

    static ScenarioBuilding Scenario(int player, string building, int x, int z) {
      return new ScenarioBuilding { Player = player, Building = new AssetRef<BuildingSpec>(Resources.Load<BuildingSpec>("FactoryWars/" + building).Guid), Position = new FPVector3(x, 0, z) };
    }

    /// <summary>
    /// 같은 시드로 AI 대 AI(항모 대 미사일)를 두 번 돌려 90초 시점 체크섬이 같은지 보고,
    /// 실제 Photon 서버의 온라인 세션(클라이언트 둘)에서 준비·항모·드론·미사일을 쓰는 동안 두 클라이언트의 확정 프레임 체크섬이 같은지 본다.
    /// </summary>
    [UnityTest, Timeout(900000)]
    public IEnumerator Ultimate_DeterministicAcrossRunsAndOnline() {
      var checksums = new ulong[2];
      for (var run = 0; run < 2; run++) {
        ShortenUltimates();
        EagerAi();
        yield return StartMatch(players: 2, bots: new[] { BotDifficulty.Normal, BotDifficulty.Normal }, seed: 3303, buildings: DuelBases, units: DuelUnits, stock: Stock(20000, 20000, 20000, 1000));
        var stop = 90 * Fps;
        var sum = 0UL;
        QuantumCallback.Subscribe<CallbackSimulateFinished>(_listener, e => {
          if (e.Frame.Number == stop && sum == 0UL) {
            sum = e.Frame.CalculateChecksum();
          }
        });
        yield return WaitFor(() => sum != 0UL, 120f, "90초 시점 체크섬");
        checksums[run] = sum;
        Debug.Log($"[FW-TEST] ultimate run {run}: checksum {sum} uses {StateOf(0).UltimateUses}/{StateOf(1).UltimateUses} drones {Verified.ComponentCount<Drone>()}");
        Assert.Greater(StateOf(0).UltimateUses, 0, "이 장면에서 항모를 쓴다");
        Assert.Greater(StateOf(1).UltimateUses, 0, "이 장면에서 미사일을 쓴다");
        TearDown();
        yield return null;
      }
      Assert.AreEqual(checksums[0], checksums[1], "같은 시드면 같은 결과");

      // ---------- 온라인(실제 Photon 서버, 클라이언트 둘) ----------
      Time.timeScale = 1f;
      LogAssert.ignoreFailingMessages = true;
      ShortenUltimates();
#if UNITY_EDITOR
      yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(TestScene, new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
      var local = Object.FindAnyObjectByType<QuantumRunnerLocalDebug>();
      local.enabled = false;
      QuantumRunner.ShutdownAll();
      var factions = local.LocalPlayers.Take(2).Select(p => p.Faction).ToArray();
      var template = local.RuntimeConfig;
      template.Map = Object.FindAnyObjectByType<QuantumMapData>().AssetRef;
      // 서버가 플레이어 번호를 정하므로 두 자리 모두에 두 진영의 전용 건물을 둔다(자기 진영 것만 쓰인다). 조립창 옆에는 상대 전차 한 대(드론 표적).
      var buildings = new List<ScenarioBuilding>();
      for (var p = 0; p < 2; p++) {
        var x = p == 0 ? -1 : 1;
        buildings.AddRange(new[] {
          Scenario(p, "ff_power_plant", 46 * x, -10), Scenario(p, "ff_power_plant", 46 * x, -18), Scenario(p, "ff_power_plant", 46 * x, -26),
          Scenario(p, "ff_research_lab", 38 * x, -24), Scenario(p, "ff_carrier_yard", 18 * x, 14), Scenario(p, "cf_missile_silo", 18 * x, 28),
        });
      }
      template.ScenarioBuildings = buildings.ToArray();
      var tank = new AssetRef<UnitSpec>(Resources.Load<UnitSpec>("FactoryWars/ff_eagle_tank").Guid);
      template.ScenarioUnits = new[] {
        new ScenarioUnit { Player = 1, Unit = tank, Position = new FPVector3(-18, 0, 4) },
        new ScenarioUnit { Player = 0, Unit = tank, Position = new FPVector3(18, 0, 4) },
      };
      template.OverrideStartingResources = true;
      template.StartingResources = Stock(20000, 20000, 20000, 1000);
      template.StartingTier = 3;

      var host = OnlineRoom.CreateExtra("HostRoom");
      var guest = OnlineRoom.CreateExtra("GuestRoom");
      host.KeepScenario = true;
      guest.KeepScenario = true;
      var errors = 0;
      var subscription = QuantumCallback.SubscribeManual<CallbackChecksumError>(e => errors++);
      try {
        var create = host.Create(new MatchSettings { FactionIndex = 0 });
        yield return Await(create, 60f, "방 만들기");
        Assert.IsTrue(((Task<bool>)create).Result, $"방 만들기 실패(온라인 부분은 실제 서버가 필요하다): {host.Error}");
        var join = guest.Join(host.Code, new MatchSettings { FactionIndex = 1 });
        yield return Await(join, 60f, "참가");
        Assert.IsTrue(((Task<bool>)join).Result, guest.Error);
        guest.SetMine(1, 1, true);
        host.HostSet(MatchMode.Duel, 0, new int[0]);
        yield return WaitForReal(() => host.StartBlocker() == null && host.TruceMinutes == 0, 20f, "시작 가능");
        Assert.IsTrue(host.HostStart());
        yield return WaitForReal(() => host.StartReceived && guest.StartReceived, 20f, "시작 신호 전달");
        var a = host.StartSession(template, factions);
        var b = guest.StartSession(template, factions);
        yield return Await(Task.WhenAll(a, b), 90f, "세션 시작");
        Assert.IsTrue(a.Result && b.Result, $"세션 실패: {host.Error} / {guest.Error}");
        var games = new[] { host.Runner.Game, guest.Runner.Game };
        yield return WaitForReal(() => games[0].Frames.Verified != null && games[1].Frames.Verified != null && BothActive(games[0].Frames.Verified), 60f, "두 사람 합류");

        var checks = 0;
        var stage = new int[2];
        var launched = false;
        var startReal = Time.realtimeSinceStartup;
        // 각 클라이언트가 자기 연구 → 준비 → (미사일이면) 발사를 명령으로 보낸다. 그동안 같은 확정 프레임의 체크섬을 계속 비교한다.
        while (true) {
          var fa = games[0].Frames.Verified;
          var fb = games[1].Frames.Verified;
          if (fa.Number == fb.Number) {
            Assert.AreEqual(fa.CalculateChecksum(), fb.CalculateChecksum(), $"프레임 {fa.Number} 체크섬");
            checks++;
          }
          for (var i = 0; i < 2; i++) {
            var frame = games[i].Frames.Verified;
            var locals = games[i].GetLocalPlayers();
            if (locals.Count == 0) {
              // 아직 서버가 플레이어 번호를 주지 않았다.
              continue;
            }
            var player = locals[0];
            var state = StateIn(frame, player);
            var spec = UltimateSystem.SpecOf(frame, player);
            if (stage[i] == 0 && state.Stock.Power >= spec.Cost.Power + 200) {
              var lab = EntityRef.None;
              foreach (var pair in frame.GetComponentIterator<Building>()) {
                if (pair.Component.Owner == player && frame.FindAsset(pair.Component.Spec).Role == BuildingRole.ResearchLab) {
                  lab = pair.Entity;
                }
              }
              games[i].SendCommand(0, new ResearchCommand { Building = lab, Index = TechSystem.IndexOf(frame, player, spec.Research) });
              stage[i] = 1;
            } else if (stage[i] == 1 && UltimateSystem.Locks(frame, player) == UltimateLock.None) {
              games[i].SendCommand(0, new StartUltimateCommand());
              stage[i] = 2;
            } else if (stage[i] == 2 && state.UltimatePhase == UltimatePhase.Ready && spec.Kind == UltimateKind.StrategicMissile) {
              // 상대 항모 조립창 쪽에 쏜다.
              var enemy = (int)player == 0 ? 1 : 0;
              games[i].SendCommand(0, new LaunchUltimateCommand { Target = new FPVector3(enemy == 0 ? -18 : 18, 0, 14) });
              stage[i] = 3;
              launched = true;
            }
          }
          var verified = games[0].Frames.Verified;
          var used = StateIn(verified, 0).UltimateUses > 0 && StateIn(verified, 1).UltimateUses > 0;
          if (used && launched && verified.ComponentCount<MissileStrike>() == 0 && verified.ComponentCount<Carrier>() + StateIn(verified, 0).UltimateLost + StateIn(verified, 1).UltimateLost > 0) {
            // 착탄 뒤 5초 더 비교하고 끝낸다.
            var end = verified.Number + 5 * Fps;
            while (games[0].Frames.Verified.Number < end) {
              var ga = games[0].Frames.Verified;
              var gb = games[1].Frames.Verified;
              if (ga.Number == gb.Number) {
                Assert.AreEqual(ga.CalculateChecksum(), gb.CalculateChecksum(), $"프레임 {ga.Number} 체크섬");
                checks++;
              }
              yield return null;
            }
            break;
          }
          if (Time.realtimeSinceStartup - startReal > 240f) {
            Assert.Fail($"온라인 세션에서 궁극 전력이 끝까지 진행하지 않음(단계 {stage[0]}/{stage[1]})");
          }
          yield return null;
        }
        var final = games[0].Frames.Verified;
        var drones = final.ComponentCount<Drone>();
        OnlineLog($"궁극 전력 온라인 체크섬 일치 {checks}회, 디싱크 {errors}, 사용 {StateIn(final, 0).UltimateUses}/{StateIn(final, 1).UltimateUses}, 드론 {drones}, 핑 {host.Ping}/{guest.Ping}ms");
        Assert.Greater(checks, 20, "비교 횟수");
        Assert.AreEqual(0, errors, "체크섬 오류(디싱크) 없음");
      } finally {
        subscription.Dispose();
        var a = host.Leave();
        var b = guest.Leave();
        Object.Destroy(host.gameObject);
        Object.Destroy(guest.gameObject);
      }
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Ai_UsesAndCountersUltimate() {
      ShortenUltimates();
      EagerAi();
      // 미사일 준비는 길게(100초) 두어 자유 연방 AI가 대응할 시간을 준다.
      var missile = Resources.Load<UltimateSpec>("FactoryWars/ultimate_cf_missile");
      missile.PrepareSeconds = 100;
      var ai = Resources.Load<AiSpec>("FactoryWars/ai_normal");
      Assert.IsTrue(ai.UseUltimate && ai.CounterUltimate && ai.EvacuateBlast, "보통 AI는 궁극 전력을 쓰고 대응한다");
      Assert.IsFalse(Resources.Load<AiSpec>("FactoryWars/ai_easy").UseUltimate, "쉬움은 쓰지 않는다");
      yield return StartMatch(players: 2, bots: new[] { BotDifficulty.Normal, BotDifficulty.Normal }, seed: 3304, buildings: DuelBases, units: DuelUnits, stock: Stock(20000, 20000, 20000, 1000));
      ListenNotices();
      var silo = OwnBuilding(1, "cf_missile_silo");
      var siloPosition = Verified.Get<Transform3D>(silo).Position;
      var siloHealth = Verified.Get<Building>(silo).Health;

      // 1. AI가 스스로 연구하고 준비를 시작한다(알림이 간다).
      yield return WaitFor(() => Noticed(0, UltimateStage.Started) && Noticed(1, UltimateStage.Started), 60f, "두 AI 모두 준비 시작");
      Assert.Greater(StateOf(0).UltimateStartFrame, 0);
      Assert.Greater(StateOf(1).UltimateStartFrame, 0);
      // 2. 상대의 준비 알림을 받은 AI는 그 전용 건물을 기억한다.
      Assert.Greater(BotOf(0).ThreatFrame, 0, "자유 연방 AI가 미사일 준비를 알아챔");
      Assert.AreEqual(silo, BotOf(0).ThreatSite, "위협 = 미사일 격납고");
      Assert.Greater(BotOf(1).ThreatFrame, 0, "대륙 연방 AI가 항모 준비를 알아챔");
      // 3. 준비 중 전용 건물 방어: 대륙 연방 AI가 격납고 옆에 병력을 붙인다.
      yield return WaitFor(() => Army(1, true).Count(u => FactoryWarsUtil.FlatDistanceSqr(Verified.Get<Transform3D>(u).Position, siloPosition) < 15 * 15) >= 2, 60f, "격납고 방어 병력");
      // 4. 대응 생산: 대륙 연방 AI는 항모에 대비해 대공차를 평소(IdleAntiAir 2기)보다 많이 뽑는다.
      yield return WaitFor(() => {
        var antiAir = UnitsNamed(1, "cf_thunder_aa").Count;
        foreach (var pair in Verified.GetComponentIterator<Building>()) {
          for (var i = 0; pair.Component.Owner == 1 && i < pair.Component.QueueCount; i++) {
            antiAir += Verified.FindAsset(pair.Component.Queue[i].Unit).Attack == AttackType.AntiAir ? 1 : 0;
          }
        }
        return antiAir > ai.IdleAntiAir;
      }, 120f, "대공차 추가 생산");
      // 5. 항모가 뜬다(자유 연방 AI의 궁극 전력 사용).
      yield return WaitFor(() => StateOf(0).UltimateUses > 0, 60f, "AI 항모 출격");
      // 6. 대응: 자유 연방 AI는 미사일에 대비해 방공포대를 짓고, 군대의 목표를 격납고로 잡는다(경유점을 거쳐 간다). 격납고가 이미 부서졌으면 그것도 대응의 결과다.
      yield return WaitFor(() => OwnBuilding(0, "ff_air_defense") != EntityRef.None, 120f, "방공포대 건설(요격 대비)");
      yield return WaitFor(() => Verified.Exists(silo) == false || Verified.Get<Building>(silo).Health < siloHealth || BotOf(0).LaneTarget == siloPosition
        || Army(0, true).Any(u => Verified.Get<Unit>(u).TargetPosition == siloPosition), 180f, "격납고 공격");
      Debug.Log($"[FW-TEST] ai ultimate: uses {StateOf(0).UltimateUses}/{StateOf(1).UltimateUses} sitesLost {StateOf(1).UltimateSitesLost} notices {string.Join(",", _notices.Select(n => $"{n.Player}{n.Stage}"))}");
    }

    /// <summary>명령 카드의 궁극 전력 칸: 자물쇠와 이유 목록, 준비 중 남은 시간·취소, 발동 대기 때 목표 지정 → 발사.</summary>
    [UnityTest]
    public IEnumerator Ui_UltimateSlotLocksPreparesAndTargets() {
      ShortenUltimates(prepare: 6);
      // 자리 0이 대륙 연방(미사일). 연구소·발전소만 있고 격납고는 아직 없다.
      yield return StartMatch(swapFactions: true, buildings: UltimateBase(0, "cf", null, withSite: false), stock: Stock(20000, 20000, 20000, 1000));
      yield return WaitForHud();
      var hud = Controller().Hud;
      var input = Controller().Input;
      ContextEntry Slot() {
        hud.Refresh(Verified);
        return hud.Entries.FirstOrDefault(e => e.Label != null && e.Label.StartsWith("전략 미사일"));
      }
      // 사령부 카드: 궁극 칸은 잠겨 있고 이유를 모두 보여 준다(전용 건물·연구).
      input.Selection.SelectBuilding(Headquarters(0));
      var locked = Slot();
      Assert.IsTrue(locked.Locked && locked.Enabled == false, "조건이 모자라면 자물쇠");
      StringAssert.Contains("미사일 격납고 필요", locked.Reason);
      StringAssert.Contains("궁극 전력 연구 필요", locked.Reason);
      hud.PressContext(hud.Entries.FindIndex(e => e.Label != null && e.Label.StartsWith("전략 미사일")));
      StringAssert.Contains("미사일 격납고 필요", hud.TooltipText, "잠긴 칸을 누르면 이유 표시");
      Assert.AreEqual(UltimatePhase.None, PhaseOf(0), "잠긴 칸은 명령을 보내지 않음");

      // 격납고를 짓고(시험에서는 바로 완공) 연구하면 칸이 열린다.
      ProductionSystem.CreateFinishedBuilding(Verified, 0, BuildingOf(0, "_missile_silo"), V(-18, 14));
      yield return WaitFrames(3);
      StringAssert.DoesNotContain("미사일 격납고 필요", Slot().Reason ?? "");
      StringAssert.Contains("궁극 전력 연구 필요", Slot().Reason);
      yield return ResearchUltimate(0, "cf");
      var open = Slot();
      Assert.IsTrue(open.Enabled && open.Locked == false, "조건을 채우면 열림");
      hud.PressContext(hud.Entries.FindIndex(e => e.Label != null && e.Label.StartsWith("전략 미사일")));
      yield return WaitFor(() => PhaseOf(0) == UltimatePhase.Preparing, 10f, "칸을 누르면 준비 시작");
      hud.Refresh(Verified);
      Assert.IsTrue(hud.Entries.Any(e => e.Label.Contains("준비") && e.Label.Contains("초")), "준비 중 남은 시간");
      Assert.IsTrue(hud.Entries.Any(e => e.Label == "준비 취소" && e.Enabled), "취소 칸");
      Assert.AreEqual(1, hud.UltimateBars, "상단 진행 막대(내 준비)");
      StringAssert.Contains("내 전략 미사일 준비", hud.UltimateBarText(0));

      // 전용 건물 카드에도 같은 칸이 있다. 준비가 끝나면 목표 지정 → 땅을 누르면 발사.
      yield return WaitFor(() => PhaseOf(0) == UltimatePhase.Ready, 30f, "준비 완료");
      input.Selection.SelectBuilding(OwnBuilding(0, "cf_missile_silo"));
      hud.Refresh(Verified);
      StringAssert.Contains("발동 대기", hud.UltimateBarText(0));
      var fire = hud.Entries.First(e => e.Label.Contains("발사"));
      hud.PressContext(hud.Entries.FindIndex(e => e.Label != null && e.Label.Contains("발사")));
      Assert.AreEqual(MatchInput.TapMode.Ultimate, input.Mode, "목표 지정 모드");
      hud.Refresh(Verified);
      Assert.IsTrue(hud.Entries.Any(e => e.Label == "목표 지정 중"));
      var command = input.Tap(Verified, 0, new Vector3(20, 0, 30)) as LaunchUltimateCommand;
      Assert.IsNotNull(command, "땅을 누르면 발사 명령");
      Assert.AreEqual(V(20, 30), command.Target);
      Assert.AreEqual(MatchInput.TapMode.None, input.Mode, "한 번 쓰면 모드 해제");
      yield return Send(0, command);
      hud.Refresh(Verified);
      Assert.AreEqual(1, Verified.ComponentCount<MissileStrike>());
      Assert.IsTrue(Enumerable.Range(0, hud.UltimateBars).Any(i => hud.UltimateBarText(i).Contains("착탄")), "착탄 카운트다운 막대");
      Assert.AreEqual(1, hud.MinimapUltimateMarks, "미니맵 착탄 지점 표시");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Ai_EvacuatesMissileBlast() {
      // 사람(자리 0, 대륙 연방)이 AI(자리 1, 자유 연방)의 전차 무리에 미사일을 쏜다. AI는 카운트다운 동안 폭발 반경 밖으로 뺀다.
      var tanks = Enumerable.Range(0, 4).Select(i => (1, "ff_eagle_tank", -2 + 3 * (i % 2), 18 + 3 * (i / 2))).ToArray();
      ShortenUltimates(warning: 10);
      yield return StartMatch(swapFactions: true, players: 2, bots: new[] { BotDifficulty.Normal }, seed: 3305, buildings: UltimateBase(0, "cf", "cf_missile_silo"), units: tanks, stock: Stock(20000, 20000, 20000, 1000));
      ListenNotices();
      yield return PrepareUltimate(0, "cf", UltimatePhase.Ready);
      var spec = UltimateOf(0);
      var target = V(0, 20);
      var inside = UnitsNamed(1, "ff_eagle_tank").Where(u => FactoryWarsUtil.FlatDistanceSqr(Verified.Get<Transform3D>(u).Position, target) < spec.BlastRadius * spec.BlastRadius).ToArray();
      Assert.GreaterOrEqual(inside.Length, 4, "폭발 반경 안의 AI 전차");
      var health = inside.Sum(Health);
      yield return Send(0, new LaunchUltimateCommand { Target = target });
      yield return WaitFor(() => Noticed(0, UltimateStage.Impact), 60f, "착탄");
      foreach (var unit in inside) {
        Assert.IsTrue(Verified.Exists(unit), "대피한 전차 생존");
        Assert.Greater(FPMath.Sqrt(FactoryWarsUtil.FlatDistanceSqr(Verified.Get<Transform3D>(unit).Position, target)).AsFloat, spec.BlastRadius.AsFloat, "폭발 반경 밖으로 나감");
      }
      Assert.AreEqual(health, inside.Sum(Health), 0.01f, "피해 없음");
    }
  }
}
