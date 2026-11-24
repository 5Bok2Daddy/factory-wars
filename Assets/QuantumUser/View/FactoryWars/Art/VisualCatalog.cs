namespace FactoryWars.View {
  using System;
  using System.Collections.Generic;
  using UnityEngine;

  /// <summary>비주얼 슬롯 분류. 슬롯 Id 앞부분과 같다(unit/…, building/…, dress/…, fx/…).</summary>
  public enum VisualCategory { Unit, Building, Resource, City, Terrain, Prop, Effect }

  /// <summary>팀 색을 모델에 입히는 방법.</summary>
  public enum TeamColorMode {
    /// <summary>팀 색을 입히지 않는다(자원·소품·지형).</summary>
    None,
    /// <summary>머티리얼 이름에 TeamMaterialToken이 들어간 칸만 팀 색 머티리얼(ArtP*)로 바꾼다. 지금 절차 모델과 같은 방식.</summary>
    MaterialName,
    /// <summary>TeamMaterialSlots에 적은 서브메쉬 칸을 팀 색 머티리얼로 바꾼다.</summary>
    MaterialSlots,
    /// <summary>팔레트 교체: 모든 머티리얼의 기본 텍스처를 TeamTextures[팀 색 번호]로 바꾼다(Synty처럼 아틀라스 한 장을 쓰는 팩).</summary>
    PaletteTexture,
    /// <summary>모든 머티리얼의 기본 색(_BaseColor)에 팀 색을 곱한다.</summary>
    Tint,
  }

  /// <summary>실존 무기·차량 닮음 확인 상태(가상 진영 원칙). 교체 검증 테스트가 Unchecked·Rejected 슬롯을 경고한다.</summary>
  public enum RealWorldCheck { Unchecked, Clear, Modified, Rejected }

  /// <summary>
  /// 게임 요소 하나의 그래픽 슬롯. 프리팹이 비어 있으면 지금 모델(ViewCatalog·KenneyAssetMap·절차 메쉬)을 그대로 쓴다.
  /// 화면 전용이다. 시뮬레이션의 크기·사거리·충돌은 이 값과 무관하다.
  /// </summary>
  [Serializable]
  public class VisualSlot {
    /// <summary>"unit/ff_eagle_tank", "building/cf_barracks", "dress/CitySmall", "fx/explosion" 같은 이름.</summary>
    public string Id;
    public VisualCategory Category;
    /// <summary>비어 있으면 대체 모델. 여러 개면 소품처럼 해시로 고른다(유닛·건물은 첫 번째만 쓴다).</summary>
    public List<GameObject> Prefabs = new List<GameObject>();
    /// <summary>모델 1단위 → 월드 미터. 0이면 AutoFit 또는 1.</summary>
    public float Scale = 1f;
    /// <summary>true면 판정 크기(유닛 반경·건물 부지)에 맞춰 Scale을 자동으로 정한다.</summary>
    public bool AutoFit = true;
    /// <summary>모델 앞이 +Z가 아니면 돌리는 각도(도).</summary>
    public float YawOffset;
    /// <summary>바닥 가운데를 원점으로 옮기는 보정(모델 좌표). 가져오기 도구가 바운드로 잰다.</summary>
    public Vector3 PivotOffset;
    /// <summary>모델 좌표의 크기(바운드). 가져오기 도구가 잰다.</summary>
    public Vector3 Size;
    public TeamColorMode TeamColor = TeamColorMode.None;
    public string TeamMaterialToken = "Team";
    public int[] TeamMaterialSlots = new int[0];
    /// <summary>PaletteTexture용. 순서는 Palette 색 번호.</summary>
    public Texture2D[] TeamTextures = new Texture2D[0];
    /// <summary>비어 있으면 프리팹의 Animator 컨트롤러를 그대로 쓴다. 둘 다 없으면 애니메이션 없이 움직인다.</summary>
    public RuntimeAnimatorController Animator;
    /// <summary>사망 애니메이션을 보여 줄 시간(초). 0이면 바로 지운다.</summary>
    public float DeathSeconds = 1.5f;
    /// <summary>파티클 같은 효과 슬롯이 스스로 사라지는 시간(초).</summary>
    public float Lifetime = 2f;
    public RealWorldCheck RealWorld = RealWorldCheck.Unchecked;
    /// <summary>후보 프리팹 경로·닮은 실물·변형 내용 등 메모.</summary>
    public string Notes;
    /// <summary>
    /// 에셋 팩 모델의 움직이는 부분(항목 32). 팩 파일은 고치지 않으므로, 모델 안 자식 이름에 이 글자가 들어 있는 것을 찾아 화면 코드가 움직인다.
    /// 포탑(조준 회전), 포신(반동·포구), 돌아가는 부분(회전축은 메쉬가 가장 얇은 축).
    /// </summary>
    public string TurretPart;
    /// <summary>2026-11-24 (AGY fix A): optional destroyed model left where the unit died (e.g. Synty *_Destroyed). Empty = generic wreck.</summary>
    public GameObject WreckPrefab;
    public string BarrelPart;
    public string[] SpinParts = new string[0];
    /// <summary>사격 효과 위치. MuzzlePart가 비어 있으면 모델 원점 + MuzzleOffset(모델 좌표).</summary>
    public string MuzzlePart;
    public Vector3 MuzzleOffset;
    /// <summary>품질(낮음·보통·높음)별로 보여 줄 분대원 수. 자식 이름이 Member0, Member1…인 분대 프리팹에만 쓴다. 비어 있으면 모두 보인다.</summary>
    public int[] MembersByQuality = new int[0];
    /// <summary>
    /// 팩 모델은 팀 색을 칠하지 않으므로(사용자 결정 2026-11-14), 소유자를 알아볼 수 있게 바닥에 얇은 팀 색 고리를 둔다(모델 밖, 화면 전용).
    /// </summary>
    public bool OwnerRing;

    public GameObject Prefab => Prefabs != null && Prefabs.Count > 0 ? Prefabs[0] : null;
    public bool IsFilled => Prefabs != null && Prefabs.Exists(p => p != null);
  }

  /// <summary>
  /// 그래픽 교체 표(항목 30). 사용자가 산 에셋 팩의 프리팹을 슬롯에 넣으면 코드 수정 없이 화면이 바뀐다.
  /// `Tools/Factory Wars/Assets/…` 메뉴가 슬롯을 만들고 CSV로 주고받는다. 사용법: Game/Docs/ASSET-SWAP.md
  /// </summary>
  [CreateAssetMenu(menuName = "FactoryWars/Visual Catalog")]
  public class VisualCatalog : ScriptableObject {
    public const string ResourcePath = "FactoryWars/Visual/VisualCatalog";

    public List<VisualSlot> Slots = new List<VisualSlot>();
    /// <summary>false면 슬롯을 모두 무시하고 지금 모델을 쓴다(교체 전후 비교용).</summary>
    public bool Enabled = true;

    [NonSerialized] Dictionary<string, VisualSlot> _lookup;
    [NonSerialized] readonly Dictionary<DressKind, KenneyAssetMap.Entry> _dress = new Dictionary<DressKind, KenneyAssetMap.Entry>();

    static VisualCatalog _current;
    static bool _loaded;

    /// <summary>Resources의 표. 없으면 null이고 모든 화면이 지금 모델을 쓴다.</summary>
    public static VisualCatalog Current {
      get {
        if (_loaded == false) {
          _loaded = true;
          _current = Resources.Load<VisualCatalog>(ResourcePath);
        }
        return _current;
      }
      set {
        _current = value;
        _loaded = true;
      }
    }

    /// <summary>ViewCatalog 항목 이름 → 슬롯 Id. 철광산은 자원, 다리는 지형 조각이다.</summary>
    public static string ViewSlotId(string viewId, bool unit) {
      if (unit) {
        return UnitId(viewId);
      }
      switch (viewId) {
        case "iron_mine": return "resource/iron_mine";
        case "bridge": return "terrain/bridge";
        default: return BuildingId(viewId);
      }
    }

    public static string UnitId(string specName) => "unit/" + specName;
    public static string BuildingId(string specName) => "building/" + specName;
    public static string DressId(DressKind kind) => "dress/" + kind;
    public static string FxId(string name) => "fx/" + name;

    public void Invalidate() {
      _lookup = null;
      _dress.Clear();
    }

    public VisualSlot Get(string id) {
      if (_lookup == null) {
        _lookup = new Dictionary<string, VisualSlot>();
        foreach (var slot in Slots) {
          if (slot != null && string.IsNullOrEmpty(slot.Id) == false) {
            _lookup[slot.Id] = slot;
          }
        }
      }
      return _lookup.TryGetValue(id, out var found) ? found : null;
    }

    /// <summary>프리팹이 들어 있는 슬롯만 돌려준다.</summary>
    public static bool TryFilled(string id, out VisualSlot slot) {
      var catalog = Current;
      slot = catalog != null && catalog.Enabled ? catalog.Get(id) : null;
      if (slot != null && slot.IsFilled == false) {
        slot = null;
      }
      return slot != null;
    }

    /// <summary>
    /// 소품·도시 건물·지형 조각 슬롯을 KenneyAssetMap 항목 모양으로 바꿔 준다. 채워진 슬롯이 있으면 Kenney 대신 이것을 쓴다.
    /// 기존 항목이 있으면 회전·품질별 남김 비율·그림자 설정은 그대로 물려받는다.
    /// </summary>
    public static KenneyAssetMap.Entry DressOverride(DressKind kind, KenneyAssetMap.Entry fallback) {
      if (TryFilled(DressId(kind), out var slot) == false) {
        return null;
      }
      var catalog = Current;
      if (catalog._dress.TryGetValue(kind, out var cached)) {
        return cached;
      }
      var entry = new KenneyAssetMap.Entry {
        Kind = kind,
        Scale = slot.Scale > 0f ? slot.Scale : 1f,
        ScaleJitter = fallback != null ? fallback.ScaleJitter : Vector2.one,
        Yaw = fallback != null ? fallback.Yaw : new Vector2(0f, 360f),
        SnapYaw90 = fallback != null && fallback.SnapYaw90,
        Fit = fallback != null && fallback.Fit,
        Shadows = fallback == null || fallback.Shadows,
        LowKeep = fallback != null ? fallback.LowKeep : 1f,
        MediumKeep = fallback != null ? fallback.MediumKeep : 1f,
      };
      foreach (var prefab in slot.Prefabs) {
        if (prefab != null) {
          var (offset, size) = Measure(prefab, slot);
          entry.Variants.Add(new KenneyAssetMap.Variant { Prefab = prefab, Weight = 1f, Offset = offset, Size = size });
        }
      }
      catalog._dress[kind] = entry;
      return entry;
    }

    /// <summary>슬롯에 잰 값이 있으면 그것을, 없으면 렌더러 바운드로 잰다.</summary>
    public static (Vector3 Offset, Vector3 Size) Measure(GameObject prefab, VisualSlot slot) {
      if (slot != null && slot.Size.sqrMagnitude > 0f) {
        return (slot.PivotOffset, slot.Size);
      }
      var bounds = LocalBounds(prefab);
      return (new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z), bounds.size);
    }

    /// <summary>프리팹 루트 좌표의 렌더러 바운드(메쉬 정점 기준). 렌더러가 없으면 단위 상자.</summary>
    public static Bounds LocalBounds(GameObject prefab) {
      var has = false;
      var bounds = new Bounds(Vector3.zero, Vector3.one);
      var root = prefab.transform.worldToLocalMatrix * Matrix4x4.TRS(prefab.transform.position, prefab.transform.rotation, prefab.transform.lossyScale);
      foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true)) {
        if (filter.sharedMesh != null) {
          Encapsulate(ref bounds, ref has, root * filter.transform.localToWorldMatrix, filter.sharedMesh.bounds);
        }
      }
      foreach (var skinned in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
        if (skinned.sharedMesh != null) {
          Encapsulate(ref bounds, ref has, root * skinned.transform.localToWorldMatrix, skinned.sharedMesh.bounds);
        }
      }
      return bounds;
    }

    static void Encapsulate(ref Bounds bounds, ref bool has, Matrix4x4 matrix, Bounds local) {
      var min = local.min;
      var max = local.max;
      for (var i = 0; i < 8; i++) {
        var corner = matrix.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z));
        if (has == false) {
          bounds = new Bounds(corner, Vector3.zero);
          has = true;
        } else {
          bounds.Encapsulate(corner);
        }
      }
    }

    /// <summary>
    /// 유닛·건물 슬롯을 ViewCatalog 항목에 덮어쓴다. 판정 크기(ringRadius = 유닛 반경·건물 부지 반)에 맞춰 배율을 정하고,
    /// 선택 고리·체력바 높이도 새 모델 크기를 따른다. 채워진 슬롯이 없으면 entry를 그대로 둔다.
    /// </summary>
    public static bool Override(string id, ref ViewCatalog.Entry entry, out VisualSlot slot) {
      if (TryFilled(id, out slot) == false) {
        return false;
      }
      var (offset, size) = Measure(slot.Prefab, slot);
      var footprint = Mathf.Max(0.01f, Mathf.Max(size.x, size.z));
      var target = entry.RingRadius > 0f ? entry.RingRadius * 2f * 0.9f : footprint;
      var scale = slot.AutoFit ? target / footprint : (slot.Scale > 0f ? slot.Scale : 1f);
      entry.Prefab = slot.Prefab;
      entry.DisplayScale = scale;
      if (slot.AutoFit == false && entry.Unit) {
        var diagonal = new Vector2(size.x, size.z).magnitude * scale;
        entry.RingRadius = Mathf.Max(0.65f, diagonal * 0.48f);
      }
      entry.Center = new Vector2(-offset.x, -offset.z);
      // 공중 유닛은 모델이 고도만큼 떠 있으므로 체력바도 그 위에 둔다.
      entry.BarHeight = Mathf.Max(entry.BarHeight * 0.5f, size.y * scale + entry.Altitude + 0.6f);
      entry.MuzzlePart = string.IsNullOrEmpty(entry.MuzzlePart) ? "" : entry.MuzzlePart;
      // 팩 모델은 지금 모델의 포구·굴뚝 자리를 쓰지 않는다. 슬롯 값으로 바꾼다.
      entry.MuzzlePart = slot.MuzzlePart ?? "";
      entry.Muzzle = slot.MuzzleOffset;
      entry.Smoke = new Vector3[0];
      return true;
    }

    /// <summary>슬롯의 팀 색 방식대로 색을 입힌다. 같은 (슬롯, 색)은 머티리얼을 나눠 써 SRP Batcher를 깨지 않는다.</summary>
    public static void ApplyTeamColor(GameObject model, VisualSlot slot, int color, bool powered) {
      switch (slot.TeamColor) {
        case TeamColorMode.None:
          return;
        case TeamColorMode.MaterialName:
          foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) {
            var materials = renderer.sharedMaterials;
            for (var i = 0; i < materials.Length; i++) {
              if (materials[i] != null && (materials[i].name.Contains(slot.TeamMaterialToken) || materials[i].name.StartsWith(ArtLook.TeamPrefix))) {
                materials[i] = ArtLook.Team(color, powered);
              }
            }
            renderer.sharedMaterials = materials;
          }
          return;
        case TeamColorMode.MaterialSlots:
          foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) {
            var materials = renderer.sharedMaterials;
            foreach (var index in slot.TeamMaterialSlots ?? new int[0]) {
              if (index >= 0 && index < materials.Length) {
                materials[index] = ArtLook.Team(color, powered);
              }
            }
            renderer.sharedMaterials = materials;
          }
          return;
        case TeamColorMode.PaletteTexture:
        case TeamColorMode.Tint:
          foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) {
            var materials = renderer.sharedMaterials;
            for (var i = 0; i < materials.Length; i++) {
              materials[i] = Variant(materials[i], slot, color);
            }
            renderer.sharedMaterials = materials;
          }
          return;
      }
    }

    static readonly Dictionary<(Material, int, bool), Material> Variants = new Dictionary<(Material, int, bool), Material>();
    static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    static readonly int MainTex = Shader.PropertyToID("_MainTex");
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    static Material Variant(Material source, VisualSlot slot, int color) {
      if (source == null) {
        return null;
      }
      var tint = slot.TeamColor == TeamColorMode.Tint;
      var key = (source, color, tint);
      if (Variants.TryGetValue(key, out var made) && made != null) {
        return made;
      }
      made = new Material(source) { name = $"{source.name}_team{color}" };
      if (tint) {
        if (made.HasProperty(BaseColor)) {
          made.SetColor(BaseColor, made.GetColor(BaseColor) * (Color)Palette.Colors[((color % Palette.Count) + Palette.Count) % Palette.Count]);
        }
      } else if (slot.TeamTextures != null && color >= 0 && color < slot.TeamTextures.Length && slot.TeamTextures[color] != null) {
        if (made.HasProperty(BaseMap)) {
          made.SetTexture(BaseMap, slot.TeamTextures[color]);
        }
        if (made.HasProperty(MainTex)) {
          made.SetTexture(MainTex, slot.TeamTextures[color]);
        }
      }
      Variants[key] = made;
      return made;
    }

    /// <summary>효과 슬롯이 채워져 있으면 그 자리에 만들고 Lifetime 뒤에 지운다. 만들었으면 true(기존 절차 효과는 건너뛴다).</summary>
    public static bool SpawnEffect(string name, Vector3 position, Vector3 forward, float size) {
      if (TryFilled(FxId(name), out var slot) == false) {
        return false;
      }
      var rotation = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward) : Quaternion.identity;
      var item = Instantiate(slot.Prefab, position, rotation);
      item.transform.localScale *= size * (slot.Scale > 0f ? slot.Scale : 1f);
      Destroy(item, Mathf.Max(0.1f, slot.Lifetime));
      return true;
    }
  }
}
