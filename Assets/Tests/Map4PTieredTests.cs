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
  /// 2026-11-22 표준 맵(Map4P) 3단 지형 재구성 검사. 도로·물길·다리 없음, 본진 고지대·앞마당 중간 대지·중앙 저지대,
  /// 휴전 동안 앞마당 출구 관문으로 자리끼리 막히고 끝나면 열린다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static Navigation.FindPathResult.Code GroundPath(Frame frame, FPVector3 from, FPVector3 to) {
      var rules = FactoryWarsUtil.Rules(frame);
      var ground = frame.Map.GetNavMesh(rules.NavMeshName);
      Assert.IsTrue(FactoryWarsUtil.FindNavPointNear(frame, rules.NavMeshName, from, 6, out var a), $"출발점 {from}");
      Assert.IsTrue(FactoryWarsUtil.FindNavPointNear(frame, rules.NavMeshName, to, 6, out var b), $"도착점 {to}");
      var mask = *frame.NavMeshRegionMask;
      return frame.Navigation.FindPath((FrameThreadSafe)frame, a, b, ground, in mask).Result;
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator Map4P_TieredLayoutSealsSeatsDuringTruceAndOpensAfter() {
      yield return StartMatch(Map4PScene, 4, truce: 25);
      var frame = Verified;
      var map = FactoryWarsUtil.MapInfo(frame);
      Assert.IsTrue(map.Tiered, "3단 지형 맵");
      Assert.IsEmpty(map.Roads ?? new MapShape[0], "도로 메쉬 없음");
      Assert.IsEmpty(map.River ?? new MapShape[0], "중앙 해자·모서리 운하 없음");
      Assert.IsEmpty(map.Bridges ?? new MapShape[0], "다리 없음");
      Assert.AreEqual(4, map.HighGrounds.Length, "본진 고지대 4");
      Assert.AreEqual(4, map.MidGrounds.Length, "앞마당 중간 대지 4");
      Assert.AreEqual(8, map.TruceGates.Length, "앞마당 출구 관문 자리마다 2");
      Assert.IsTrue(map.HasTrader && map.TraderPosition == FPVector3.Zero, "중앙 암시장");
      Assert.AreEqual(4 * 5 + 8, map.ResourceNodes.Length, "자원: 자리마다 1단 3 + 2단 2, 중앙 4방위 주머니 8");

      // 화면 높이: 본진 3m, 앞마당 1.5m, 중앙 0m.
      var start = map.StartPositions[0];
      Assert.AreEqual(TieredTerrain.MainHeight, TerrainKit.SurfaceHeight(map, start.X.AsFloat, start.Z.AsFloat), 0.01f, "본진 고지대");
      var natural = map.MidGrounds[0].Center;
      Assert.AreEqual(TieredTerrain.MidHeight, TerrainKit.SurfaceHeight(map, natural.X.AsFloat, natural.Z.AsFloat), 0.01f, "앞마당");
      Assert.AreEqual(0f, TerrainKit.SurfaceHeight(map, 0f, 24f), 0.01f, "중앙 저지대");

      // 휴전 중: 본진 ↔ 앞마당은 열리고, 이웃 본진·중앙은 막힌다.
      Assert.IsFalse(BarrierOpen());
      Assert.AreEqual(Navigation.FindPathResult.Code.Success, GroundPath(frame, start, natural), "휴전 중 본진→앞마당(비탈)");
      for (var seat = 1; seat < 4; seat++) {
        Assert.AreNotEqual(Navigation.FindPathResult.Code.Success, GroundPath(frame, start, map.StartPositions[seat]), $"휴전 중 자리 {seat} 본진 차단");
      }
      Assert.AreNotEqual(Navigation.FindPathResult.Code.Success, GroundPath(frame, start, Pos(0, 24)), "휴전 중 중앙 차단");

      yield return WaitFor(() => BarrierOpen(), 60f, "휴전 종료");
      frame = Verified;
      for (var seat = 1; seat < 4; seat++) {
        Assert.AreEqual(Navigation.FindPathResult.Code.Success, GroundPath(frame, start, map.StartPositions[seat]), $"휴전 뒤 자리 {seat} 본진");
      }
      Assert.AreEqual(Navigation.FindPathResult.Code.Success, GroundPath(frame, start, Pos(0, 24)), "휴전 뒤 중앙");
      // 중앙 4방위 자원 주머니는 중앙 쪽에서 닿는다.
      foreach (var node in map.ResourceNodes.Where(n => n.Tier == 3)) {
        var toward = node.Shape.Center * FP._0_75;
        Assert.AreEqual(Navigation.FindPathResult.Code.Success, GroundPath(frame, Pos(0, 24), toward), $"자원 주머니 {node.Shape.Center}");
      }
    }

    static FPVector3 Pos(int x, int z) => new FPVector3(x, 0, z);

    /// <summary>개요(위에서 본 전체)와 게임 화면 각도. 사용자 요청 파일명으로 Docs/status_images에도 둔다.</summary>
    [UnityTest, Explicit, Timeout(600000)]
    public IEnumerator Map4P_CaptureTieredOverviewAndAngle() {
      var tag = System.Environment.GetEnvironmentVariable("FW_UI_SHOT_TAG");
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "DevelopmentReports", "Screenshots", "Map4PTiered", string.IsNullOrEmpty(tag) ? "after" : tag));
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
        CaptureCameraPng(camera, Path.Combine(dir, "map_standard.png"), 1440, 1440);
      } finally {
        camera.orthographic = false;
        camera.transform.SetPositionAndRotation(position, rotation);
        camera.farClipPlane = clip;
      }
      // 게임 카메라: 자리 0 본진 비탈과 앞마당, 그 너머 중앙.
      var rts = Controller().Camera;
      rts.MoveTo(new Vector2(-70f, -100f));
      rts.Height = 78f;
      rts.Apply();
      yield return WaitFrames(Fps / 2);
      CaptureCameraPng(camera, Path.Combine(dir, "map_standard_angle1.png"), 1920, 1080);
      rts.MoveTo(new Vector2(0f, -10f));
      rts.Height = 95f;
      rts.Apply();
      yield return WaitFrames(Fps / 2);
      CaptureCameraPng(camera, Path.Combine(dir, "map_standard_angle2_center.png"), 1920, 1080);
      if (string.IsNullOrEmpty(tag) || tag == "after") {
        foreach (var name in new[] { "map_standard.png", "map_standard_angle1.png" }) {
          File.Copy(Path.Combine(dir, name), Path.Combine(status, name), true);
        }
      }
    }
  }
}
