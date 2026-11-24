namespace FactoryWars.Tests {
  using System.Collections;
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
  /// Task 46 phase A: the west waterway over a real Photon session (two clients, dev App ID as the other online tests). Player 0's
  /// scout boat leaves its dock basin through its own sea gate (open, then close) while player 1's artillery ship shoots that
  /// sector's barrier down. The verified-frame checksums of both clients must match for up to 5 minutes.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static SeaGate GateOfPlayer(Frame frame, PlayerRef player, out bool found) {
      found = false;
      foreach (var pair in frame.GetComponentIterator<SeaGate>()) {
        var g = pair.Component;
        if (g.Owner == player && g.Barrier != EntityRef.None) { found = true; return g; }
      }
      return default;
    }

    static EntityRef UnitOf(Frame frame, PlayerRef player, string specName) {
      foreach (var pair in frame.GetComponentIterator<Unit>()) {
        if (pair.Component.Owner == player && frame.FindAsset(pair.Component.Spec).name == specName) return pair.Entity;
      }
      return EntityRef.None;
    }

    [UnityTest, Explicit, Category("Online"), Timeout(1200000)]
    public IEnumerator Online_T46_WaterwayChecksumsMatch() {
      Time.timeScale = 1f;
      LogAssert.ignoreFailingMessages = true;
#if UNITY_EDITOR
      yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(Map4PScene, new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
      var local = Object.FindAnyObjectByType<QuantumRunnerLocalDebug>();
      local.enabled = false;
      QuantumRunner.ShutdownAll();
      var factions = local.LocalPlayers.Take(2).Select(p => p.Faction).ToArray();
      var template = local.RuntimeConfig;
      template.Map = Object.FindAnyObjectByType<QuantumMapData>().AssetRef;
      var boat = Resources.Load<UnitSpec>("FactoryWars/ff_scout_boat");
      var ship = Resources.Load<UnitSpec>("FactoryWars/ff_artillery_ship");
      template.ScenarioUnits = new[] {
        // Seats differ between sessions: each player gets a boat in one dock basin and a ship outside the other gate, so whichever
        // player owns a west sea gate has its own boat behind it and the other player's ship outside it.
        new ScenarioUnit { Player = 0, Unit = new AssetRef<UnitSpec>(boat.Guid), Position = new FPVector3(-308, 0, -300) },
        new ScenarioUnit { Player = 1, Unit = new AssetRef<UnitSpec>(ship.Guid), Position = new FPVector3(-308, 0, -60) },
        new ScenarioUnit { Player = 1, Unit = new AssetRef<UnitSpec>(boat.Guid), Position = new FPVector3(-308, 0, 300) },
        new ScenarioUnit { Player = 0, Unit = new AssetRef<UnitSpec>(ship.Guid), Position = new FPVector3(-308, 0, 60) },
      };
      template.ScenarioBuildings = new ScenarioBuilding[0];
      template.OverrideStartingResources = true;
      template.StartingResources = new ResourceCost { Iron = 5000, Wood = 5000, Steel = 2000, Power = 2000 };
      template.OverrideTruce = true;
      template.TruceSeconds = 0;

      var host = OnlineRoom.CreateExtra("HostRoom");
      var guest = OnlineRoom.CreateExtra("GuestRoom");
      host.KeepScenario = true;
      guest.KeepScenario = true;
      var errors = 0;
      var subscription = QuantumCallback.SubscribeManual<CallbackChecksumError>(e => errors++);
      try {
        var create = host.Create(new MatchSettings { FactionIndex = 0 });
        yield return Await(create, 60f, "create room");
        Assert.IsTrue(((Task<bool>)create).Result, $"create room failed (needs the real Photon server): {host.Error}");
        var join = guest.Join(host.Code, new MatchSettings { FactionIndex = 1 });
        yield return Await(join, 60f, "join");
        Assert.IsTrue(((Task<bool>)join).Result, guest.Error);
        guest.SetMine(1, 1, true);
        host.HostSet(MatchMode.Duel, 0, new int[0]);
        yield return WaitForReal(() => host.StartBlocker() == null && host.TruceMinutes == 0, 20f, "ready");
        Assert.IsTrue(host.HostStart());
        yield return WaitForReal(() => host.StartReceived && guest.StartReceived, 20f, "start received");
        var a = host.StartSession(template, factions);
        var b = guest.StartSession(template, factions);
        yield return Await(Task.WhenAll(a, b), 90f, "session start");
        Assert.IsTrue(a.Result && b.Result, $"session failed: {host.Error} / {guest.Error}");
        var games = new[] { host.Runner.Game, guest.Runner.Game };
        yield return WaitForReal(() => games[0].Frames.Verified != null && games[1].Frames.Verified != null && BothActive(games[0].Frames.Verified), 60f, "both joined");

        var checks = 0;
        var sent = new bool[2];
        var opened = false;
        var broken = false;
        var gateIndex = -1;
        var owner = PlayerRef.None;
        var start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < 300f) {
          var fa = games[0].Frames.Verified;
          var fb = games[1].Frames.Verified;
          if (fa.Number == fb.Number) {
            Assert.AreEqual(fa.CalculateChecksum(), fb.CalculateChecksum(), $"frame {fa.Number} checksum");
            checks++;
          }
          if (gateIndex < 0) {
            for (var p = 0; p < 2 && gateIndex < 0; p++) {
              var mine = GateOfPlayer(fa, p, out var found);
              if (found) { gateIndex = mine.Index; owner = p; }
            }
          }
          if (gateIndex >= 0) {
            foreach (var pair in fa.GetComponentIterator<SeaGate>()) {
              if (pair.Component.Index != gateIndex) continue;
              opened |= pair.Component.State == SeaGateSystem.Open;
              broken |= pair.Component.Barrier == EntityRef.None || fa.Exists(pair.Component.Barrier) == false;
            }
          }
          for (var i = 0; i < 2; i++) {
            if (sent[i] || gateIndex < 0) continue;
            var locals = games[i].GetLocalPlayers();
            if (locals.Count == 0) continue;
            var player = locals[0];
            var frame = games[i].Frames.Verified;
            if (player == owner) {
              var mineBoat = UnitOf(frame, player, boat.name);
              if (mineBoat == EntityRef.None) continue;
              games[i].SendCommand(0, new MoveUnitsCommand { Units = new[] { mineBoat }, Target = new FPVector3(-308, 0, 0) });
            } else {
              var raider = UnitOf(frame, player, ship.name);
              var target = EntityRef.None;
              foreach (var pair in frame.GetComponentIterator<SeaGate>()) if (pair.Component.Index == gateIndex) target = pair.Component.Barrier;
              if (raider == EntityRef.None || target == EntityRef.None) continue;
              games[i].SendCommand(0, new AttackTargetCommand { Units = new[] { raider }, Target = target });
            }
            sent[i] = true;
          }
          // Run the full 5 minutes (acceptance: 5 min with a barrier break and a gate cycle).
          yield return null;
        }
        OnlineLog($"T46 online: checksums matched {checks}x over {Time.realtimeSinceStartup - start:0} s, desyncs {errors}, gate {gateIndex} owner {owner} opened {opened}, barrier broken {broken}, ping {host.Ping}/{guest.Ping}ms");
        Assert.GreaterOrEqual(gateIndex, 0, "one player owns a west sea gate");
        Assert.IsTrue(opened, "the owner's gate opened for its boat");
        Assert.IsTrue(broken, "the raider broke the barrier");
        Assert.Greater(checks, 600, "compared frames");
        Assert.AreEqual(0, errors, "no checksum errors");
      } finally {
        subscription.Dispose();
        host.Leave();
        guest.Leave();
        Object.Destroy(host.gameObject);
        Object.Destroy(guest.gameObject);
      }
    }
  }
}
