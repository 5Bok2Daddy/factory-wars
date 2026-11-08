namespace FactoryWars.View {
  using System;
  using UnityEngine;

  /// <summary>유닛·건물 ID와 프리팹의 표. 챕터 6에서 구매 에셋으로 바꿀 때 이 표의 프리팹만 갈아끼운다.</summary>
  public class ViewCatalog : ScriptableObject {
    public const string ResourcePath = "FactoryWars/Art/ViewCatalog";

    /// <summary>표시 값은 화면 전용이다. 시뮬레이션의 크기·사거리·충돌은 이 값과 무관하다.</summary>
    [Serializable]
    public struct Entry {
      public string Id;
      public GameObject Prefab;
      public bool Unit;
      public float DisplayScale;
      public float Altitude;
      public Vector2 Center;
      public float RingRadius;
      public float BarHeight;
      /// <summary>사격 효과가 나오는 파츠 이름과 그 파츠 기준의 포구 위치.</summary>
      public string MuzzlePart;
      public Vector3 Muzzle;
      /// <summary>굴뚝 위치. 모델 기준이다.</summary>
      public Vector3[] Smoke;
    }

    public Entry[] Entries;

    public bool TryGet(string id, out Entry found) {
      foreach (var entry in Entries) {
        if (entry.Id == id) {
          found = entry;
          return true;
        }
      }
      found = default;
      return false;
    }

    public GameObject Find(string id) => TryGet(id, out var entry) ? entry.Prefab : null;
  }

  /// <summary>
  /// 프리팹 인스턴스의 색과 전력 상태를 공유 머티리얼로 정한다. 메쉬는 서브메쉬 하나이고, 칸마다 색·금속감·발광이 팔레트에 있다.
  /// MaterialPropertyBlock은 렌더러를 SRP Batcher에서 빼므로 쓰지 않는다.
  /// </summary>
  public static class ArtLook {
    public const string TeamPrefix = "ArtP";
    public const string Neutral = "ArtNeutral";
    const string Root = "FactoryWars/Art/";

    static readonly Material[] Materials = new Material[Palette.Count * 2];
    static Material _neutral;

    public static string MaterialName(int color, bool powered) => $"{TeamPrefix}{color % Palette.Count}{(powered ? "On" : "Off")}";

    public static Material Team(int color, bool powered) {
      var index = color % Palette.Count * 2 + (powered ? 1 : 0);
      return Materials[index] != null ? Materials[index] : Materials[index] = Resources.Load<Material>(Root + MaterialName(color, powered));
    }

    public static Material NeutralMaterial => _neutral != null ? _neutral : _neutral = Resources.Load<Material>(Root + Neutral);

    /// <summary>중립 머티리얼을 쓰는 자원·지형은 건드리지 않는다.</summary>
    public static void Apply(GameObject root, int color, bool powered = true) {
      foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) {
        var current = renderer.sharedMaterial;
        if (current != null && current.name.StartsWith(TeamPrefix)) {
          // 외곽선 서브메쉬가 있으면 두 칸 모두 같은 머티리얼이다.
          var materials = renderer.sharedMaterials;
          for (var i = 0; i < materials.Length; i++) {
            materials[i] = Team(color, powered);
          }
          renderer.sharedMaterials = materials;
        }
      }
    }
  }
}
