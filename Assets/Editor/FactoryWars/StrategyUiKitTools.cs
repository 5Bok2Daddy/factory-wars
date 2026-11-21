namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// 구매한 Strategy UI Kit(Genetic Gorilla)를 HUD 칸(UiCatalog)과 색(UiTheme)에 연결한다. 키트 원본은 수정하지 않는다.
  /// 키트는 Assets/ThirdParty/StrategyGUIKit/(Git 제외)에 둔다. 키트가 없는 체크아웃에서는 칸이 비어 기존 임시 그림을 쓴다.
  /// 테마: 짙은 군수 산업 패널 + Cyan 강조(키트 Cyan 테마). 글꼴은 한국어 때문에 기존 글꼴을 유지한다(키트 Inter·Rajdhani는 라틴 전용).
  /// </summary>
  public static class StrategyUiKitTools {
    const string Kit = "Assets/ThirdParty/StrategyGUIKit/Sprite sheets/";

    /// <summary>칸 → (시트, 스프라이트). 흰 스프라이트는 테마 색으로 물들인다.</summary>
    static readonly (string Slot, string Sheet, string Sprite)[] Map = {
      ("panel", "Sheet 1", "smooth_square_white"),
      ("button", "Sheet 1", "btn_4_white"),
      ("button_pressed", "Sheet 1", "btn_4_white"),
      ("button_disabled", "Sheet 1", "btn_4_white"),
      ("icon_frame", "Sheet 1", "frame_smooth_square"),
      ("tooltip", "Sheet 1", "smooth_square_white"),
      ("tab", "Sheet 1", "btn_3_white"),
      ("minimap_frame", "Sheet 1", "frame_square_thick"),
      ("res_iron", "Sheet 4", "icon_stone"),
      ("res_wood", "Sheet 4", "icon_log"),
      ("res_steel", "Sheet 4", "icon_ingot"),
      ("res_power", "Sheet 4", "icon_lightning"),
      ("res_population", "Sheet 4", "icon_people"),
      ("lock", "Sheet 4", "icon_lock"),
    };

    public static bool KitPresent => AssetDatabase.LoadAssetAtPath<Texture2D>(Kit + "Sheet 1.png") != null;

    [MenuItem("Tools/Factory Wars/UI/Apply Strategy UI Kit (Cyan)")]
    public static void Apply() {
      if (!KitPresent) {
        Debug.LogWarning("[FW-UIKit] Assets/ThirdParty/StrategyGUIKit 없음: 구매 키트를 이 경로에 넣은 뒤 다시 실행한다.");
        return;
      }
      var catalog = Resources.Load<UiCatalog>(UiCatalog.ResourcePath);
      var theme = Resources.Load<UiTheme>(UiTheme.ResourcePath);
      if (catalog == null || theme == null) {
        throw new System.InvalidOperationException("UiCatalog/UiTheme 에셋 없음(Tools/Factory Wars/Assets/UI 칸 만들기 먼저)");
      }
      var sprites = new Dictionary<string, Sprite>();
      foreach (var sheet in Map.Select(m => m.Sheet).Distinct()) {
        foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(Kit + sheet + ".png").OfType<Sprite>()) {
          sprites[sheet + "/" + sprite.name] = sprite;
        }
      }
      var assigned = 0;
      foreach (var (slotId, sheet, name) in Map) {
        var slot = catalog.Get(slotId);
        if (slot == null) {
          slot = new UiCatalog.Slot { Id = slotId };
          catalog.Slots.Add(slot);
        }
        if (sprites.TryGetValue(sheet + "/" + name, out var sprite) == false) {
          throw new System.InvalidOperationException($"키트 스프라이트 없음: {sheet}/{name}");
        }
        slot.Sprite = sprite;
        slot.Border = Vector4.zero;
        slot.Note = $"Strategy UI Kit {sheet}/{name}";
        assigned++;
      }
      // 키트 Cyan 테마 색(시트의 Cyan 버튼 #03C2B2, 금색 #D4A21D, 짙은 패널 #2B3139).
      theme.Panel = new Color(0.13f, 0.16f, 0.19f, 0.94f);
      theme.PanelText = new Color(0.90f, 0.95f, 0.96f, 1f);
      // 버튼은 짙은 슬레이트(전장을 덜 가림), 누름·강조·제목줄만 Cyan.
      theme.Header = new Color(0.00f, 0.50f, 0.47f, 1f);
      theme.IconPlate = new Color(0.80f, 0.90f, 0.90f, 0.92f);
      theme.Button = new Color(0.20f, 0.25f, 0.29f, 0.95f);
      theme.ButtonPressed = new Color(0.01f, 0.76f, 0.70f, 1f);
      theme.ButtonDisabled = new Color(0.15f, 0.17f, 0.19f, 0.85f);
      theme.ButtonText = new Color(0.86f, 0.97f, 0.96f, 1f);
      theme.DisabledText = new Color(0.62f, 0.66f, 0.70f, 1f);
      theme.Accent = new Color(0.01f, 0.76f, 0.70f, 1f);
      theme.Warning = new Color(0.83f, 0.63f, 0.11f, 1f);
      theme.Danger = new Color(0.90f, 0.32f, 0.26f, 1f);
      EditorUtility.SetDirty(catalog);
      EditorUtility.SetDirty(theme);
      AssetDatabase.SaveAssets();
      Debug.Log($"[FW-UIKit] {assigned}칸 연결, Cyan 테마 적용");
    }
  }
}
