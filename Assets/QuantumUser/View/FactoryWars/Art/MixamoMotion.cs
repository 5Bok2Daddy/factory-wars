namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>Optional Humanoid actions driven by the View. Unity never owns the actor's position.</summary>
  public sealed class MixamoMotion {
    static RuntimeAnimatorController _worker;
    static RuntimeAnimatorController _infantry;
    static bool _lookedForWorker;
    static bool _lookedForInfantry;
    public static bool HasWorkerController => FindController(false) != null;
    readonly Animator _animator;
    readonly int _locomotion;
    int _active;

    MixamoMotion(Animator animator, RuntimeAnimatorController controller, string locomotion) {
      _animator = animator;
      _locomotion = Animator.StringToHash("Base Layer." + locomotion);
      animator.runtimeAnimatorController = controller;
      animator.applyRootMotion = false;
    }

    public static MixamoMotion Attach(Animator animator, bool infantry = false) {
      if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman) return null;
      var controller = FindController(infantry);
      return controller != null ? new MixamoMotion(animator, controller, infantry ? "Locomotion" : "WorkerLocomotion") : null;
    }

    static RuntimeAnimatorController FindController(bool infantry) {
      if (infantry) {
        if (!_lookedForInfantry) {
          _infantry = Resources.Load<RuntimeAnimatorController>("FactoryWarsMixamo/Infantry");
          _lookedForInfantry = true;
        }
        return _infantry;
      }
      if (!_lookedForWorker) {
        _worker = Resources.Load<RuntimeAnimatorController>("FactoryWarsMixamo/Worker");
        _lookedForWorker = true;
      }
      return _worker;
    }

    public bool Set(string pose, float phase = 0f, float speed = 1f) {
      if (!_animator.isActiveAndEnabled) return false;
      var next = pose == null ? _locomotion : Animator.StringToHash("Base Layer.Mixamo" + pose);
      if (next == _active || (pose == null && _active == 0)) {
        if (pose != null) _animator.speed = Mathf.Lerp(_animator.speed, speed, 0.28f);
        return true;
      }
      if (!_animator.HasState(0, next)) return false;
      _active = pose == null ? 0 : next;
      _animator.speed = Mathf.Lerp(_animator.speed, speed, 0.45f);
      _animator.CrossFadeInFixedTime(next, 0.22f, 0, phase);
      return true;
    }
  }
}
