namespace FactoryWars.View {
  using System.Collections.Generic;
  using UnityEngine;
  using UnityEngine.Events;
  using UnityEngine.EventSystems;
  using UnityEngine.UI;

  /// <summary>uGUI를 코드로 만드는 도구. 아트가 들어오는 챕터 6 전까지 쓴다.</summary>
  /// <summary>Ui.Modal이 붙인다. 창과 함께 켜지고 꺼지는 어두운 뒤판.</summary>
  public class ModalBackdrop : MonoBehaviour {
    RectTransform _dim;

    void OnEnable() {
      var parent = transform.parent;
      if (parent == null) {
        return;
      }
      if (_dim == null) {
        _dim = Ui.Rect(name + "Backdrop", parent);
        Ui.Stretch(_dim, -4000f);
        var image = _dim.gameObject.AddComponent<Image>();
        image.color = new Color(0.02f, 0.04f, 0.06f, 0.62f);
        image.raycastTarget = false;
      }
      _dim.gameObject.SetActive(true);
      _dim.SetAsLastSibling();
      transform.SetAsLastSibling();
    }

    void OnDisable() {
      if (_dim != null) {
        _dim.gameObject.SetActive(false);
      }
    }

    void OnDestroy() {
      if (_dim != null) {
        Destroy(_dim.gameObject);
      }
    }
  }

  public static class Ui {
    /// <summary>29 2-5: 6.1인치 431dpi 기준 9mm 규격(154px) 보장</summary>
    /// <remarks>
    /// 2026-11-22 HUD 개편: 154(9mm) → 128. 6.1인치 2400x1080(431dpi)에서 7.5mm, 약 48dp(Material)·44pt(iOS) 이상.
    /// 사용자 지시("44~48 논리 단위 또는 같은 물리 크기")에 따라 낮췄다. 더 크게 쓰려면 설정의 UI 크기를 올린다.
    /// </remarks>
    public const float MinButtonSize = 128f;
    public static readonly Vector2 ReferenceResolution = new Vector2(1920, 1080);

    // 항목 31: 색은 UiTheme 에셋에서 읽는다.
    public static Color PanelColor => UiTheme.Current.Panel;
    public static Color ButtonColor => UiTheme.Current.Button;
    public static Color ButtonTextColor => UiTheme.Current.ButtonText;
    public static Color DisabledColor => UiTheme.Current.ButtonDisabled;

    static Font _font;

    static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();

    public static Font Font => _font != null ? _font : _font = UiCatalog.FontOr(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

    public static Canvas CreateCanvas(string name, int sortOrder = 0) {
      var root = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
      var canvas = root.GetComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      canvas.sortingOrder = sortOrder;
      var scaler = root.GetComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      scaler.referenceResolution = ReferenceResolution / PlayerOptions.UiScale;
      // 16:9~21:9에서 높이를 맞추면 버튼 크기가 일정하고 넓은 화면은 좌우만 늘어난다.
      scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
      scaler.matchWidthOrHeight = 1f;
      if (EventSystem.current == null) {
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
      }
      return canvas;
    }

    /// <summary>노치를 피하도록 화면 안전 영역에 맞춘 틀을 만든다.</summary>
    public static RectTransform SafeArea(Canvas canvas) {
      var area = Rect("SafeArea", canvas.transform);
      var safe = Screen.safeArea;
      var width = Mathf.Max(Screen.width, 1);
      var height = Mathf.Max(Screen.height, 1);
      area.anchorMin = new Vector2(safe.xMin / width, safe.yMin / height);
      area.anchorMax = new Vector2(safe.xMax / width, safe.yMax / height);
      area.offsetMin = Vector2.zero;
      area.offsetMax = Vector2.zero;
      return area;
    }

    public static RectTransform Rect(string name, Transform parent) {
      var item = new GameObject(name, typeof(RectTransform));
      item.transform.SetParent(parent, false);
      return (RectTransform)item.transform;
    }

    /// <summary>앵커 한 점 기준으로 놓는다. anchor는 (0,0) 왼쪽 아래부터 (1,1) 오른쪽 위까지다.</summary>
    public static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size) {
      rect.anchorMin = anchor;
      rect.anchorMax = anchor;
      rect.pivot = anchor;
      rect.anchoredPosition = position;
      rect.sizeDelta = size;
    }

    public static void Stretch(RectTransform rect, float padding = 0f) {
      rect.anchorMin = Vector2.zero;
      rect.anchorMax = Vector2.one;
      rect.offsetMin = new Vector2(padding, padding);
      rect.offsetMax = new Vector2(-padding, -padding);
    }

