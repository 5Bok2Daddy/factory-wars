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
      var mask = *frame.NavMeshRegionMask;
      return ground.Contains(point, mask, true);
    }

    static FPVector3 Pos(float x, float z) => new FPVector3(FP.FromFloat_UNSAFE(x), 0, FP.FromFloat_UNSAFE(z));

    static FPVector3 Rot(FPVector3 p, int quarterTurns) {
      for (var i = 0; i < quarterTurns; i++) {
        p = new FPVector3(-p.Z, 0, p.X);
      }
      return p;
    }

    static bool HasMirror(MapShape[] shapes, MapShape shape) => shapes.Any(o => (o.Center + shape.Center).Magnitude.AsFloat < 0.6f);

    [UnityTest, Timeout(600000)]
    public IEnumerator Map4P_LayoutSymmetryCanalBridgesTiersAndTruce() {
      yield return StartMatch(Map4PScene, 4, truce: 25);
      var frame = Verified;
      var map = FactoryWarsUtil.MapInfo(frame);
      Assert.IsTrue(map.Tiered, "3단 지형 맵");
      Assert.AreEqual(4, map.StartPositions.Length);
      Assert.AreEqual(6, map.Bridges.Length, "다리 6(대각 4 + 북·남 2)");
      Assert.AreEqual(4, map.HighGrounds.Length, "능선 대지 4");
      Assert.AreEqual(4, map.MidGrounds.Length, "본진 테라스 4");
      Assert.AreEqual(12, map.TruceGates.Length, "본진마다 비탈 관문 3");
      Assert.AreEqual(12, map.Trenches.Length, "참호 자리마다 3");
      Assert.IsTrue(map.Roads.Length > 40, "대각 간선·외곽 순환로");
      Assert.IsTrue(map.River.Length > 40, "S자 운하");
      Assert.IsTrue(map.HasTrader && map.TraderPosition == FPVector3.Zero, "중앙 단지 암시장");
      Assert.AreEqual(4 * 5 + 8, map.ResourceNodes.Length, "자원: 자리마다 1단 3 + 2단 2, 변 가운데 자원지 4곳 x 2");

      // 네 본진은 모서리, 서로 90도 회전(모서리끼리 거리가 같다).
      for (var seat = 0; seat < 4; seat++) {
        Assert.Less((map.StartPositions[seat] - Rot(map.StartPositions[0], seat)).Magnitude.AsFloat, 0.01f, $"자리 {seat} 회전 대칭");
        Assert.Greater(FPMath.Abs(map.StartPositions[seat].X).AsFloat, 110f);
        Assert.Greater(FPMath.Abs(map.StartPositions[seat].Z).AsFloat, 110f);
      }
      // 운하·다리는 180도 회전 대칭.
      foreach (var piece in map.River) {
        Assert.IsTrue(HasMirror(map.River, piece), $"운하 조각 {piece.Center} 180도 대칭");
      }
      foreach (var bridge in map.Bridges) {
        Assert.IsTrue(HasMirror(map.Bridges, bridge), $"다리 {bridge.Center} 180도 대칭");
      }
      // 변 가운데 자원지: 맵 가장자리 쪽, 변 가운데에서 ±12m.
      var sites = map.ResourceNodes.Where(n => n.Tier == 3).ToArray();
      Assert.AreEqual(8, sites.Length);
      foreach (var node in sites) {
        var x = FPMath.Abs(node.Shape.Center.X).AsFloat;
        var z = FPMath.Abs(node.Shape.Center.Z).AsFloat;
        Assert.Greater(Mathf.Max(x, z), 130f, $"자원지 {node.Shape.Center} 가장자리");
        Assert.Less(Mathf.Min(x, z), 12f, $"자원지 {node.Shape.Center} 변 가운데");
      }
      // 도시 건물(공장·창고 줄)은 길·물과 겹치지 않는다.
      foreach (var city in map.CityBuildings) {
        foreach (var road in map.Roads) {
          Assert.Greater(road.Distance(city.Shape.Center).AsFloat, 2f, $"건물 {city.Shape.Center} 길 위");
        }
        Assert.Greater(map.River.Min(r => r.Distance(city.Shape.Center).AsFloat), 4f, $"건물 {city.Shape.Center} 물 위");
      }

      // 화면 높이: 테라스 2m, 능선 3.5m, 길·저지대 0m.
      var start = map.StartPositions[0];
      Assert.AreEqual(TieredTerrain.MidHeight, TerrainKit.SurfaceHeight(map, start.X.AsFloat, start.Z.AsFloat), 0.01f, "본진 테라스");
      var ridge = map.HighGrounds[0].Center;
      Assert.AreEqual(TieredTerrain.MainHeight, TerrainKit.SurfaceHeight(map, ridge.X.AsFloat, ridge.Z.AsFloat), 0.01f, "능선 대지");
      Assert.AreEqual(0f, TerrainKit.SurfaceHeight(map, -117f, 0f), 0.01f, "외곽 순환로");
      foreach (var gate in map.TruceGates.Take(3)) {
        Assert.Less(TerrainKit.SurfaceHeight(map, gate.Center.X.AsFloat, gate.Center.Z.AsFloat), TieredTerrain.MidHeight + 0.01f, $"비탈 {gate.Center}");
      }

      // 운하는 지상 NavMesh에 없다(다리로만 건넌다).
      foreach (var water in new[] { Pos(-56, 140), Pos(56, -140), Pos(0, 54), Pos(54, 0), Pos(-30, 46) }) {
        Assert.IsFalse(OnGroundNavMesh(frame, water), $"운하 {water} 지상 통과 불가");
      }

      // 휴전 중: 본진 테라스 안만 다닌다.
      Assert.IsFalse(BarrierOpen());
      Assert.AreEqual(Navigation.FindPathResult.Code.Success, GroundPath(frame, start, Pos(-150, -100)), "휴전 중 본진 안");
      for (var seat = 1; seat < 4; seat++) {
        Assert.AreNotEqual(Navigation.FindPathResult.Code.Success, GroundPath(frame, start, map.StartPositions[seat]), $"휴전 중 자리 {seat} 본진 차단");
      }
      Assert.AreNotEqual(Navigation.FindPathResult.Code.Success, GroundPath(frame, start, Pos(-117, 0)), "휴전 중 순환로 차단");

      yield return WaitFor(() => BarrierOpen(), 60f, "휴전 종료");
      frame = Verified;
      for (var seat = 1; seat < 4; seat++) {
        Assert.AreEqual(Navigation.FindPathResult.Code.Success, GroundPath(frame, start, map.StartPositions[seat]), $"휴전 뒤 자리 {seat} 본진");
      }
      // 운하 양쪽은 북쪽 다리로 이어진다.
      Assert.AreEqual(Navigation.FindPathResult.Code.Success, GroundPath(frame, Pos(-75, 140), Pos(-37, 140)), "북쪽 운하 다리");
      // 변 가운데 자원지는 순환로에서 닿는다.
      foreach (var node in sites) {
        Assert.AreEqual(Navigation.FindPathResult.Code.Success, GroundPath(frame, start, node.Shape.Center * FP._0_75), $"자원지 {node.Shape.Center}");
      }
      // 공정성: 본진에서 중앙 단지 입구(자기 대각 문)까지 경로 길이가 ±5% 안(대각 간선, 다리 하나).
      var lengths = Enumerable.Range(0, 4).Select(seat => RouteLength(frame, map.StartPositions[seat], Rot(Pos(-21f, -21f), seat))).ToArray();
      var shortest = lengths.Min();
      Debug.Log($"Map4P base→compound route lengths: {string.Join(", ", lengths.Select(l => l.ToString("F1")))}");
      foreach (var length in lengths) {
        Assert.LessOrEqual(length / shortest, 1.05f, $"단지 입구 경로 {string.Join(", ", lengths.Select(l => l.ToString("F1")))}");
      }
    }

    static string ScreenshotRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "map4p"));

    /// <summary>위에서 본 전체(map_top_vN.png)와 게임 카메라 화면 5곳. FW_MAP_ITER(기본 v1)로 반복 번호를 붙인다.</summary>
    [UnityTest, Explicit, Timeout(600000)]
    public IEnumerator Map4P_CaptureTopAndGameplayViews() {
      var iteration = System.Environment.GetEnvironmentVariable("FW_MAP_ITER");
      iteration = string.IsNullOrEmpty(iteration) ? "v1" : iteration;
      var dir = ScreenshotRoot();
      Directory.CreateDirectory(dir);
      var status = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images"));
      yield return StartMatch(Map4PScene, 4, truce: 300);
      yield return WaitForHud();
      yield return WaitForBattle();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) {
        doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
      }
      Time.timeScale = 1f;
      yield return WaitFrames(Fps);
      var map = FactoryWarsUtil.MapInfo(Verified);
      var camera = Camera.main;
      var position = camera.transform.position;
      var rotation = camera.transform.rotation;
      var clip = camera.farClipPlane;
      try {
        camera.orthographic = true;
        camera.orthographicSize = (map.BoundsMax.X - map.BoundsMin.X).AsFloat * 0.56f;
        camera.farClipPlane = 1200f;
        camera.transform.SetPositionAndRotation(new Vector3(0f, 420f, 0f), Quaternion.Euler(90f, 0f, 0f));
        CaptureCameraPng(camera, Path.Combine(dir, $"map_top_{iteration}.png"), 1440, 1440);
      } finally {
        camera.orthographic = false;
        camera.transform.SetPositionAndRotation(position, rotation);
        camera.farClipPlane = clip;
      }
      var rts = Controller().Camera;
      foreach (var (name, focus, height) in new[] {
          ("base_ramps", new Vector2(-104f, -104f), 70f),
          ("canal_bridge", new Vector2(-56f, 112f), 62f),
          ("central_compound", new Vector2(0f, -8f), 72f),
          ("resource_site", new Vector2(-132f, -6f), 60f),
          ("gully", new Vector2(-76f, -6f), 52f) }) {
        rts.MoveTo(focus);
        rts.Height = height;
        rts.Apply();
        yield return WaitFrames(Fps / 2);
        CaptureCameraPng(camera, Path.Combine(dir, $"view_{name}_{iteration}.png"), 1920, 1080);
      }
      File.Copy(Path.Combine(dir, $"map_top_{iteration}.png"), Path.Combine(status, "map_standard.png"), true);
      File.Copy(Path.Combine(dir, $"view_base_ramps_{iteration}.png"), Path.Combine(status, "map_standard_angle1.png"), true);
    }
  }
}
