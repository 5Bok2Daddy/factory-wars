namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>
  /// AI 대 AI 자동 대전. 시간이 오래 걸려 일반 테스트에서 빠지고, -testCategory AiBatch로 따로 돌린다.
  /// 시뮬레이션은 건드리지 않고 프레임을 읽어 Game-logs/ai에 기록한다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    const float AiTimeScale = 40f;
    const int AiMaxSeconds = 30 * 60;
    const int AiCheckSeconds = 5;
    // 이동 명령을 받고 교전도 없이 이만큼 제자리면 끼인 것으로 센다.
    const int StuckSeconds = 20;
    const float StuckMove = 1f;
    const float ArrivedDistance = 8f;
    const float RiverReach = 15f;

    static readonly int[] AiRuns = Enumerable.Range(0, 10).ToArray();

    static string AiFolder() {
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "ai"));
      Directory.CreateDirectory(folder);
      return folder;
    }

    static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    [UnityTest, Explicit, Category("AiBatch"), Timeout(3000000)]
    public IEnumerator AiBatch_TwoPlayer([ValueSource(nameof(AiRuns))] int run) {
      yield return PlayAiMatch(Map2PScene, "2P", 2, run);
    }

    [UnityTest, Explicit, Category("AiBatch"), Timeout(3000000)]
    public IEnumerator AiBatch_FourPlayer([ValueSource(nameof(AiRuns))] int run) {
      yield return PlayAiMatch(Map4PScene, "4P", 4, run);
    }

    IEnumerator PlayAiMatch(string scene, string map, int players, int run) {
      var bots = Enumerable.Repeat(BotDifficulty.Normal, players).ToArray();
      yield return StartMatch(scene, players, bots: bots, seed: 7000 + run, swapFactions: run % 2 == 1, truce: -1);
      Time.timeScale = AiTimeScale;
      var view = Object.FindAnyObjectByType<FactoryWars.View.BattleView>();
      if (view != null) {
        view.enabled = false;
      }

      var firsts = new Dictionary<string, float>();
      var minutes = new List<string>();
      var nextCheck = 0;
      var nextMinute = 60 * Fps;
      var tracker = new StuckTracker(players);
      var realStart = Time.realtimeSinceStartup;
      while (GameEnded() == false && Verified.Number < AiMaxSeconds * Fps) {
        var frame = Verified.Number;
        if (frame >= nextCheck) {
          nextCheck = frame + AiCheckSeconds * Fps;
          RecordFirsts(firsts, players, frame / (float)Fps);
          tracker.Sample(map, run, frame);
        }
        if (frame >= nextMinute) {
          nextMinute += 60 * Fps;
          for (var i = 0; i < players; i++) {
            minutes.Add(MinuteRow(map, run, frame / (60 * Fps), i));
          }
        }
        yield return null;
      }

      var seconds = (GameEnded() ? EndFrame() : Verified.Number) / (float)Fps;
      var winners = GameEnded() ? string.Join("+", Enumerable.Range(0, players).Where(i => (WinnerMask() & (1 << i)) != 0).Select(i => FactionOf(i).FactionId)) : "timeout";
      var alive = Enumerable.Range(0, players).Count(i => StateOf(i).Defeated == false);
      var seats = string.Join("+", Enumerable.Range(0, players).Select(i => FactionOf(i).FactionId));
      var firstText = string.Join(";", firsts.OrderBy(p => p.Value).Select(p => $"{p.Key}={F(p.Value)}"));
      var endText = string.Join(" ", Enumerable.Range(0, players).Select(EndState)) + " " + Relations(players);
      var row = $"{map},{run},{F(seconds)},{winners},{alive},{seats},{firstText},{endText}";
      File.AppendAllLines(Path.Combine(AiFolder(), "matches.csv"), new[] { row });
      File.AppendAllLines(Path.Combine(AiFolder(), "minutes.csv"), minutes);
      File.AppendAllLines(Path.Combine(AiFolder(), "stuck.csv"), tracker.Rows);
      File.AppendAllLines(Path.Combine(AiFolder(), "crossing.csv"), tracker.Crossings(map, run));
      File.AppendAllLines(Path.Combine(AiFolder(), "expansions.csv"), tracker.Expansions);
      File.AppendAllLines(Path.Combine(AiFolder(), "lanes.csv"), LaneRows(map, run, players));
      File.AppendAllLines(Path.Combine(AiFolder(), "battlefield.csv"), BattlefieldRows(map, run, players));
      Debug.Log($"[FW-AI] {row} real {F(Time.realtimeSinceStartup - realStart)}s");
      Assert.Greater(seconds, 0f);
    }

    /// <summary>자리별 결과와 전장 요소 기록. 승리는 자리 번호로 남긴다.</summary>
    static unsafe IEnumerable<string> BattlefieldRows(string map, int run, int players) {
      var rows = new List<string>();
      var spec = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var broken = (spec.Barricades?.Length ?? 0) - Verified.ComponentCount<Barricade>();
      var collapsed = 0;
      for (var i = 0; spec.CityBuildings != null && i < spec.CityBuildings.Length; i++) {
        collapsed += CitySystem.IsCollapsed(Verified, i) ? 1 : 0;
      }
      var winners = Verified.Global->GameEnded ? Verified.Global->WinnerMask : 0;
      for (var p = 0; p < players; p++) {
        var state = Verified.Global->Players.GetPointer(p);
        rows.Add($"{map},{run},{p},{FactionOf(p).FactionId},{((winners >> p) & 1)},{state->Captures},{F(state->CaptureFrames / (float)Fps)},{state->CratesPicked},{state->HighGroundShots},{broken},{collapsed},{state->GarrisonEntries}");
      }
      return rows;
    }

    static unsafe IEnumerable<string> LaneRows(string map, int run, int players) {
      var rows = new List<string>();
      for (var p = 0; p < players; p++) {
        var bot = Verified.Global->Bots.GetPointer(p);
        rows.Add($"{map},{run},{p},{FactionOf(p).FactionId},{bot->CenterWaves},{bot->SideWaves}");
      }
      return rows;
    }

    /// <summary>지상 전투 유닛이 강을 건넜는지, 이동 중에 끼였는지 기록한다. 프레임을 읽기만 한다.</summary>
    sealed class StuckTracker {
      struct Seen {
        public Vector3 Position;
        public int Since;
        public bool Reported;
      }

      readonly Dictionary<EntityRef, Seen> _seen = new Dictionary<EntityRef, Seen>();
      readonly HashSet<EntityRef>[] _ground;
      readonly HashSet<EntityRef>[] _crossed;
      readonly Rect[] _homes;
      readonly Vector3[] _bridges;
      public readonly List<string> Rows = new List<string>();
      public readonly List<string> Expansions = new List<string>();
      readonly HashSet<EntityRef> _yards = new HashSet<EntityRef>();

      public StuckTracker(int players) {
        var spec = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
        _bridges = spec.Bridges.Select(b => b.Center.ToUnityVector3()).ToArray();
        _ground = Enumerable.Range(0, players).Select(_ => new HashSet<EntityRef>()).ToArray();
        _crossed = Enumerable.Range(0, players).Select(_ => new HashSet<EntityRef>()).ToArray();
        _homes = new Rect[players];
        for (var p = 0; p < players; p++) {
          var start = spec.StartPositions[p].ToUnityVector3();
          _homes[p] = spec.BaseAreas.Select(a => FactoryWars.View.TerrainKit.Extent(a, RiverReach)).First(r => r.Contains(new Vector2(start.x, start.z)));
        }
      }

      void SampleYards(string map, int run, int frame) {
        var sites = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset).Expansions;
        foreach (var pair in Verified.GetComponentIterator<Building>()) {
          var spec = Verified.FindAsset(pair.Component.Spec);
          if (spec.Role != BuildingRole.CollectionYard || pair.Component.ConstructionRemaining > 0 || _yards.Add(pair.Entity) == false) {
            continue;
          }
          var position = Verified.Get<Transform3D>(pair.Entity).Position;
          var site = sites.OrderBy(s => (s.Position - position).SqrMagnitude.AsFloat).First();
          Expansions.Add($"{map},{run},{(int)pair.Component.Owner},{FactionOf(pair.Component.Owner).FactionId},{site.Kind},{F(frame / (float)Fps)},{F((site.Position - position).Magnitude.AsFloat)}");
        }
      }

      public void Sample(string map, int run, int frame) {
        SampleYards(map, run, frame);
        foreach (var pair in Verified.GetComponentIterator<Unit>()) {
          var unit = pair.Component;
          var spec = Verified.FindAsset(unit.Spec);
          if (spec.IsAir || spec.Attack == AttackType.None || unit.Owner >= _ground.Length) {
            continue;
          }
          var position = Verified.Get<Transform3D>(pair.Entity).Position.ToUnityVector3();
          _ground[unit.Owner].Add(pair.Entity);
          if (_homes[unit.Owner].Contains(new Vector2(position.x, position.z)) == false) {
            _crossed[unit.Owner].Add(pair.Entity);
          }

          var goal = unit.TargetPosition.ToUnityVector3();
          var travelling = (unit.Order == UnitOrder.Move || unit.Order == UnitOrder.AttackMove)
            && unit.TargetEntity == EntityRef.None
            && unit.EngagedUntilFrame <= frame
            && (goal - position).magnitude > ArrivedDistance;
          if (travelling == false || _seen.TryGetValue(pair.Entity, out var last) == false || (position - last.Position).magnitude >= StuckMove) {
            _seen[pair.Entity] = new Seen { Position = position, Since = frame };
            continue;
          }
          var seconds = (frame - last.Since) / Fps;
          if (seconds >= StuckSeconds && last.Reported == false) {
            last.Reported = true;
            _seen[pair.Entity] = last;
            var bridge = _bridges.Min(b => (b - position).magnitude);
            Rows.Add($"{map},{run},{F(frame / (float)Fps)},{FactionOf(unit.Owner).FactionId},{spec.name},{unit.Order},{F(position.x)},{F(position.z)},{F(bridge)},{Detail(pair.Entity, unit, position)}");
          }
        }
      }

      /// <summary>목적지, 다음 경유점, 미끄러지는 방향, 맞닿은 건물. 끼인 원인을 가른다.</summary>
      static string Detail(EntityRef entity, Unit unit, Vector3 position) {
        var path = Verified.Get<NavMeshPathfinder>(entity);
        var goal = unit.TargetPosition.ToUnityVector3();
        var waypoint = path.IsActive && path.WaypointCount > 0 ? path.GetWaypoint(Verified, path.WaypointIndex).ToUnityVector3() : Vector3.zero;
        var nearest = "none";
        var best = float.MaxValue;
        foreach (var pair in Verified.GetComponentIterator<Building>()) {
          var center = Verified.Get<Transform3D>(pair.Entity).Position.ToUnityVector3();
          var spec = Verified.FindAsset(pair.Component.Spec);
          var gap = Mathf.Max(Mathf.Abs(center.x - position.x), Mathf.Abs(center.z - position.z)) - spec.FootprintRadius.AsFloat;
          if (gap < best) {
            best = gap;
            nearest = $"{spec.name}@{F(center.x)}/{F(center.z)} r{F(spec.FootprintRadius.AsFloat)} gap{F(gap)}";
          }
        }
        return $"goal{F(goal.x)}/{F(goal.z)} way{path.WaypointIndex}of{path.WaypointCount}:{F(waypoint.x)}/{F(waypoint.z)} active{path.IsActive} turn{unit.SlideTurn} {nearest}";
      }

      public IEnumerable<string> Crossings(string map, int run) => Enumerable.Range(0, _ground.Length)
        .Select(p => $"{map},{run},{p},{FactionOf(p).FactionId},{_ground[p].Count},{_crossed[p].Count}");
    }

    /// <summary>군대가 놀고 있는지 본다. 명령별 수, 평균 위치, 다음 공격 시각, 웨이브 크기.</summary>
    static unsafe string ArmyStates(int players) {
      var parts = new List<string>();
      for (var p = 0; p < players; p++) {
        int stop = 0, move = 0, attackMove = 0, attackTarget = 0, targets = 0;
        var sum = Vector3.zero;
        var kinds = new Dictionary<string, int>();
        foreach (var pair in Verified.GetComponentIterator<Unit>()) {
          if (pair.Component.Owner != p) {
            continue;
          }
          var spec = Verified.FindAsset(pair.Component.Spec);
          if (spec.Attack == AttackType.None) {
            continue;
          }
          stop += pair.Component.Order == UnitOrder.Stop ? 1 : 0;
          move += pair.Component.Order == UnitOrder.Move ? 1 : 0;
          attackMove += pair.Component.Order == UnitOrder.AttackMove ? 1 : 0;
          attackTarget += pair.Component.Order == UnitOrder.AttackTarget ? 1 : 0;
          targets += pair.Component.TargetEntity != EntityRef.None ? 1 : 0;
          sum += Verified.Get<Transform3D>(pair.Entity).Position.ToUnityVector3();
          kinds[spec.name] = kinds.TryGetValue(spec.name, out var n) ? n + 1 : 1;
        }
        var count = stop + move + attackMove + attackTarget;
        var center = count > 0 ? sum / count : Vector3.zero;
        var bot = Verified.Global->Bots.GetPointer(p);
        parts.Add($"p{p}[s{stop}/m{move}/am{attackMove}/at{attackTarget}/t{targets} c{center.x:0},{center.z:0} next{bot->NextAttackFrame - Verified.Number} wave{bot->WaveSize} {string.Join("+", kinds.Select(k => $"{k.Key.Substring(3)}{k.Value}"))}]");
      }
      return string.Join(" ", parts);
    }

    /// <summary>끝났을 때 동맹 관계와 맵에 남은 철. 안 끝난 경기의 원인을 찾는 데 쓴다.</summary>
    static string Relations(int players) {
      var pairs = new List<string>();
      for (var a = 0; a < players; a++) {
        for (var b = a + 1; b < players; b++) {
          var relation = DiplomacySystem.GetRelation(Verified, a, b);
          if (relation != Relation.Enemy) {
            pairs.Add($"{a}{relation.ToString()[0]}{b}");
          }
        }
      }
      var iron = 0f;
      foreach (var pair in Verified.GetComponentIterator<ResourceNode>()) {
        iron += pair.Component.Kind == ResourceKind.Iron ? pair.Component.Remaining.AsFloat : 0f;
      }
      return $"rel[{string.Join("/", pairs)}] ironleft{iron:0} {ArmyStates(players)}";
    }

    /// <summary>끝났을 때 자리별 상태. 안 끝난 경기의 원인을 찾는 데 쓴다.</summary>
    static string EndState(int player) {
      var s = StateOf(player);
      var buildings = 0;
      var headquarters = 0;
      foreach (var pair in Verified.GetComponentIterator<Building>()) {
        if (pair.Component.Owner == player) {
          buildings++;
          headquarters += Verified.FindAsset(pair.Component.Spec).Role == BuildingRole.Headquarters ? 1 : 0;
        }
      }
      return $"p{player}:hq{headquarters}/b{buildings}/u{Army(player, true).Count}/e{Engineers(player).Count}/i{s.Stock.Iron.AsInt}/w{s.Stock.Wood.AsInt}/s{s.Stock.Steel.AsInt}";
    }

    static string MinuteRow(string map, int run, int minute, int player) {
      var s = StateOf(player);
      return string.Join(",", map, run.ToString(), minute.ToString(), player.ToString(), FactionOf(player).FactionId,
        F(s.Earned.Iron.AsFloat), F(s.Earned.Wood.AsFloat), F(s.Earned.Steel.AsFloat), F(s.Earned.Power.AsFloat),
        F(s.PowerRate.AsFloat), F(s.OutageSeconds.AsFloat), s.OutageCount.ToString(),
        F(s.PopulationUsed.AsFloat), s.UnitsProduced.ToString(), s.UnitsLost.ToString(), s.BuildingsLost.ToString(), (s.Defeated ? "1" : "0"));
    }

    /// <summary>주요 건물과 유닛이 처음 나타난 시각을 진영별로 남긴다.</summary>
    static void RecordFirsts(Dictionary<string, float> firsts, int players, float seconds) {
      void Mark(string key) {
        if (firsts.ContainsKey(key) == false) {
          firsts[key] = seconds;
        }
      }
      foreach (var pair in Verified.GetComponentIterator<Building>()) {
        if (pair.Component.ConstructionRemaining <= Photon.Deterministic.FP._0) {
          var role = Verified.FindAsset(pair.Component.Spec).Role;
          if (role != BuildingRole.Headquarters) {
            Mark($"{FactionOf(pair.Component.Owner).FactionId[0]}:{role}");
          }
        }
      }
      foreach (var pair in Verified.GetComponentIterator<Unit>()) {
        var spec = Verified.FindAsset(pair.Component.Spec);
        if (spec.Size >= UnitSize.Medium) {
          Mark($"{FactionOf(pair.Component.Owner).FactionId[0]}:{spec.Size}{(spec.IsAir ? "Air" : "")}");
        }
      }
    }
  }
}
