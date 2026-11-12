namespace FactoryWars.View {
  using System.Collections.Generic;
  using UnityEngine;

  /// <summary>유닛 화면 상태. 시뮬레이션 상태를 읽어 BattleView가 정한다.</summary>
  public enum UnitAnimState { Idle, Move, Attack, Build, Gather, Dead }

  /// <summary>
  /// 교체 모델의 Animator에 유닛 상태를 보낸다(항목 30). 파라미터 이름은 흔한 인간형·차량 컨트롤러에 맞췄고,
  /// 컨트롤러에 없는 파라미터는 건너뛴다. Animator가 없는 모델은 이 컴포넌트를 붙이지 않는다.
  /// <list type="bullet">
  /// <item>Speed(float, m/s) · Moving(bool)</item>
  /// <item>Attack(trigger) — 발사할 때마다</item>
  /// <item>Building(bool) · Gathering(bool)</item>
  /// <item>Die(trigger) · Dead(bool)</item>
  /// </list>
  /// </summary>
  public class UnitAnimatorBridge : MonoBehaviour {
    public static readonly int Speed = Animator.StringToHash("Speed");
    public static readonly int Moving = Animator.StringToHash("Moving");
    public static readonly int AttackTrigger = Animator.StringToHash("Attack");
    public static readonly int Building = Animator.StringToHash("Building");
    public static readonly int Gathering = Animator.StringToHash("Gathering");
    public static readonly int DieTrigger = Animator.StringToHash("Die");
    public static readonly int Dead = Animator.StringToHash("Dead");

    public Animator Animator;
    public UnitAnimState State { get; private set; } = UnitAnimState.Idle;
    public int AttackCount { get; private set; }
    public float DeathSeconds = 1.5f;
    readonly HashSet<int> _params = new HashSet<int>();

    /// <summary>모델에 Animator가 있거나 슬롯에 컨트롤러가 있으면 붙인다. 아니면 null.</summary>
    public static UnitAnimatorBridge Attach(GameObject model, VisualSlot slot) {
      var animator = model.GetComponentInChildren<Animator>(true);
      if (animator == null && slot != null && slot.Animator != null) {
        animator = model.AddComponent<Animator>();
      }
      if (animator == null) {
        return null;
      }
      if (slot != null && slot.Animator != null) {
        animator.runtimeAnimatorController = slot.Animator;
      }
      // 이동은 시뮬레이션 위치가 정한다. 루트 모션이 모델을 끌고 가지 않게 끈다.
      animator.applyRootMotion = false;
      animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
      var bridge = model.AddComponent<UnitAnimatorBridge>();
      bridge.Animator = animator;
      bridge.DeathSeconds = slot != null ? slot.DeathSeconds : 0f;
      bridge.Refresh();
      return bridge;
    }

    void Refresh() {
      _params.Clear();
      if (Animator == null || Animator.runtimeAnimatorController == null) {
        return;
      }
      foreach (var parameter in Animator.parameters) {
        _params.Add(parameter.nameHash);
      }
    }

    public bool Has(int hash) => _params.Contains(hash);

    /// <summary>매 화면 프레임. speed는 화면 이동 속도(m/s).</summary>
    public void Set(UnitAnimState state, float speed) {
      if (State == UnitAnimState.Dead) {
        return;
      }
      State = state;
      if (Animator == null || Animator.isActiveAndEnabled == false) {
        return;
      }
      if (Has(Speed)) {
        Animator.SetFloat(Speed, speed);
      }
      if (Has(Moving)) {
        Animator.SetBool(Moving, state == UnitAnimState.Move);
      }
      if (Has(Building)) {
        Animator.SetBool(Building, state == UnitAnimState.Build);
      }
      if (Has(Gathering)) {
        Animator.SetBool(Gathering, state == UnitAnimState.Gather);
      }
    }

    public void Fire() {
      AttackCount++;
      if (Animator != null && Animator.isActiveAndEnabled && Has(AttackTrigger)) {
        Animator.SetTrigger(AttackTrigger);
      }
    }

    /// <summary>사망 연출을 시작하고, 보여 줄 시간(초)을 돌려준다. 0이면 바로 지워도 된다.</summary>
    public float Die() {
      State = UnitAnimState.Dead;
      if (Animator == null || Animator.isActiveAndEnabled == false || (Has(DieTrigger) == false && Has(Dead) == false)) {
        return 0f;
      }
      if (Has(DieTrigger)) {
        Animator.SetTrigger(DieTrigger);
      }
      if (Has(Dead)) {
        Animator.SetBool(Dead, true);
      }
      return DeathSeconds;
    }
  }
}
