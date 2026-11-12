namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>
  /// HUD 스타일 값(항목 31). 색·모서리·글자 크기 3단계·간격·버튼 최소 크기. 산 UI 팩으로 바꿀 때 이 에셋과 UiCatalog만 고친다.
  /// Resources/FactoryWars/UI/UiTheme.asset이 없으면 기본값을 쓴다.
  /// </summary>
  [CreateAssetMenu(menuName = "Factory Wars/UI Theme")]
  public class UiTheme : ScriptableObject {
    public const string ResourcePath = "FactoryWars/UI/UiTheme";

    [Header("색")]
    public Color Panel = new Color(0.96f, 0.98f, 1f, 0.92f);
    public Color PanelText = new Color(0.16f, 0.20f, 0.30f, 1f);
    public Color Button = Color.white;
    public Color ButtonPressed = new Color(0.82f, 0.90f, 1f, 1f);
    public Color ButtonDisabled = new Color(0.78f, 0.81f, 0.86f, 0.80f);
    public Color ButtonText = new Color(0.16f, 0.20f, 0.30f, 1f);
    public Color DisabledText = new Color(0.40f, 0.44f, 0.50f, 1f);
    public Color Accent = new Color(0.18f, 0.49f, 0.84f, 1f);
    public Color Warning = new Color(0.96f, 0.72f, 0.10f, 1f);
    public Color Danger = new Color(0.88f, 0.33f, 0.24f, 1f);
    public Color HealthHigh = new Color(0.30f, 0.78f, 0.36f, 1f);
    public Color HealthLow = new Color(0.88f, 0.33f, 0.24f, 1f);
    [Tooltip("크기 배지: 소·중·대")]
    public Color[] SizeBadge = { new Color(0.35f, 0.70f, 0.40f, 1f), new Color(0.22f, 0.52f, 0.86f, 1f), new Color(0.62f, 0.32f, 0.78f, 1f) };

    [Header("모양")]
    [Tooltip("9-slice 스프라이트 모서리 배율. 1이 원본, 클수록 덜 둥글다.")]
    public float CornerScale = 1f;
    public int FontSmall = 26;
    public int FontMedium = 30;
    public int FontLarge = 36;
    public float Spacing = 8f;
    [Tooltip("손가락 버튼 최소 크기(mm). 시험 Ui_TouchTargetsAtLeast9mm가 기기 해상도로 잰다.")]
    public float MinTouchMillimeters = 9f;
    [Tooltip("기준 해상도(1920x1080, 높이 맞춤)에서의 버튼 최소 크기(px).")]
    public float MinButton = 154f;

    static UiTheme _current;

    public static UiTheme Current {
      get {
        if (_current == null) {
          _current = Resources.Load<UiTheme>(ResourcePath);
          if (_current == null) {
            _current = CreateInstance<UiTheme>();
          }
        }
        return _current;
      }
      set => _current = value;
    }

    public Color SizeColor(Quantum.UnitSize size) {
      var index = size == Quantum.UnitSize.Large ? 2 : size == Quantum.UnitSize.Medium ? 1 : 0;
      return SizeBadge != null && index < SizeBadge.Length ? SizeBadge[index] : Accent;
    }
  }
}
