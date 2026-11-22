namespace FactoryWars.Tests {
  using System.Collections;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 2026-11-22 표준 맵(Map4P) 참고 그림 재구성 검사: 모서리 테라스 본진(비탈 3 = 휴전 관문), S자 운하 + 다리 6, 대각 간선·외곽 순환로,
  /// 중앙 팔각 단지(암시장), 변 가운데 자원지, 능선(고지대)·참호(은폐), 180도 대칭과 경로 공정성.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static Navigation.FindPathResult GroundRoute(Frame frame, FPVector3 from, FPVector3 to) {
      var rules = FactoryWarsUtil.Rules(frame);
      var ground = frame.Map.GetNavMesh(rules.NavMeshName);
      Assert.IsTrue(FactoryWarsUtil.FindNavPointNear(frame, rules.NavMeshName, from, 6, out var a), $"출발점 {from}");
      Assert.IsTrue(FactoryWarsUtil.FindNavPointNear(frame, rules.NavMeshName, to, 6, out var b), $"도착점 {to}");
      var mask = *frame.NavMeshRegionMask;
      return frame.Navigation.FindPath((FrameThreadSafe)frame, a, b, ground, in mask);
    }

    static Navigation.FindPathResult.Code GroundPath(Frame frame, FPVector3 from, FPVector3 to) => GroundRoute(frame, from, to).Result;

    static float RouteLength(Frame frame, FPVector3 from, FPVector3 to) {
      var route = GroundRoute(frame, from, to);
      Assert.AreEqual(Navigation.FindPathResult.Code.Success, route.Result, $"경로 {from}→{to}");
      var length = 0f;
      for (var i = 0; i + 1 < route.PathSize; i++) {
        length += (route.Path[i + 1].Point - route.Path[i].Point).Magnitude.AsFloat;
      }
      return length;
    }

    static bool OnGroundNavMesh(Frame frame, FPVector3 point) {
      var ground = frame.Map.GetNavMesh(FactoryWarsUtil.Rules(frame).NavMeshName);
      var mask = NavigationMask(frame);
      return ground.Contains(point, mask, true);
    }

    static NavMeshRegionMask NavigationMask(Frame frame) => *frame.NavMeshRegionMask;

    static FPVector3 Pos(float x, float z) => new FPVector3(FP.FromFloat_UNSAFE(x), 0, FP.FromFloat_UNSAFE(z));

    static FPVector3 Rot(FPVector3 p, int quarterTurns) {
      for (var i = 0; i < quarterTurns; i++) {
        p = new FPVector3(-p.Z, 0, p.X);
      }
      return p;
    }

    // 2026-11-22: 예전 참고 그림 배치(320m, 테라스·능선·참호·고리 운하)를 검사하던 Map4P_LayoutSymmetryCanalBridgesTiersAndTruce는
    // 사용자가 칸 지도 v4(640m)로 바꾸면서 기준이 사라져 아래 Map4P_v4_* 시험으로 대체했다(SETUP-REPORT "Map4P v4 시범").

    static char[,] DesignGrid() {
      var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "maps", "Map4P_grid_v4.csv"));
      var lines = File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToArray();
      var grid = new char[80, 80];
      for (var r = 0; r < 80; r++) {
        var cells = lines[r].Split(',');
        for (var c = 0; c < 80; c++) grid[r, c] = cells[c].Trim()[0];
      }
      return grid;
    }

    static FPVector3 CellCenter(int r, int c) => Pos(-320f + 8f * c + 4f, 320f - 8f * r - 4f);

    /// <summary>판정이 같은 칸 종류(X·Y·K·L·w·M은 모두 통행 가능한 땅).</summary>
    static char SimClass(char cell) => "XYKLwM".IndexOf(cell) >= 0 ? '.' : cell;

    [UnityTest, Timeout(900000)]
    public IEnumerator Map4P_v4_GridMatchesCsv() {
      yield return StartMatch(Map4PScene, 4, truce: 5);
      yield return WaitFor(BarrierOpen, 90f, "truce end for baseline traversability");
      var frame = Verified;
      var map = FactoryWarsUtil.MapInfo(frame);
      var grid = DesignGrid();
      Assert.AreEqual(80, map.CellGridSize, "80x80 cells");
      Assert.AreEqual(8f, map.CellGridCell.AsFloat, 0.001f, "8 m per cell");
      Assert.AreEqual(-320f, map.BoundsMin.X.AsFloat, 0.01f);
      Assert.AreEqual(320f, map.BoundsMax.Y.AsFloat, 0.01f);
      Assert.AreEqual(320, map.GroundGridSize, "2 m ground grid over 640 m");
      Assert.AreEqual(2f, map.GroundGridCell.AsFloat, 0.001f);
      var ground = frame.Map.GetNavMesh(FactoryWarsUtil.Rules(frame).NavMeshName);
      var active = NavigationMask(frame);
      var connected = ReachableNow(ground, map.StartPositions[0]);
      var offsets = new[] { -3.5f, -2f, 0f, 2f, 3.5f };
      var resourceCentres = 0;
      var wrongNav = new System.Collections.Generic.List<string>();
      for (var r = 0; r < 80; r++) {
        for (var c = 0; c < 80; c++) {
          Assert.AreEqual(grid[r, c], (char)map.CellGrid[r * 80 + c], $"cell ({r},{c}) matches CSV");
          var blocked = grid[r, c] == 'E' || grid[r, c] == 'H' || grid[r, c] == 'W';
          var point = CellCenter(r, c);
          Assert.AreEqual(grid[r, c] == 'P', map.Ramps.Any(shape => shape.Distance(point) <= FP._0), $"ramp occupancy ({r},{c})");
          if (OnGroundNavMesh(frame, point) == blocked) {
            var nearestResource = map.ResourceNodes.Min(n => n.Shape.Distance(point).AsFloat);
            var nearestWall = map.Terrain.Where(t => t.Kind == TerrainKind.Wall).Min(t => t.Shape.Distance(point).AsFloat);
            // A mine/grove and the bake's agent clearance can occupy the centre of a B or M cell.
            // In that case require connected ground in the same cell, never a neighbouring tile.
            if (!blocked && (grid[r, c] == 'B' || grid[r, c] == 'M') && nearestResource <= 0.6f &&
                offsets.Any(x => offsets.Any(z => {
                  var sample = point + Pos(x, z);
                  return ground.Contains(sample, active, true) && connected.Contains(ground.FindTriangle2D(sample, active, true));
                }))) {
              resourceCentres++;
              continue;
            }
            wrongNav.Add($"({r},{c}){grid[r, c]} resource={nearestResource:F1} wall={nearestWall:F1}");
          }
        }
      }
      Assert.IsEmpty(wrongNav, $"E/H/W centres must be blocked; other centres must be walkable except for resource footprints: {string.Join(" ", wrongNav.Take(40))}");
      Debug.Log($"Map4P v4 traversability: {resourceCentres} centres occupied by resources have connected ground inside their cells");
      var bridges = 0;
      for (var r = 0; r < 80; r++) {
        for (var c = 0; c < 80; c++) {
          if (grid[r, c] == 'G' && (c == 0 || grid[r, c - 1] != 'G')) bridges++;
        }
      }
      Assert.AreEqual(bridges, map.Bridges.Length, "one bridge per G run");
      Assert.AreEqual(6, map.Bridges.Length, "six canal bridges");
      var waterNav = frame.Map.GetNavMesh(FactoryWarsUtil.Rules(frame).WaterNavMeshName);
      Assert.IsNotNull(waterNav, "water NavMesh");
      foreach (var bridge in map.Bridges) {
        Assert.IsTrue(waterNav.Contains(bridge.Center, active, true), $"water NavMesh under bridge at {bridge.Center}");
      }
      // 고지대: 모서리 본진(B·p·v) 칸 중심은 고지대, 맵 가장자리 B 띠는 아니다.
      var start = map.StartPositions[0];
      Assert.AreEqual(TieredTerrain.MainHeight, TerrainKit.SurfaceHeight(map, start.X.AsFloat, start.Z.AsFloat), 0.01f, "base plateau");
      Assert.Less(map.HighGroundAt(CellCenter(40, 2)), 0, "edge sidewalk strip is not high ground");
      Assert.AreEqual(4, map.Ramps.Length, "one ramp block per base");
      Assert.AreEqual(4, map.TruceGates.Length, "truce gate on each ramp");
      foreach (var ramp in map.Ramps) {
        var center = ramp.Center.ToUnityVector3();
        var slope = -new Vector2(center.x, center.z).normalized;
        var before = TieredTerrain.MainHeight;
        for (var t = -13f; t <= 16f; t += .5f) {
          var point = new Vector2(center.x, center.z) + slope * t;
          var current = TieredTerrain.SurfaceHeight(map, point.x, point.y);
          Assert.LessOrEqual(current, before + 0.02f, $"ramp {center} monotonically descends");
          Assert.LessOrEqual(before - current, 0.30f, $"ramp {center} has no lip at {point}");
          before = current;
        }
        Assert.AreEqual(0, before, 0.03f, $"ramp {center} joins the road");
      }
      Assert.IsTrue(map.HasTrader && map.TraderPosition == FPVector3.Zero, "trader at the compound centre");
      Assert.AreEqual(4 * 5 + 8, map.ResourceNodes.Length, "per base 3 tier-1 + 2 tier-2, 4 outposts x 2");
    }

    [UnityTest, Timeout(900000)]
    public IEnumerator Map4P_v4_SymmetryFairnessAndReach() {
      yield return StartMatch(Map4PScene, 4, truce: 25);
      var frame = Verified;
      var map = FactoryWarsUtil.MapInfo(frame);
      var grid = DesignGrid();
      // 칸 지도 구조(통행 판정 종류)는 180도 회전 대칭. X·Y·K·L 차이는 화면만 다르다.
      for (var r = 0; r < 80; r++) {
        for (var c = 0; c < 80; c++) {
          Assert.AreEqual(SimClass(grid[r, c]), SimClass(grid[79 - r, 79 - c]), $"180 symmetry ({r},{c})");
        }
      }
      for (var seat = 0; seat < 4; seat++) {
        Assert.Less((map.StartPositions[seat] - Rot(map.StartPositions[0], seat)).Magnitude.AsFloat, 0.01f, $"seat {seat} start is the turned seat 0 start");
      }
      yield return WaitFor(BarrierOpen, 90f, "truce end");
      frame = Verified;
      // The baked river also includes the water below bridges. Compare occupancy instead of merge rectangles.
      for (var r = 0; r < 80; r++) {
        for (var c = 0; c < 80; c++) {
          var point = CellCenter(r, c);
          var cell = grid[r, c];
          Assert.AreEqual(cell == 'W' || cell == 'G', map.River.Any(s => s.Distance(point) <= FP._0), $"canal occupancy ({r},{c})");
          Assert.AreEqual(cell == 'G', map.Bridges.Any(s => s.Distance(point) <= FP._0), $"bridge occupancy ({r},{c})");
          if (cell == 'W') Assert.IsFalse(OnGroundNavMesh(frame, point), $"canal ({r},{c}) blocks ground");
          if (cell == 'G') Assert.IsTrue(OnGroundNavMesh(frame, point), $"bridge ({r},{c}) walkable");
        }
      }
      // 네 본진 모두 중앙 요새(가장 가까운 출입구)와 모든 외곽 거점에 닿는다. 공정성: 경로 길이 ±5%.
      var gates = CompoundEntries();
      var outposts = map.ResourceNodes.Where(n => n.Tier == 3).ToArray();
      Assert.AreEqual(8, outposts.Length);
      var compound = new float[4];
      var nearOutpost = new float[4];
      for (var seat = 0; seat < 4; seat++) {
        var from = map.StartPositions[seat];
        compound[seat] = gates.Min(g => RouteLength(frame, from, g));
        foreach (var node in outposts) {
          Assert.AreEqual(Navigation.FindPathResult.Code.Success, GroundPath(frame, from, node.Shape.Approach(FPVector3.Zero, 3)), $"seat {seat} reaches outpost {node.Shape.Center}");
        }
        nearOutpost[seat] = outposts.Min(n => RouteLength(frame, from, n.Shape.Approach(FPVector3.Zero, 3)));
      }
      Debug.Log($"Map4P v4 base->compound gate: {string.Join(", ", compound.Select(l => l.ToString("F1")))}; base->nearest outpost: {string.Join(", ", nearOutpost.Select(l => l.ToString("F1")))}");
      foreach (var lengths in new[] { compound, nearOutpost }) {
        Assert.LessOrEqual(lengths.Max() / lengths.Min(), 1.05f, $"route fairness {string.Join(", ", lengths.Select(l => l.ToString("F1")))}");
      }
    }

    static FPVector3[] CompoundEntries() => new[] { Pos(0f, 56f), Pos(56f, 0f), Pos(0f, -56f), Pos(-56f, 0f) };

    [UnityTest, Timeout(900000)]
    public IEnumerator Map4P_v4_CameraBoundsAndVehicleTravel() {
      const string tankId = "ff_eagle_tank";
      var spawns = Enumerable.Range(0, 4).Select(seat => {
        var spawn = Rot(Pos(-236f, -196f), seat);
        return (seat, tankId, (int)spawn.X, (int)spawn.Z);
      }).ToArray();
      yield return StartMatch(Map4PScene, 4, truce: 5, mirrorFaction: 0, units: spawns, seed: 42);
      yield return WaitForHud();
      yield return WaitFor(BarrierOpen, 90f, "truce end before travel");

      var rts = Controller().Camera;
      var camera = rts.GetComponent<Camera>();
      camera.aspect = 16f / 9f;
      Assert.AreEqual(new Vector2(-320f, -320f), rts.BoundsMin);
      Assert.AreEqual(new Vector2(320f, 320f), rts.BoundsMax);
      Assert.IsTrue(rts.KeepViewportInside);
      rts.Zoom(10000f);
      Assert.AreEqual(RtsCamera.MinHeight, rts.Height, 0.01f);
      rts.Zoom(0.0001f);
      var maximum = Mathf.Min(RtsCamera.MaxHeight, rts.FitHeight());
      Assert.AreEqual(maximum, rts.Height, 0.01f);
      Assert.Greater(maximum, VisualProfile.Current.MaxHeight);
      foreach (var offset in new[] { new Vector2(-1000f, -1000f), new Vector2(1000f, 1000f) }) {
        rts.MoveTo(offset);
        foreach (var x in new[] { 0f, 1f }) {
          foreach (var y in new[] { 0f, 1f }) {
            var ray = camera.ViewportPointToRay(new Vector3(x, y));
            Assert.IsTrue(new UnityEngine.Plane(Vector3.up, Vector3.zero).Raycast(ray, out var distance));
            var ground = ray.GetPoint(distance);
            Assert.That(ground.x, Is.InRange(-320.05f, 320.05f));
            Assert.That(ground.z, Is.InRange(-320.05f, 320.05f));
          }
        }
      }
      Debug.Log($"Map4P v4 camera: 640m, 16:9, zoom {RtsCamera.MinHeight:F1}–{maximum:F1}m");

      var tanks = Enumerable.Range(0, 4).Select(player => UnitsNamed(player, tankId)
        .OrderBy(t => FactoryWarsUtil.FlatDistanceSqr(Verified.Get<Transform3D>(t).Position, V(spawns[player].Item3, spawns[player].Item4))).First()).ToArray();
      var gates = CompoundEntries();
      var goals = new FPVector3[4];
      var planned = new float[4];
      var actual = new float[4];
      var finished = new int[4];
      var previous = new FPVector3[4];
      var ramps = FactoryWarsUtil.MapInfo(Verified).Ramps;
      var crossedRamps = new bool[4];
      var stalledTicks = new int[4];
      var slowestRamp = new int[4];
      var lastTick = new int[4];
      for (var player = 0; player < 4; player++) {
        previous[player] = Verified.Get<Transform3D>(tanks[player]).Position;
        Assert.Less(FactoryWarsUtil.FlatDistanceSqr(previous[player], V(spawns[player].Item3, spawns[player].Item4)).AsFloat, 4f, $"player {player} test tank spawn");
        goals[player] = gates.OrderBy(gate => RouteLength(Verified, previous[player], gate)).First();
        planned[player] = RouteLength(Verified, previous[player], goals[player]);
      }
      var start = Verified.Number;
      System.Array.Fill(lastTick, start);
      for (var player = 0; player < 4; player++) {
        Game.SendCommand(player, new MoveUnitsCommand { Units = new[] { tanks[player] }, Target = goals[player] });
      }
      yield return WaitFor(() => {
        for (var player = 0; player < 4; player++) {
          if (finished[player] != 0) continue;
          Assert.IsTrue(Verified.Exists(tanks[player]), $"player {player} tank destroyed");
          var position = Verified.Get<Transform3D>(tanks[player]).Position;
          var step = (position - previous[player]).Magnitude.AsFloat;
          actual[player] += step;
          var ramp = ramps[FactoryWarsUtil.SeatOf(Verified, player)];
          if (ramp.Distance(position) <= FP._0) {
            crossedRamps[player] = true;
            if (step < .04f) stalledTicks[player] += Verified.Number - lastTick[player];
            else stalledTicks[player] = 0;
            slowestRamp[player] = Mathf.Max(slowestRamp[player], stalledTicks[player]);
          }
          lastTick[player] = Verified.Number;
          previous[player] = position;
          if (FactoryWarsUtil.FlatDistanceSqr(position, goals[player]) > 9) continue;
          finished[player] = Verified.Number;
          var seat = FactoryWarsUtil.SeatOf(Verified, player);
          Debug.Log($"Map4P v4 travel seat {seat}: {planned[player]:F1}m planned; {actual[player]:F1}m sampled; {(finished[player] - start) / (float)Fps:F2}s; goal {goals[player]}");
        }
        return finished.All(tick => tick != 0) || Verified.Number - start >= 200 * Fps;
      }, 180f, "four vehicles enter the compound");
      for (var player = 0; player < 4; player++) {
        Assert.Greater(finished[player], 0, $"player {player} stopped at {previous[player]} entering {goals[player]}");
        Assert.IsTrue(crossedRamps[player], $"player {player} drove across its ramp");
        Assert.Less(slowestRamp[player], Fps, $"player {player} paused for a second on its ramp");
      }
      Debug.Log($"Map4P v4 stationary ticks at ramp by player: {string.Join(", ", slowestRamp)}");
    }

    static string ScreenshotRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "map4p-v4"));

    /// <summary>
    /// Map4P v4 시범 캡처(게임 카메라, 1920x1080) + 금지 검사(SceneAudit). FW_MAP_ITER(기본 v1)로 반복 번호를 붙인다.
    /// 결과: screenshots/map4p-v4/{base_close,base_wide,city_close,city_road,compound}_vN.png, map_top_vN.png, audit_vN.txt.
    /// </summary>
    [UnityTest, Explicit, Timeout(900000)]
    public IEnumerator Map4P_CaptureTopAndGameplayViews() {
      var iteration = System.Environment.GetEnvironmentVariable("FW_MAP_ITER");
      iteration = string.IsNullOrEmpty(iteration) ? "v1" : iteration;
      var dir = ScreenshotRoot();
      Directory.CreateDirectory(dir);
      yield return StartMatch(Map4PScene, 4, truce: 8);
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) {
        doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      }
      yield return WaitFor(BarrierOpen, 90f, "open truce ramps for gameplay capture");
      Time.timeScale = 1f;
      yield return WaitFrames(Fps);
      var map = FactoryWarsUtil.MapInfo(Verified);
      Assert.IsTrue(GridMapView.Uses(map), "grid map view");
      var camera = Camera.main;
      var position = camera.transform.position;
      var rotation = camera.transform.rotation;
      var clip = camera.farClipPlane;
      try {
        camera.orthographic = true;
        camera.orthographicSize = (map.BoundsMax.X - map.BoundsMin.X).AsFloat * 0.5f + 42f;
        camera.farClipPlane = 1500f;
        camera.transform.SetPositionAndRotation(new Vector3(0f, 600f, 0f), Quaternion.Euler(90f, 0f, 0f));
        CaptureCameraPng(camera, Path.Combine(dir, $"map_top_{iteration}.png"), 1600, 1600);
      } finally {
        camera.orthographic = false;
        camera.transform.SetPositionAndRotation(position, rotation);
        camera.farClipPlane = clip;
      }
      var corner = GridMapView.BaseCorner;
      var start = map.StartPositions[0].ToUnityVector3();
      var blocks = GridMapView.PlacedBlocks.Where(b => b != null).ToList();
      var dress = GridDress.Placed.Where(b => b != null).ToList();
      var report = new System.Collections.Generic.List<string> { $"Map4P v4 dress audit {iteration}" };
      Assert.Greater(dress.Count, 500, "dress blocks placed");
      string KindName(GameObject b) => b.name.Substring(0, b.name.IndexOf('_'));
      string Variant(GameObject b) => b.name.Substring(0, b.name.IndexOf('@'));
      report.Add($"dress blocks: {dress.Count} - " + string.Join(", ", dress.GroupBy(KindName).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()} ({g.Select(Variant).Distinct().Count()} variants)")));
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;
      var ramp = map.Ramps[0].Center.ToUnityVector3();
      var mapRoot = dress[0].transform.parent.parent;
      var all = mapRoot.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToList();
      var shots = new (string Name, Vector2 Focus, float Height)[] {
        ("base_wide", new Vector2(-corner.x - 75f, -corner.z - 71f), 108f),
        ("base_close", new Vector2(corner.x + 49f, corner.z + 39f), 62f),
        ("base_ramp", new Vector2(ramp.x, ramp.z - 4f), 50f),
        ("diagonal_road", new Vector2(-100f, -104f), 62f),
        ("city", new Vector2(-168f, 92f), 62f),
        ("city_close", new Vector2(-150f, 178f), 38f),
        ("canal_bridge", new Vector2(-14f, 116f), 62f),
        ("compound", new Vector2(0f, -5f), 132f),
        ("compound_close", new Vector2(0f, 36f), 70f),
        ("outer_edge", new Vector2(108f, 310f), 76f),
        ("outer_west", new Vector2(-298f, 150f), 76f),
        ("rubble", new Vector2(-176f, 4f), 55f),
        ("outpost", new Vector2(-282f, -6f), 70f),
      };
      report.Add("view | height | renderers | draw calls (renderer x submesh + shadow casters) | triangles | low estimate (lodBias 0.3, no shadows, no skyline) draws / tris");
      foreach (var (name, focus, height) in shots) {
        rts.MoveTo(focus);
        rts.Height = height;
        rts.Apply();
        yield return WaitFrames(Fps / 2);
        CaptureCameraPng(camera, Path.Combine(dir, $"{name}_{iteration}.png"), 1920, 1080);
        var planes = GeometryUtility.CalculateFrustumPlanes(camera);
        var tan = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        bool LodVisible(MeshRenderer r, float bias) {
          var group = r.GetComponent<LODGroup>();
          if (group == null) return true;
          var distance = Vector3.Distance(camera.transform.position, r.bounds.center);
          var relative = group.size / (2f * distance * tan) * bias;
          var at = r.name.IndexOf('_');
          var factor = bias < 0.35f && at > 0 ? GridDress.LowCullFactor(r.name.Substring(0, at)) : 1f;
          return relative >= group.GetLODs()[0].screenRelativeTransitionHeight * factor;
        }
        var inView = all.Where(r => GeometryUtility.TestPlanesAABB(planes, r.bounds)).ToList();
        var medium = inView.Where(r => LodVisible(r, QualitySettings.lodBias)).ToList();
        var lowSet = inView.Where(r => LodVisible(r, 0.3f) && !r.name.StartsWith("SK_")).ToList();
        int Draws(System.Collections.Generic.List<MeshRenderer> rs, bool shadows) => rs.Sum(r => r.sharedMaterials.Length) + (shadows ? rs.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) : 0);
        report.Add($"{name} | {rts.Height:0}m | {medium.Count} | {Draws(medium, true)} | {PilotTriangles(medium)} | {Draws(lowSet, false)} / {PilotTriangles(lowSet)}");
      }

      // ---- 금지 검사(블록 단위). 경계·담장·난간·스카이라인 조각은 맞대어 잇는 구조물이라 겹침·줄지음에서 뺀다.
      var boundary = new System.Collections.Generic.HashSet<string> { "E", "EL", "EI", "SK", "CW", "CP", "CT", "GR" };
      var cells = dress.Where(b => !boundary.Contains(KindName(b))).ToList();
      var items = cells.Select(b => new SceneAudit.Item { Name = b.name, Kind = Variant(b), Bounds = SceneAudit.BoundsOf(b) }).ToList();
      Bounds Footprint(GameObject b) {
        var slot = VisualCatalog.Current.Slots.First(s => s != null && s.Id == GridDress.Prefix + Variant(b));
        var size = b.transform.rotation * slot.Size;
        return new Bounds(b.transform.position + Vector3.up, new Vector3(Mathf.Abs(size.x), 2f, Mathf.Abs(size.z)));
      }
      // 블록 바닥 사각형(칸) 겹침·침범: 블록 칸 크기로 본다(처마·가로등 팔은 길 위로 나갈 수 있다).
      var footprints = cells.Select(b => new SceneAudit.Item { Name = b.name, Kind = Variant(b), Bounds = Footprint(b) }).ToList();
      var overlaps = SceneAudit.Overlaps(footprints, 0.15f);
      var floating = SceneAudit.FloatingOrSunk(items.Where(i => !i.Name.StartsWith("Q_") && !i.Name.StartsWith("M_")).ToList(), (x, z) => TerrainKit.SurfaceHeight(map, x, z) + 0.03f, 0.4f);
      var keepOut = new System.Collections.Generic.List<(string, Rect)>();
      for (var r = 0; r < 80; r++) {
        for (var c = 0; c < 80; c++) {
          var cell = GridMapView.Cell(map, r, c);
          if (cell == 'R' || cell == 's' || cell == 'G' || cell == 'P') keepOut.Add(($"road({r},{c})", new Rect(-320f + 8f * c + 0.3f, 320f - 8f * (r + 1) + 0.3f, 7.4f, 7.4f)));
        }
      }
      var intrusions = SceneAudit.Intrusions(footprints, keepOut);
      var rows = SceneAudit.RegularRows(items, maxSpacing: 24f);
      var flat = new System.Collections.Generic.List<string>();
      foreach (var (name, _, _) in shots) {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(Path.Combine(dir, $"{name}_{iteration}.png")));
        var ratio = SceneAudit.FlatColorRatio(tex);
        report.Add($"flat-colour {name}: {ratio:P1}");
        if (ratio > SceneAudit.FlatColorLimit) flat.Add($"{name} {ratio:P1}");
        Object.Destroy(tex);
      }
      // 빈 칸: 도시형 칸(H K L X Y)은 모두 블록이 있어야 한다.
      var empty = new System.Collections.Generic.List<string>();
      var dressed = new System.Collections.Generic.HashSet<(int, int)>();
      foreach (var b in dress) {
        var f = Footprint(b);
        for (var x = f.min.x + 4f; x < f.max.x; x += 8f)
          for (var z = f.min.z + 4f; z < f.max.z; z += 8f)
            dressed.Add((Mathf.FloorToInt((320f - z) / 8f), Mathf.FloorToInt((x + 320f) / 8f)));
      }
      for (var r = 0; r < 80; r++) for (var c = 0; c < 80; c++) {
        var cell = GridMapView.Cell(map, r, c);
        if ("HKLXY".IndexOf(cell) >= 0 && !dressed.Contains((r, c))) empty.Add($"undressed {cell} ({r},{c})");
      }
      // 중앙 요새 안 빈 사각형(엄폐물·건물·컨테이너·상인 기지 기준).
      var compoundItems = dress.Where(b => Mathf.Abs(b.transform.position.x) < 80f && Mathf.Abs(b.transform.position.z) < 80f).Select(SceneAudit.BoundsOf).ToList();
      compoundItems.Add(new Bounds(Vector3.zero, new Vector3(30f, 4f, 30f)));
      foreach (var (u, w) in new[] { (32f, 32f), (32f, -32f), (-32f, -32f), (-32f, 32f) }) compoundItems.Add(new Bounds(new Vector3(u, 1f, w), new Vector3(9f, 2f, 9f)));
      var compoundEmpty = SceneAudit.LargestEmptySquare(new Rect(-80f, -80f, 160f, 160f), compoundItems, (x, z) =>
        GridMapView.Cell(map, Mathf.FloorToInt((320f - z) / 8f), Mathf.FloorToInt((x + 320f) / 8f)) == 'C' && Mathf.Abs(x) > 9f && Mathf.Abs(z) > 9f, out var compoundClear);
      report.Add($"compound empty square (outside cross/gate roads |x|,|z| < 9): {compoundEmpty:0}m at ({compoundClear.center.x:0},{compoundClear.center.y:0}) (limit {SceneAudit.EmptyLimit:0.0}m)");
      if (compoundEmpty >= SceneAudit.EmptyLimit) empty.Add($"compound empty {compoundEmpty:0}m");
      // 본진(자리 0): 시설·자원·시작 위치 기준, 건설 부지·확장 부지 밖 빈 사각형.
      var baseBlock = blocks.First(b => b.name == "Base0");
      var baseItems = SolidItems(baseBlock).Select(i => i.Bounds).ToList();
      foreach (var node in map.ResourceNodes.Where(n => n.Tier <= 2)) baseItems.Add(new Bounds(node.Shape.Center.ToUnityVector3(), new Vector3(8f, 4f, 8f)));
      baseItems.Add(new Bounds(start, new Vector3(14f, 4f, 14f)));
      bool InBase(float x, float z) => map.HighGroundAt(new FPVector3(FP.FromFloat_UNSAFE(x), 0, FP.FromFloat_UNSAFE(z))) >= 0 && x < corner.x + 175f && z < corner.z + 175f;
      bool BuildPlot(float x, float z) {
        var c = new FPVector3(FP.FromFloat_UNSAFE(x), 0, FP.FromFloat_UNSAFE(z));
        return map.BaseAreas.Any(a => a.Distance(c) <= FP._0) && map.NoBuildZones.Any(zn => zn.Distance(c) <= FP._0) == false;
      }
      var area = new Rect(corner.x, corner.z, 175f, 175f);
      var reserved = map.Expansions.Single(site => site.Kind == ExpansionKind.IronForecourt && site.SeatMask == 1).Position.ToUnityVector3();
      var expansion = new Rect(reserved.x - 8f, reserved.z - 7f, 16f, 14f);
      var unassigned = SceneAudit.LargestEmptySquare(area, baseItems, (x, z) => InBase(x, z) && !BuildPlot(x, z) && !expansion.Contains(new Vector2(x, z)), out var unassignedArea);
      report.Add($"base unassigned empty square: {unassigned:0}m at local ({unassignedArea.center.x - corner.x:0},{unassignedArea.center.y - corner.z:0})");
      if (unassigned >= SceneAudit.EmptyLimit) empty.Add($"base (outside build and marked expansion plots) empty {unassigned:0}m");
      var magenta = dress.Concat(blocks).SelectMany(b => b.GetComponentsInChildren<Renderer>())
        .Where(r => r.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name.Contains("InternalError"))).Select(r => r.name).Distinct().ToList();
      void Section(string title, System.Collections.Generic.List<string> hits) {
        report.Add($"{title}: {(hits.Count == 0 ? "PASS" : $"FAIL {hits.Count}")}");
        report.AddRange(hits.Take(30).Select(h => "  " + h));
      }
      Section("overlap (block footprints)", overlaps);
      Section("floating/sunk", floating);
      Section("road intrusion (block footprints)", intrusions);
      Section("regular rows (same variant)", rows);
      Section("flat colour > 30%", flat);
      Section("empty > 8 units", empty);
      Section("magenta/missing material", magenta);
      report.Add($"map renderers {all.Count}, triangles {PilotTriangles(all)}, distinct materials {all.SelectMany(r => r.sharedMaterials).Distinct().Count()}");
      File.WriteAllLines(Path.Combine(dir, $"audit_{iteration}.txt"), report);
      Debug.Log(string.Join("\n", report));
    }

    static System.Collections.Generic.IEnumerable<SceneAudit.Item> SolidItems(GameObject block) =>
      SceneAudit.ItemsOf(block).Where(item => item.Name.EndsWith("/Paving") == false);

    static long PilotTriangles(System.Collections.Generic.IEnumerable<MeshRenderer> renderers) {
      return renderers.Select(r => r.GetComponent<MeshFilter>()).Where(f => f != null && f.sharedMesh != null)
        .Sum(filter => {
          long indices = 0;
          for (var i = 0; i < filter.sharedMesh.subMeshCount; i++) indices += filter.sharedMesh.GetIndexCount(i);
          return indices / 3;
        });
    }
  }
}
