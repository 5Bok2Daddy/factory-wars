namespace FactoryWars.Tests {
  using System.Collections;
  using System.IO;
  using FactoryWars.View;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 남은 Mixamo 클립 연결(2026-11-21): 의무대 작업자는 처치(Aid)·부상자 대기(InjuredIdle), 정비소 작업자는 밀기·당기기(Push/Pull).
  /// 화면 전용 작업자이며 시뮬레이션 상태를 바꾸지 않는다. Mixamo 파생 컨트롤러가 없는 체크아웃에서는 건너뛴다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    [UnityTest, Timeout(300000)]
    public IEnumerator Mixamo_MedicAndRepairWorkersUseSupportPoses() {
      if (MixamoMotion.HasWorkerController == false) {
        Assert.Ignore("Mixamo 작업자 컨트롤러 없음(로컬 구매 에셋 필요)");
      }
      var quality = GraphicsQuality.Current;
      GraphicsQuality.Apply(QualityTier.Medium);
      Restores.Add(() => GraphicsQuality.Apply(quality));
      yield return StartMatch(Map4PScene, 2, truce: 0, startingTier: 3, stock: Stock(20000, 20000, 20000, 200));
      var start = FactoryWarsUtil.StartPosition(Verified, 0);
      // 즉시 생성은 건설 판정을 거치지 않는다. 본진 안에서 실제로 지을 수 있는 자리(도로·건물·상자 밖)를 고른다.
      var medicSpot = FreeSpot(BuildingOf(0, "_medic_tent"), start);
      ProductionSystem.CreateFinishedBuilding(Verified, 0, BuildingOf(0, "_medic_tent"), medicSpot);
      var repairSpot = FreeSpot(BuildingOf(0, "_repair_bay"), start);
      ProductionSystem.CreateFinishedBuilding(Verified, 0, BuildingOf(0, "_repair_bay"), repairSpot);
      yield return WaitForBattle();
      var ambient = Object.FindAnyObjectByType<BattleView>().AmbientWorkers;
      yield return WaitFor(() => ambient.PoseCount("Aid") + ambient.PoseCount("InjuredIdle") > 0, 40f, "의무대 처치·부상자 동작");
      yield return WaitFor(() => ambient.PoseCount("Push") + ambient.PoseCount("Pull") > 0, 40f, "정비소 밀기·당기기 동작");
      Controller().Camera.MoveTo(new Vector2((medicSpot.X + repairSpot.X).AsFloat / 2f, (medicSpot.Z + repairSpot.Z).AsFloat / 2f));
      yield return WaitFrames(Fps);
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "DevelopmentReports", "Screenshots", "Mixamo"));
      CaptureCameraPng(Camera.main, Path.Combine(dir, "medic_and_repair_workers.png"), 1600, 900);
    }

    static FPVector3 FreeSpot(Quantum.AssetRef<Quantum.BuildingSpec> building, FPVector3 start) {
      var spec = Verified.FindAsset(building);
      for (var ring = 12; ring <= 40; ring += 4) {
        for (var dx = -ring; dx <= ring; dx += 4) {
          foreach (var dz in new[] { -ring, ring }) {
            var spot = start + new FPVector3(dx, 0, dz);
            if (ProductionSystem.CanPlace(Verified, spec, spot, 0)) return spot;
          }
        }
      }
      Assert.Fail("본진 안에 빈 땅이 없음");
      return start;
    }
  }
}
