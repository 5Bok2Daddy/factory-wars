namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;

  /// <summary>
  /// 중앙 철조망 암시장 화면(시뮬레이션 NpcTrader를 읽기만 함): 둘레 철조망(길 방향 두 곳은 출입구), 컨테이너 부스·텐트, 무기상과 경비 2명(대기 동작),
  /// 방어 포탑, 선택했을 때만 보이는 회전 점선 고리. 적대 상태면 경비 위에 붉은 표시, 파괴되면 부스가 무너지고 연기.
  /// </summary>
  public class TraderView {
    readonly Transform _root;
    GameObject _compound;
    GameObject _ring;
    readonly List<ArtView> _people = new List<ArtView>();
    GameObject _alive;
    GameObject _ruin;
    bool _destroyed;

    public EntityRef Entity { get; private set; }
    public bool RingVisible => _ring != null && _ring.activeSelf;
    public bool Built => _compound != null;

    public TraderView(Transform root) {
      _root = root;
    }

    public void Update(Frame frame, ViewCatalog catalog, BattleFx fx, bool selected, float dt) {
      var found = false;
      foreach (var pair in frame.GetComponentIterator<NpcTrader>()) {
        found = true;
        if (_compound == null) {
          Build(frame, pair.Entity, catalog);
        }
        if (pair.Component.Destroyed && _destroyed == false) {
          _destroyed = true;
          _alive.SetActive(false);
          _ruin.SetActive(true);
          fx?.Explosion(_compound.transform.position + Vector3.up * 2f, 2.2f);
        }
      }
      if (found == false || _ring == null) {
        return;
      }
      if (_ring.activeSelf != selected) {
        _ring.SetActive(selected);
      }
      if (selected) {
        _ring.transform.Rotate(0f, 25f * dt, 0f, Space.Self);
      }
    }

    void Build(Frame frame, EntityRef entity, ViewCatalog catalog) {
      Entity = entity;
      var rules = frame.FindAsset(frame.RuntimeConfig.Rules);
      var map = frame.FindAsset<MapSpec>(frame.Map.UserAsset);
      var center = frame.Get<Transform3D>(entity).Position.ToUnityVector3();
      var radius = rules.TraderRadius.AsFloat;
      _compound = new GameObject("TraderCompound");
      _compound.transform.SetParent(_root, false);
      _compound.transform.position = center;
      _alive = new GameObject("Alive");
      _alive.transform.SetParent(_compound.transform, false);
      _ruin = new GameObject("Ruin");
      _ruin.transform.SetParent(_compound.transform, false);
      _ruin.SetActive(false);
      // 출입구: 가운데로 들어오는 길 두 방향.
      var gates = new List<float>();
      foreach (var road in map.Roads ?? new MapShape[0]) {
        var c = new Vector3(road.Center.X.AsFloat, 0f, road.Center.Z.AsFloat);
        var dir = Quaternion.Euler(0f, road.Yaw.AsFloat, 0f) * Vector3.right;
        var half = road.HalfLength.AsFloat;
        foreach (var end in new[] { c - dir * half, c + dir * half }) {
          var offset = end - center;
          if (offset.magnitude < radius + 20f && gates.Count < 2) {
            var angle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            if (gates.TrueForAll(g => Mathf.Abs(Mathf.DeltaAngle(g, angle)) > 60f)) {
              gates.Add(angle);
            }
          }
        }
      }
      var segments = Mathf.CeilToInt(Mathf.PI * 2f * radius / 3.2f);
      for (var i = 0; i < segments; i++) {
        var angle = 360f * (i + 0.5f) / segments;
        if (gates.Exists(g => Mathf.Abs(Mathf.DeltaAngle(g, angle)) < 14f)) {
          continue;
        }
        var local = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
        DressingSpawn.Place(DressKind.Checkpoint, _alive.transform, local, angle + 90f, 9800 + i);
      }
      // 부스·텐트·포탑.
      DressingSpawn.Place(DressKind.Container, _alive.transform, new Vector3(-2.5f, 0f, 2.5f), 30f, 9901);
      DressingSpawn.Place(DressKind.BaseTent, _alive.transform, new Vector3(3f, 0f, 2f), -20f, 9902);
      DressingSpawn.Place(DressKind.OutpostTower, _alive.transform, new Vector3(0f, 0f, -radius * 0.55f), 0f, 9903);
      DressingSpawn.Place(DressKind.Ruin, _ruin.transform, Vector3.zero, 0f, 9904);
      // 무기상(민간 작업복)과 경비 2명: 보병 모델을 빌려 대기 동작을 재생한다(중립 색).
      Person(catalog, "cf_engineer", new Vector3(0f, 0f, 0.5f), 180f);
      Person(catalog, "ff_ranger_squad", new Vector3(-2.2f, 0f, -1.5f), 200f);
      Person(catalog, "ff_ranger_squad", new Vector3(2.2f, 0f, -1.5f), 160f);
      // 선택 고리(점선, 기본 숨김).
      var kit = new MeshKit().Paint(Swatch.White);
      ArtView.DashedRing(kit, radius + 0.6f, radius + 1.3f, 48);
      _ring = new GameObject("TraderRing", typeof(MeshFilter), typeof(MeshRenderer));
      _ring.transform.SetParent(_compound.transform, false);
      _ring.transform.localPosition = Vector3.up * 0.06f;
      _ring.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("trader_ring");
      _ring.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
      _ring.SetActive(false);
    }

    void Person(ViewCatalog catalog, string id, Vector3 local, float yaw) {
      var view = ArtView.Create(catalog, id, _alive.transform, Palette.Count - 1);
      if (view == null) {
        return;
      }
      view.transform.localPosition = local;
      view.SetYaw(yaw);
      _people.Add(view);
    }
  }
}
