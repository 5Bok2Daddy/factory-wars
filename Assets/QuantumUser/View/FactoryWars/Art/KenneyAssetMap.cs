namespace FactoryWars.View {
  using System;
  using System.Collections.Generic;
  using UnityEngine;

  /// <summary>Kenney 모델로 바꿀 수 있는 화면 요소 종류(항목 19B). 판정과 무관한 겉모습 분류다.</summary>
  public enum DressKind {
    Conifer,
    Broadleaf,
    Autumn,
    Bush,
    RockLarge,
    RockSmall,
    Cliff,
    CliffCorner,
    CityWarehouse,
    CityFactory,
    CityOffice,
    CityTower,
    CityRuin,
    Chimney,
    Tank,
    Pipe,
    Roof,
    TowerBase,
    Barricade,
    Prop,
  }

  /// <summary>
  /// 게임 요소 종류 → Kenney 프리팹 후보(가중치)·스케일·회전·틴트. 화면 전용이다.
  /// 파일 이름은 코드에 쓰지 않고 이 에셋에만 둔다. VisualProfile.Dressing이 이 맵을 가리키면 절차 메쉬 대신 이 모델을 쓴다.
  /// 변종·회전은 오브젝트 ID·좌표 해시로 고르므로 같은 장면은 항상 같은 화면이다.
  /// </summary>
  [CreateAssetMenu(menuName = "FactoryWars/Kenney Asset Map")]
  public class KenneyAssetMap : ScriptableObject {
    public const string ResourcePath = "FactoryWars/Visual/KenneyAssetMap";

    [Serializable]
    public class Variant {
      public GameObject Prefab;
      public float Weight = 1f;
      /// <summary>모델 원점에서 바닥까지의 보정(모델마다 원점이 다르다).</summary>
      public float Lift;
    }

    [Serializable]
    public class Entry {
      public DressKind Kind;
      public List<Variant> Variants = new List<Variant>();
      /// <summary>모델 1단위 → 월드 미터. 전차 기준(나무 높이 전차 2~3배, 건물 1층 전차 1.5배)으로 맞춘다.</summary>
      public float Scale = 1f;
      /// <summary>개체마다 곱하는 배율 범위.</summary>
      public Vector2 ScaleJitter = new Vector2(0.9f, 1.1f);
      /// <summary>Y축 회전 범위(도). 건물은 90도 단위로 맞춘다.</summary>
      public Vector2 Yaw = new Vector2(0f, 360f);
      public bool SnapYaw90;
      public Color Tint = Color.white;
      /// <summary>품질 낮음에서 남기는 비율(나무·소품 수 줄이기).</summary>
      [Range(0f, 1f)] public float LowKeep = 1f;
    }

    public List<Entry> Entries = new List<Entry>();

    Dictionary<DressKind, Entry> _lookup;

    public Entry Get(DressKind kind) {
      if (_lookup == null) {
        _lookup = new Dictionary<DressKind, Entry>();
        foreach (var entry in Entries) {
          if (entry.Variants.Count > 0) {
            _lookup[entry.Kind] = entry;
          }
        }
      }
      return _lookup.TryGetValue(kind, out var found) ? found : null;
    }

    public bool Has(DressKind kind) => Get(kind) != null;

    /// <summary>해시(0~1) 하나로 가중치에 맞는 변종을 고른다.</summary>
    public static Variant Pick(Entry entry, float hash) {
      var total = 0f;
      foreach (var variant in entry.Variants) {
        total += Mathf.Max(0f, variant.Weight);
      }
      var target = hash * total;
      foreach (var variant in entry.Variants) {
        target -= Mathf.Max(0f, variant.Weight);
        if (target <= 0f) {
          return variant;
        }
      }
      return entry.Variants[entry.Variants.Count - 1];
    }

    /// <summary>개체 하나의 배치: 변종, 회전, 배율. seed는 오브젝트 ID나 좌표에서 만든다.</summary>
    public static (Variant Variant, Quaternion Rotation, float Scale) Place(Entry entry, int seed) {
      var variant = Pick(entry, MeshKit.Hash(seed, 1));
      var yaw = Mathf.Lerp(entry.Yaw.x, entry.Yaw.y, MeshKit.Hash(seed, 2));
      if (entry.SnapYaw90) {
        yaw = Mathf.Round(yaw / 90f) * 90f;
      }
      var scale = entry.Scale * Mathf.Lerp(entry.ScaleJitter.x, entry.ScaleJitter.y, MeshKit.Hash(seed, 3));
      return (variant, Quaternion.Euler(0f, yaw, 0f), scale);
    }

    /// <summary>좌표 해시. 같은 자리면 같은 값이다.</summary>
    public static int Seed(Vector3 position, int salt) => Mathf.RoundToInt(position.x * 7.31f) * 73856093 ^ Mathf.RoundToInt(position.z * 5.17f) * 19349663 ^ salt * 83492791;
  }
}
