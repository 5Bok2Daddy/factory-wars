namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>기존 걷기 Animator 위에 현장 도구 동작을 얹는다.</summary>
  [DefaultExecutionOrder(50)]
  public sealed class ConstructionWorkView : MonoBehaviour {
    Transform _hand, _elbow, _shoulder, _tool;
    float _weight, _phase;
    MixamoMotion _motion;

    public bool Working { get; set; }
    public string WorkPose { get; set; } = "Device";
    public Transform Tool => _tool;

    public static ConstructionWorkView Attach(GameObject model, GameObject toolPrefab, float phase = 0f) {
      if (toolPrefab == null) return null;
      var hand = RiflemanRig.Find(model.transform, "Hand_R");
      if (hand == null || hand.parent == null || hand.parent.parent == null) return null;
      var rig = model.AddComponent<ConstructionWorkView>();
      rig._hand = hand;
      rig._elbow = hand.parent;
      rig._shoulder = hand.parent.parent;
      rig._phase = phase;
      rig._motion = MixamoMotion.Attach(model.GetComponentInChildren<Animator>(true));
      var tool = Instantiate(toolPrefab, model.transform);
      tool.name = "EngineerWorkTool";
      foreach (var collider in tool.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
      rig._tool = tool.transform;
      tool.SetActive(false);
      return rig;
    }

    void LateUpdate() {
      var dt = Time.deltaTime;
      _motion?.Set(Working ? WorkPose : null, _phase);
      _weight = Mathf.MoveTowards(_weight, Working ? 1f : 0f, dt * 5f);
      var show = _weight > 0.02f;
      if (_tool.gameObject.activeSelf != show) _tool.gameObject.SetActive(show);
      if (show == false) return;

      _phase = Mathf.Repeat(_phase + dt * 1.8f, 1f);
      var stroke = Mathf.Sin(_phase * (2f * Mathf.PI));
      var size = Mathf.Max(0.01f, transform.lossyScale.y);
      var forward = transform.forward;
      var right = transform.right;
      var goal = _shoulder.position + (forward * (0.30f + stroke * 0.075f) - Vector3.up * (0.29f - stroke * 0.065f)) * size;
      var handGoal = Vector3.Lerp(_hand.position, goal, _weight);
      RiflemanRig.Solve(_shoulder, _elbow, _hand, handGoal, _shoulder.position + (right * 0.32f - Vector3.up * 0.48f) * size);
      _tool.SetPositionAndRotation(_hand.position + forward * (0.06f * size),
        Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(16f - stroke * 17f, 0f, 0f));
    }
  }
}
