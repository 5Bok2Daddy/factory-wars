namespace FactoryWars.View {
  using UnityEngine;
  using UnityEngine.Events;
  using UnityEngine.EventSystems;
  using UnityEngine.UI;

  /// <summary>uGUI를 코드로 만드는 도구. 아트가 들어오는 챕터 6 전까지 쓴다.</summary>
  public static class Ui {
    /// <summary>기준 해상도 1080에서 약 9mm. 가로로 든 휴대폰의 세로 폭을 70mm로 잡았다.</summary>
    public const float MinButtonSize = 140f;
    public static readonly Vector2 ReferenceResolution = new Vector2(1920, 1080);

    public static readonly Color PanelColor = new Color(0.08f, 0.1f, 0.13f, 0.82f);
    public static readonly Color ButtonColor = new Color(0.2f, 0.25f, 0.32f, 0.95f);
    public static readonly Color DisabledColor = new Color(0.14f, 0.15f, 0.17f, 0.9f);

    static Font _font;

    public static Font Font => _font != null ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    public static Canvas CreateCanvas(string name, int sortOrder = 0) {
      var root = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
      var canvas = root.GetComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      canvas.sortingOrder = sortOrder;
      var scaler = root.GetComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      scaler.referenceResolution = ReferenceResolution;
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

    public static RectTransform Panel(string name, Transform parent, Color? color = null) {
      var rect = Rect(name, parent);
      rect.gameObject.AddComponent<Image>().color = color ?? PanelColor;
      return rect;
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
      var rect = Panel(name, parent, ButtonColor);
      var button = rect.gameObject.AddComponent<Button>();
      button.targetGraphic = rect.GetComponent<Image>();
      if (onClick != null) {
        button.onClick.AddListener(onClick);
      }
      var label = Label("Label", rect, value, size, TextAnchor.MiddleCenter);
      Stretch(label.rectTransform, 6f);
      return button;
    }

    public static void SetLabel(Button button, string value) {
      button.GetComponentInChildren<Text>().text = value;
    }

    public static void SetEnabled(Button button, bool enabled) {
      button.interactable = enabled;
      button.GetComponent<Image>().color = enabled ? ButtonColor : DisabledColor;
    }
  }
}
