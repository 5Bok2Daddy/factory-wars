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
      Ui.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, Debug.isDebugBuild ? 800 : 620));
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
      Button shake = null;
      shake = Ui.Button("Shake", panel, ScreenShake.Label, () => {
        ScreenShake.Enabled = ScreenShake.Enabled == false;
        Ui.SetLabel(shake, ScreenShake.Label);
      }, 30);
      Ui.Place((RectTransform)shake.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -110), new Vector2(420, Ui.MinButtonSize));
      var language = Ui.Label("Language", panel, "언어: 한국어", 34, TextAnchor.MiddleCenter);
      Ui.Place(language.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -200), new Vector2(600, 70));
      if (Debug.isDebugBuild) {
        // 개발 빌드 전용: 룩 후보 바꾸기. 다음 경기부터 적용된다.
        Ui.Place(language.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-220, -200), new Vector2(400, 70));
        string LookLabel() => "화면: " + VisualPresets.Title(PlayerPrefs.GetString(VisualProfile.ChoiceKey, "A"));
        Button look = null;
        look = Ui.Button("Look", panel, LookLabel(), () => {
          var names = VisualPresets.Names;
          var next = names[(System.Array.IndexOf(names, PlayerPrefs.GetString(VisualProfile.ChoiceKey, "A")) + 1) % names.Length];
          PlayerPrefs.SetString(VisualProfile.ChoiceKey, next);
          VisualProfile.Use(next);
          Ui.SetLabel(look, LookLabel());
        }, 28);
        Ui.Place((RectTransform)look.transform, new Vector2(0.5f, 0.5f), new Vector2(220, -200), new Vector2(400, Ui.MinButtonSize));
      }
      if (Debug.isDebugBuild) {
        // 항목 23: 실기기 시험용. 성능 표시를 켜고 끄고, 경기 기록(CSV)을 클립보드로 복사해 메신저로 보낸다.
        string PerfLabel() => "성능 표시: " + (PerfMonitor.OverlayEnabled ? "켬" : "끔");
        Button perf = null;
        perf = Ui.Button("Perf", panel, PerfLabel(), () => {
          PerfMonitor.OverlayEnabled = PerfMonitor.OverlayEnabled == false;
          Ui.SetLabel(perf, PerfLabel());
        }, 28);
        Ui.Place((RectTransform)perf.transform, new Vector2(0.5f, 0.5f), new Vector2(-220, -300), new Vector2(400, Ui.MinButtonSize));
        Button share = null;
        share = Ui.Button("ShareLog", panel, "기록 공유(복사)", () => {
          var lines = PerfMonitor.CopyLog();
          Ui.SetLabel(share, $"복사됨: 경기 {lines}판");
        }, 28);
        Ui.Place((RectTransform)share.transform, new Vector2(0.5f, 0.5f), new Vector2(220, -300), new Vector2(400, Ui.MinButtonSize));
      }
      var close = Ui.Button("Close", panel, "닫기", onClose, 32);
      Ui.Place((RectTransform)close.transform, new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(260, Ui.MinButtonSize));
      return panel;
    }
  }

  /// <summary>시작 화면 → 진영 선택 → 경기 설정 → 대전.</summary>
  public class MenuController : MonoBehaviour {
    public const string SceneName = "Menu";

    public enum Screen { Title, Faction, Options, Settings, OnlineMenu, OnlineLobby }

    public Screen Current { get; private set; }
    public MatchSettings Settings { get; } = new MatchSettings();

    RectTransform _root;
    RectTransform _title;
    RectTransform _faction;
    RectTransform _options;
    RectTransform _settings;
    RectTransform _onlineMenu;
    RectTransform _onlineLobby;
    Text _notice;
    Button _mode;
    Button _bots;
    Button _difficulty;
    Button _truce;
    Button _color;

    // 온라인 대전 UI (항목 28)
    Text _lobbyCodeText;
    Button[] _slotButtons;
    Button _lobbyModeBtn;
    Button _lobbyTruceBtn;
    string _currentRoomCode = "FW1024";
    bool _isLobbyHost = true;
    readonly LobbySlotInfo[] _lobbySlots = new LobbySlotInfo[4];

    public string NoticeText => _notice != null ? _notice.text : "";

    void Awake() {
      var canvas = Ui.CreateCanvas("Menu");
      canvas.transform.SetParent(transform, false);
      _root = Ui.SafeArea(canvas);
      var background = Ui.Panel("Background", _root, new Color(0.1f, 0.13f, 0.17f, 1f));
      Ui.Stretch(background);

      _title = Page("Title");
      var name = Ui.Label("Name", _title, "팩토리 워즈", 110, TextAnchor.MiddleCenter);
      Ui.Place(name.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 240), new Vector2(1200, 180));
      Add(_title, "로컬 대전 시작", 50, () => Show(Screen.Faction));
      Add(_title, "온라인 방 코드 대전", -60, () => Show(Screen.OnlineMenu));
      Add(_title, "설정", -170, () => Show(Screen.Settings));
      if (Debug.isDebugBuild) {
        // 항목 23: 개발 빌드 전용 빠른 시작. 왼쪽 아래에 작게 둔다.
        Quick("AI 대전 2인", new Vector2(-640, -330), () => QuickStart(false, 1));
        Quick("AI 대전 4인", new Vector2(-330, -330), () => QuickStart(true, 3));
        Button truce = null;
        truce = Quick(TruceLabel(), new Vector2(330, -330), () => {
          _quickTruceZero = _quickTruceZero == false;
          Ui.SetLabel(truce, TruceLabel());
        });
        Quick("대규모 교전 장면", new Vector2(640, -330), LoadBattleScene);
      }

      _faction = Page("Faction");
      var pick = Ui.Label("Pick", _faction, "진영 선택", 60, TextAnchor.MiddleCenter);
      Ui.Place(pick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 300), new Vector2(1000, 100));
      Add(_faction, "자유 연방\n고가·고성능·공중", 110, () => PickFaction(0), 620, 180);
      Add(_faction, "대륙 연방\n물량·저가", -100, () => PickFaction(1), 620, 180);
      Add(_faction, "뒤로", -300, () => Show(Screen.Title));

      _options = Page("Options");
      var match = Ui.Label("Match", _options, "경기 설정", 60, TextAnchor.MiddleCenter);
      Ui.Place(match.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 470), new Vector2(1000, 90));
      _notice = Ui.Label("Notice", _options, "", 30, TextAnchor.MiddleCenter);
      Ui.Place(_notice.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 340), new Vector2(1000, 70));
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

      // 온라인 메뉴 (항목 28)
      _onlineMenu = Page("OnlineMenu");
      var onlineTitle = Ui.Label("OnlineTitle", _onlineMenu, "온라인 6자리 방 코드 대전", 60, TextAnchor.MiddleCenter);
      Ui.Place(onlineTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 340), new Vector2(1000, 90));
      Add(_onlineMenu, "방 만들기 (새 방 코드 생성)", 120, () => {
        _currentRoomCode = OnlineMatchManager.GenerateRoomCode();
        _isLobbyHost = true;
        SetupLobbySlots();
        Show(Screen.OnlineLobby);
      });
      Add(_onlineMenu, "빠른 대전 (방 자동 참가)", -10, () => {
        _currentRoomCode = OnlineMatchManager.GenerateRoomCode(2026);
        _isLobbyHost = false;
        SetupLobbySlots();
        Show(Screen.OnlineLobby);
      });
      Add(_onlineMenu, "뒤로", -160, () => Show(Screen.Title));

      // 온라인 대기실 (항목 28)
      _onlineLobby = Page("OnlineLobby");
      var lobbyHeader = Ui.Label("LobbyHeader", _onlineLobby, "온라인 대기실 (2~4인)", 52, TextAnchor.MiddleCenter);
      Ui.Place(lobbyHeader.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 470), new Vector2(1000, 70));
      _lobbyCodeText = Ui.Label("LobbyCode", _onlineLobby, "", 34, TextAnchor.MiddleCenter);
      Ui.Place(_lobbyCodeText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 400), new Vector2(1000, 50));
      var verText = Ui.Label("LobbyVer", _onlineLobby, $"버전 확인: {OnlineMatchManager.VersionHash} (일치)", 24, TextAnchor.MiddleCenter);
      Ui.Place(verText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 345), new Vector2(1000, 40));

      var slotsPanel = Ui.Panel("SlotsPanel", _onlineLobby, new Color(0.96f, 0.98f, 1f, 0.92f));
      Ui.Place(slotsPanel, new Vector2(0.5f, 0.5f), new Vector2(0, 80), new Vector2(980, 430));
      _slotButtons = new Button[4];
      for (var i = 0; i < 4; i++) {
        var sIdx = i;
        var btn = Ui.Button($"Slot{i}", slotsPanel, "", () => ToggleSlot(sIdx), 26);
        Ui.Place((RectTransform)btn.transform, new Vector2(0.5f, 1f), new Vector2(0, -50 - sIdx * 95), new Vector2(920, 80));
        _slotButtons[i] = btn;
      }

      _lobbyModeBtn = Ui.Button("LobbyMode", _onlineLobby, "", NextLobbyMode, 28);
      Ui.Place((RectTransform)_lobbyModeBtn.transform, new Vector2(0.5f, 0.5f), new Vector2(-220, -220), new Vector2(400, Ui.MinButtonSize));
      _lobbyTruceBtn = Ui.Button("LobbyTruce", _onlineLobby, "", NextLobbyTruce, 28);
      Ui.Place((RectTransform)_lobbyTruceBtn.transform, new Vector2(0.5f, 0.5f), new Vector2(220, -220), new Vector2(400, Ui.MinButtonSize));

      var lobbyLeave = Ui.Button("LobbyLeave", _onlineLobby, "대기실 나가기", () => Show(Screen.OnlineMenu), 32);
      Ui.Place((RectTransform)lobbyLeave.transform, new Vector2(0.5f, 0.5f), new Vector2(-220, -380), new Vector2(380, Ui.MinButtonSize));
      var lobbyStart = Ui.Button("LobbyStart", _onlineLobby, "대전 시작", StartOnlineMatch, 32);
      Ui.Place((RectTransform)lobbyStart.transform, new Vector2(0.5f, 0.5f), new Vector2(220, -380), new Vector2(380, Ui.MinButtonSize));

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
      _onlineMenu.gameObject.SetActive(screen == Screen.OnlineMenu);
      _onlineLobby.gameObject.SetActive(screen == Screen.OnlineLobby);
      if (screen == Screen.Options) {
        RefreshOptions();
      } else if (screen == Screen.OnlineLobby) {
        RefreshLobbyUi();
      }
    }

    void SetupLobbySlots() {
      for (var i = 0; i < 4; i++) {
        _lobbySlots[i] = new LobbySlotInfo {
          Index = i,
          Type = i == 0 ? LobbySlotType.Human : (Settings.Mode == MatchMode.Duel && i > 1 ? LobbySlotType.Empty : LobbySlotType.BotNormal),
          Nickname = i == 0 ? "지휘관 (나)" : $"지휘관 {i + 1}",
          FactionIndex = i % 2,
          ColorIndex = Palette.Recommended(i % 2),
          IsReady = true,
          IsHost = i == 0 && _isLobbyHost,
        };
      }
    }

    public void ToggleSlot(int index) {
      if (_isLobbyHost == false || index == 0) {
        return;
      }
      var current = _lobbySlots[index].Type;
      _lobbySlots[index].Type = current == LobbySlotType.Empty ? LobbySlotType.BotNormal
        : current == LobbySlotType.BotNormal ? LobbySlotType.BotEasy : LobbySlotType.Empty;
      RefreshLobbyUi();
    }

    public void NextLobbyMode() {
      if (_isLobbyHost == false) return;
      Settings.Mode = (MatchMode)(((int)Settings.Mode + 1) % 3);
      SetupLobbySlots();
      RefreshLobbyUi();
    }

    public void NextLobbyTruce() {
      if (_isLobbyHost == false) return;
      NextTruce();
      RefreshLobbyUi();
    }

    void RefreshLobbyUi() {
      if (_lobbyCodeText != null) {
        _lobbyCodeText.text = $"방 코드: {_currentRoomCode}   [클릭하여 코드 복사]";
      }
      for (var i = 0; i < 4; i++) {
        var slot = _lobbySlots[i];
        if (slot == null || _slotButtons == null || i >= _slotButtons.Length) continue;
        string text;
        if (slot.Type == LobbySlotType.Empty) {
          text = $"[슬롯 {i + 1}]  빈자리  {(_isLobbyHost ? "(클릭하여 봇 채우기)" : "")}";
        } else if (slot.Type == LobbySlotType.Human) {
          text = $"[슬롯 {i + 1}]  {(slot.IsHost ? "👑 " : "")}{slot.Nickname} | 진영: {(slot.FactionIndex == 0 ? "자유 연방" : "대륙 연방")} | 준비 완료";
        } else {
          var diff = slot.Type == LobbySlotType.BotNormal ? "보통" : "쉬움";
          text = $"[슬롯 {i + 1}]  AI 봇 ({diff}) | 진영: {(slot.FactionIndex == 0 ? "자유 연방" : "대륙 연방")}  {(_isLobbyHost ? "(클릭하여 변경)" : "")}";
        }
        Ui.SetLabel(_slotButtons[i], text);
      }
      var modeStr = Settings.Mode == MatchMode.Duel ? "1대1 (대각선)" : Settings.Mode == MatchMode.FreeForAll ? "자유 대전 (4인)" : "팀전 (2대2)";
      Ui.SetLabel(_lobbyModeBtn, $"모드: {modeStr}");
      Ui.SetLabel(_lobbyTruceBtn, Settings.TruceMinutes == 0 ? "휴전: 없음" : $"휴전: {Settings.TruceMinutes}분");
    }

    public void StartOnlineMatch() {
      Settings.IsOnline = true;
      Settings.RoomCode = _currentRoomCode;
      var botCount = 0;
      for (var i = 1; i < 4; i++) {
        if (_lobbySlots[i] != null && _lobbySlots[i].Type != LobbySlotType.Empty) {
          botCount++;
        }
      }
      Settings.Bots = Mathf.Max(1, botCount);
      Begin();
    }

    public void PickFaction(int index) {
      Settings.FactionIndex = index;
      Show(Screen.Options);
    }

    public void NextMode() {
      Settings.Mode = (MatchMode)(((int)Settings.Mode + 1) % 3);
      if (Settings.Mode == MatchMode.FreeForAll) {
        Settings.Bots = 3;
      }
      RefreshOptions();
    }

    public void NextBots() {
      if (Settings.Mode == MatchMode.Team) {
        Settings.DiagonalTeams = Settings.DiagonalTeams == false;
      } else {
        Settings.Bots = Settings.Bots == 2 ? 3 : 2;
      }
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
      var mode = Settings.Mode == MatchMode.Duel ? "1대1" : Settings.Mode == MatchMode.FreeForAll ? "자유 대전" : "팀전";
      Ui.SetLabel(_mode, $"모드: {mode}");
      if (Settings.Mode == MatchMode.Team) {
        Ui.SetLabel(_bots, $"2대2: {(Settings.DiagonalTeams ? "대각선 동맹" : "이웃 동맹")}");
      } else if (Settings.Mode == MatchMode.FreeForAll) {
        Ui.SetLabel(_bots, Settings.Bots == 2 ? "봇: 2명 (빈자리 비우기)" : "봇: 3명 (봇 채우기)");
      } else {
        Ui.SetLabel(_bots, $"봇: {Settings.Bots}명");
      }
      Ui.SetEnabled(_mode, true);
      Ui.SetEnabled(_bots, Settings.Mode != MatchMode.Duel);
      if (_notice != null) {
        _notice.text = Settings.Mode == MatchMode.Duel
          ? "배치: 남서·북동 / 남동·북서 대각선 (빈 본진은 중립 대형 확장)"
          : Settings.Players == 3
            ? "3인은 자리 유불리가 있을 수 있음"
            : Settings.Mode == MatchMode.Team
              ? (Settings.DiagonalTeams ? "배치: 대각선끼리 같은 편 (기본)" : "배치: 이웃끼리 같은 편")
              : "배치: 4인 전원 본진 배치";
      }
      Ui.SetLabel(_difficulty, $"난이도: {(Settings.Difficulty == BotDifficulty.Easy ? "쉬움" : "보통")}");
      Ui.SetLabel(_truce, Settings.TruceMinutes == 0 ? "휴전: 없음" : $"휴전: {Settings.TruceMinutes}분");
      var color = Palette.Colors[Settings.Color];
      Ui.SetLabel(_color, $"색: {Palette.Names[Settings.Color]}");
      _color.GetComponent<Image>().color = color;
      _color.GetComponentInChildren<Text>().color = Palette.IsLight(color) ? Color.black : Color.white;
    }

    bool _quickTruceZero = true;

    string TruceLabel() => "휴전 0분: " + (_quickTruceZero ? "켬" : "끔");

    Button Quick(string label, Vector2 position, UnityEngine.Events.UnityAction onClick) {
      var button = Ui.Button(label, _title, label, onClick, 28);
      var rect = (RectTransform)button.transform;
      Ui.Place(rect, new Vector2(0.5f, 0.5f), position, new Vector2(290, Ui.MinButtonSize));
      rect.pivot = new Vector2(0.5f, 0.5f);
      return button;
    }

    void QuickStart(bool fourPlayer, int bots) {
      Settings.Mode = fourPlayer ? MatchMode.FreeForAll : MatchMode.Duel;
      Settings.Bots = bots;
      Settings.Difficulty = BotDifficulty.Normal;
      Settings.TruceMinutes = _quickTruceZero ? 0 : 7;
      Settings.Seed = -1;
      Settings.FastForwardSeconds = 0;
      Begin();
    }

    /// <summary>19A 고정 장면과 같은 시드로 2인 경기를 시작해 11:00까지 빨리 감는다(대규모 교전 성능 확인용).</summary>
    void LoadBattleScene() {
      Settings.Mode = MatchMode.Duel;
      Settings.Bots = 1;
      Settings.Difficulty = BotDifficulty.Normal;
      Settings.TruceMinutes = 0;
      Settings.Seed = MatchSettings.BattleSceneSeed;
      Settings.FastForwardSeconds = 660;
      Begin();
    }

    public void Begin() {
      Settings.Normalize();
      MatchSetup.Pending = Settings;
      SceneManager.LoadScene(Settings.Scene);
    }
  }
}
