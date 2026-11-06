using System.Collections.Generic;
using Quantum;
using UnityEngine;

/// <summary>
/// 시뮬레이션 상태를 읽어 기본 도형으로 그리는 임시 뷰. 시뮬레이션을 변경하지 않는다.
/// 유닛은 캡슐, 건물은 큐브이고 진영 색으로 칠한다.
/// </summary>
public class FactoryWarsDebugView : MonoBehaviour {
  static readonly Color FreeFederation = new Color32(0x1F, 0x3A, 0x6B, 0xFF);
  static readonly Color ContinentalFederation = new Color32(0xA3, 0x22, 0x2B, 0xFF);
  static readonly Color ResourceNodeColor = new Color32(0xE0, 0xB0, 0x30, 0xFF);

  readonly Dictionary<EntityRef, GameObject> _views = new Dictionary<EntityRef, GameObject>();
  readonly HashSet<EntityRef> _seen = new HashSet<EntityRef>();
  readonly List<EntityRef> _removed = new List<EntityRef>();

  void LateUpdate() {
    var frame = QuantumRunner.Default?.Game?.Frames?.Predicted;
    if (frame == null) {
      return;
    }

    _seen.Clear();
    foreach (var pair in frame.GetComponentIterator<Unit>()) {
      Draw(frame, pair.Entity, FactionColor(frame, pair.Component.Owner), PrimitiveType.Capsule, Vector3.one);
    }
    foreach (var pair in frame.GetComponentIterator<Building>()) {
      Draw(frame, pair.Entity, FactionColor(frame, pair.Component.Owner), PrimitiveType.Cube, new Vector3(5, 3, 5));
    }
    foreach (var pair in frame.GetComponentIterator<ResourceNode>()) {
      Draw(frame, pair.Entity, ResourceNodeColor, PrimitiveType.Cylinder, new Vector3(3, 1, 3));
    }

    _removed.Clear();
    foreach (var pair in _views) {
      if (_seen.Contains(pair.Key) == false) {
        _removed.Add(pair.Key);
      }
    }
    foreach (var entity in _removed) {
      Destroy(_views[entity]);
      _views.Remove(entity);
    }
  }

  void Draw(Frame frame, EntityRef entity, Color color, PrimitiveType shape, Vector3 scale) {
    _seen.Add(entity);
    if (_views.TryGetValue(entity, out var view) == false) {
      view = GameObject.CreatePrimitive(shape);
      view.name = $"{shape} {entity}";
      view.transform.SetParent(transform, false);
      view.transform.localScale = scale;
      Destroy(view.GetComponent<Collider>());
      view.GetComponent<Renderer>().material.color = color;
      _views.Add(entity, view);
    }
    var position = frame.Get<Transform3D>(entity).Position.ToUnityVector3();
    position.y += scale.y * 0.5f;
    view.transform.position = position;
  }

  static Color FactionColor(Frame frame, PlayerRef owner) {
    var faction = frame.FindAsset(frame.GetPlayerData(owner)?.Faction ?? default);
    return faction != null && faction.FactionId == "continental_federation" ? ContinentalFederation : FreeFederation;
  }
}
