namespace FactoryWars.View {
  using System.Collections.Generic;
  using UnityEngine;

  /// <summary>
  /// Kenney 머티리얼을 프로필에 맞게 고른다(항목 27). 기본은 원본 색이다. 프로필 KenneyMilitary면 머티리얼마다 군용판을 한 번 만들어 같이 쓴다
  /// (인스턴스마다 만들지 않는다). 군용판은 KenneyAssetMap.Military의 아틀라스·색을 쓴다.
  /// </summary>
  public static class KenneyLook {
    static readonly Dictionary<Material, Material> Cache = new Dictionary<Material, Material>();
    static Dictionary<Material, KenneyAssetMap.MilitarySwap> _swaps;

    public static Material Resolve(Material source) {
      if (source == null || VisualProfile.Current.KenneyMilitary == false) {
        return source;
      }
      var map = VisualProfile.Current.Dressing;
      if (map == null) {
        return source;
      }
      if (Cache.TryGetValue(source, out var cached)) {
        return cached;
      }
      if (_swaps == null) {
        _swaps = new Dictionary<Material, KenneyAssetMap.MilitarySwap>();
        foreach (var swap in map.Military) {
          if (swap.Material != null) {
            _swaps[swap.Material] = swap;
          }
        }
      }
      var result = source;
      if (_swaps.TryGetValue(source, out var found)) {
        result = new Material(source) { name = source.name + "_military" };
        if (found.Texture != null) {
          result.SetTexture("_BaseMap", found.Texture);
        }
        if (found.HasColor) {
          result.SetColor("_BaseColor", found.Color);
        }
      }
      Cache[source] = result;
      return result;
    }

    public static Material[] Resolve(Material[] sources) {
      if (VisualProfile.Current.KenneyMilitary == false) {
        return sources;
      }
      var result = new Material[sources.Length];
      for (var i = 0; i < sources.Length; i++) {
        result[i] = Resolve(sources[i]);
      }
      return result;
    }
  }
}