    static readonly Dictionary<string, Sprite> UiSprites = new Dictionary<string, Sprite>();

    public static Sprite UiSprite(string name, Vector4? border = null) {
      if (UiSprites.TryGetValue(name, out var sprite) == false || sprite == null) {
        var tex = Resources.Load<Texture2D>($"FactoryWars/UI/{name}");
        if (tex != null) {
          var b = border ?? (name.Contains("bar_round") ? new Vector4(12, 12, 12, 12) : new Vector4(14, 18, 14, 14));
          sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, b);
          UiSprites[name] = sprite;
        }
      }
      return sprite;
    }

    public static RectTransform Panel(string name, Transform parent, Color? color = null) {
      var rect = Rect(name, parent);
      var img = rect.gameObject.AddComponent<Image>();
      var sprite = UiCatalog.SpriteFor("panel");
      if (sprite != null) {
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
      }
      img.color = color ?? PanelColor;
      return rect;
    }

    public static RectTransform Capsule(string name, Transform parent, Color? color = null) {
      var rect = Rect(name, parent);
      var img = rect.gameObject.AddComponent<Image>();
      var sprite = UiCatalog.SpriteFor("tab");
      if (sprite != null) {
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
      }
      img.color = color ?? PanelColor;
      return rect;
    }

    /// <summary>
    /// 창(모달) 뒤를 어둡게 하고, 창이 켜질 때 같은 부모의 맨 앞(HUD 띠·알림 위)으로 올린다.
    /// 화면 전용 장식이라 입력은 막지 않는다(raycastTarget 끔: 기존 조작 흐름 유지).
    /// </summary>
    public static void Modal(RectTransform panel) {
      ModalCore(panel);
    }

