namespace FactoryWars.View {
  using System;
  using System.Collections.Generic;
  using Quantum;
  using UnityEngine;

  /// <summary>내 유닛·건물 선택과 부대 지정. 화면 쪽 상태라 시뮬레이션에 넣지 않는다.</summary>
  public class SelectionModel {
    public const int GroupCount = 3;

    public readonly List<EntityRef> Units = new List<EntityRef>();
    public EntityRef Building;
    readonly List<EntityRef>[] _groups = { new List<EntityRef>(), new List<EntityRef>(), new List<EntityRef>() };

    public bool IsEmpty => Units.Count == 0 && Building == EntityRef.None;

    public void Clear() {
      Units.Clear();
      Building = EntityRef.None;
    }

    public void SelectUnit(EntityRef unit) {
      Clear();
      Units.Add(unit);
    }

    public void SelectUnits(IEnumerable<EntityRef> units) {
      Clear();
      if (units != null) {
        Units.AddRange(units);
      }
    }

    public void SelectBuilding(EntityRef building) {
      Clear();
      Building = building;
    }

    /// <summary>화면 사각형 안의 내 유닛을 고른다. project는 월드 좌표를 화면 좌표로 바꾼다.</summary>
    public void SelectBox(Frame frame, PlayerRef player, Rect box, Func<Vector3, Vector2> project) {
      Clear();
      foreach (var pair in frame.GetComponentIterator<Unit>()) {
        if (pair.Component.Owner == player && pair.Component.Transport == EntityRef.None && pair.Component.Garrison == EntityRef.None
            && box.Contains(project(frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3()))) {
          Units.Add(pair.Entity);
        }
      }
    }

    /// <summary>같은 종류의 내 유닛 전부. isVisible로 화면에 보이는 것만 고른다.</summary>
    public void SelectSameType(Frame frame, PlayerRef player, EntityRef unit, Func<Vector3, bool> isVisible) {
      var spec = frame.Get<Unit>(unit).Spec;
      Clear();
      foreach (var pair in frame.GetComponentIterator<Unit>()) {
        if (pair.Component.Owner == player && pair.Component.Spec == spec && pair.Component.Transport == EntityRef.None && pair.Component.Garrison == EntityRef.None
            && isVisible(frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3())) {
          Units.Add(pair.Entity);
        }
      }
    }

    public void SaveGroup(int index) {
      _groups[index].Clear();
      _groups[index].AddRange(Units);
    }

    public void RecallGroup(int index) {
      Clear();
      Units.AddRange(_groups[index]);
    }

    public int GroupSize(int index) => _groups[index].Count;

    /// <summary>죽은 유닛과 파괴된 건물, 탑승 완료된 유닛을 뺀다.</summary>
    public void Prune(Frame frame) {
      Units.RemoveAll(e => frame.Exists(e) == false || (frame.Has<Unit>(e) && frame.Get<Unit>(e).Transport != EntityRef.None));
      foreach (var group in _groups) {
        group.RemoveAll(e => frame.Exists(e) == false);
      }
      if (Building != EntityRef.None && frame.Exists(Building) == false) {
        Building = EntityRef.None;
      }
    }
  }
}
