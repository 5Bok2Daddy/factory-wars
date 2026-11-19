namespace FactoryWars.View {
  using System.Collections.Generic;
  using UnityEngine;

  /// <summary>
  /// 보병 소총 자세(화면 전용). 사격 애니메이션 팩(Kubold 등)이 없어 절차적으로 만든다:
  /// 팩 소총(SM_Wep_Rifle_Assault_03 / 저격수 SM_Wep_Sniper_Mil_01)을 분대원마다 들리고, Animator가 자세를 만든 뒤(LateUpdate)
  /// 두 마디 IK로 오른손을 개머리판 손잡이에, 왼손을 총열 덮개에 붙인다. 평소엔 총구를 아래로 든 경계 자세(low ready),
  /// 표적이 있으면 어깨 높이 조준 자세로 상체를 표적 쪽으로 돌린다. 쏠 때마다 총·상체가 -6도 들리고 0.06m 뒤로 밀린다(0.12초 회복).
  /// 뼈 축 방향을 가정하지 않도록 모든 계산은 월드 위치·FromToRotation으로 한다.
  /// </summary>
  public class RiflemanRig : MonoBehaviour {
    public const float RecoilPitch = 6f;
    public const float RecoilBack = 0.06f;
    const float RecoilSeconds = 0.12f;
    const float AimBlendRate = 8f;

    Transform _spine;
    Transform _shoulderR, _elbowR, _handR;
    Transform _shoulderL, _elbowL, _handL;
    Transform _rifle;
    float _muzzleZ;
    float _kick;
    float _aim;

    public bool Aiming;
    public Vector3 AimPoint;
    /// <summary>대전차 로켓병(RPG): 어깨 거치형 발사기 오프셋 적용.</summary>
    public bool ShoulderMounted;
    public int Shots { get; private set; }
    public Transform Rifle => _rifle;

    /// <summary>총구 끝(월드). 총이 없으면 가슴 높이.</summary>
    public Vector3 Muzzle => _rifle != null ? _rifle.TransformPoint(new Vector3(0f, 0.03f, _muzzleZ)) : transform.position + Vector3.up * 1.3f;
    public Vector3 MuzzleForward => _rifle != null ? _rifle.forward : transform.forward;

    static Transform Find(Transform root, string name) {
      if (root.name == name) {
        return root;
      }
      foreach (Transform child in root) {
        var found = Find(child, name);
        if (found != null) {
          return found;
        }
      }
      return null;
    }

    /// <summary>사람형 뼈(Synty: Shoulder/Elbow/Hand_R·L, Spine_03)가 있는 모델마다 붙인다. 붙인 수를 돌려준다.</summary>
    public static List<RiflemanRig> AttachAll(GameObject model, GameObject riflePrefab, bool shoulderMounted = false) {
      var rigs = new List<RiflemanRig>();
      if (model == null || riflePrefab == null) {
        return rigs;
      }
      var members = new List<Transform>();
      foreach (var animator in model.GetComponentsInChildren<Animator>(true)) {
        members.Add(animator.transform);
      }
      if (members.Count == 0) {
        members.Add(model.transform);
      }
      foreach (var member in members) {
        var rig = Attach(member, riflePrefab, shoulderMounted);
        if (rig != null) {
          rigs.Add(rig);
        }
      }
      return rigs;
    }

    public static RiflemanRig Attach(Transform member, GameObject riflePrefab, bool shoulderMounted = false) {
      var handR = Find(member, "Hand_R");
      var handL = Find(member, "Hand_L");
      if (handR == null || handL == null || handR.parent == null || handR.parent.parent == null || handL.parent == null || handL.parent.parent == null) {
        return null;
      }
      if (member.GetComponent<RiflemanRig>() is RiflemanRig existing) {
        existing.ShoulderMounted = shoulderMounted;
        return existing;
      }
      var rig = member.gameObject.AddComponent<RiflemanRig>();
      rig.ShoulderMounted = shoulderMounted;
      rig._handR = handR;
      rig._elbowR = handR.parent;
      rig._shoulderR = handR.parent.parent;
      rig._handL = handL;
      rig._elbowL = handL.parent;
      rig._shoulderL = handL.parent.parent;
      rig._spine = Find(member, "Spine_03") ?? Find(member, "Spine_02");
      var rifle = Instantiate(riflePrefab, member);
      rifle.name = "Rifle";
      // 팩 소총에 붙은 섬광 메쉬는 끈다(섬광은 쏠 때 효과로 그린다).
      foreach (var renderer in rifle.GetComponentsInChildren<Renderer>(true)) {
        if (renderer.name.Contains("Flash")) {
          renderer.enabled = false;
        }
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      }
      var bounds = new Bounds(Vector3.zero, Vector3.zero);
      var first = true;
      foreach (var filter in rifle.GetComponentsInChildren<MeshFilter>(true)) {
        if (filter.sharedMesh == null || filter.name.Contains("Flash")) {
          continue;
        }
        var b = filter.sharedMesh.bounds;
        var local = rifle.transform.InverseTransformPoint(filter.transform.TransformPoint(b.max));
        if (first) {
          bounds = new Bounds(local, Vector3.zero);
          first = false;
        }
        bounds.Encapsulate(local);
      }
      rig._rifle = rifle.transform;
      // 팩 소총은 +Z가 총구, 원점이 손잡이다.
      rig._muzzleZ = Mathf.Max(0.45f, bounds.max.z);
      return rig;
    }

    public void Fire() {
      _kick = 1f;
      Shots++;
    }

    /// <summary>캡처·스폰 직후 한 프레임 지연을 없애기 위해 현재 Kubold 뼈 위치에 무기를 즉시 밀착한다.</summary>
    public void SnapNow() {
      _aim = Aiming ? 1f : 0f;
      ApplyWeaponPose(0f);
    }

    void LateUpdate() {
      ApplyWeaponPose(Time.deltaTime);
    }

    void ApplyWeaponPose(float dt) {
      if (_rifle == null || _shoulderR == null || _handR == null || _handL == null) {
        return;
      }
      _kick = Mathf.MoveTowards(_kick, 0f, dt / RecoilSeconds);
      _aim = Mathf.MoveTowards(_aim, Aiming ? 1f : 0f, dt * AimBlendRate);
      var scale = Mathf.Max(0.01f, transform.lossyScale.y);
      var up = Vector3.up;
      var facing = transform.forward;
      facing.y = 0f;
      facing = facing.sqrMagnitude > 0.0001f ? facing.normalized : Vector3.forward;
      if (Aiming) {
        var toTarget = AimPoint - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.01f) {
          facing = Vector3.Slerp(facing, toTarget.normalized, _aim);
        }
      }
      var right = Vector3.Cross(up, facing).normalized;

      // 상체: 표적 쪽으로 자연스럽게 회전 및 사격 반동.
      if (_spine != null) {
        var chestForward = transform.forward;
        chestForward.y = 0f;
        if (chestForward.sqrMagnitude > 0.0001f) {
          var twist = Vector3.SignedAngle(chestForward.normalized, facing, up) * 0.45f * _aim;
          _spine.rotation = Quaternion.AngleAxis(twist, up) * _spine.rotation;
        }
        _spine.rotation = Quaternion.AngleAxis(-RecoilPitch * 0.45f * _kick, right) * _spine.rotation;
      }

      if (ShoulderMounted) {
        // 대전차 로켓(RPG): 오른 어깨 위에 정확히 거치하고 오른손이 하부 손잡이를 쥔다.
        var dir = facing;
        if (Aiming) {
          var toAim = AimPoint - _shoulderR.position;
          if (toAim.sqrMagnitude > 0.01f) {
            dir = Vector3.Slerp(facing, toAim.normalized, 0.55f).normalized;
          }
        }
        var pitchAxis = Vector3.Cross(up, dir).normalized;
        if (pitchAxis.sqrMagnitude < 0.0001f) pitchAxis = right;
        dir = Quaternion.AngleAxis(-RecoilPitch * _kick, pitchAxis) * dir;
        var tubePos = _shoulderR.position + (right * 0.16f + up * 0.10f + dir * 0.14f) * scale - dir * (RecoilBack * _kick * scale);
        _rifle.position = tubePos;
        _rifle.rotation = Quaternion.LookRotation(dir, up);
        _rifle.localScale = Vector3.one * 1.05f;
        Solve(_shoulderR, _elbowR, _handR, _rifle.TransformPoint(new Vector3(0f, -0.09f, 0.16f)), _shoulderR.position - up * 0.45f * scale + right * 0.35f * scale);
        Solve(_shoulderL, _elbowL, _handL, _rifle.TransformPoint(new Vector3(0f, -0.08f, 0.36f)), _shoulderL.position - up * 0.45f * scale - right * 0.3f * scale);
        return;
      }

      // Kubold 실제 손바닥 뼈 기준 보정: 손목(Hand_R/L)에서 손바닥 중심으로 이동해 권총손잡이가 오른손 안에, 총열 덮개가 왼손 위에 밀착된다.
      var foreR = (_handR.position - _elbowR.position);
      var foreL = (_handL.position - _elbowL.position);
      var palmR = _handR.position + (foreR.sqrMagnitude > 1e-5f ? foreR.normalized : facing) * (0.055f * scale);
      var palmL = _handL.position + (foreL.sqrMagnitude > 1e-5f ? foreL.normalized : facing) * (0.045f * scale);
      var handSpan = palmL - palmR;
      var hasTwoHandHold = handSpan.magnitude > 0.14f * scale && Vector3.Dot(handSpan.normalized, facing) > 0.25f;

      Vector3 grip;
      Vector3 weaponDir;
      if (hasTwoHandHold) {
        weaponDir = handSpan.normalized;
        var pitchAxis = Vector3.Cross(up, weaponDir).normalized;
        if (pitchAxis.sqrMagnitude < 0.0001f) pitchAxis = right;
        weaponDir = Quaternion.AngleAxis(-RecoilPitch * _kick, pitchAxis) * weaponDir;
        // 총 손잡이를 오른손바닥 중심에 맞추고 총열 덮개가 왼손에 딱 걸치도록 미세 상향(+0.05m)·전진(+0.01m) 보정.
        grip = palmR + up * (0.05f * scale) + weaponDir * ((0.01f - RecoilBack * _kick) * scale);
        _rifle.position = grip;
        _rifle.rotation = Quaternion.LookRotation(weaponDir, up);
      } else {
        // 달리기 등 양손 파지가 풀린 동작에서는 가슴 높이 저자세로 유지(목·헬멧 높이 부양 금지).
        var shoulder = _shoulderR.position;
        grip = shoulder + (facing * 0.22f - up * 0.32f - right * 0.05f) * scale;
        weaponDir = (facing - up * 0.32f - right * 0.28f).normalized;
        _rifle.position = grip;
        _rifle.rotation = Quaternion.LookRotation(weaponDir, up);
        Solve(_shoulderR, _elbowR, _handR, grip - up * (0.02f * scale), shoulder - up * 0.5f * scale + right * 0.35f * scale);
        Solve(_shoulderL, _elbowL, _handL, _rifle.TransformPoint(new Vector3(0f, -0.02f, 0.3f)), _shoulderL.position - up * 0.5f * scale - right * 0.3f * scale);
      }
    }

    /// <summary>두 마디 IK(어깨·팔꿈치를 돌려 손이 target에 닿게, pole 쪽으로 팔꿈치를 굽힌다).</summary>
    static void Solve(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole) {
      var a = upper.position;
      var b = lower.position;
      var c = hand.position;
      var la = (b - a).magnitude;
      var lb = (c - b).magnitude;
      if (la < 1e-4f || lb < 1e-4f) {
        return;
      }
      var toTarget = target - a;
      var distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(la - lb) + 1e-3f, (la + lb) * 0.999f);
      var direction = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : (c - a).normalized;
      var cosA = Mathf.Clamp((la * la + distance * distance - lb * lb) / (2f * la * distance), -1f, 1f);
      var angle = Mathf.Acos(cosA) * Mathf.Rad2Deg;
      var bend = Vector3.Cross(direction, pole - a);
      if (bend.sqrMagnitude < 1e-6f) {
        bend = Vector3.Cross(direction, Vector3.down);
      }
      var elbowDirection = Quaternion.AngleAxis(angle, bend.normalized) * direction;
      var newElbow = a + elbowDirection * la;
      upper.rotation = Quaternion.FromToRotation(b - a, newElbow - a) * upper.rotation;
      b = lower.position;
      c = hand.position;
      lower.rotation = Quaternion.FromToRotation(c - b, a + direction * distance - b) * lower.rotation;
    }
  }
}
