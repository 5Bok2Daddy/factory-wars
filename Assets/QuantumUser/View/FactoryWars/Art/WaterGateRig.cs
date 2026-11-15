namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>
  /// 항목 36 수문 조립 틀. 팩 모델(옹벽·다리 기둥·철제 문)은 그대로 두고, 우리 폴더 프리팹(`FactoryWarsArt/Synty/Urban/water_gate.prefab`)의
  /// 감싸개 오브젝트(Left/Right/PierL/PierR/Door)에만 배율·위치를 준다. 수문이 물길을 가로지르는 폭(span)과 높이는 코드가 정하고 숫자는 VisualProfile 데이터다.
  /// 화면 전용(시뮬레이션 상태를 읽기만 한다).
  /// </summary>
  public class WaterGateRig : MonoBehaviour {
    public Transform Left;
    public Transform Right;
    public Transform PierL;
    public Transform PierR;
    public Transform Door;
    /// <summary>문 아래 물보라(팩 FX_Waterfall_Foam). 없을 수 있다.</summary>
    public Transform Foam;
    /// <summary>프리팹을 만들 때 잰 팩 모델 크기(미터, 감싸개 배율 1 기준). 바닥 중앙이 감싸개 원점이다.</summary>
    public Vector3 WallSize = Vector3.one;
    public Vector3 PierSize = Vector3.one;
    public Vector3 DoorSize = Vector3.one;

    /// <summary>문이 닫혔을 때 밑면 높이(물 속에 잠기게 음수).</summary>
    public const float DoorBaseY = -7f;

    float _doorHeight;
    float _drop = -1f;
    ParticleSystem[] _foam;

    public float Drop => Mathf.Max(_drop, 0f);
    public bool DoorVisible => Door != null && Door.gameObject.activeSelf;
    public bool FoamPlaying { get; private set; }

    /// <summary>폭(span)·기둥 높이·문 높이로 배치한다. 로컬 X가 폭 방향, Z가 물 흐름 방향이다.</summary>
    public void Layout(float span, float postHeight, float doorHeight) {
      _doorHeight = doorHeight;
      var wallScale = postHeight / Mathf.Max(0.1f, WallSize.y);
      // 옹벽은 길이가 긴 변이 물 흐름(Z) 쪽으로 가도록 눕힌다(둑 벽 역할).
      var wallTurn = WallSize.x > WallSize.z;
      var wallLong = (wallTurn ? WallSize.x : WallSize.z) * wallScale;
      var wallThick = (wallTurn ? WallSize.z : WallSize.x) * wallScale;
      Place(Left, new Vector3(-(span / 2f + wallThick / 2f), 0f, 0f), wallTurn ? 90f : 0f, Vector3.one * wallScale);
      Place(Right, new Vector3(span / 2f + wallThick / 2f, 0f, 0f), wallTurn ? 90f : 0f, Vector3.one * wallScale);
      // 다리 기둥은 문 양 끝 안쪽에 세워 문이 끼는 홈 기둥이 된다. 높이 기준 배율이 너무 두꺼워지지 않게 폭 상한(1.5m)도 건다.
      var pierWide = Mathf.Max(PierSize.x, PierSize.z);
      var pierScale = Mathf.Min((postHeight * 0.92f) / Mathf.Max(0.1f, PierSize.y), 1.5f / Mathf.Max(0.1f, pierWide));
      var pierHalf = PierSize.x * pierScale * 0.5f;
      var pierX = span / 2f - pierHalf;
      Place(PierL, new Vector3(-pierX, 0f, 0f), 0f, Vector3.one * pierScale);
      Place(PierR, new Vector3(pierX, 0f, 0f), 0f, Vector3.one * pierScale);
      // 문: 기둥 사이 폭에 맞춰 늘리고 높이는 doorHeight, 두께는 높이 배율을 따른다. 우리 감싸개에만 배율을 주므로 팩 파일은 그대로다.
      if (Door != null) {
        // 야전 룩: 팩 문 모델 대신 두꺼운 짙은 강철판(#4A5568)을 수로 바닥(-7m)부터 벽 위(문 높이)까지 세워 빈틈없이 막는다.
        foreach (Transform child in Door) {
          child.gameObject.SetActive(false);
        }
        var plate = Door.Find("SteelPlate");
        if (plate == null) {
          plate = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
          plate.name = "SteelPlate";
          Object.Destroy(plate.GetComponent<Collider>());
          plate.GetComponent<MeshRenderer>().sharedMaterial = SteelMaterial();
          plate.SetParent(Door, false);
        }
        plate.gameObject.SetActive(true);
        Door.localScale = Vector3.one;
        Door.localRotation = Quaternion.identity;
        var full = doorHeight + Mathf.Abs(DoorBaseY);
        plate.localScale = new Vector3(Mathf.Max(0.5f, span - 0.2f), full, 1.2f);
        plate.localPosition = new Vector3(0f, full * 0.5f, 0f);
        _doorHeight = full;
      }
      if (false && Door != null) {
        var sx = Mathf.Max(0.5f, span - pierHalf * 4f) / Mathf.Max(0.1f, DoorSize.x);
        var sy = doorHeight / Mathf.Max(0.1f, DoorSize.y);
        // 두께는 1m로 고정(구멍 없는 판 문이 수로를 빈틈없이 막는다).
        Door.localScale = new Vector3(sx, sy, 1f / Mathf.Max(0.1f, DoorSize.z));
        Door.localRotation = Quaternion.identity;
      }
      if (Foam != null) {
        Foam.localPosition = new Vector3(0f, 0.2f, 0f);
        _foam = Foam.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var system in _foam) {
          var main = system.main;
          main.playOnAwake = false;
          system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
      }
      SetDrop(0f);
    }

    static void Place(Transform part, Vector3 position, float yaw, Vector3 scale) {
      if (part == null) {
        return;
      }
      part.localPosition = position;
      part.localRotation = Quaternion.Euler(0f, yaw, 0f);
      part.localScale = scale;
    }

    /// <summary>0 = 문이 올라와 있음, 1 = 완전히 내려감. 시뮬레이션의 개방 상태를 읽어 부르는 쪽이 값을 정한다.</summary>
    public void SetDrop(float value) {
      _drop = Mathf.Clamp01(value);
      if (Door == null) {
        return;
      }
      var eased = _drop * _drop * (3f - 2f * _drop);
      Door.localPosition = new Vector3(0f, DoorBaseY - _doorHeight * eased, 0f);
      var visible = _drop < 1f;
      if (Door.gameObject.activeSelf != visible) {
        Door.gameObject.SetActive(visible);
      }
    }

    static Material _steel;

    /// <summary>수문 강철판 공용 머티리얼(URP Lit, 짙은 강철 #4A5568, 무광에 가까움). 팩 머티리얼은 건드리지 않는다.</summary>
    static Material SteelMaterial() {
      if (_steel == null) {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        _steel = new Material(shader) { name = "FloodgateSteel", color = new Color32(0x4A, 0x55, 0x68, 0xFF) };
        _steel.SetFloat("_Smoothness", 0.25f);
        _steel.SetFloat("_Metallic", 0.6f);
      }
      return _steel;
    }

    /// <summary>문이 다 내려가고 몇 초 뒤 물보라를 끈다.</summary>
    public void StopFoam() {
      if (FoamPlaying == false || _foam == null) {
        return;
      }
      FoamPlaying = false;
      foreach (var system in _foam) {
        system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
      }
    }

    /// <summary>문이 내려가기 시작할 때 한 번 부른다. 하얀 물보라.</summary>
    public void PlayFoam() {
      if (FoamPlaying) {
        return;
      }
      FoamPlaying = true;
      if (_foam == null) {
        return;
      }
      foreach (var system in _foam) {
        system.Play(true);
      }
    }
  }
}
