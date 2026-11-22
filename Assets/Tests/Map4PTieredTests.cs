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
      for (var player = 0; player < 4; player++) {
        previous[player] = Verified.Get<Transform3D>(tanks[player]).Position;
        Assert.Less(FactoryWarsUtil.FlatDistanceSqr(previous[player], V(spawns[player].Item3, spawns[player].Item4)).AsFloat, 4f, $"player {player} test tank spawn");
        goals[player] = gates.OrderBy(gate => RouteLength(Verified, previous[player], gate)).First();
        planned[player] = RouteLength(Verified, previous[player], goals[player]);
      }
      var start = Verified.Number;
      for (var player = 0; player < 4; player++) {
        Game.SendCommand(player, new MoveUnitsCommand { Units = new[] { tanks[player] }, Target = goals[player] });
      }
      yield return WaitFor(() => {
        for (var player = 0; player < 4; player++) {
          if (finished[player] != 0) continue;
          Assert.IsTrue(Verified.Exists(tanks[player]), $"player {player} tank destroyed");
          var position = Verified.Get<Transform3D>(tanks[player]).Position;
          actual[player] += (position - previous[player]).Magnitude.AsFloat;
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
      }
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
        camera.orthographicSize = (map.BoundsMax.X - map.BoundsMin.X).AsFloat * 0.5f;
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
      var renderers = blocks.SelectMany(b => b.GetComponentsInChildren<MeshRenderer>()).ToList();
      var report = new System.Collections.Generic.List<string> { $"Map4P v4 pilot audit {iteration}" };
      var cities = blocks.Where(b => b.name.StartsWith("City_")).OrderBy(b => (b.transform.position - start).sqrMagnitude).ToList();
      report.Add($"blocks: {blocks.Count} (cities {cities.Count})");
      Assert.Greater(cities.Count, 0, "city pilot blocks placed");
      // 도로 옆 블록: 가운데에서 12m 안에 간선 도로 칸(R)이 있는 가장 가까운 블록.
      bool NextToRoad(GameObject b) {
        var p = b.transform.position;
        foreach (var (dx, dz) in new[] { (12f, 0f), (-12f, 0f), (0f, 12f), (0f, -12f) }) {
          var c = Mathf.FloorToInt((p.x + dx + 320f) / 8f);
          var r = Mathf.FloorToInt((320f - (p.z + dz)) / 8f);
          if (GridMapView.Cell(map, r, c) == 'R') return true;
        }
        return false;
      }
      var cityClose = cities.FirstOrDefault(b => b.transform.Find("Buildings/Apartment4") != null && NextToRoad(b)) ?? cities[0];
      var cityRoad = cities.Where(NextToRoad)
        .OrderByDescending(b => cities.Count(other => (other.transform.position - b.transform.position).sqrMagnitude < 36f * 36f))
        .FirstOrDefault() ?? cityClose;
      var rts = Controller().Camera;
      camera.aspect = 16f / 9f;
      foreach (var (name, focus, height) in new[] {
          ("base_close", new Vector2(corner.x + 49f, corner.z + 39f), 62f),
          ("base_wide", new Vector2(-corner.x - 75f, -corner.z - 71f), 108f),
          ("city_close", new Vector2(cityClose.transform.position.x, cityClose.transform.position.z), 35f),
          ("city_road", new Vector2(cityRoad.transform.position.x, cityRoad.transform.position.z - 6f), 62f),
          ("compound", new Vector2(0f, -5f), 155f) }) {
        rts.MoveTo(focus);
        rts.Height = height;
        rts.Apply();
        yield return WaitFrames(Fps / 2);
        CaptureCameraPng(camera, Path.Combine(dir, $"{name}_{iteration}.png"), 1920, 1080);
        var planes = GeometryUtility.CalculateFrustumPlanes(camera);
        var inView = renderers.Where(renderer => GeometryUtility.TestPlanesAABB(planes, renderer.bounds)).ToList();
        report.Add($"frustum {name}: height {rts.Height:0}m, {inView.Count} renderers, {PilotTriangles(inView)} triangles");
      }

      // ---- 금지 검사(시범 구역: 본진 블록 4 + 도시 블록) ----
      var items = blocks.SelectMany(SolidItems).ToList();
      bool Flat(SceneAudit.Item i) => i.Bounds.size.y < 1.0f && i.Bounds.size.x * i.Bounds.size.z > 20f;
      var overlaps = SceneAudit.Overlaps(items, 0.15f, (a, b) => Flat(a));
      var floating = SceneAudit.FloatingOrSunk(items, (x, z) => TerrainKit.SurfaceHeight(map, x, z) + 0.03f);
      var keepOut = new System.Collections.Generic.List<(string, Rect)>();
      for (var r = 0; r < 80; r++) {
        for (var c = 0; c < 80; c++) {
          var cell = GridMapView.Cell(map, r, c);
          if (cell == 'R' || cell == 's' || cell == 'G' || cell == 'P') {
            keepOut.Add(($"road({r},{c})", new Rect(-320f + 8f * c + 0.3f, 320f - 8f * (r + 1) + 0.3f, 7.4f, 7.4f)));
          }
        }
      }
      // 채집 길: 본진 자원(1·2단) → 가장 가까운 시작 위치, 폭 4m(자원 둘레 6m·사령부 둘레 8m는 뺀다).
      foreach (var node in map.ResourceNodes.Where(n => n.Tier <= 2)) {
        var p = node.Shape.Center.ToUnityVector3();
        var home = map.StartPositions.OrderBy(sp => (sp.ToUnityVector3() - p).sqrMagnitude).First().ToUnityVector3();
        var way = (new Vector2(home.x, home.z) - new Vector2(p.x, p.z)).normalized;
        keepOut.AddRange(SceneAudit.Corridor($"harvest{p.x:0},{p.z:0}", new Vector2(p.x, p.z) + way * 6f, new Vector2(home.x, home.z) - way * 8f, 4f));
      }
      var intrusions = SceneAudit.Intrusions(items, keepOut);
      // 건설 부지: 휴전 건설 구역 안인데 건설 금지 구역(장식 자리)·지형 벽 밖에 놓인 장식.
      var plots = new System.Collections.Generic.List<string>();
      foreach (var item in items) {
        var c = new FPVector3(FP.FromFloat_UNSAFE(item.Bounds.center.x), 0, FP.FromFloat_UNSAFE(item.Bounds.center.z));
        if (map.BaseAreas.Any(a => a.Distance(c) <= FP._0) == false) continue;
        if (map.NoBuildZones.Any(z => z.Distance(c) <= FP._0)) continue;
        if (map.Terrain.Any(t => t.Shape.Distance(c).AsFloat < 2.5f)) continue;
        plots.Add($"{item} on build plot");
      }
      // Boundary caps follow the retaining edge by design. Only freestanding props are tested for decorative rows.
      var rows = SceneAudit.RegularRows(items.Where(i => i.Name.Contains("/PerimeterWall/") == false).ToList(), maxSpacing: 24f);
      var flat = new System.Collections.Generic.List<string>();
      foreach (var name in new[] { "base_close", "base_wide", "city_close", "city_road", "compound" }) {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(Path.Combine(dir, $"{name}_{iteration}.png")));
        var ratio = SceneAudit.FlatColorRatio(tex);
        report.Add($"flat-colour {name}: {ratio:P1}");
        if (ratio > SceneAudit.FlatColorLimit) flat.Add($"{name} {ratio:P1}");
        Object.Destroy(tex);
      }
      // 빈 구역: 도시 블록마다(블록 안 전체), 본진은 (a) 그대로, (b) 건설 부지를 목적 있는 칸으로 보고.
      var empty = new System.Collections.Generic.List<string>();
      foreach (var city in cities) {
        var p = city.transform.position;
        var own = SolidItems(city).Select(i => i.Bounds).ToList();
        var side = SceneAudit.LargestEmptySquare(new Rect(p.x - 8f, p.z - 8f, 16f, 16f), own, (x, z) => true, 1f);
        if (side >= SceneAudit.EmptyLimit) empty.Add($"{city.name} empty {side:0}m");
      }
      var baseBlock = blocks.First(b => b.name == "Base0");
      var baseItems = SolidItems(baseBlock).Select(i => i.Bounds).ToList();
      foreach (var node in map.ResourceNodes.Where(n => n.Tier <= 2)) {
        baseItems.Add(new Bounds(node.Shape.Center.ToUnityVector3(), new Vector3(8f, 4f, 8f)));
      }
      baseItems.Add(new Bounds(start, new Vector3(14f, 4f, 14f)));
      bool InBase(float x, float z) => map.HighGroundAt(new FPVector3(FP.FromFloat_UNSAFE(x), 0, FP.FromFloat_UNSAFE(z))) >= 0 && x < corner.x + 175f && z < corner.z + 175f;
      bool BuildPlot(float x, float z) {
        var c = new FPVector3(FP.FromFloat_UNSAFE(x), 0, FP.FromFloat_UNSAFE(z));
        return map.BaseAreas.Any(a => a.Distance(c) <= FP._0) && map.NoBuildZones.Any(zn => zn.Distance(c) <= FP._0) == false;
      }
      var area = new Rect(corner.x, corner.z, 175f, 175f);
      var rawEmpty = SceneAudit.LargestEmptySquare(area, baseItems, InBase);
      var plotEmpty = SceneAudit.LargestEmptySquare(area, baseItems, (x, z) => InBase(x, z) && BuildPlot(x, z) == false, out var clear);
      report.Add($"base empty square: raw {rawEmpty:0}m, outside build plot {plotEmpty:0}m at local ({clear.center.x - corner.x:0},{clear.center.y - corner.z:0}) (limit {SceneAudit.EmptyLimit:0.0}m)");
      if (plotEmpty >= SceneAudit.EmptyLimit) empty.Add($"base (outside build plot) empty {plotEmpty:0}m");
      var storeys = cities.SelectMany(c => c.GetComponentsInChildren<Transform>().Where(t => t.name == "Ground"))
        .Select(t => (t.parent.name, SceneAudit.BoundsOf(t.gameObject).size.y)).Distinct().ToList();
      var scale = SceneAudit.ScaleSanity(storeys, new[] { ("alley s", 8f), ("main road R", 16f) });
      var magenta = blocks.SelectMany(b => b.GetComponentsInChildren<Renderer>())
        .Where(r => r.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name.Contains("InternalError"))).Select(r => r.name).ToList();
      void Section(string title, System.Collections.Generic.List<string> hits) {
        report.Add($"{title}: {(hits.Count == 0 ? "PASS" : $"FAIL {hits.Count}")}");
        report.AddRange(hits.Take(30).Select(h => "  " + h));
      }
      Section("overlap", overlaps);
      Section("floating/sunk", floating);
      Section("road/harvest intrusion", intrusions);
      Section("build-plot intrusion", plots);
      Section("regular rows", rows);
      Section("flat colour > 30%", flat);
      Section("empty > 8 units", empty);
      Section("scale", scale);
      Section("magenta/missing material", magenta);
      report.Add($"pilot renderers {renderers.Count}, triangles {PilotTriangles(renderers)}, distinct materials {renderers.SelectMany(r => r.sharedMaterials).Distinct().Count()}");
      foreach (var b in blocks.Where(b => b.name == "Base0" || b == cityClose)) {
        var rs = b.GetComponentsInChildren<MeshRenderer>();
        report.Add($"  {b.name}: renderers {rs.Length}, triangles {PilotTriangles(rs)}");
      }
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
