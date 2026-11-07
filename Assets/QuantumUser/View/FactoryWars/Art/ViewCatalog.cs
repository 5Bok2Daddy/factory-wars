namespace FactoryWars.View {
  using System;
  using UnityEngine;

  /// <summary>유닛·건물 ID와 프리팹의 표. 챕터 6에서 구매 에셋으로 바꿀 때 이 표의 프리팹만 갈아끼운다.</summary>
  public class ViewCatalog : ScriptableObject {
    public const string ResourcePath = "FactoryWars/Art/ViewCatalog";

    [Serializable]
    public struct Entry {
      public string Id;
      public GameObject Prefab;
    }

    public Entry[] Entries;

    public GameObject Find(string id) {
      foreach (var entry in Entries) {
        if (entry.Id == id) {
          return entry.Prefab;
        }
      }
      return null;
    }
  }

  /// <summary>
  /// 프리팹 인스턴스의 색과 발광을 공유 머티리얼을 바꿔 끼워 정한다.
  /// MaterialPropertyBlock은 렌더러를 SRP Batcher에서 빼므로 쓰지 않는다.
  /// </summary>
  public static class ArtLook {
    public const string TeamPrefix = "Player";
    public const string GlowOn = "GlowOn";
    public const string GlowOff = "GlowOff";

    static readonly Material[] TeamMaterials = new Material[Palette.Count];
    static Material _glowOn;
    static Material _glowOff;

    static Material Team(int color) {
      var index = color % Palette.Count;
      return TeamMaterials[index] != null ? TeamMaterials[index] : TeamMaterials[index] = Resources.Load<Material>($"FactoryWars/Materials/{TeamPrefix}{index}");
    }

    static Material Glow(bool powered) {
      if (_glowOn == null) {
        _glowOn = Resources.Load<Material>($"FactoryWars/Art/{GlowOn}");
        _glowOff = Resources.Load<Material>($"FactoryWars/Art/{GlowOff}");
      }
      return powered ? _glowOn : _glowOff;
    }

    public static void Apply(GameObject root, int color, bool powered = true) {
      foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) {
        var materials = renderer.sharedMaterials;
        for (var i = 0; i < materials.Length; i++) {
          var name = materials[i] != null ? materials[i].name : "";
          if (name.StartsWith(TeamPrefix)) {
            materials[i] = Team(color);
          } else if (name == GlowOn || name == GlowOff) {
            materials[i] = Glow(powered);
          }
        }
        renderer.sharedMaterials = materials;
      }
    }
  }
}
