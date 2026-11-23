namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using Photon.Deterministic;
  using Quantum;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// 칸 지도 → 맵 데이터 생성기(2026-11-22 Map4P v4, PROMPT-MAP4P-V4 3절). 사람이 고친 CSV가 구도의 기준이고
  /// AI는 좌표를 만들지 않는다. 칸 글자를 판정 모양(지형·도로·물·고지·비탈·관문·자원·확장·시작 위치)으로 바꾼다.
  ///
  /// 칸 규칙(8m 칸, 첫 줄 = 북쪽 끝):
  /// - E 경계: 통행 불가(Wall). H 도시 건물: 통행 불가(Wall, 칸 가장자리 0.6m 안쪽).
  /// - B·p·v(모서리 본진, p에서 이어진 칸): 고지대(+3.5m). 본진 둘레(경사로 P·경계 E 쪽 제외)는 옹벽(Wall).
  ///   맵 가장자리의 얇은 B 띠(순환로 바깥 보도)는 고지대가 아니라 중간 높이 보도다.
  /// - P 경사로: Ramps + 휴전 관문(자리마다 1개). W 수로: 지상 통행 불가(River). G 다리: ExtraBridges.
  /// - R·s·G·P: 도로(이동 속도). C·p(요새 안)·Y: 중앙 요새 안, 둘레는 담장(Wall)이고 동서남북 가운데 16m가 출입구다.
  /// - X·Y·K·L·w·M: 통행 가능한 땅. X(잔해)와 Y(컨테이너)는 칸 지도에서 동서가 서로 다르므로(122칸) 판정을 같게 두어 자리 공정성을 지킨다(화면만 다르다).
  /// - 자리 순서는 예전 표준 맵과 같다: 0 남서, 1 남동, 2 북동, 3 북서(반시계). 본진 안의 물건은 자리 0 좌표를 90도씩 돌려 만든다
  ///   (본진 삼각형은 대각선 대칭이라 회전 = 거울이다. 생성할 때 확인한다).
  /// </summary>
  public static partial class FactoryWarsSetup {
    public const int GridCells = 80;
    public const float GridCell = 8f;
    public const float GridHalf = GridCells * GridCell / 2f;
    /// <summary>사용자가 고치는 원본(작업 루트 design/maps). 없으면 저장소 안 사본을 쓴다.</summary>
    const string GridCsvDesign = "../design/maps/Map4P_grid_v6.csv";
    const string GridCsvRepo = "Tools/maps/Map4P_grid_v6.csv";

    public static string GridCsvPath => File.Exists(GridCsvDesign) ? GridCsvDesign : GridCsvRepo;

    public static char[,] ReadGrid(string path) {
      var lines = File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToArray();
      var grid = new char[GridCells, GridCells];
      for (var r = 0; r < GridCells; r++) {
        var cells = lines[r].Split(',');
        for (var c = 0; c < GridCells; c++) {
          grid[r, c] = cells[c].Trim()[0];
        }
      }
      return grid;
    }

    public static float CellX(float c) => -GridHalf + GridCell * c + GridCell / 2f;
    public static float CellZ(float r) => GridHalf - GridCell * r - GridCell / 2f;

    /// <summary>같은 칸 집합을 큰 직사각형으로 묶는다(가로 줄 → 같은 줄이 아래로 이어지면 합침). (r0, c0, r1, c1) 포함.</summary>
    static List<(int R0, int C0, int R1, int C1)> MergeCells(System.Func<int, int, bool> mask) {
      var used = new bool[GridCells, GridCells];
      var rects = new List<(int, int, int, int)>();
      for (var r = 0; r < GridCells; r++) {
        for (var c = 0; c < GridCells; c++) {
          if (used[r, c] || mask(r, c) == false) continue;
          var c1 = c;
          while (c1 + 1 < GridCells && used[r, c1 + 1] == false && mask(r, c1 + 1)) c1++;
          var r1 = r;
          while (r1 + 1 < GridCells && Enumerable.Range(c, c1 - c + 1).All(x => used[r1 + 1, x] == false && mask(r1 + 1, x))) r1++;
          for (var y = r; y <= r1; y++) for (var x = c; x <= c1; x++) used[y, x] = true;
          rects.Add((r, c, r1, c1));
        }
      }
      return rects;
    }

    static MapShape CellRect((int R0, int C0, int R1, int C1) cells, float inset = 0f) {
      var cx = (CellX(cells.C0) + CellX(cells.C1)) / 2f;
      var cz = (CellZ(cells.R0) + CellZ(cells.R1)) / 2f;
      var halfX = (cells.C1 - cells.C0 + 1) * GridCell / 2f - inset;
      var halfZ = (cells.R1 - cells.R0 + 1) * GridCell / 2f - inset;
      return Rect(cx, cz, halfX, halfZ);
    }

    static HashSet<(int, int)> Flood(char[,] g, (int, int) seed, System.Func<char, bool> allowed) {
      var set = new HashSet<(int, int)>();
      var queue = new Queue<(int, int)>();
      queue.Enqueue(seed);
      while (queue.Count > 0) {
        var (r, c) = queue.Dequeue();
        if (r < 0 || c < 0 || r >= GridCells || c >= GridCells || set.Contains((r, c)) || allowed(g[r, c]) == false) continue;
        set.Add((r, c));
        queue.Enqueue((r + 1, c));
        queue.Enqueue((r - 1, c));
        queue.Enqueue((r, c + 1));
        queue.Enqueue((r, c - 1));
      }
      return set;
    }

    /// <summary>칸 경계선 위의 얇은 벽. 같은 선 위에서 이어지는 변은 하나로 합친다. 벽은 inside 칸 쪽으로 thickness만큼 들어간다.</summary>
    static IEnumerable<TerrainFeature> EdgeWalls(HashSet<(int, int)> inside, System.Func<int, int, bool> needsWall, float thickness, System.Func<float, float, bool> gap) {
      // 방향: 0 북(r-1), 1 남(r+1), 2 서(c-1), 3 동(c+1).
      var dirs = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
      for (var d = 0; d < 4; d++) {
        var (dr, dc) = dirs[d];
        var edges = inside.Where(cell => needsWall(cell.Item1 + dr, cell.Item2 + dc) && inside.Contains((cell.Item1 + dr, cell.Item2 + dc)) == false)
          .Select(cell => {
            var x = CellX(cell.Item2) + dc * GridCell / 2f;
            var z = CellZ(cell.Item1) - dr * GridCell / 2f;
            return (cell.Item1, cell.Item2, x, z);
          })
          .Where(e => gap(e.x, e.z) == false)
          .ToList();
        var horizontal = d < 2;
        // 같은 선(가로 벽은 같은 줄, 세로 벽은 같은 열)에서 이어진 칸끼리 묶는다.
        foreach (var line in edges.GroupBy(e => horizontal ? e.Item1 : e.Item2)) {
          var sorted = line.OrderBy(e => horizontal ? e.Item2 : e.Item1).ToList();
          var start = 0;
          for (var i = 1; i <= sorted.Count; i++) {
            var broken = i == sorted.Count || (horizontal ? sorted[i].Item2 - sorted[i - 1].Item2 : sorted[i].Item1 - sorted[i - 1].Item1) != 1;
            if (broken == false) continue;
            var a = sorted[start];
            var b = sorted[i - 1];
            var inward = thickness / 2f;
            if (horizontal) {
              var z = a.z + dr * inward; // 북쪽 변이면 남쪽(안쪽)으로
              var x0 = CellX(a.Item2) - GridCell / 2f;
              var x1 = CellX(b.Item2) + GridCell / 2f;
              yield return Feature(TerrainKind.Wall, Rect((x0 + x1) / 2f, z, (x1 - x0) / 2f, thickness / 2f));
            } else {
              var x = a.x - dc * inward;
              var z0 = CellZ(b.Item1) - GridCell / 2f;
              var z1 = CellZ(a.Item1) + GridCell / 2f;
              yield return Feature(TerrainKind.Wall, Rect(x, (z0 + z1) / 2f, thickness / 2f, (z1 - z0) / 2f));
            }
            start = i;
          }
        }
      }
    }

    static FPVector3 TurnPoint(FPVector3 p, int quarterTurns) => TurnShape(new MapShape { Center = p }, quarterTurns).Center;

    /// <summary>가장 가까운 도로 칸(R·s) 중심. 상자·점령지를 장애물 위에 두지 않게 한다.</summary>
    static Vector2 SnapToRoad(char[,] g, float x, float z) {
      var best = new Vector2(x, z);
      var bestDistance = float.MaxValue;
      for (var r = 0; r < GridCells; r++) {
        for (var c = 0; c < GridCells; c++) {
          if (g[r, c] != 'R' && g[r, c] != 's') continue;
          var d = (CellX(c) - x) * (CellX(c) - x) + (CellZ(r) - z) * (CellZ(r) - z);
          if (d < bestDistance) {
            bestDistance = d;
            best = new Vector2(CellX(c), CellZ(r));
          }
        }
      }
      return best;
    }

    /// <summary>v5 요새 반폭(6칸 = 48m). 출입구는 축 위 |x| &lt; 8.5m에서 이 값의 0.6배보다 바깥 담장 변이다.</summary>
    public const float CompoundHalf = 48f;
    const float CompoundGateReach = CompoundHalf * 0.6f;
    const float CompoundNoBuildRadius = CompoundHalf + 14f;

    public static bool GridBlocked(char cell) => cell == 'E' || cell == 'H' || cell == 'W';

    static MapDef BuildGridFourPlayer() => BuildGridMap("Map4P", ReadGrid(GridCsvPath));

    static MapDef BuildGridMap(string id, char[,] g) {
      bool At(int r, int c, params char[] kinds) => r >= 0 && c >= 0 && r < GridCells && c < GridCells && kinds.Contains(g[r, c]);
      var terrain = new List<TerrainFeature>();

      // 경계 + 도시 건물.
      terrain.AddRange(MergeCells((r, c) => g[r, c] == 'E').Select(cells => Feature(TerrainKind.Wall, CellRect(cells))));
      terrain.AddRange(MergeCells((r, c) => g[r, c] == 'H').Select(cells => Feature(TerrainKind.Wall, CellRect(cells, 0.6f))));

      // 본진: 네 모서리의 헬기장(p)에서 B·p·v로 이어진 칸.
      var corners = new[] { (GridCells - 8, 8), (GridCells - 8, GridCells - 9), (7, GridCells - 9), (7, 8) }; // 자리 0 남서, 1 남동, 2 북동, 3 북서
      var bases = corners.Select(seed => Flood(g, seed, ch => ch == 'B' || ch == 'p' || ch == 'v')).ToArray();
      for (var seat = 0; seat < 4; seat++) {
        if (bases[seat].Count < 50) {
          throw new System.InvalidOperationException($"grid base {seat} not found at {corners[seat]} ({bases[seat].Count} cells)");
        }
      }
      // 본진 칸이 90도 회전 대칭인지 확인(자리 0 → 자리 s). 칸 (r, c)를 반시계 90도 돌리면 (79 - c, r)... 월드 (x, z) → (-z, x).
      for (var seat = 1; seat < 4; seat++) {
        var turned = new HashSet<(int, int)>(bases[0].Select(cell => {
          var (r, c) = cell;
          for (var t = 0; t < seat; t++) (r, c) = (GridCells - 1 - c, r);
          return (r, c);
        }));
        if (turned.SetEquals(bases[seat]) == false) {
          Debug.LogWarning($"[FW-Grid] base {seat} is not the {seat * 90} degree turn of base 0 ({turned.Except(bases[seat]).Count()} cells differ)");
        }
      }
      var allBase = new HashSet<(int, int)>(bases.SelectMany(b => b));
      var highRects = new List<MapShape>();
      foreach (var b in bases) {
        highRects.AddRange(MergeCells((r, c) => b.Contains((r, c))).Select(cells => CellRect(cells)));
      }
      // 본진 옹벽: 밖이 경사로·경계가 아닌 변.
      terrain.AddRange(EdgeWalls(allBase, (r, c) => At(r, c, 'P', 'E') == false && r >= 0 && c >= 0 && r < GridCells && c < GridCells, 1.6f, (x, z) => false));

      // 중앙 요새: 가운데에서 C·p·Y로 이어진 칸. 둘레 담장, 동서남북 가운데 16m 출입구.
      var compound = Flood(g, (GridCells / 2, GridCells / 2), ch => ch == 'C' || ch == 'p' || ch == 'Y');
      terrain.AddRange(EdgeWalls(compound, (r, c) => true, 1.2f, (x, z) => (Mathf.Abs(x) < 8.5f && Mathf.Abs(z) > CompoundGateReach) || (Mathf.Abs(z) < 8.5f && Mathf.Abs(x) > CompoundGateReach)));
      foreach (var prop in BlockKit.CompoundCover.Where(prop => prop.Half.x > 0)) {
        var cover = Rect(prop.U, prop.W, prop.Half.x, prop.Half.y, prop.Yaw);
        for (var seat = 0; seat < 4; seat++) terrain.Add(Feature(TerrainKind.Wall, TurnShape(cover, seat)));
      }

      // 수로(W)는 지상 통행 불가, 다리(G)는 길. 화면 물은 W + G(다리 밑).
      var water = MergeCells((r, c) => g[r, c] == 'W').Select(cells => CellRect(cells)).ToList();
      var bridges = MergeCells((r, c) => g[r, c] == 'G').Select(cells => CellRect(cells)).ToList();
      var underBridge = bridges.Select(b => b).ToList();

      var ramps = new List<MapShape>();
      var gates = new List<MapShape>();
      for (var seat = 0; seat < 4; seat++) {
        var entry = MergeCells((r, c) => g[r, c] == 'P' && bases[seat].Any(b => Mathf.Abs(b.Item1 - r) + Mathf.Abs(b.Item2 - c) == 1));
        if (entry.Count != 1) {
          throw new System.InvalidOperationException($"grid seat {seat}: expected one ramp entrance, found {entry.Count}");
        }
        var cells = Flood(g, (entry[0].R0, entry[0].C0), ch => ch == 'P');
        var footprint = MergeCells((r, c) => cells.Contains((r, c)));
        if (footprint.Count != 1) {
          throw new System.InvalidOperationException($"grid seat {seat}: ramp cells must make a rectangle");
        }
        ramps.Add(CellRect(footprint[0]));
        gates.Add(CellRect(entry[0]));
      }

      var roads = MergeCells((r, c) => g[r, c] == 'R' || g[r, c] == 's' || g[r, c] == 'G').Select(cells => CellRect(cells)).ToList();

      // 자리 0(남서) 본진 기준점: 본진 안쪽 모서리(서쪽 B 열의 서쪽 변, 남쪽 B 줄의 남쪽 변).
      var minC = bases[0].Min(b => b.Item2);
      var maxR = bases[0].Max(b => b.Item1);
      var cornerX = CellX(minC) - GridCell / 2f;
      var cornerZ = CellZ(maxR) - GridCell / 2f;
      FPVector3 Local(float u, float w) => Position(cornerX + u, cornerZ + w);
      IEnumerable<FPVector3> Seats(FPVector3 p) => Enumerable.Range(0, 4).Select(s => TurnPoint(p, s));
      IEnumerable<MapShape> SeatShapes(MapShape shape) => Enumerable.Range(0, 4).Select(s => TurnShape(shape, s));
      // 본진 안 좌표가 자리 0 본진 칸 위인지 확인한다(칸 지도가 바뀌면 여기서 알린다).
      void Check(string what, float u, float w) {
        var x = cornerX + u;
        var z = cornerZ + w;
        var c = Mathf.FloorToInt((x + GridHalf) / GridCell);
        var r = Mathf.FloorToInt((GridHalf - z) / GridCell);
        if (bases[0].Contains((r, c)) == false) {
          Debug.LogWarning($"[FW-Grid] {what} at local ({u},{w}) = ({x},{z}) cell ({r},{c}) '{g[Mathf.Clamp(r, 0, 79), Mathf.Clamp(c, 0, 79)]}' is not base");
        }
      }
      // 본진 배치(자리 0 기준 m): 시작(사령부) · 1단 자원 3 · 앞마당 확장 + 2단 자원 2. 헬기장(p)·차량장(v) 대각선은 피한다.
      var layout = new (string Name, float U, float W)[] {
        ("start", 44, 92), ("iron-a", 14, 128), ("iron-b", 128, 14), ("wood", 14, 60),
        ("forecourt", BlockKit.Forecourt.x, BlockKit.Forecourt.y), ("iron-2", 136, 18), ("wood-2", 96, 14), ("crate-a", 20, 100), ("crate-b", 100, 20),
      };
      foreach (var item in layout) Check(item.Name, item.U, item.W);
      foreach (var decor in BlockKit.BaseDecor) Check(decor.Name, decor.U, decor.W);
      float U(string name) => layout.First(l => l.Name == name).U;
      float W(string name) => layout.First(l => l.Name == name).W;

      var nodes = new List<ResourceNodePlacement>();
      foreach (var (name, amount, tier, wood) in new[] { ("iron-a", 4600, 1, false), ("iron-b", 4600, 1, false), ("wood", 6000, 1, true), ("iron-2", 3000, 2, false), ("wood-2", 3000, 2, true) }) {
        for (var s = 0; s < 4; s++) {
          var p = TurnPoint(Local(U(name), W(name)), s);
          var node = wood ? Grove(p.X.AsFloat, p.Z.AsFloat, 5.5, amount) : Mine(p.X.AsFloat, p.Z.AsFloat, amount);
          node.Tier = tier;
          nodes.Add(node);
        }
      }
      // 외곽 거점(M) 4곳: 변 가운데. 풍부한 철 + 목재, 이웃 두 본진이 다툰다(공유 확장).
      var center = new List<ResourceNodePlacement>();
      var siteWest = new Vector2(CellX(3.5f), CellZ(39.5f));
      // Task 46 phase A: the west yard sits behind its harbour quay (yard content 16 m inland). Other sides follow in phase B.
      var harbourShift = new Vector2(WestHarbour(g) ? 16f : 0f, 0f);
      Vector2 SiteBase(int turn) => turn == 0 ? siteWest + harbourShift : siteWest;
      for (var turn = 0; turn < 4; turn++) {
        var mine = Mine(SiteBase(turn).x + 6, SiteBase(turn).y - 12, 6500, 3.5);
        var grove = Grove(SiteBase(turn).x + 6, SiteBase(turn).y + 12, 5.5, 6500);
        mine.Shape = TurnShape(mine.Shape, turn);
        grove.Shape = TurnShape(grove.Shape, turn);
        center.Add(mine);
        center.Add(grove);
      }
      var expansions = new List<ExpansionSite>();
      for (var s = 0; s < 4; s++) {
        expansions.Add(new ExpansionSite { Kind = ExpansionKind.IronForecourt, Position = TurnPoint(Local(U("forecourt"), W("forecourt")), s), SeatMask = 1 << s });
      }
      // 서쪽 거점은 자리 0(남서)·3(북서), 남쪽은 0·1, 동쪽은 1·2, 북쪽은 2·3이 다툰다.
      var sharedMasks = new[] { 1 << 0 | 1 << 3, 1 << 0 | 1 << 1, 1 << 1 | 1 << 2, 1 << 2 | 1 << 3 };
      for (var turn = 0; turn < 4; turn++) {
        var westSite = Position(SiteBase(turn).x + 22, SiteBase(turn).y);
        expansions.Add(new ExpansionSite { Kind = ExpansionKind.Shared, Position = TurnPoint(westSite, turn), SeatMask = sharedMasks[turn] });
      }
      var naval = BuildWestWaterway(g, bases);

      var crates = new List<CrateSpot>();
      for (var s = 0; s < 4; s++) {
        crates.Add(new CrateSpot { Position = TurnPoint(Local(U("crate-a"), W("crate-a")), s), Seat = s, Group = 0 });
        crates.Add(new CrateSpot { Position = TurnPoint(Local(U("crate-b"), W("crate-b")), s), Seat = s, Group = 1 });
      }
      // 보급 낙하: 자리 0 사분면의 도로 칸 3곳을 돌린다.
      var drops = new[] { SnapToRoad(g, -170, -60), SnapToRoad(g, -60, -170), SnapToRoad(g, -120, -120) };
      for (var group = 0; group < drops.Length; group++) {
        for (var s = 0; s < 4; s++) {
          crates.Add(new CrateSpot { Position = TurnPoint(Position(drops[group].x, drops[group].y), s), Seat = -1, Group = group });
        }
      }
      // 점령지: 자리마다 셋(중계소 = 외곽 거점 앞, 레이더 = 대각 간선 중간, 연료 = 순환로 교차로).
      var relay = SnapToRoad(g, siteWest.x + 40, siteWest.y);
      var radar = SnapToRoad(g, -100, -100);
      var fuel = SnapToRoad(g, -150, -190);
      var captures = new List<CaptureSitePlacement>();
      foreach (var (kind, p, radius) in new[] { (CaptureKind.Relay, relay, 8.0), (CaptureKind.Radar, radar, 5.5), (CaptureKind.Fuel, fuel, 6.0) }) {
        for (var s = 0; s < 4; s++) {
          captures.Add(new CaptureSitePlacement { Kind = kind, Center = TurnPoint(Position(p.x, p.y), s), Radius = N(radius) });
        }
      }

      var cellBytes = new byte[GridCells * GridCells];
      for (var r = 0; r < GridCells; r++) for (var c = 0; c < GridCells; c++) cellBytes[r * GridCells + c] = (byte)g[r, c];

      // 휴전 중 건설 구역: 자리 0 본진의 모서리 정사각형(두 다리 길이의 0.62배). 본진 밖으로는 관문이 닫혀 나가지 못한다.
      var legs = (cornerX: cornerX, span: (bases[0].Max(b => b.Item2) - minC + 1) * GridCell);
      var half = legs.span * 0.31f;
      var baseArea = Rect(cornerX + half, cornerZ + half, half, half);

      Debug.Log($"[FW-Grid] {id}: base cells {bases[0].Count}x4, compound {compound.Count}, walls {terrain.Count}, water {water.Count}, bridges {bridges.Count}, roads {roads.Count}, high {highRects.Count}");
      return new MapDef {
        Id = id, Players = 4, HalfSize = (int)GridHalf, Start = Local(U("start"), W("start")), RiverAmplitude = 0f, RiverSeed = 23,
        Tiered = true, NoBaseCanal = true, Explicit = true,
        Starts = Seats(Local(U("start"), W("start"))).ToArray(),
        SeatBaseAreas = SeatShapes(baseArea).ToArray(),
        BaseArea = baseArea,
        Bridges = new MapShape[0],
        ExtraBridges = bridges.ToArray(),
        // Task 39: every bridge is a deck 4 m above the water; the pair nearest the centre (main E-W roads) are drawbridges.
        ExtraBridgeClearances = bridges.Select(_ => 8f).ToArray(),
        ExtraDrawbridges = bridges.Select((b, i) => (i, z: Mathf.Abs(b.Center.Z.AsFloat))).OrderBy(t => t.z).Take(2).Select(t => t.i).OrderBy(i => i).ToArray(),
        WaterRects = water.ToArray(),
        WaterVisual = underBridge.ToArray(),
        Nodes = nodes.ToArray(),
        CenterNodes = center.ToArray(),
        Trader = true,
        TraderPosition = Position(0, 0),
        MercenaryCamp = true,
        LogisticsEconomy = true,
        BlackBoxSpots = new[] { SnapToRoad(g, -76, 0), SnapToRoad(g, 0, 76), SnapToRoad(g, 76, 0), SnapToRoad(g, 0, -76) }.Select(p => Position(p.x, p.y)).ToArray(),
        Terrain = terrain.ToArray(),
        // 경사로 둘레, 중앙 요새, 본진 장식(BlockKit.BaseDecor와 같은 표: 장식 위에 건물을 짓지 않게 2m 여유).
        NoBuildZones = ramps.Select(r => { r.HalfLength += 4; r.HalfWidth += 4; return r; }).Concat(new[] { Circle(0, 0, CompoundNoBuildRadius) })
          .Concat(BlockKit.BaseDecor.Where(d => d.Half.x > 0f).SelectMany(d => SeatShapes(Rect(cornerX + d.U, cornerZ + d.W, d.Half.x + 2f, d.Half.y + 2f, d.Yaw))))
          .ToArray(),
        CameraPosition = new Vector3(0, 480, -400),
        CaptureSites = new CaptureSitePlacement[0],
        AllCaptureSites = captures.ToArray(),
        CenterSites = new CaptureSitePlacement[0],
        Barricades = new MapShape[0],
        BaseCrates = new FPVector3[0],
        DropCrates = new FPVector3[0],
        Crates = crates.ToArray(),
        HighGrounds = highRects.ToArray(),
        MidGrounds = new MapShape[0],
        Ramps = ramps.ToArray(),
        TruceGates = gates.ToArray(),
        Trenches = new MapShape[0],
        Roads = roads.ToArray(),
        Fords = new MapShape[0],
        Expansions = new ExpansionSite[0],
        SharedExpansions = new ExpansionSite[0],
        CenterExpansions = new ExpansionSite[0],
        AllExpansions = expansions.ToArray(),
        SharedSeatOffsets = new[] { 0 },
        ExtraWater = new WaterStrip[0],
        CellGrid = cellBytes,
        SeaGates = naval.Gates,
        SeaGateSeats = naval.Seats,
        SeaGateTowers = naval.Towers,
        SeaGateTriggers = naval.Triggers,
        ShipyardZones = naval.Zones,
        ShipyardZoneSeats = naval.ZoneSeats,
        ShipyardZoneSmallOnly = naval.ZoneSmall,
        Harbours = naval.Harbours,
      };
    }

    /// <summary>Task 46: the west harbour exists in the grid (col 2 water and col 3 quay beside the west yard).</summary>
    static bool WestHarbour(char[,] g) => g[39, 0] == 'W' && g[39, 2] == 'W' && g[39, 3] == 'w';

    /// <summary>Task 46 sea-gate geometry (m). The barrier sits this far past the end of the base wedge (artillery-ship range).</summary>
    public const float SeaBarrierBeyondWedge = 24f;
    const float SeaBarrierHalfThickness = 1.5f;
    const float SeaTriggerReach = 18f;
    const float SeaTowerOffset = 6f;

    struct NavalData {
      public MapShape[] Gates, Triggers, Zones, Harbours;
      public int[] Seats, ZoneSeats;
      public FPVector3[] Towers;
      public bool[] ZoneSmall;
    }

    /// <summary>
    /// Task 46 phase A (west only): one barrier per sector across the west waterway (cols 0-1), SeaBarrierBeyondWedge past the end of
    /// that sector's base wedge; a watchtower on the coast strip on the sector side; a trigger zone around the barrier; the sector's
    /// own coast strip (col 2, beyond the tower) as its shipyard zone; canal-bank quay cells ('w' next to the central canal) as
    /// small-vessel zones for anyone; the W harbour basin.
    /// </summary>
    static NavalData BuildWestWaterway(char[,] g, HashSet<(int, int)>[] bases) {
      var gates = new List<MapShape>();
      var seats = new List<int>();
      var towers = new List<FPVector3>();
      var triggers = new List<MapShape>();
      var zones = new List<MapShape>();
      var zoneSeats = new List<int>();
      var zoneSmall = new List<bool>();
      var harbours = new List<MapShape>();
      var westWater = Enumerable.Range(0, GridCells).All(r => g[r, 0] == 'W' && g[r, 1] == 'W');
      if (westWater) {
        var waterX = (CellX(0) + CellX(1)) / 2f;
        var coastX = CellX(2);
        // Seat 3 (NW) sector lies north of its barrier, seat 0 (SW) south.
        foreach (var (seat, dir) in new[] { (3, 1f), (0, -1f) }) {
          var cells = bases[seat].Where(b => b.Item2 <= 6).ToList();
          var wedgeEdge = dir > 0 ? cells.Min(b => CellZ(b.Item1)) - GridCell / 2f : cells.Max(b => CellZ(b.Item1)) + GridCell / 2f;
          var z = wedgeEdge - dir * SeaBarrierBeyondWedge;
          gates.Add(Rect(waterX, z, GridCell, SeaBarrierHalfThickness));
          seats.Add(seat);
          var towerZ = z + dir * SeaTowerOffset;
          towers.Add(Position(coastX, towerZ));
          triggers.Add(Rect(waterX, z, GridCell, SeaTriggerReach));
          var near = towerZ + dir * 8f;
          var far = dir * (GridHalf - 24f);
          zones.Add(Rect(coastX, (near + far) / 2f, GridCell / 2f, Mathf.Abs(far - near) / 2f));
          zoneSeats.Add(seat);
          zoneSmall.Add(false);
          Debug.Log($"[FW-Grid] T46 seat {seat}: wedge edge z {wedgeEdge}, barrier z {z}, tower ({coastX},{towerZ}), shipyard zone z {near}..{far}");
        }
      }
      // Canal banks: quay cells beside the central canal (|x| < 150 m) are small-vessel shipyard zones for anyone.
      bool CanalQuay(int r, int c) {
        if (g[r, c] != 'w' || Mathf.Abs(CellX(c)) > 150f) return false;
        for (var k = 0; k < 4; k++) {
          var rr = r + (k == 0 ? -1 : k == 1 ? 1 : 0);
          var cc = c + (k == 2 ? -1 : k == 3 ? 1 : 0);
          if (rr >= 0 && cc >= 0 && rr < GridCells && cc < GridCells && g[rr, cc] == 'W') return true;
        }
        return false;
      }
      foreach (var rect in MergeCells(CanalQuay)) {
        zones.Add(CellRect(rect));
        zoneSeats.Add(-1);
        zoneSmall.Add(true);
      }
      if (WestHarbour(g)) harbours.Add(Rect(CellX(1), CellZ(39.5f), 1.5f * GridCell, 4f * GridCell));
      return new NavalData {
        Gates = gates.ToArray(), Seats = seats.ToArray(), Towers = towers.ToArray(), Triggers = triggers.ToArray(),
        Zones = zones.ToArray(), ZoneSeats = zoneSeats.ToArray(), ZoneSmall = zoneSmall.ToArray(), Harbours = harbours.ToArray(),
      };
    }

    [MenuItem("Tools/Factory Wars/Map4P v4/Copy design CSV into repo")]
    public static void CopyGridCsv() {
      if (File.Exists(GridCsvDesign)) {
        Directory.CreateDirectory(Path.GetDirectoryName(GridCsvRepo));
        File.Copy(GridCsvDesign, GridCsvRepo, true);
        Debug.Log($"[FW-Grid] copied {GridCsvDesign} -> {GridCsvRepo}");
      }
    }
  }
}
