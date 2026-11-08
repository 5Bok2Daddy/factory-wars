namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;
  using Object = UnityEngine.Object;

  /// <summary>항목 25-1: 본진 장식 작업자가 실제로 걷는다(애니메이션이 돌고 뼈가 움직인다). 품질 낮음에서는 없다.</summary>
  public unsafe partial class LocalMatchTests {
    [UnityTest]
    public IEnumerator Kenney_BaseWorkersWalk() {
      GraphicsQuality.Apply(QualityTier.Medium);
      yield return StartMatch(Map4PScene, 2, truce: 0);
      yield return WaitForBattle();
      yield return WaitFrames(10);
      Assume.That(VisualProfile.Current.Dressing, Is.Not.Null, "Kenney 맵이 있는 프로필");
      var workers = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name == "BaseWorker").ToList();
      Assert.Greater(workers.Count, 0, "본진 장식 작업자가 있음");
      var animation = workers[0].GetComponentInChildren<Animation>();
      Assert.IsNotNull(animation, "작업자에 애니메이션 컴포넌트가 있음");
      Assert.IsTrue(animation.isPlaying, "애니메이션이 재생 중");
      // 배치 모드에는 화면이 없어 렌더러 기준 컬링이면 멈춰 있다. 클립 연결만 확인하려고 항상 재생으로 바꾼다.
      animation.cullingType = AnimationCullingType.AlwaysAnimate;
      var bones = workers[0].GetComponentsInChildren<Transform>().Where(t => t != workers[0] && t.GetComponent<Renderer>() == null).Take(40).ToList();
      var before = bones.Select(b => b.localRotation).ToList();
      yield return new WaitForSeconds(0.4f);
      var moved = bones.Where((b, i) => Quaternion.Angle(b.localRotation, before[i]) > 1f).Count();
      Debug.Log($"[FW-WORKER] 작업자 {workers.Count}명, 움직인 뼈 {moved}/{bones.Count}");
      Assert.Greater(moved, 0, "걷는 동안 뼈가 움직임");
    }
  }
}
