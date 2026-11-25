namespace FactoryWars.View {
  using System.Collections.Generic;
  using UnityEngine;

  /// <summary>유닛 화면 상태. 시뮬레이션 상태를 읽어 BattleView가 정한다.</summary>
  public enum UnitAnimState { Idle, Move, Attack, Build, Gather, Dead }

  /// <summary>
  /// 교체 모델의 Animator에 유닛 상태를 보낸다(항목 30). 파라미터 이름은 흔한 인간형·차량 컨트롤러에 맞췄고,
  /// 컨트롤러에 없는 파라미터는 건너뛴다. Animator가 없는 모델은 이 컴포넌트를 붙이지 않는다.
  /// 분대 모델(자식마다 Animator)은 자식 전부에 같은 값을 보내되, 시작 위치를 분대원마다 달리해 걸음이 겹치지 않는다(항목 32-2).
  /// <list type="bullet">
  /// <item>Speed(float, m/s) · Moving(bool)</item>
  /// <item>Attack(trigger) — 발사할 때마다</item>
  /// <item>Building(bool) · Gathering(bool) · Cover(bool, 엄폐 중)</item>
  /// <item>Die(trigger) · Dead(bool)</item>
  /// </list>
  /// </summary>
  public class UnitAnimatorBridge : MonoBehaviour {
    public static readonly int Speed = Animator.StringToHash("Speed");
    public static readonly int Moving = Animator.StringToHash("Moving");
    public static readonly int AttackTrigger = Animator.StringToHash("Attack");
    public static readonly int Building = Animator.StringToHash("Building");
    public static readonly int Gathering = Animator.StringToHash("Gathering");
    public static readonly int CoverParam = Animator.StringToHash("Cover");
    public static readonly int AimingParam = Animator.StringToHash("Aiming");
    public static readonly int DieTrigger = Animator.StringToHash("Die");
    public static readonly int Dead = Animator.StringToHash("Dead");

    /// <summary>첫 번째 Animator(단일 모델이면 유일한 것).</summary>
    public Animator Animator;
    /// <summary>모델 안 모든 Animator. 분대는 분대원 수만큼.</summary>
    public readonly List<Animator> All = new List<Animator>();
    public UnitAnimState State { get; private set; } = UnitAnimState.Idle;
    public int AttackCount { get; private set; }
    public float DeathSeconds = 1.5f;
    /// <summary>엄폐 중이면 true. 시뮬레이션의 InCover를 BattleView가 넣는다.</summary>
    public bool Cover;
    /// <summary>은신(엎드려 숨은 저격수) 중이면 true. 사망 연출이 엎드린 자세(DeathProne)가 된다.</summary>
    public bool Prone;
    /// <summary>교전/경계 상태면 true(전투 조준·전투 이동), false면 평시 순찰 보행·대기.</summary>
    public bool CombatAlert;
    /// <summary>기동/질주 업그레이드 해금 여부. true면 이동 시 질주(SprintForward/RifleRun) 자세로 전환된다.</summary>
    public bool SprintUnlocked;
    /// <summary>현재 활성 Mixamo 포즈 이름(검증·디버그용).</summary>
    public string ActiveMixamoPose { get; private set; }
    readonly HashSet<int> _params = new HashSet<int>();
    readonly List<MixamoMotion> _mixamo = new List<MixamoMotion>();
    static readonly string[] DeathPoses = { "DeathFront", "DeathBack", "DeathSide" };
    bool _wasInCover;
    UnitAnimState _prevState = UnitAnimState.Idle;
    float _smoothedSpeed;
    float _stopGraceUntil;
    float _actionUntil;
    public bool IsActing => Time.time < _actionUntil;

    public void EnableMixamoInfantry() {
      foreach (var animator in All) {
        var motion = MixamoMotion.Attach(animator, infantry: true);
        if (motion != null) _mixamo.Add(motion);
      }
      Refresh();
    }

    void Action(string pose, float seconds) {
      if (_mixamo.Count == 0) return;
      var applied = false;
      foreach (var motion in _mixamo) {
        if (motion.Set(pose)) applied = true;
      }
      if (applied) {
        ActiveMixamoPose = pose;
        _actionUntil = Time.time + seconds;
      }
    }

    public void Hit() {
      if (State == UnitAnimState.Idle && !IsActing) Action("Hit", 0.4f);
    }

    /// <summary>업그레이드 해금 수류탄 투척 모션 재생(Mixamo GrenadeThrow, X Bot 미사용).</summary>
    public void ThrowGrenade() {
      if (!Cover) Action("GrenadeThrow", 0.85f);
    }

    /// <summary>모델에 Animator가 있거나 슬롯에 컨트롤러가 있으면 붙인다. 아니면 null.</summary>
    public static UnitAnimatorBridge Attach(GameObject model, VisualSlot slot, RuntimeAnimatorController overrideController = null) {
      var animators = new List<Animator>(model.GetComponentsInChildren<Animator>(true));
      var controller = overrideController != null ? overrideController : slot?.Animator;
      if (animators.Count == 0 && controller != null) {
        animators.Add(model.AddComponent<Animator>());
      }
      if (animators.Count == 0) {
        return null;
      }
      var bridge = model.AddComponent<UnitAnimatorBridge>();
      var index = 0;
      foreach (var animator in animators) {
        if (controller != null) {
          animator.runtimeAnimatorController = controller;
        }
        // 이동은 시뮬레이션 위치가 정한다. 루트 모션이 모델을 끌고 가지 않게 끈다.
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        bridge.All.Add(animator);
        if (animator.runtimeAnimatorController != null) {
          // 분대원마다 걸음 시작 위치를 달리한다(같은 모델 이름이어도 index가 달라 겹치지 않는다).
          animator.Play(0, 0, Mathf.Repeat(0.37f * index + 0.11f * model.GetInstanceID() % 1f, 1f));
          animator.speed = 0.94f + 0.06f * (index % 3);
        }
        index++;
      }
      bridge.Animator = animators[0];
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

    bool Usable(Animator animator) => animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null;

    /// <summary>매 화면 프레임. speed는 화면 이동 속도(m/s).</summary>
    public void Set(UnitAnimState state, float speed) {
      if (State == UnitAnimState.Dead) {
        return;
      }
      var dt = Mathf.Max(Time.deltaTime, 0.016f);
      _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, speed, 1f - Mathf.Exp(-10f * dt));
      if (state == UnitAnimState.Move || _smoothedSpeed > 0.28f) {
        _stopGraceUntil = Time.time + 0.16f;
      }
      // 히스테리시스: 미세한 NavMesh 속도 떨림으로 Move<->Idle이 뚝뚝 끊기지 않게 부드럽게 유지한다.
      var effectiveState = state;
      if (state == UnitAnimState.Idle && _prevState == UnitAnimState.Move && (_smoothedSpeed > 0.14f || Time.time < _stopGraceUntil)) {
        effectiveState = UnitAnimState.Move;
      }
      State = effectiveState;
      if (_mixamo.Count != 0) {
        if (effectiveState == UnitAnimState.Move) _actionUntil = 0f;
        if (Cover != _wasInCover && effectiveState != UnitAnimState.Move) {
          Action(Cover ? "EnterCover" : "LeaveCover", 0.36f);
        } else if (effectiveState == UnitAnimState.Idle && _prevState == UnitAnimState.Move && !CombatAlert && !Cover && !IsActing) {
          Action("MilitarySignaling", 0.85f);
        }
        if (!IsActing) {
          string pose;
          float clipSpeed = 1f;
          if (Cover && effectiveState != UnitAnimState.Move) {
            pose = "CoverIdle";
          } else if (effectiveState == UnitAnimState.Move) {
            var moveSpeed = Mathf.Max(_smoothedSpeed, speed);
            if (SprintUnlocked) {
              // 승인된 Option A 질주: 전방 약경사 + 상체 밀착 양손 소총 파지(RifleRun)로 전환·정지 시 클리핑 0 보장.
              pose = "RifleRun";
              clipSpeed = Mathf.Clamp(moveSpeed / 3.6f, 1.08f, 1.55f);
            } else if (CombatAlert) {
              pose = moveSpeed >= 3.2f ? "RifleRun" : "RifleWalk";
              clipSpeed = Mathf.Clamp(moveSpeed / 3.6f, 0.8f, 1.45f);
            } else {
              pose = "WalkWithRifle";
              clipSpeed = Mathf.Clamp(moveSpeed / 2.6f, 0.78f, 1.35f);
            }
          } else if (effectiveState == UnitAnimState.Attack || CombatAlert) {
            pose = "IdleAiming";
          } else {
            pose = "RifleIdle";
          }
          var ok = false;
          for (var i = 0; i < _mixamo.Count; i++) {
            var phase = Mathf.Repeat(0.29f * i, 1f);
            if (_mixamo[i].Set(pose, phase, clipSpeed)) ok = true;
            else _mixamo[i].Set(null);
          }
          ActiveMixamoPose = ok ? pose : null;
        }
        _wasInCover = Cover;
      }
      _prevState = effectiveState;
      var hasSpeed = Has(Speed);
      var hasMoving = Has(Moving);
      var hasBuilding = Has(Building);
      var hasGathering = Has(Gathering);
      var hasCover = Has(CoverParam);
      var hasAiming = Has(AimingParam);
      foreach (var animator in All) {
        if (Usable(animator) == false) {
          continue;
        }
        if (hasSpeed) {
          // 가속·감속이 끊기지 않게 파라미터를 짧게 보간한다.
          animator.SetFloat(Speed, speed, 0.12f, Time.deltaTime);
        }
        if (hasMoving) {
          animator.SetBool(Moving, state == UnitAnimState.Move);
        }
        if (hasBuilding) {
          animator.SetBool(Building, state == UnitAnimState.Build);
        }
        if (hasGathering) {
          animator.SetBool(Gathering, state == UnitAnimState.Gather);
        }
        if (hasCover) {
          animator.SetBool(CoverParam, Cover);
        }
        if (hasAiming) {
          animator.SetBool(AimingParam, state == UnitAnimState.Attack);
        }
      }
    }

    public void Fire() {
      AttackCount++;
      if (AttackCount % 7 == 0 && State == UnitAnimState.Attack && !Cover && !IsActing) {
        Action("Reload", 0.65f);
      } else if (!Cover && !IsActing) {
        Action("FiringRifle", 0.42f);
      }
      if (Has(AttackTrigger) == false) {
        return;
      }
      foreach (var animator in All) {
        if (Usable(animator)) {
          animator.SetTrigger(AttackTrigger);
        }
      }
    }

    /// <summary>사망 연출을 시작하고, 보여 줄 시간(초)을 돌려준다. 0이면 바로 지워도 된다.</summary>
    public float Die() {
      State = UnitAnimState.Dead;
      if (_mixamo.Count > 0 && Usable(Animator)) {
        for (var i = 0; i < _mixamo.Count; i++) {
          var pose = Prone ? "DeathProne" : Cover ? "DeathCrouch" : DeathPoses[(Mathf.Abs(GetInstanceID() % DeathPoses.Length) + i) % DeathPoses.Length];
          _mixamo[i].Set(pose);
        }
        return Mathf.Max(DeathSeconds, 1.5f);
      }
      if (Usable(Animator) == false || (Has(DieTrigger) == false && Has(Dead) == false)) {
        return 0f;
      }
      foreach (var animator in All) {
        if (Usable(animator) == false) {
          continue;
        }
        if (Has(DieTrigger)) {
          animator.SetTrigger(DieTrigger);
        }
        if (Has(Dead)) {
          animator.SetBool(Dead, true);
        }
      }
      return DeathSeconds;
    }
  }
}
