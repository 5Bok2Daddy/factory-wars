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
    public GameObject LumberMillSaw;
    public GameObject IronExtractorDrill;
    public GameObject LumberCargo;
    public GameObject IronCargo;
    public RuntimeAnimatorController EngineerLocomotion;

    [Serializable]
    public struct WarFxEntry {
      public string Key;
      public GameObject Prefab;
      public float Scale;
      public float Lifetime;
    }

    /// <summary>Jean Moreno War FX(WFX_*) 실전 전투 이펙트 프리팹 직접 바인딩 표.</summary>
    public WarFxEntry[] WarFx;

    public bool TryGetWarFx(string key, out WarFxEntry found) {
      if (WarFx != null && string.IsNullOrEmpty(key) == false) {
        for (var i = 0; i < WarFx.Length; i++) {
          if (WarFx[i].Key == key && WarFx[i].Prefab != null) {
            found = WarFx[i];
            return true;
          }
        }
      }
      found = default;
      return false;
    }

    /// <summary>
    /// 항목 31 새 유닛의 임시 모델: 기존 모델을 빌려 크기만 바꾼다. 에셋 팩으로 VisualCatalog 칸(unit/새 id)을 채우면 그 모델이 우선한다.
    /// </summary>
    public static readonly (string Id, string Borrow, float Scale)[] TemporaryModels = {
      ("ff_scout_buggy", "cf_thunder_aa", 0.7f),
      // Task 39 card 4: PMC goods and the cargo truck borrow a donor entry; unit/pmc_* and unit/merc_cargo_truck slots give the model.
      ("pmc_operator_squad", "ff_ranger_squad", 1f),
      ("pmc_marksman", "ff_sniper", 1f),
      ("pmc_tank", "cf_dragonscale_tank", 1f),
      ("pmc_attack_heli", "ff_hawk_gunship", 1f),
      ("pmc_attack_drone", "ff_hawk_gunship", 0.4f),
      ("pmc_armored_suv", "ff_scout_buggy", 1f),
      ("pmc_scout_heli", "ff_hawk_gunship", 0.6f),
      ("merc_cargo_truck", "cf_apc_transport", 1f),
      ("logistics_truck", "cf_apc_transport", 1f),
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
      // 항목 34-C 주둔 저격수(팩 Ghillie 캐릭터가 빈 슬롯 후보: 지금은 보병 모델을 빌린다)
      ("ff_sortie_fighter", "ff_hawk_gunship", 0.8f), ("ff_sortie_striker", "ff_skyfortress_bomber", 0.6f), ("ff_sortie_transport", "ff_skyfortress_bomber", 0.5f),
      ("cf_sortie_fighter", "cf_gale_gunship", 0.8f), ("cf_sortie_striker", "ff_skyfortress_bomber", 0.6f), ("cf_sortie_transport", "ff_skyfortress_bomber", 0.5f),
      ("ff_sniper", "ff_ranger_squad", 0.85f),
      ("cf_sniper", "cf_militia", 0.85f),
      // 항목 33 궁극 전력(그래픽 작업 없음: 기존 절차 모델을 빌리고 칸만 만든다)
      ("ff_stormnest_carrier", "ff_skyfortress_bomber", 1.3f),
      ("ff_nest_drone", "ff_hawk_gunship", 0.5f),
      ("tf_wavecrest_lander", "ff_artillery_ship", 0.9f),
      ("ff_carrier_yard", "ff_airfield", 0.95f),
      ("cf_missile_silo", "cf_heavy_works", 0.95f),
      ("tf_assault_dock", "ff_shipyard", 0.95f),
      // 신규 공격 유닛 4종(양 진영) & 패트리어트 대탄도 요격 포대(T2.5).
      ("ff_at_rocket", "ff_ranger_squad", 1.0f),
      ("cf_at_rocket", "cf_militia", 1.0f),
      ("ff_breacher", "cf_flame_squad", 1.05f),
      ("cf_breacher", "cf_flame_squad", 1.05f),
      ("ff_mlrs_artillery", "cf_longwall_mlrs", 0.9f),
      ("cf_rocket_artillery", "cf_longwall_mlrs", 0.9f),
      ("ff_mobile_sam", "cf_thunder_aa", 0.95f),
      ("cf_mobile_sam", "cf_thunder_aa", 0.95f),
      ("ff_patriot_turret", "ff_air_defense", 1.15f),
      ("cf_patriot_turret", "cf_air_defense", 1.15f),
      ("ff_lumber_mill", "ff_collection_yard", 1f),
      ("cf_lumber_mill", "cf_collection_yard", 1f),
      ("ff_iron_extractor", "ff_processor", 1f),
      ("cf_iron_extractor", "cf_processor", 1f),
      ("ff_mlrs_battery", "MLRS_Battery", 1f),
      ("cf_mlrs_battery", "MLRS_Battery", 1f),
      // 플레이어 조종 수송 유닛(APC · 수륙양용 상륙정 · 전술 수송헬기).
      ("cf_apc_transport", "ff_field_support", 1.0f),
      ("ff_landing_craft", "ff_scout_boat", 1.35f),
      ("cf_landing_craft", "cf_scout_boat", 1.35f),
      ("ff_transport_heli", "ff_hawk_gunship", 1.0f),
      ("cf_transport_heli", "cf_gale_gunship", 1.0f),
      // Task 46: sea barrier boom and watchtower (building/sea_barrier, building/sea_watchtower slots give the pack models).
      ("sea_barrier", "ff_ground_tower", 1f),
      ("sea_watchtower", "ff_ground_tower", 1f),
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

    public static string MaterialName(int color, bool powered) {
      var safe = ((color % Palette.Count) + Palette.Count) % Palette.Count;
      return $"{TeamPrefix}{safe}{(powered ? "On" : "Off")}";
    }

    public static Material Team(int color, bool powered) {
      var safe = ((color % Palette.Count) + Palette.Count) % Palette.Count;
      var index = safe * 2 + (powered ? 1 : 0);
      return Materials[index] != null ? Materials[index] : Materials[index] = Resources.Load<Material>(Root + MaterialName(safe, powered));
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
