using System.Collections.Generic;
using FactoryWars.View;
using Quantum;
using UnityEngine;

/// <summary>
/// 시뮬레이션 상태를 읽어 기본 도형으로 그리는 임시 뷰. 시뮬레이션을 변경하지 않는다.
/// 유닛은 캡슐, 건물은 큐브이고 진영 색으로 칠한다.
/// </summary>
public class FactoryWarsDebugView : MonoBehaviour {
  readonly Material[] _playerMaterials = new Material[Palette.Count];
  Material _nodeMaterial;

  static readonly Color BridgeOpen = new Color32(0x8A, 0x70, 0x50, 0xFF);
  static readonly Color BridgeClosed = new Color32(0xB0, 0x30, 0x28, 0xFF);

  Renderer[] _bridges;
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
      Draw(frame, pair.Entity, PlayerMaterial(frame, pair.Component.Owner), PrimitiveType.Capsule, Vector3.one);
    }
    foreach (var pair in frame.GetComponentIterator<Building>()) {
      Draw(frame, pair.Entity, PlayerMaterial(frame, pair.Component.Owner), BuildingShape(frame, pair.Component.Owner), new Vector3(5, 3, 5));
    }
    foreach (var pair in frame.GetComponentIterator<ResourceNode>()) {
      Draw(frame, pair.Entity, NodeMaterial(), PrimitiveType.Cylinder, new Vector3(3, 1, 3));
    }
    DrawBridges(frame);

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

  /// <summary>닫힌 다리는 붉게, 열린 다리는 낮게 그린다. 3-5에서 다리 모델로 바뀐다.</summary>
  unsafe void DrawBridges(Frame frame) {
    var bridges = frame.FindAsset<MapSpec>(frame.Map.UserAsset).Bridges;
    if (bridges == null) {
      return;
    }
    if (_bridges == null) {
      _bridges = new Renderer[bridges.Length];
      for (var i = 0; i < bridges.Length; i++) {
        var view = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(view.GetComponent<Collider>());
        view.name = "Bridge";
        view.transform.SetParent(transform, false);
        view.transform.position = bridges[i].Center.ToUnityVector3();
        view.transform.rotation = Quaternion.Euler(0, bridges[i].Yaw.AsFloat, 0);
        view.transform.localScale = new Vector3(bridges[i].HalfLength.AsFloat * 2, 0.3f, bridges[i].HalfWidth.AsFloat * 2);
        _bridges[i] = view.GetComponent<Renderer>();
        _bridges[i].sharedMaterial = NodeMaterial();
      }
    }
    var color = frame.Global->BridgesOpen ? BridgeOpen : BridgeClosed;
    foreach (var bridge in _bridges) {
      bridge.material.color = color;
    }
  }

  void Draw(Frame frame, EntityRef entity, Material material, PrimitiveType shape, Vector3 scale) {
    _seen.Add(entity);
    if (_views.TryGetValue(entity, out var view) == false) {
      view = GameObject.CreatePrimitive(shape);
      view.name = $"{shape} {entity}";
      view.transform.SetParent(transform, false);
      view.transform.localScale = scale;
      Destroy(view.GetComponent<Collider>());
      view.GetComponent<Renderer>().sharedMaterial = material;
      _views.Add(entity, view);
    }
    var position = frame.Get<Transform3D>(entity).Position.ToUnityVector3();
    position.y += scale.y * 0.5f;
    view.transform.position = position;
  }

  /// <summary>색은 플레이어가 고른 공유 머티리얼이다. 인스턴스마다 새로 만들지 않는다.</summary>
  unsafe Material PlayerMaterial(Frame frame, PlayerRef owner) {
    var index = frame.Global->Players.GetPointer(owner)->ColorIndex % Palette.Count;
    if (_playerMaterials[index] == null) {
      _playerMaterials[index] = Resources.Load<Material>($"FactoryWars/Materials/Player{index}");
    }
    return _playerMaterials[index];
  }

  Material NodeMaterial() {
    if (_nodeMaterial == null) {
      _nodeMaterial = Resources.Load<Material>("FactoryWars/Materials/Node");
    }
    return _nodeMaterial;
  }

  /// <summary>진영은 색이 아니라 형태로 구분한다. 자유 연방은 돔, 대륙 연방은 각진 상자다.</summary>
  static unsafe PrimitiveType BuildingShape(Frame frame, PlayerRef owner) {
    var faction = frame.FindAsset(frame.Global->Players.GetPointer(owner)->Faction);
    return faction != null && faction.FactionId == "free_federation" ? PrimitiveType.Sphere : PrimitiveType.Cube;
  }
}
