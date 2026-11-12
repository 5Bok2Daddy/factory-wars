namespace FactoryWars.View {
  using System;
  using System.Collections.Generic;
  using UnityEngine;

  /// <summary>
  /// HUD 그림 칸(항목 31). VisualCatalog처럼 칸이 비어 있으면 지금 그림(Resources/FactoryWars/UI의 임시 스프라이트)을 쓴다.
  /// 산 UI 팩을 넣을 때는 칸에 스프라이트를 끌어 놓고 9-slice 테두리(Border)를 맞춘다. 순서: Game/Docs/UI-SWAP.md.
  /// </summary>
  [CreateAssetMenu(menuName = "Factory Wars/UI Catalog")]
  public class UiCatalog : ScriptableObject {
    public const string ResourcePath = "FactoryWars/UI/UiCatalog";

    [Serializable]
    public class Slot {
      public string Id;
      [Tooltip("비면 Fallback 임시 그림")]
      public Sprite Sprite;
      [Tooltip("9-slice 테두리(왼쪽, 아래, 오른쪽, 위 픽셀). 스프라이트 자체 테두리가 있으면 0으로 둔다.")]
      public Vector4 Border;
      [Tooltip("임시 그림: Resources/FactoryWars/UI 또는 Icons의 파일 이름")]
      public string Fallback;
      public string Note;
    }

    /// <summary>칸 이름과 임시 그림. 시험(Ui_CatalogHasAllSlots)이 이 목록을 기준으로 본다.</summary>
    public static readonly (string Id, string Fallback, string Note)[] Defaults = {
      ("panel", "UI/panel_rectangle", "패널 바탕(9-slice)"),
      ("button", "UI/button_square_depth", "버튼 보통"),
      ("button_pressed", "UI/button_square", "버튼 누름"),
      ("button_disabled", "UI/button_square", "버튼 비활성(잠김)"),
      ("icon_frame", "UI/button_square_header_small_square", "아이콘 테두리"),
      ("res_iron", "Icons/res_iron", "자원 아이콘: 철"),
      ("res_wood", "Icons/res_wood", "자원 아이콘: 목재"),
      ("res_steel", "Icons/res_steel", "자원 아이콘: 강철"),
      ("res_power", "Icons/res_power", "자원 아이콘: 전기"),
      ("res_population", "Icons/res_population", "자원 아이콘: 인구"),
      ("badge_small", "UI/bar_round_small", "크기 배지: 소"),
      ("badge_medium", "UI/bar_round_small", "크기 배지: 중"),
      ("badge_large", "UI/bar_round_small", "크기 배지: 대"),
      ("health_bar", "UI/bar_round_small", "체력 막대 바탕"),
      ("health_fill", "UI/bar_round_small", "체력 막대 채움"),
      ("minimap_frame", "UI/panel_rectangle", "미니맵 테두리"),
      ("tab", "UI/bar_round_large", "탭·상태 줄"),
      ("tooltip", "UI/panel_glass", "툴팁(잠김 이유)"),
      ("lock", "Icons/ui_lock", "자물쇠 표시"),
    };

    public List<Slot> Slots = new List<Slot>();
    [Tooltip("비면 기본 글꼴. Pretendard·Noto Sans KR(SIL OFL)을 넣으면 여기에 연결한다.")]
    public Font Font;

    static UiCatalog _current;
    static bool _loaded;

    public static UiCatalog Current {
      get {
        if (_loaded == false) {
          _current = Resources.Load<UiCatalog>(ResourcePath);
          _loaded = true;
        }
        return _current;
      }
      set {
        _current = value;
        _loaded = true;
      }
    }

    public Slot Get(string id) {
      foreach (var slot in Slots) {
        if (slot.Id == id) {
          return slot;
        }
      }
      return null;
    }

    public static List<Slot> DefaultSlots() {
      var list = new List<Slot>();
      foreach (var (id, fallback, note) in Defaults) {
        list.Add(new Slot { Id = id, Fallback = fallback, Note = note });
      }
      return list;
    }

    static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

    /// <summary>칸의 스프라이트. 칸이 채워져 있으면 그것, 아니면 임시 그림(UI는 9-slice로 만든다).</summary>
    public static Sprite SpriteFor(string id) {
      var slot = Current?.Get(id);
      if (slot != null && slot.Sprite != null) {
        if (slot.Border == Vector4.zero || slot.Sprite.border != Vector4.zero) {
          return slot.Sprite;
        }
        var key = "slot:" + id;
        if (Cache.TryGetValue(key, out var bordered) == false || bordered == null) {
          var source = slot.Sprite;
          bordered = Sprite.Create(source.texture, source.rect, new Vector2(0.5f, 0.5f), source.pixelsPerUnit, 0, SpriteMeshType.FullRect, slot.Border);
          Cache[key] = bordered;
        }
        return bordered;
      }
      var fallback = slot != null && string.IsNullOrEmpty(slot.Fallback) == false ? slot.Fallback : DefaultFallback(id);
      if (string.IsNullOrEmpty(fallback)) {
        return null;
      }
      if (fallback.StartsWith("UI/")) {
        return Ui.UiSprite(fallback.Substring(3));
      }
      return Ui.IconSprite(fallback.StartsWith("Icons/") ? fallback.Substring(6) : fallback);
    }

    static string DefaultFallback(string id) {
      foreach (var entry in Defaults) {
        if (entry.Id == id) {
          return entry.Fallback;
        }
      }
      return null;
    }

    public static Font FontOr(Font builtin) => Current != null && Current.Font != null ? Current.Font : builtin;
  }
}
