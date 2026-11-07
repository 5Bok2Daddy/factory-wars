namespace FactoryWars.View {
  using Quantum;
  using UnityEngine;
  using UnityEngine.SceneManagement;
  using UnityEngine.UI;
  using Button = UnityEngine.UI.Button;

  /// <summary>음량 자리와 언어 표시. 음량은 챕터 6에서 소리가 들어오면 쓴다.</summary>
  public static class SettingsPanel {
    const string VolumeKey = "fw_volume";

    public static RectTransform Build(Transform parent, UnityEngine.Events.UnityAction onClose) {
      var panel = Ui.Panel("Settings", parent);
      Ui.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 620));
      panel.pivot = new Vector2(0.5f, 0.5f);
      var title = Ui.Label("Title", panel, "설정", 44, TextAnchor.MiddleCenter);
      Ui.Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(600, 70));
      var volume = Ui.Label("Volume", panel, "", 34, TextAnchor.MiddleCenter);
      Ui.Place(volume.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(360, Ui.MinButtonSize));

      void Show() {
        volume.text = $"음량 {Mathf.RoundToInt(AudioListener.volume * 100)}%";
      }
      void Change(float delta) {
        AudioListener.volume = Mathf.Clamp01(AudioListener.volume + delta);
        PlayerPrefs.SetFloat(VolumeKey, AudioListener.volume);
        Show();
      }
      AudioListener.volume = PlayerPrefs.GetFloat(VolumeKey, 1f);
      Show();

      var down = Ui.Button("VolumeDown", panel, "-", () => Change(-0.1f), 44);
      Ui.Place((RectTransform)down.transform, new Vector2(0.5f, 0.5f), new Vector2(-260, 90), new Vector2(Ui.MinButtonSize, Ui.MinButtonSize));
      var up = Ui.Button("VolumeUp", panel, "+", () => Change(0.1f), 44);
      Ui.Place((RectTransform)up.transform, new Vector2(0.5f, 0.5f), new Vector2(260, 90), new Vector2(Ui.MinButtonSize, Ui.MinButtonSize));
      Button quality = null;
      quality = Ui.Button("Quality", panel, $"그래픽: {GraphicsQuality.Name(GraphicsQuality.Current)}", () => {
        Ui.SetLabel(quality, $"그래픽: {GraphicsQuality.Name(GraphicsQuality.Cycle())}");
      }, 34);
      Ui.Place((RectTransform)quality.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(420, Ui.MinButtonSize));
      var language = Ui.Label("Language", panel, "언어: 한국어", 34, TextAnchor.MiddleCenter);
      Ui.Place(language.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -130), new Vector2(600, 70));
      var close = Ui.Button("Close", panel, "닫기", onClose, 32);
      Ui.Place((RectTransform)close.transform, new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(260, Ui.MinButtonSize));
      return panel;
    }
  }

  /// <summary>시작 화면 → 진영 선택 → 경기 설정 → 대전.</summary>
  public class MenuController : MonoBehaviour {
    public const string SceneName = "Menu";

    public enum Screen { Title, Faction, Options, Settings }

    public Screen Current { get; private set; }
    public MatchSettings Settings { get; } = new MatchSettings();

    RectTransform _root;
    RectTransform _title;
    RectTransform _faction;
    RectTransform _options;
    RectTransform _settings;
    Button _map;
    Button _mode;
    Button _bots;
    Button _difficulty;
    Button _truce;
    Button _color;

    void Awake() {
      var canvas = Ui.CreateCanvas("Menu");
      canvas.transform.SetParent(transform, false);
      _root = Ui.SafeArea(canvas);
      var background = Ui.Panel("Background", _root, new Color(0.1f, 0.13f, 0.17f, 1f));
      Ui.Stretch(background);

      _title = Page("Title");
      var name = Ui.Label("Name", _title, "팩토리 워즈", 110, TextAnchor.MiddleCenter);
      Ui.Place(name.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 240), new Vector2(1200, 180));
      Add(_title, "시작", 20, () => Show(Screen.Faction));
      Add(_title, "설정", -160, () => Show(Screen.Settings));

      _faction = Page("Faction");
      var pick = Ui.Label("Pick", _faction, "진영 선택", 60, TextAnchor.MiddleCenter);
      Ui.Place(pick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 300), new Vector2(1000, 100));
      Add(_faction, "자유 연방\n고가·고성능·공중", 110, () => PickFaction(0), 620, 180);
      Add(_faction, "대륙 연방\n물량·저가", -100, () => PickFaction(1), 620, 180);
      Add(_faction, "뒤로", -300, () => Show(Screen.Title));

      _options = Page("Options");
      var match = Ui.Label("Match", _options, "경기 설정", 60, TextAnchor.MiddleCenter);
      Ui.Place(match.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 470), new Vector2(1000, 90));
      _map = Add(_options, "", 340, ToggleMap);
      _mode = Add(_options, "", 190, NextMode);
      _bots = Add(_options, "", 40, NextBots);
      _difficulty = Add(_options, "", -110, NextDifficulty);
      _truce = Add(_options, "", -260, NextTruce, 300);
      ((RectTransform)_truce.transform).anchoredPosition = new Vector2(-160, -260);
      _color = Add(_options, "", -260, NextColor, 300);
      ((RectTransform)_color.transform).anchoredPosition = new Vector2(160, -260);
      var back = Ui.Button("Back", _options, "뒤로", () => Show(Screen.Faction), 34);
      Ui.Place((RectTransform)back.transform, new Vector2(0.5f, 0.5f), new Vector2(-220, -420), new Vector2(380, Ui.MinButtonSize));
      var begin = Ui.Button("Begin", _options, "대전 시작", Begin, 34);
      Ui.Place((RectTransform)begin.transform, new Vector2(0.5f, 0.5f), new Vector2(220, -420), new Vector2(380, Ui.MinButtonSize));

      _settings = SettingsPanel.Build(_root, () => Show(Screen.Title));
      Show(Screen.Title);
    }

    RectTransform Page(string name) {
      var page = Ui.Rect(name, _root);
      Ui.Stretch(page);
      return page;
    }

    Button Add(RectTransform page, string label, float y, UnityEngine.Events.UnityAction onClick, float width = 620, float height = Ui.MinButtonSize) {
      var button = Ui.Button(label, page, label, onClick, 34);
      var rect = (RectTransform)button.transform;
      Ui.Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(width, height));
      rect.pivot = new Vector2(0.5f, 0.5f);
      return button;
    }

    public void Show(Screen screen) {
      Current = screen;
      _title.gameObject.SetActive(screen == Screen.Title);
      _faction.gameObject.SetActive(screen == Screen.Faction);
      _options.gameObject.SetActive(screen == Screen.Options);
      _settings.gameObject.SetActive(screen == Screen.Settings);
      RefreshOptions();
    }

    public void PickFaction(int index) {
      Settings.FactionIndex = index;
      Show(Screen.Options);
    }

    public void ToggleMap() {
      Settings.FourPlayerMap = Settings.FourPlayerMap == false;
      if (Settings.FourPlayerMap) {
        Settings.Mode = MatchMode.FreeForAll;
        Settings.Bots = 3;
      }
      RefreshOptions();
    }

    public void NextMode() {
      if (Settings.FourPlayerMap) {
        Settings.Mode = (MatchMode)(((int)Settings.Mode + 1) % 3);
        if (Settings.Mode == MatchMode.FreeForAll) {
          Settings.Bots = 3;
        }
      }
      RefreshOptions();
    }

    public void NextBots() {
      Settings.Bots = Settings.Bots == 2 ? 3 : 2;
      RefreshOptions();
    }

    public void NextDifficulty() {
      Settings.Difficulty = Settings.Difficulty == BotDifficulty.Easy ? BotDifficulty.Normal : BotDifficulty.Easy;
      RefreshOptions();
    }

    /// <summary>휴전 시간은 0·5·7·10분 중에 고른다.</summary>
    public void NextTruce() {
      Settings.TruceMinutes = Settings.TruceMinutes == 0 ? 5 : Settings.TruceMinutes == 5 ? 7 : Settings.TruceMinutes == 7 ? 10 : 0;
      RefreshOptions();
    }

    public void NextColor() {
      Settings.ColorIndex = (Settings.Color + 1) % Palette.Count;
      RefreshOptions();
    }

    void RefreshOptions() {
      Settings.Normalize();
      Ui.SetLabel(_map, $"맵: {(Settings.FourPlayerMap ? "4인" : "2인")}");
      var mode = Settings.Mode == MatchMode.Duel ? "1대1" : Settings.Mode == MatchMode.FreeForAll ? "자유 대전" : "팀전";
      Ui.SetLabel(_mode, $"모드: {mode}");
      Ui.SetLabel(_bots, $"봇: {Settings.Bots}명");
      Ui.SetEnabled(_mode, Settings.FourPlayerMap);
      Ui.SetEnabled(_bots, Settings.Mode == MatchMode.FreeForAll);
      Ui.SetLabel(_difficulty, $"난이도: {(Settings.Difficulty == BotDifficulty.Easy ? "쉬움" : "보통")}");
      Ui.SetLabel(_truce, Settings.TruceMinutes == 0 ? "휴전: 없음" : $"휴전: {Settings.TruceMinutes}분");
      var color = Palette.Colors[Settings.Color];
      Ui.SetLabel(_color, $"색: {Palette.Names[Settings.Color]}");
      _color.GetComponent<Image>().color = color;
      _color.GetComponentInChildren<Text>().color = Palette.IsLight(color) ? Color.black : Color.white;
    }

    public void Begin() {
      Settings.Normalize();
      MatchSetup.Pending = Settings;
      SceneManager.LoadScene(Settings.Scene);
    }
  }
}
