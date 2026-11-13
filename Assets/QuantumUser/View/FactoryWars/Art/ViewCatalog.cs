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

    /// <summary>
    /// 항목 31 새 유닛의 임시 모델: 기존 모델을 빌려 크기만 바꾼다. 에셋 팩으로 VisualCatalog 칸(unit/새 id)을 채우면 그 모델이 우선한다.
    /// </summary>
    public static readonly (string Id, string Borrow, float Scale)[] TemporaryModels = {
      ("ff_scout_buggy", "cf_thunder_aa", 0.7f),
      ("ff_field_support", "cf_longwall_mlrs", 0.8f),
      ("ff_bulwark_tank", "ff_eagle_tank", 1.3f),
      ("cf_scout_bike", "ff_eagle_tank", 0.6f),
      ("cf_gale_gunship", "ff_hawk_gunship", 0.9f),
      ("cf_fortress_tank", "cf_dragonscale_tank", 1.3f),
      // 항목 32 새 건물(모델 없음, 에셋 팩이 없어 기존 건물을 빌림)
      ("ff_sandbags", "ff_collection_yard", 0.55f),
      ("ff_bunker", "ff_ground_tower", 0.9f),
      ("ff_medic_tent", "ff_supply_depot", 0.8f),
      ("ff_repair_bay", "ff_vehicle_factory", 0.75f),
      ("ff_radar", "ff_air_defense", 0.9f),
      ("ff_helipad", "ff_airfield", 0.7f),
      ("ff_artillery", "ff_ground_tower", 0.98f),
      ("cf_sandbags", "cf_collection_yard", 0.55f),
      ("cf_bunker", "cf_ground_tower", 0.9f),
      ("cf_medic_tent", "cf_supply_depot", 0.8f),
      ("cf_repair_bay", "cf_vehicle_factory", 0.75f),
      ("cf_radar", "cf_air_defense", 0.9f),
      ("cf_helipad", "cf_airfield", 0.7f),
      ("cf_artillery", "cf_ground_tower", 0.98f),
      // 항목 33 궁극 전력(그래픽 작업 없음: 기존 절차 모델을 빌리고 칸만 만든다)
      ("ff_stormnest_carrier", "ff_skyfortress_bomber", 1.3f),
      ("ff_nest_drone", "ff_hawk_gunship", 0.5f),
      ("tf_wavecrest_lander", "ff_artillery_ship", 0.9f),
      ("ff_carrier_yard", "ff_airfield", 0.95f),
      ("cf_missile_silo", "cf_heavy_works", 0.95f),
      ("tf_assault_dock", "ff_shipyard", 0.95f),
    };

    public static string BorrowedId(string id) {
      foreach (var model in TemporaryModels) {
        if (model.Id == id) {
          return model.Borrow;
        }
      }
      return null;
    }

    public bool TryGet(string id, out Entry found) {
      foreach (var entry in Entries) {
        if (entry.Id == id) {
          found = entry;
          return true;
        }
      }
      foreach (var model in TemporaryModels) {
        if (model.Id == id && TryGet(model.Borrow, out found)) {
          found.Id = id;
          found.DisplayScale *= model.Scale;
          found.RingRadius *= model.Scale;
          found.BarHeight *= model.Scale;
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
      // 교체 모델(항목 30)은 슬롯의 팀 색 방식을 따른다.
      var view = root.GetComponentInParent<ArtView>();
      if (view != null && view.Slot != null && view.Model == root) {
        view.ApplySlotColor(color, powered);
        return;
      }
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
