namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.Profiling;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 항목 33 화면 캡처(GPU 필요, -testCategory Capture33): screenshots/ultimate/33_*.png. HUD(진행 막대·알림)까지 같이 찍는다.
  /// 그리고 안정성(-testCategory Stability): 4인 AI 30분, 틱 시간·메모리·GC CSV, 같은 시드 두 번의 체크섬 비교.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static string UltimateFolder() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "ultimate"));

    /// <summary>화면(전장 + HUD)을 1920x1080으로 찍는다.</summary>
    IEnumerator Shoot33(string name) {
      Directory.CreateDirectory(UltimateFolder());
      var canvas = Controller().Hud.Root.GetComponentInParent<Canvas>();
      var camera = Camera.main;
      var surface = new RenderTexture(1920, 1080, 24);
      camera.targetTexture = surface;
      canvas.renderMode = RenderMode.ScreenSpaceCamera;
      canvas.worldCamera = camera;
      canvas.planeDistance = 2f;
      yield return null;
      Controller().Hud.Refresh(Controller().Frame);
      Canvas.ForceUpdateCanvases();
      yield return null;
      camera.Render();
      RenderTexture.active = surface;
      var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
      image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
      image.Apply();
      RenderTexture.active = null;
      File.WriteAllBytes(Path.Combine(UltimateFolder(), name + ".png"), image.EncodeToPNG());
      Object.Destroy(image);
      camera.targetTexture = null;
      surface.Release();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    }

    [UnityTest, Explicit, Category("Capture33"), Timeout(1200000)]
    public IEnumerator Capture33_CarrierMissileLanding() {
      // 1. 항모와 드론 발진: 조립창 근처의 적 전차들을 드론이 친다.
      ShortenUltimates();
      var enemies = new[] { (1, "cf_dragonscale_tank", -14, 24), (1, "cf_dragonscale_tank", -20, 26), (1, "cf_fortress_tank", -24, 22) };
      yield return StartMatch(buildings: UltimateBase(0, "ff", "ff_carrier_yard"), units: enemies, stock: Stock(20000, 20000, 20000, 1000));
      ListenNotices();
      yield return WaitForHud();
      yield return Send(0, new ChooseDoctrineCommand { Doctrine = 1 });
      yield return WaitForBattle();
      Controller().Camera.MoveTo(new Vector2(-18, 16));
      yield return ResearchUltimate(0, "ff");
      yield return Send(0, new StartUltimateCommand());
      Time.timeScale = 1f;
      yield return WaitFor(() => StateOf(0).UltimateRemaining < 5, 60f, "준비 중");
      Controller().Input.Selection.SelectBuilding(OwnBuilding(0, "ff_carrier_yard"));
      yield return Shoot33("33_carrier_preparing");
      yield return WaitFor(() => CountComponent<Drone>(0) >= 4, 90f, "드론 발진");
      yield return WaitFrames(Fps);
      var carrierEntity = FirstOf<Carrier>().Entity;
      Controller().Input.Selection.SelectUnit(carrierEntity);
      var cPos = Verified.Get<Transform3D>(carrierEntity).Position.ToUnityVector3();
      Controller().Camera.MoveTo(new Vector2(cPos.x, cPos.z + 2f));
      Controller().Camera.Height = 38f;
      Controller().Camera.Apply();
      yield return null;
      yield return Shoot33("33_carrier_drones");
      var statusDir = Path.Combine(Application.dataPath, "..", "Docs", "status_images");
      Directory.CreateDirectory(statusDir);
      File.Copy(Path.Combine(UltimateFolder(), "33_carrier_drones.png"), Path.Combine(statusDir, "53_ultimate_flying_carrier.png"), true);

      // 2. 전략 미사일: 착탄 좌표 (0, 21) 정중앙 고정 + 피크 화염구/충격파 링/영구 크레이터 + 전술 레드 경보 배너 캡처.
      TearDown();
      yield return null;
      ShortenUltimates(warning: 10);
      var targets = new[] { (1, "ff_eagle_tank", 0, 22), (1, "ff_eagle_tank", 4, 20), (1, "ff_ranger_squad", -3, 20), (1, "ff_ranger_squad", 2, 17) };
      var bases = UltimateBase(0, "cf", "cf_missile_silo").Concat(new[] { (1, "ff_power_plant", -4, 26), (1, "ff_supply_depot", 6, 26) }).ToArray();
      yield return StartMatch(swapFactions: true, buildings: bases, units: targets, stock: Stock(20000, 20000, 20000, 1000));
      ListenNotices();
      yield return WaitForHud();
      yield return Send(0, new ChooseDoctrineCommand { Doctrine = 1 });
      yield return WaitForBattle();
      yield return PrepareUltimate(0, "cf", UltimatePhase.Ready);
      Time.timeScale = 1f;
      Controller().Camera.MoveTo(new Vector2(0f, 21f));
      Controller().Camera.Height = 32f;
      Controller().Camera.Apply();
      yield return Send(0, new LaunchUltimateCommand { Target = V(0, 21) });
      yield return WaitFor(() => FirstOf<MissileStrike>().Component.ImpactFrame - Verified.Number < 6 * Fps, 30f, "카운트다운");
      yield return Shoot33("33_missile_warning");
      yield return WaitFor(() => Noticed(0, UltimateStage.Impact), 60f, "착탄");
      Controller().Camera.MoveTo(new Vector2(0f, 23f));
      Controller().Camera.Height = 58f;
      Controller().Camera.Apply();
      yield return WaitFrames(3);
      yield return Shoot33("33_missile_impact");
      File.Copy(Path.Combine(UltimateFolder(), "33_missile_impact.png"), Path.Combine(statusDir, "54_ultimate_missile_silo_strike.png"), true);

      // 3. 상륙 강습: 상륙정이 해안에 부대를 내린다(4인 맵 수로).
      TearDown();
      yield return null;
      var goal = new FPVector3[1];
      yield return AssaultReady(goal);
      yield return WaitForHud();
      yield return WaitForBattle();
      Time.timeScale = 20f;
      yield return Send(0, new LaunchUltimateCommand { Target = goal[0] });
      var craft = FirstOf<LandingCraft>();
      Controller().Camera.MoveTo(new Vector2(craft.Component.Home.X.AsFloat, craft.Component.Home.Z.AsFloat));
      yield return WaitFrames(3 * Fps);
      yield return Shoot33("33_assault_departure");
      yield return WaitFor(() => Noticed(0, UltimateStage.Landed), 240f, "하선");
      Time.timeScale = 1f;
      Controller().Camera.MoveTo(new Vector2(craft.Component.Shore.X.AsFloat, craft.Component.Shore.Z.AsFloat));
      yield return WaitFrames(Fps);
      yield return Shoot33("33_assault_landing");
      foreach (var name in new[] { "33_carrier_preparing", "33_carrier_drones", "33_missile_warning", "33_missile_impact", "33_assault_departure", "33_assault_landing" }) {
        Assert.IsTrue(File.Exists(Path.Combine(UltimateFolder(), name + ".png")), name);
      }
    }

    static void SpawnCarrierFor(int player, FPVector3 position) {
      var f = Verified;
      var spec = UltimateSystem.SpecOf(f, player);
      var entity = ProductionSystem.SpawnUnitAt(f, player, spec.Carrier, position, null);
      f.Add(entity, new Carrier { Docked = spec.DroneCount });
      f.Global->Players.GetPointer(player)->UltimatePhase = UltimatePhase.Active;
    }

    /// <summary>
    /// 항목 33 성능: 4인 모두 항모 1 + 드론 8, 자리마다 지상·공중 80기(합계 약 360기)가 한가운데서 맞붙는 장면의 틱 시간.
    /// 화면을 끄고 잰 틱 시간(유니티 프레임 시간 / 진행한 틱 수)과, 화면을 켜고 1배속으로 잰 프레임 시간을 남긴다.
    /// 에디터 측정이라 실기기 수치가 아니다. -testCategory Perf33, 결과 Game-logs/perf/ultimate-engagement.csv.
    /// </summary>
    [UnityTest, Explicit, Category("Perf33"), Timeout(900000)]
    public IEnumerator Perf_FourCarriersMaxEngagementTickTime() {
      var starts = new[] { V(-30, 0), V(30, 0), V(0, 30), V(0, -30) };
      var units = new List<(int, string, int, int)>();
      for (var p = 0; p < 4; p++) {
        for (var i = 0; i < 80; i++) {
          var name = i % 4 == 0 ? "ff_eagle_tank" : i % 4 == 1 ? "ff_ranger_squad" : i % 4 == 2 ? "ff_skyguard_squad" : "ff_hawk_gunship";
          var x = starts[p].X.AsInt + (i % 10 - 5) * 2;
          var z = starts[p].Z.AsInt + (i / 10 - 4) * 2;
          units.Add((p, name, Mathf.Clamp(x, -48, 48), Mathf.Clamp(z, -48, 48)));
        }
      }
      yield return StartMatch(players: 4, mirrorFaction: 0, units: units.ToArray(), jointVictory: false, stock: Stock(20000, 20000, 20000, 200));
      for (var p = 0; p < 4; p++) {
        SpawnCarrierFor(p, starts[p] * FP._0_50);
      }
      yield return WaitFrames(3);
      for (var p = 0; p < 4; p++) {
        var army = Army(p, true).Where(u => Verified.Has<Drone>(u) == false).ToArray();
        for (var i = 0; i < army.Length; i += MoveUnitsCommand.MaxUnits) {
          Game.SendCommand(p, new MoveUnitsCommand { Units = army.Skip(i).Take(MoveUnitsCommand.MaxUnits).ToArray(), Target = FPVector3.Zero, AttackMove = true });
          yield return WaitFrames(2);
        }
      }
      var rows = new List<string> { "phase,second,units,drones,avg_ms,max_ms" };
      // 1. 화면 끔: 시뮬레이션 틱 시간.
      var view = Object.FindAnyObjectByType<BattleView>();
      view.enabled = false;
      Time.timeScale = 4f;
      var start = Verified.Number;
      var last = start;
      var ticks = new List<float>();
      var all = new List<float>();
      var maxDrones = 0;
      var nextSecond = start + 5 * Fps;
      while (Verified.Number < start + 40 * Fps) {
        var now = Verified.Number;
        if (now > last) {
          ticks.Add(Time.unscaledDeltaTime * 1000f / (now - last));
          last = now;
        }
        maxDrones = Mathf.Max(maxDrones, Verified.ComponentCount<Drone>());
        if (now >= nextSecond && ticks.Count > 0) {
          nextSecond += 5 * Fps;
          rows.Add($"sim,{(now - start) / Fps},{Verified.ComponentCount<Unit>()},{Verified.ComponentCount<Drone>()},{ticks.Average():0.00},{ticks.Max():0.00}");
          all.AddRange(ticks);
          ticks.Clear();
        }
        yield return null;
      }
      // 2. 화면 켬(보통 품질), 1배속: 프레임 시간.
      GraphicsQuality.Apply(QualityTier.Medium);
      Restores.Add(() => GraphicsQuality.Apply(GraphicsQuality.Default));
      view.enabled = true;
      Time.timeScale = 1f;
      yield return WaitFrames(Fps);
      var frames = new List<float>();
      var until = Time.realtimeSinceStartup + 8f;
      while (Time.realtimeSinceStartup < until) {
        frames.Add(Time.unscaledDeltaTime * 1000f);
        yield return null;
      }
      rows.Add($"view,8,{Verified.ComponentCount<Unit>()},{Verified.ComponentCount<Drone>()},{frames.Average():0.00},{frames.Max():0.00}");
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "perf"));
      Directory.CreateDirectory(folder);
      File.WriteAllLines(Path.Combine(folder, "ultimate-engagement.csv"), rows);
      Debug.Log("[FW-Perf] " + string.Join(" | ", rows) + $" | quality {GraphicsQuality.Name(GraphicsQuality.Current)} max drones {maxDrones}");
      // 네 진영 모두 대공 분대가 20개씩 있어 드론은 뜨는 대로 격추된다. 동시에 8기 이상 떠 있었으면 교전한 것으로 본다.
      Assert.GreaterOrEqual(maxDrones, 8, "드론이 실제로 떠서 싸우는 장면");
      Assert.Less(all.Average(), 50f, "틱 시간 평균이 틱 간격(50ms)보다 짧다");
    }

    /// <summary>
    /// 항목 33 안정성: 4인 AI 30분(경기 시간) 한 판을 같은 시드로 두 번 돌린다. 첫 판에서 분마다 틱 시간·메모리·GC를 CSV로 남기고,
    /// 두 판의 30분 시점 체크섬을 비교한다(다르면 결정론이 깨진 것 = 디싱크). 화면은 끄고 잰다(틱 시간 = 유니티 프레임 시간 / 그 프레임에 진행한 틱 수).
    /// 시드는 FW_STABILITY_SEED(기본 7005). 결과 Game-logs/stability/ultimate-30min.csv.
    /// </summary>
    [UnityTest, Explicit, Category("Stability"), Timeout(3000000)]
    public IEnumerator Stability_ThirtyMinuteFourAiTickTimeAndChecksum() {
      var seedText = System.Environment.GetEnvironmentVariable("FW_STABILITY_SEED");
      var seed = string.IsNullOrEmpty(seedText) ? 7005 : int.Parse(seedText);
      var end = 30 * 60 * Fps;
      var checksums = new ulong[2];
      var endFrames = new int[2];
      var rows = new List<string> { "minute,avg_tick_ms,max_tick_ms,mono_mb,total_alloc_mb,gc_count,units,buildings,drones,strikes,ended" };
      for (var run = 0; run < 2; run++) {
        var bots = Enumerable.Repeat(BotDifficulty.Normal, 4).ToArray();
        yield return StartMatch(Map4PScene, 4, bots: bots, seed: seed, truce: -1);
        Time.timeScale = 20f;
        var view = Object.FindAnyObjectByType<BattleView>();
        if (view != null) {
          view.enabled = false;
        }
        var sum = 0UL;
        QuantumCallback.Subscribe<CallbackSimulateFinished>(_listener, e => {
          if (e.Frame.Number == end && sum == 0UL) {
            sum = e.Frame.CalculateChecksum();
          }
        });
        var gcStart = System.GC.CollectionCount(0);
        var nextMinute = 60 * Fps;
        var ticks = new List<float>();
        var last = Verified.Number;
        while (sum == 0UL) {
          var now = Verified.Number;
          if (now > last) {
            ticks.Add(Time.unscaledDeltaTime * 1000f / (now - last));
            last = now;
          }
          if (run == 0 && now >= nextMinute && ticks.Count > 0) {
            nextMinute += 60 * Fps;
            rows.Add($"{now / (60 * Fps)},{ticks.Average():0.00},{ticks.Max():0.00},{Profiler.GetMonoUsedSizeLong() / 1048576f:0.0},{Profiler.GetTotalAllocatedMemoryLong() / 1048576f:0.0},{System.GC.CollectionCount(0) - gcStart},"
              + $"{Verified.ComponentCount<Unit>()},{Verified.ComponentCount<Building>()},{Verified.ComponentCount<Drone>()},{Verified.ComponentCount<MissileStrike>()},{(GameEnded() ? 1 : 0)}");
            ticks.Clear();
          }
          yield return null;
        }
        checksums[run] = sum;
        endFrames[run] = GameEnded() ? EndFrame() : 0;
        Debug.Log($"[FW-Stability] run {run}: checksum {sum} ended {endFrames[run]} uses {string.Join("/", Enumerable.Range(0, 4).Select(i => StateOf(i).UltimateUses))}");
        TearDown();
        yield return null;
      }
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "stability"));
      Directory.CreateDirectory(folder);
      rows.Add($"# seed {seed} checksum {checksums[0]} / {checksums[1]} end_frame {endFrames[0]} / {endFrames[1]}");
      File.WriteAllLines(Path.Combine(folder, "ultimate-30min.csv"), rows);
      Debug.Log("[FW-Stability]\n" + string.Join("\n", rows));
      Assert.AreEqual(checksums[0], checksums[1], "같은 시드 두 번의 30분 시점 체크섬(디싱크 0)");
      Assert.AreEqual(endFrames[0], endFrames[1], "끝난 프레임도 같음");
    }
  }
}
