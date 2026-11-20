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
      var animator = workers[0].GetComponentInChildren<Animator>();
      if (animator != null) {
        Assert.IsNotNull(animator.runtimeAnimatorController, "Synty 작업자에 Base Locomotion Animator 컨트롤러가 있음");
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
      } else {
        var animation = workers[0].GetComponentInChildren<Animation>();
        Assert.IsNotNull(animation, "작업자에 애니메이션 컴포넌트가 있음");
        Assert.IsTrue(animation.isPlaying, "애니메이션이 재생 중");
        // 배치 모드에는 화면이 없어 렌더러 기준 컬링이면 멈춰 있다. 클립 연결만 확인하려고 항상 재생으로 바꾼다.
        animation.cullingType = AnimationCullingType.AlwaysAnimate;
      }
      var bones = workers[0].GetComponentsInChildren<Transform>().Where(t => t != workers[0] && t.GetComponent<Renderer>() == null).Take(40).ToList();
      var before = bones.Select(b => b.localRotation).ToList();
      yield return new WaitForSeconds(0.4f);
      var moved = bones.Where((b, i) => Quaternion.Angle(b.localRotation, before[i]) > 1f).Count();
      Debug.Log($"[FW-WORKER] 작업자 {workers.Count}명, 움직인 뼈 {moved}/{bones.Count}");
      Assert.Greater(moved, 0, "걷는 동안 뼈가 움직임");

      // 멀리서 볼 때도 경로 탐색 주기와 화면 이동 주기가 분리돼 있어야 한다.
      var camera = Camera.main;
      var cameraPosition = camera.transform.position;
      var timeScale = Time.timeScale;
      var targetRate = Application.targetFrameRate;
      var vSync = QualitySettings.vSyncCount;
      try {
        camera.transform.position += Vector3.up * 180f;
        Time.timeScale = 1f;
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        for (var i = 0; i < 4; i++) yield return null;

        const int samples = 36;
        var changes = new int[workers.Count];
        var positions = workers.Select(worker => worker.position).ToArray();
        var maxMs = 0f;
        var maxStep = 0f;
        for (var i = 0; i < samples; i++) {
          yield return null;
          maxMs = Mathf.Max(maxMs, Time.unscaledDeltaTime * 1000f);
          for (var j = 0; j < workers.Count; j++) {
            if (workers[j] == null) continue;
            var position = workers[j].position;
            var step = Vector3.Distance(positions[j], position);
            if (step > 0.0005f) changes[j]++;
            maxStep = Mathf.Max(maxStep, step);
            positions[j] = position;
          }
        }
        Debug.Log($"[ExtractorMotion] distant-worker: moved={changes.Max()}/{samples}, max={maxMs:F1}ms, maxStep={maxStep:F3}m");
        Assert.GreaterOrEqual(changes.Max(), Mathf.CeilToInt(samples * 0.8f), "걷는 작업자는 원거리에서도 렌더 프레임마다 이동");
      } finally {
        camera.transform.position = cameraPosition;
        Time.timeScale = timeScale;
        Application.targetFrameRate = targetRate;
        QualitySettings.vSyncCount = vSync;
      }
    }
  }
}