    /// <summary>설정의 UI 크기를 지금 켜진 캔버스(Ui.CreateCanvas로 만든 것)에 바로 적용한다.</summary>
    public static void ApplyScale() {
      foreach (var scaler in Object.FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None)) {
        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) {
          scaler.referenceResolution = ReferenceResolution / PlayerOptions.UiScale;
        }
      }
    }

    static void ModalCore(RectTransform panel) {
      if (panel.GetComponent<ModalBackdrop>() == null) {
        panel.gameObject.AddComponent<ModalBackdrop>();
      }
      // 창 바탕은 불투명하게: 뒤의 HUD 글자가 비쳐 읽기 어려워지지 않게 한다.
      var image = panel.GetComponent<Image>();
      if (image != null) {
        var color = image.color;
        color.a = 1f;
        image.color = color;
      }
    }

    /// <summary>창 맨 위 제목줄(테마 Header 색 캡슐 + 큰 글자). 도크트린·외교·설정·일시정지 창이 같은 모양을 쓴다.</summary>
    public static Text Header(string name, Transform parent, string value, float width, float top = -14f, float height = 60f) {
      var header = Capsule(name, parent, UiTheme.Current.Header);
      Place(header, new Vector2(0.5f, 1), new Vector2(0f, top), new Vector2(width, height));
      header.GetComponent<Image>().raycastTarget = false;
      var title = Label("Title", header, value, UiTheme.Current.FontLarge, TextAnchor.MiddleCenter);
      Stretch(title.rectTransform, 8f);
      return title;
    }

    public static Text Label(string name, Transform parent, string value, int size, TextAnchor alignment = TextAnchor.MiddleLeft) {
      var rect = Rect(name, parent);
      var text = rect.gameObject.AddComponent<Text>();
      text.font = Font;
      text.fontSize = size;
      text.alignment = alignment;
      text.color = Color.white;
      text.text = value;
      text.supportRichText = true;
      text.raycastTarget = false;
      text.horizontalOverflow = HorizontalWrapMode.Wrap;
      text.verticalOverflow = VerticalWrapMode.Truncate;
      return text;
    }

    public static Button Button(string name, Transform parent, string value, UnityAction onClick, int size = 28) {
      var rect = Rect(name, parent);
      var img = rect.gameObject.AddComponent<Image>();
      var sprite = UiCatalog.SpriteFor("button");
      if (sprite != null) {
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
      }
      img.color = ButtonColor;
      var button = rect.gameObject.AddComponent<Button>();
      button.targetGraphic = img;
      if (onClick != null) {
        button.onClick.AddListener(onClick);
      }
      button.onClick.AddListener(() => GameAudio.Play(SoundEvent.Click));
      var label = Label("Label", rect, value, size, TextAnchor.MiddleCenter);
      label.color = ButtonTextColor;
      // 항목 31: 긴 글자는 줄여서 칸 안에 넣는다(최소 14).
      label.resizeTextForBestFit = true;
      label.resizeTextMinSize = Mathf.Min(14, size);
      label.resizeTextMaxSize = size;
      Stretch(label.rectTransform, 6f);
      return button;
    }

    public static void SetLabel(Button button, string value) {
      button.GetComponentInChildren<Text>().text = value;
    }

    /// <summary>스프라이트 아이콘. 이름은 Resources/FactoryWars/Icons의 파일 이름이다.</summary>
    public static Sprite IconSprite(string name) {
      if (Icons.TryGetValue(name, out var sprite) == false) {
        sprite = Resources.Load<Sprite>($"FactoryWars/Icons/{name}");
        Icons[name] = sprite;
      }
      return sprite;
    }

    public static Image Icon(string name, Transform parent, string icon, Vector2 anchor, Vector2 position, Vector2 size) {
      var rect = Rect(name, parent);
      Place(rect, anchor, position, size);
      var image = rect.gameObject.AddComponent<Image>();
      image.sprite = IconSprite(icon);
      image.raycastTarget = false;
      image.preserveAspect = true;
      return image;
    }

    /// <summary>버튼 위쪽에 아이콘을 놓고 글자를 아래로 내린다. null이면 글자만 가운데에 둔다.</summary>
    public static void SetIcon(Button button, Sprite sprite) {
      var label = button.GetComponentInChildren<Text>();
      var slot = button.transform.Find("Icon");
      if (sprite == null) {
        if (slot != null) {
          slot.gameObject.SetActive(false);
        }
        var oldPlate = button.transform.Find("IconPlate");
        if (oldPlate != null) {
          oldPlate.gameObject.SetActive(false);
        }
        Stretch(label.rectTransform, 6f);
        return;
      }
      Image image;
      if (slot == null) {
        image = Icon("Icon", button.transform, "", new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(56, 56));
        ((RectTransform)image.transform).pivot = new Vector2(0.5f, 1);
        ((RectTransform)image.transform).anchoredPosition = new Vector2(0, -4);
      } else {
        image = slot.GetComponent<Image>();
        slot.gameObject.SetActive(true);
      }
      image.sprite = sprite;
      var plateColor = UiTheme.Current.IconPlate;
      var plate = button.transform.Find("IconPlate");
      if (plateColor.a > 0f) {
        if (plate == null) {
          var rect = Rect("IconPlate", button.transform);
          rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
          rect.pivot = new Vector2(0.5f, 1f);
          rect.anchoredPosition = new Vector2(0, -2);
          rect.sizeDelta = new Vector2(64, 60);
          var back = rect.gameObject.AddComponent<Image>();
          back.sprite = UiCatalog.SpriteFor("panel");
          back.type = Image.Type.Sliced;
          back.raycastTarget = false;
          back.color = plateColor;
          rect.SetSiblingIndex(image.transform.GetSiblingIndex());
          plate = rect;
        }
        plate.gameObject.SetActive(true);
      } else if (plate != null) {
        plate.gameObject.SetActive(false);
      }
      label.rectTransform.anchorMin = Vector2.zero;
      label.rectTransform.anchorMax = Vector2.one;
      label.rectTransform.offsetMin = new Vector2(2, 2);
      label.rectTransform.offsetMax = new Vector2(-2, -60);
    }

    public static void SetEnabled(Button button, bool enabled) {
      button.interactable = enabled;
      button.GetComponent<Image>().color = enabled ? ButtonColor : DisabledColor;
    }

    /// <summary>항목 31: 잠긴 칸. 누를 수는 있고(이유 표시), 회색 바탕·흐린 글자·자물쇠.</summary>
    public static void SetLocked(Button button, bool locked, bool highlight = false) {
      button.interactable = true;
      var theme = UiTheme.Current;
      button.GetComponent<Image>().color = locked ? DisabledColor : highlight ? Color.Lerp(theme.Button, theme.Accent, 0.19f) : ButtonColor;
      var label = button.GetComponentInChildren<Text>();
      if (label != null) {
        label.color = locked ? UiTheme.Current.DisabledText : ButtonTextColor;
      }
      var mark = button.transform.Find("Lock");
      if (mark == null && locked) {
        var image = Icon("Lock", button.transform, "", new Vector2(1, 1), new Vector2(-6, -6), new Vector2(40, 40));
        image.sprite = UiCatalog.SpriteFor("lock");
        mark = image.transform;
      }
      if (mark != null) {
        mark.gameObject.SetActive(locked);
      }
    }
  }
}
