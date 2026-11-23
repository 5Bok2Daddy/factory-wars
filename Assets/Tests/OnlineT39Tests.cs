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
  /// Task 39 (C6): drawbridge + mercenary camp over a real Photon session (two clients, dev App ID as the other online tests).
  /// A tall ship raises a drawbridge while both clients send the camp diplomacy commands (message, cargo truck); the verified-frame
  /// checksums of both clients must match the whole time. Needs network access to the Photon cloud.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static NpcTrader CampIn(Frame frame) {
      foreach (var pair in frame.GetComponentIterator<NpcTrader>()) return pair.Component;
      return default;
    }

    static int LiftIn(Frame frame, int index) {
      foreach (var pair in frame.GetComponentIterator<Bridge>()) if (pair.Component.Index == index) return pair.Component.Lift;
      return -1;
    }

    [UnityTest, Explicit, Category("Online"), Timeout(900000)]
    public IEnumerator Online_T39_DrawbridgeAndCampChecksumsMatch() {
      EnableDrawbridgeMode();
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
      var ship = new AssetRef<UnitSpec>(Resources.Load<UnitSpec>("FactoryWars/ff_artillery_ship").Guid);
      template.ScenarioUnits = new[] { new ScenarioUnit { Player = 0, Unit = ship, Position = new FPVector3(-16, 0, 146) } };
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
        var stage = new int[2];
        var raised = false;
        var start = Time.realtimeSinceStartup;
        var drawbridge = FactoryWarsUtil.MapInfo(games[0].Frames.Verified).Drawbridges[0];
        while (Time.realtimeSinceStartup - start < 90f) {
          var fa = games[0].Frames.Verified;
          var fb = games[1].Frames.Verified;
          if (fa.Number == fb.Number) {
            Assert.AreEqual(fa.CalculateChecksum(), fb.CalculateChecksum(), $"frame {fa.Number} checksum");
            checks++;
          }
          raised |= LiftIn(fa, drawbridge) == BridgeTraffic.Up;
          for (var i = 0; i < 2; i++) {
            var locals = games[i].GetLocalPlayers();
            if (locals.Count == 0) continue;
            var player = locals[0];
            var camp = CampIn(games[i].Frames.Verified);
            if (stage[i] == 0) { games[i].SendCommand(0, new TradeCommand { Offer = MercenaryCamp.OfferMessage }); stage[i] = 1; }
            else if (stage[i] == 1 && (camp.ContactMask & (1 << (int)player)) != 0) { games[i].SendCommand(0, new TradeCommand { Offer = MercenaryCamp.OfferDispatch }); stage[i] = 2; }
          }
          if (raised && stage[0] == 2 && stage[1] == 2 && checks > 200) break;
          yield return null;
        }
        var final = games[0].Frames.Verified;
        OnlineLog($"T39 online: checksums matched {checks}x, desyncs {errors}, drawbridge raised {raised}, contact mask {CampIn(final).ContactMask}, ping {host.Ping}/{guest.Ping}ms");
        Assert.IsTrue(raised, "drawbridge raised during the online session");
        Assert.AreEqual(3, CampIn(final).ContactMask & 3, "both players contacted the camp");
        Assert.Greater(checks, 100, "compared frames");
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
