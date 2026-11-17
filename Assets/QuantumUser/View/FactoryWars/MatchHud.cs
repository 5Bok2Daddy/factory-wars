namespace FactoryWars.View {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.UI;
  using Button = UnityEngine.UI.Button;

  /// <summary>명령 카드의 칸 하나. Locked면 회색·자물쇠이고 누르거나 길게 누르면 Reason을 보인다.</summary>
  public struct ContextEntry {
    public string Label;
    public bool Enabled;
    public Action Action;
    public string Icon;
    public bool Locked;
    public string Reason;
    /// <summary>길게 누를 때 보일 설명(잠기지 않은 칸).</summary>
    public string Detail;
  }

  /// <summary>
  /// 대전 화면 HUD(항목 31 재구성). 4구역: 상단 왼쪽 자원 한 줄, 상단 가운데 휴전·경고(없으면 그리지 않음), 상단 오른쪽 외교·메뉴 아이콘,
  /// 하단 왼쪽 미니맵 + 빠른 버튼(유휴 공병·전체 군대·부대 1~3), 하단 가운데 선택 카드, 하단 오른쪽 3x3 명령 카드.
  /// 시뮬레이션은 읽기만 하고 행동은 커맨드로 보낸다. 색·그림은 UiTheme·UiCatalog.
  /// </summary>
  public unsafe class MatchHud : MonoBehaviour {
    const float RefreshSeconds = 0.2f;
    const float AlertSeconds = 3f;
    const float NoteSeconds = 3f;
    const float TooltipSeconds = 3f;
    const float FlashSeconds = 2f;
    public const int ContextSlots = 9;
    public const int ContextColumns = 3;
    const float Cell = 154f;
    const float Gap = 8f;
    const float Pad = 12f;
    const float HeaderHeight = 50f;
    const int MaxTypeButtons = 4;
    const int QueueIcons = 5;

    public readonly List<ContextEntry> Entries = new List<ContextEntry>();
    public readonly List<string> NoteInbox = new List<string>();

    public MatchController Controller;
    public bool BoxMode { get; private set; }
    public int ContextPage => 0;
    /// <summary>명령 카드는 한 화면(3x3)이다. 하위 메뉴(건설 단계·연구 크기)는 "뒤로"로 돌아온다.</summary>
    public int ContextPages => 1;
    public string TopBarText { get; private set; } = "";
    public int ResourceIcons => _resources.Count(r => r.Icon.sprite != null);
    public string AlertText => _alert.text;
    public string NoteText => _note.text;
    public string TruceText => _truce.text;
    public string TooltipText => _tooltipText != null ? _tooltipText.text : "";
    public string SelectionTitle => _cardTitle != null ? _cardTitle.text : "";
    public string SelectionBadge => _cardBadgeText != null && _cardBadge.gameObject.activeSelf ? _cardBadgeText.text : "";
    public string SelectionDetail => _cardDetail != null ? _cardDetail.text : "";
    public int SelectionTypeButtons => _typeButtons.Count(b => b.gameObject.activeSelf);
    public int IdleEngineers { get; private set; }
    public bool DiplomacyOpen => _diplomacy.gameObject.activeSelf;
    public RectTransform Root { get; private set; }
    public string MenuName => _menu.ToString() + (_menu == Menu.Build ? _menuTier.ToString() : _menu == Menu.Research ? _menuSize.ToString() : "");

    enum Menu { Root, Build, Research, Hangar }

    static readonly (string Icon, float Width)[] ResourceSlots = {
      ("res_iron", 150), ("res_wood", 150), ("res_steel", 150), ("res_power", 280), ("res_population", 150),
    };
    static readonly MissingResource[] ResourceBits = { MissingResource.Iron, MissingResource.Wood, MissingResource.Steel, MissingResource.Power, MissingResource.Population };

    readonly List<(Image Icon, Text Value, Image Back)> _resources = new List<(Image, Text, Image)>();
    readonly float[] _flashUntil = new float[5];
    RectTransform _truceBadge;
    RectTransform _alertBubble;
    RectTransform _noteBubble;
    RectTransform _tooltip;
    Text _tooltipText;
    Text _alert;
    Text _note;
    Text _status;
    Text _truce;
    Minimap _minimap;
    GameObject _contextPanel;
    Button[] _context;
    Button _boxButton;
    Button _idleButton;
    Text _idleBadge;
    Button[] _groupButtons;
    RectTransform _card;
    Image _cardIcon;
    Text _cardTitle;
    RectTransform _cardBadge;
    Text _cardBadgeText;
    RectTransform _cardHealth;
    Image _cardHealthFill;
    Text _cardDetail;
    readonly List<Image> _queueIcons = new List<Image>();
    readonly List<Button> _typeButtons = new List<Button>();
    readonly List<AssetRef<UnitSpec>> _typeSpecs = new List<AssetRef<UnitSpec>>();
    RectTransform _diplomacy;
    Text _inbox;
    readonly List<(PlayerRef Player, Text Name, Button Propose, Button Accept, Button Reject, Button Break, Button Note)> _rows =
      new List<(PlayerRef, Text, Button, Button, Button, Button, Button)>();
    int _phrase;
    float _nextRefresh;
    float _alertUntil;
    float _noteUntil;
    float _tooltipUntil;
    Menu _menu;
    int _menuTier = 1;
    UnitSize _menuSize = UnitSize.Small;
    string _selectionKey = "";

    // 항목 33 궁극 전력: 상단 오른쪽(아이콘 버튼 아래) 진행 막대. 준비 중·발동 대기인 플레이어(자신·적 모두)와 날아오는 미사일 카운트다운.
    const int MaxUltimateBars = 5;
    /// <summary>궁극 전력 막대 칠의 최대 불투명도(글자가 비치도록).</summary>
    public const float UltimateFillAlpha = 0.6f;
    readonly List<(RectTransform Root, RectTransform Fill, Image FillImage, Text Label)> _ultimateBars = new List<(RectTransform, RectTransform, Image, Text)>();
    public int UltimateBars => _ultimateBars.Count(b => b.Root.gameObject.activeSelf);
    public string UltimateBarText(int index) => _ultimateBars.Where(b => b.Root.gameObject.activeSelf).Select(b => b.Label.text).ElementAtOrDefault(index) ?? "";
    public int MinimapUltimateMarks => _minimap.UltimateMarks;
    /// <summary>시험용: 모든 궁극 전력 막대에서 글자가 칠(Fill)보다 위 칸이고 외곽선이 있으며 칠이 반투명인지.</summary>
    public bool UltimateTextAboveFill => _ultimateBars.Count > 0 && _ultimateBars.All(b =>
      b.Label.rectTransform.GetSiblingIndex() > b.Fill.GetSiblingIndex() && b.Label.GetComponent<Outline>() != null && b.Label.color.a > 0.9f && b.Label.color.r > 0.9f && UltimateFillAlpha <= 0.65f);

    static float ContextWidth => ContextColumns * Cell + (ContextColumns - 1) * Gap + 2 * Pad;
    static float ContextHeight => ContextColumns * Cell + (ContextColumns - 1) * Gap + 2 * Pad + HeaderHeight;
    /// <summary>미니맵(300) + 빠른 버튼 2열의 오른쪽 끝.</summary>
    const float LeftClusterWidth = 8 + 300 + 16 + 2 * 154 + 10 + 16;

    public void Build(Frame frame) {
      var theme = UiTheme.Current;
      var canvas = Ui.CreateCanvas("MatchHud");
      canvas.transform.SetParent(transform, false);
      Root = Ui.SafeArea(canvas);

      // 상단 왼쪽: 자원 한 줄.
      var top = Ui.Rect("TopBar", Root);
      top.anchorMin = new Vector2(0, 1);
      top.anchorMax = new Vector2(0, 1);
      top.pivot = new Vector2(0, 1);
      top.anchoredPosition = new Vector2(12, -8);
      top.sizeDelta = new Vector2(ResourceSlots.Sum(r => r.Width) + (ResourceSlots.Length - 1) * Gap, 60);
      _resources.Clear();
      var x = 0f;
      foreach (var (icon, width) in ResourceSlots) {
        var capsule = Ui.Capsule($"{icon}_capsule", top, theme.Panel);
        Ui.Place(capsule, new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(width, 56));
        var image = Ui.Icon(icon, capsule, icon, new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(42, 42));
        var slot = UiCatalog.SpriteFor(icon);
        if (slot != null) {
          image.sprite = slot;
        }
        var value = Ui.Label($"{icon}_value", capsule, "", theme.FontMedium - 2);
        value.color = theme.PanelText;
        value.horizontalOverflow = HorizontalWrapMode.Overflow;
        Ui.Place(value.rectTransform, new Vector2(0, 0.5f), new Vector2(52, 0), new Vector2(width - 56, 48));
        _resources.Add((image, value, capsule.GetComponent<Image>()));
        x += width + Gap;
      }

      // 상단 오른쪽: 외교·메뉴 아이콘 버튼.
      var menuButton = IconButton("MenuButton", Root, "ui_menu", "메뉴", () => Controller.TogglePause());
      Ui.Place((RectTransform)menuButton.transform, Vector2.one, new Vector2(-12, -8), new Vector2(Cell, Cell));
      var diplomacyButton = IconButton("DiplomacyButton", Root, "ui_diplomacy", "외교", ToggleDiplomacy);
      Ui.Place((RectTransform)diplomacyButton.transform, Vector2.one, new Vector2(-12 - Cell - Gap, -8), new Vector2(Cell, Cell));

      // 상단 가운데(자원 줄 아래): 휴전·연결 대기. 글자가 없으면 통째로 숨긴다(빈 막대 B5).
      _truceBadge = Ui.Capsule("TruceBadge", Root, theme.Panel);
      Ui.Place(_truceBadge, new Vector2(0.5f, 1), new Vector2(0, -76), new Vector2(560, 56));
      _truce = Ui.Label("Truce", _truceBadge, "", theme.FontMedium, TextAnchor.MiddleCenter);
      Ui.Stretch(_truce.rectTransform);
      _truce.color = theme.PanelText;
      _truceBadge.gameObject.SetActive(false);

      _alertBubble = Ui.Capsule("AlertBubble", Root, theme.Panel);
      Ui.Place(_alertBubble, new Vector2(0.5f, 1), new Vector2(0, -140), new Vector2(800, 56));
      _alert = Ui.Label("Alert", _alertBubble, "", theme.FontMedium + 2, TextAnchor.MiddleCenter);
      _alert.resizeTextForBestFit = true;
      _alert.resizeTextMinSize = theme.FontSmall - 4;
      _alert.resizeTextMaxSize = theme.FontMedium + 2;
      Ui.Stretch(_alert.rectTransform, 4f);
      _alert.color = theme.PanelText;
      _alertBubble.gameObject.SetActive(false);
      // 알림을 누르면 그 자리로 카메라가 간다(항목 30).
      _alertBubble.gameObject.AddComponent<ClickRelay>().OnClick = JumpToAlert;

      _noteBubble = Ui.Capsule("NoteBubble", Root, theme.Panel);
      Ui.Place(_noteBubble, new Vector2(0.5f, 1), new Vector2(0, -204), new Vector2(800, 56));
      _note = Ui.Label("Note", _noteBubble, "", theme.FontMedium, TextAnchor.MiddleCenter);
      _note.resizeTextForBestFit = true;
      _note.resizeTextMinSize = theme.FontSmall - 4;
      _note.resizeTextMaxSize = theme.FontMedium;
      Ui.Stretch(_note.rectTransform, 4f);
      _note.color = theme.PanelText;
      _noteBubble.gameObject.SetActive(false);

      BuildDoctrinePanel(theme);
      BuildAirOpsPanel(theme);

      // 하단 왼쪽: 미니맵.
      var mapFrame = Ui.Panel("MinimapFrame", Root, theme.Panel);
      var frameSprite = UiCatalog.SpriteFor("minimap_frame");
      if (frameSprite != null) {
        mapFrame.GetComponent<Image>().sprite = frameSprite;
      }
      Ui.Place(mapFrame, Vector2.zero, new Vector2(8, 8), new Vector2(308, 308));
      var map = Ui.Rect("Minimap", mapFrame);
      Ui.Place(map, Vector2.zero, new Vector2(4, 4), new Vector2(300, 300));
      map.gameObject.AddComponent<RawImage>();
      _minimap = map.gameObject.AddComponent<Minimap>();
      _minimap.Init(frame.FindAsset<MapSpec>(frame.Map.UserAsset));
      _minimap.OnPick = world => Controller.Camera.MoveTo(world);
      _minimap.View = Controller.Camera;
      _minimap.OnOrder = (world, attack) => {
        var command = Controller.Input.MinimapOrder(Controller.Frame, new Vector3(world.x, 0f, world.y), attack);
        if (command != null) {
          Controller.Send(command);
        }
      };

      // 화면 왼쪽 가장자리(미니맵 위): 범위 선택 토글.
      _boxButton = IconButton("BoxButton", Root, "ui_box", "범위", ToggleBoxMode);
      Ui.Place((RectTransform)_boxButton.transform, Vector2.zero, new Vector2(8, 8 + 308 + Gap), new Vector2(Cell, Cell));

      // 미니맵 옆: 유휴 공병(숫자 배지)·전체 군대·부대 1~3(누르면 선택, 두 번 누르면 카메라, 길게 누르면 지정).
      var actions = Ui.Rect("Actions", Root);
      Ui.Place(actions, Vector2.zero, new Vector2(324, 8), new Vector2(2 * Cell + 10, 3 * Cell + 20));
      var grid = actions.gameObject.AddComponent<GridLayoutGroup>();
      grid.cellSize = new Vector2(Cell, Cell);
      grid.spacing = new Vector2(10, 10);
      grid.startCorner = GridLayoutGroup.Corner.LowerLeft;
      grid.childAlignment = TextAnchor.LowerLeft;
      grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
      grid.constraintCount = 2;
      _groupButtons = new Button[SelectionModel.GroupCount];
      for (var i = 0; i < SelectionModel.GroupCount; i++) {
        var index = i;
        _groupButtons[i] = IconButton($"Group{i + 1}", actions, "ui_group", $"{i + 1}부대", () => PressGroup(index));
        LongPress.Attach(_groupButtons[i], () => AssignGroup(index));
      }
      _idleButton = IconButton("IdleEngineerButton", actions, "ui_idle", "유휴 공병", SelectIdleEngineer);
      _idleBadge = Badge(_idleButton.transform, theme.Danger);
      IconButton("ArmyButton", actions, "ui_army", "전체 군대", SelectArmy);
      // 그리드는 아래 왼쪽부터 채운다: 1층 1·2부대, 2층 3부대·유휴 공병, 3층 전체 군대.

      BuildCard(theme);
      BuildContext(theme);
      BuildUltimateBars(theme);
      BuildDiplomacy(frame);
      QuantumEvent.Subscribe<EventUltimateNotice>(this, OnUltimateNotice);

      QuantumEvent.Subscribe<EventActionRejected>(this, OnRejected);
      QuantumEvent.Subscribe<EventNoteReceived>(this, OnNote);
      QuantumEvent.Subscribe<EventPowerOutage>(this, OnOutage);
      QuantumEvent.Subscribe<EventBarrierOpened>(this, e => ShowAlert(Texts.BarrierAlert));
      // 항목 34·37 알림: 기상, 승급, 저격 당함, 도크린 기본값, 작전기 격추.
      QuantumEvent.Subscribe<EventTraderHostile>(this, e => {
        if (e.Player == Controller.Player) {
          ShowAlert("암시장과 적대! 거래가 영구히 막혔습니다");
        }
        // 미니맵: 암시장 자리에 붉은 경보 고리(누가 적대했든 모두에게).
        var map = MapOf(e.Game);
        if (map != null && map.HasTrader) {
          _minimap.Ping(map.TraderPosition.ToUnityVector3(), Minimap.RelationColors[2]);
        }
      });
      QuantumEvent.Subscribe<EventTraderDestroyed>(this, e => ShowAlert(e.Killer == Controller.Player ? "암시장 파괴 — 현상금 획득!" : "암시장이 파괴되어 닫혔습니다"));
      QuantumEvent.Subscribe<EventWeatherChanged>(this, e => ShowAlert(e.On ? "기상 악화: 시야 감소, 신호탄 구역 밖 출격 불가" : "기상 회복"));
      QuantumEvent.Subscribe<EventVeteranPromoted>(this, e => { if (e.Owner == Controller.Player) { ShowAlert($"유닛 승급: {new string('★', e.Rank)}"); } });
      QuantumEvent.Subscribe<EventSniperFired>(this, e => { if (e.Victim == Controller.Player) { ShowAlert(e.Killed ? "저격 당함! 총구 섬광 방향을 확인하세요" : "저격 공격을 받고 있습니다"); } });
      QuantumEvent.Subscribe<EventDoctrineChosen>(this, e => { if (e.Player == Controller.Player && e.ByDefault) { ShowAlert("도크린 기본값 적용: 공군 타격 사령부"); } });
      QuantumEvent.Subscribe<EventSortieEnded>(this, e => { if (e.Player == Controller.Player && e.ShotDown) { ShowAlert("작전기 격추됨 — 격납 칸이 비었습니다"); } });
      QuantumEvent.Subscribe<EventHqProtocol>(this, e => {
        if (e.Player == Controller.Player) {
          ShowAlert("사령부 비상 방어 프로토콜 발동!");
        }
        PingAlert(e.Position.ToUnityVector3());
      });
      QuantumEvent.Subscribe<EventBlackBoxDown>(this, e => {
        PingAlert(e.Position.ToUnityVector3());
        ShowAlert("추락 수송기 블랙박스 발생 — 15초 점령으로 회수");
      });
      QuantumEvent.Subscribe<EventBlackBoxEnded>(this, e => {
        if (e.Winner == Controller.Player) {
          ShowAlert(e.Reward == 1 ? "블랙박스 회수: 격납고 즉시 재정비 + 출격권!" : "블랙박스 회수: 영구 화력 보너스!");
        } else if (e.Winner.IsValid) {
          ShowAlert("적이 블랙박스를 회수했습니다");
        }
      });
      QuantumEvent.Subscribe<EventBridgeChanged>(this, e => {
        ShowAlert(e.Destroyed ? $"교량 #{e.Index + 1} 파괴됨 — 통행 차단" : $"교량 #{e.Index + 1} 가교 복구 완료");
      });
      QuantumEvent.Subscribe<EventCaptureChanged>(this, OnCapture);
      QuantumEvent.Subscribe<EventSupplyDropIncoming>(this, OnDropIncoming);
      QuantumEvent.Subscribe<EventSupplyDropStage>(this, OnDropStage);
      QuantumEvent.Subscribe<EventBarricadeDestroyed>(this, OnBarricade);
      QuantumEvent.Subscribe<EventBaseUnderAttack>(this, OnBaseAttacked);
      QuantumEvent.Subscribe<EventCratePicked>(this, OnCratePicked);
      QuantumEvent.Subscribe<EventCityBuildingCollapsed>(this, e => GameAudio.Play(SoundEvent.Collapse));
      QuantumEvent.Subscribe<EventPlayerConnection>(this, OnConnection);
      QuantumEvent.Subscribe<EventRadarContact>(this, e => {
        if (e.Player == Controller.Player) {
          GameAudio.Play(SoundEvent.Warning);
          ShowAlert("레이더: 적이 접근합니다");
          PingAlert(e.Position.ToUnityVector3());
        }
      });
      QuantumEvent.Subscribe<EventBaseTierChanged>(this, e => {
        if (e.Player == Controller.Player) {
          ShowAlert($"{Texts.TierName(e.Tier)} 도달");
        }
      });
      QuantumEvent.Subscribe<EventResearchCompleted>(this, e => {
        if (e.Player == Controller.Player) {
          var research = TechSystem.ResearchAt(e.Game.Frames.Verified, e.Player, e.Index);
          ShowAlert(research != null ? $"연구 완료: {Texts.ResearchName(research, UnitName)}" : "연구 완료");
        }
      });
    }

    void BuildUltimateBars(UiTheme theme) {
      _ultimateBars.Clear();
      var width = 2 * Cell + Gap;
      for (var i = 0; i < MaxUltimateBars; i++) {
        var bar = Ui.Capsule($"UltimateBar{i}", Root, theme.Panel);
        Ui.Place(bar, Vector2.one, new Vector2(-12, -8 - Cell - Gap - i * 52), new Vector2(width, 46));
        var fill = Ui.Capsule("Fill", bar, theme.Warning);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0, 1);
        fill.pivot = new Vector2(0, 0.5f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        fill.GetComponent<Image>().raycastTarget = false;
        var label = Ui.Label("Label", bar, "", theme.FontSmall, TextAnchor.MiddleCenter);
        // 항목 36-F: 막대의 빨간 칠이 글자를 가리던 문제. 칠은 배경(반투명), 글자는 흰색 + 검은 외곽선으로 항상 위에 또렷하게 보인다.
        label.color = Color.white;
        var outline = label.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = theme.FontSmall - 8;
        label.resizeTextMaxSize = theme.FontSmall;
        Ui.Stretch(label.rectTransform, 6f);
        label.rectTransform.SetAsLastSibling();
        bar.GetComponent<Image>().raycastTarget = false;
        bar.gameObject.SetActive(false);
        _ultimateBars.Add((bar, fill, fill.GetComponent<Image>(), label));
      }
    }

    static string Who(Frame frame, PlayerRef player) {
      var state = frame.Global->Players.GetPointer(player);
      return $"플레이어 {(int)player + 1}({Texts.FactionName(frame.FindAsset(state->Faction).FactionId)})";
    }

    /// <summary>준비 중·발동 대기인 플레이어마다 한 줄, 날아오는 미사일마다 한 줄(착탄까지 남은 시간).</summary>
    void RefreshUltimateBars(Frame frame) {
      var theme = UiTheme.Current;
      var used = 0;
      void Show(string text, float ratio, Color color) {
        if (used >= _ultimateBars.Count) {
          return;
        }
        var bar = _ultimateBars[used++];
        bar.Root.gameObject.SetActive(true);
        bar.Label.text = text;
        bar.Fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1);
        // 칠은 반투명: 글자(위 칸)를 가리지 않는다.
        color.a = Mathf.Min(color.a, UltimateFillAlpha);
        bar.FillImage.color = color;
      }
      foreach (var pair in frame.GetComponentIterator<MissileStrike>()) {
        var left = Mathf.Max(0f, (pair.Component.ImpactFrame - frame.Number) / (float)frame.SessionConfig.UpdateFPS);
        var total = Mathf.Max(1f, (pair.Component.ImpactFrame - pair.Component.LaunchFrame) / (float)frame.SessionConfig.UpdateFPS);
        Show($"전략 미사일 착탄 {Mathf.CeilToInt(left)}초", 1f - left / total, theme.Danger);
      }
      for (var i = 0; i < frame.PlayerCount; i++) {
        var state = frame.Global->Players.GetPointer(i);
        var spec = UltimateSystem.SpecOf(frame, i);
        if (state->Active == false || spec == null || (state->UltimatePhase != UltimatePhase.Preparing && state->UltimatePhase != UltimatePhase.Ready)) {
          continue;
        }
        var color = (Color)Palette.Of(frame, i);
        color.a = 0.55f;
        var mine = i == (int)Controller.Player ? "내 " : $"{Who(frame, i)} ";
        if (state->UltimatePhase == UltimatePhase.Ready) {
          Show($"{mine}{Texts.UltimateName(spec.Kind)} 발동 대기", 1f, color);
        } else {
          var total = Mathf.Max(1f, state->UltimateTotal.AsFloat);
          Show($"{mine}{Texts.UltimateName(spec.Kind)} 준비 {Mathf.CeilToInt(state->UltimateRemaining.AsFloat)}초", 1f - state->UltimateRemaining.AsFloat / total, color);
        }
      }
      for (var i = used; i < _ultimateBars.Count; i++) {
        _ultimateBars[i].Root.gameObject.SetActive(false);
      }
    }

    /// <summary>궁극 전력 알림은 모든 플레이어에게 보인다(누가·무엇을·남은 시간). 알림을 누르면 그 자리로 간다.</summary>
    void OnUltimateNotice(EventUltimateNotice e) {
      var frame = e.Game.Frames.Verified;
      var mine = e.Player == Controller.Player;
      ShowAlert(Texts.UltimateNotice(Who(frame, e.Player), e.Stage, e.Kind, e.Value));
      if (e.Stage != UltimateStage.Cancelled || e.Position != default) {
        PingAlert(e.Position.ToUnityVector3());
      }
      if (mine == false || e.Stage == UltimateStage.Lost) {
        GameAudio.Play(SoundEvent.Warning);
      }
      _nextRefresh = 0f;
    }

    /// <summary>
    /// 명령 카드의 궁극 전력 칸(사령부와 전용 건물). 조건이 모자라면 자물쇠와 이유 목록, 준비 중이면 남은 시간과 취소,
    /// 발동 대기면 목표 지정(미사일·상륙), 운용·대기 중이면 상태만 보인다.
    /// </summary>
    void UltimateEntries(Frame frame, PlayerState* state) {
      var spec = UltimateSystem.SpecOf(frame, Controller.Player);
      if (spec == null) {
        return;
      }
      var name = Texts.UltimateName(spec.Kind);
      var input = Controller.Input;
      switch (state->UltimatePhase) {
        case UltimatePhase.Preparing:
          Entries.Add(new ContextEntry {
            Label = $"{name}\n준비 {Mathf.CeilToInt(state->UltimateRemaining.AsFloat)}초", Icon = UltimateIcon(frame, spec), Enabled = false, Locked = true,
            Reason = $"{name} 준비 중: 남은 {Mathf.CeilToInt(state->UltimateRemaining.AsFloat)}초. 전용 건물이 부서지면 취소되고 절반만 돌려받습니다",
          });
          Entries.Add(new ContextEntry {
            Label = "준비 취소", Icon = "ui_cancel", Enabled = true, Detail = $"비용의 {Mathf.RoundToInt(spec.CancelRefundFraction.AsFloat * 100)}%를 돌려받습니다",
            Action = () => Controller.Send(new CancelUltimateCommand()),
          });
          return;
        case UltimatePhase.Ready:
          var aiming = input.Mode == MatchInput.TapMode.Ultimate;
          Entries.Add(new ContextEntry {
            Label = aiming ? "목표 지정 중" : spec.Kind == UltimateKind.StrategicMissile ? $"{name}\n발사" : $"{name}\n출발", Icon = UltimateIcon(frame, spec), Enabled = true,
            Detail = spec.Kind == UltimateKind.StrategicMissile ? $"땅을 누르면 발사. {spec.WarningSeconds.AsInt}초 뒤 착탄, 아군도 맞습니다" : "상륙할 해안을 누르면 상륙정이 출발합니다",
            Action = () => input.Mode = aiming ? MatchInput.TapMode.None : MatchInput.TapMode.Ultimate,
          });
          return;
        case UltimatePhase.Active:
          Entries.Add(new ContextEntry { Label = $"{name}\n운용 중", Icon = UltimateIcon(frame, spec), Enabled = false, Locked = true, Reason = $"{name} 운용 중(보유 상한 {spec.OwnedLimit})" });
          return;
      }
      var locks = UltimateSystem.Locks(frame, Controller.Player);
      var cost = UltimateSystem.CostOf(frame, Controller.Player, spec);
      var seconds = UltimateSystem.SecondsOf(frame, Controller.Player, spec);
      var siteName = Texts.NameOf(frame.FindAsset(spec.Site).name);
      var blocked = locks & ~UltimateLock.Resources;
      string reason = null;
      if (state->UltimatePhase == UltimatePhase.Cooldown) {
        reason = $"{name}: 재사용 대기 {Mathf.CeilToInt(state->UltimateRemaining.AsFloat)}초";
      } else if (blocked != UltimateLock.None) {
        reason = $"{name}: {Texts.UltimateLocks(blocked, spec.RequiredTier, siteName)}";
      }
      Entries.Add(new ContextEntry {
        Label = $"{name}\n{(reason == null ? Texts.Cost(cost, state->Stock) : state->UltimatePhase == UltimatePhase.Cooldown ? $"대기 {Mathf.CeilToInt(state->UltimateRemaining.AsFloat)}초" : "잠김")}",
        Icon = UltimateIcon(frame, spec),
        Enabled = reason == null,
        Locked = reason != null,
        Reason = reason,
        Detail = $"{name} 준비: {Texts.Cost(cost, state->Stock)}  {seconds.AsInt}초. 시작·절반·완료가 모든 플레이어에게 알려집니다",
        Action = () => Controller.Send(new StartUltimateCommand()),
      });
    }

    /// <summary>아이콘 위 + 짧은 글자 아래 버튼.</summary>
    static Button IconButton(string name, Transform parent, string icon, string label, UnityEngine.Events.UnityAction onClick) {
      var button = Ui.Button(name, parent, label, onClick, UiTheme.Current.FontSmall);
      Ui.SetIcon(button, Ui.IconSprite(icon));
      return button;
    }

    static Text Badge(Transform parent, Color color) {
      var badge = Ui.Capsule("Badge", parent, color);
      Ui.Place(badge, Vector2.one, new Vector2(-4, -4), new Vector2(52, 40));
      var text = Ui.Label("Count", badge, "", UiTheme.Current.FontSmall, TextAnchor.MiddleCenter);
      text.color = Color.white;
      Ui.Stretch(text.rectTransform);
      badge.gameObject.SetActive(false);
      return text;
    }

    RectTransform _doctrinePanel;
    Text _doctrineTitle;

    static readonly (int Id, string Name, string Body, string Icon)[] Doctrines = {
      (1, "공군 타격 사령부", "출격기 재정비 -25%\n정찰차 시야·속도 +20%", "doctrine_air"),
      (2, "그림자 특수전단", "저격수 한도 +1\n은신 +10초 · 엄폐 피해 -15%", "doctrine_shadow"),
      (3, "기갑 요새화 군단", "전차·포격함 생산비 -15%\n공병 수리 +50%", "doctrine_armor"),
    };

    /// <summary>항목 37: 경기 시작 직후 도크린 3택 1 카드. 이미지는 UiCatalog 칸(doctrine_*)이 있으면 그것, 없으면 도형 카드.</summary>
    void BuildDoctrinePanel(UiTheme theme) {
      _doctrinePanel = Ui.Panel("DoctrinePanel", Root, theme.Panel);
      Ui.Place(_doctrinePanel, new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1180, 470));
      _doctrineTitle = Ui.Label("Title", _doctrinePanel, "전술 도크린을 고르세요", theme.FontLarge, TextAnchor.MiddleCenter);
      _doctrineTitle.color = theme.PanelText;
      Ui.Place(_doctrineTitle.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(1100, 70));
      for (var i = 0; i < Doctrines.Length; i++) {
        var card = Doctrines[i];
        var id = card.Id;
        var button = Ui.Button($"Doctrine{id}", _doctrinePanel, $"{card.Name}\n\n{card.Body}", () => {
          Controller.Send(new ChooseDoctrineCommand { Doctrine = id });
          _doctrinePanel.gameObject.SetActive(false);
        }, theme.FontMedium - 2);
        Ui.Place((RectTransform)button.transform, new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 375, -30), new Vector2(350, 300));
        var art = UiCatalog.SpriteFor(card.Icon);
        if (art != null) {
          Ui.SetIcon(button, art);
        }
      }
      _doctrinePanel.gameObject.SetActive(false);
    }

    RectTransform _airOpsPanel;
    readonly List<(Button Button, Text Label, EntityRef Airfield, int Slot)> _airOpsSlots = new List<(Button, Text, EntityRef, int)>();

    /// <summary>항목 34-E: 비행장을 선택하지 않아도 격납고 작전기를 바로 출격·재정비 확인할 수 있는 항공 작전 패널.</summary>
    void BuildAirOpsPanel(UiTheme theme) {
      _airOpsPanel = Ui.Panel("AirOpsPanel", Root, theme.Panel);
      Ui.Place(_airOpsPanel, new Vector2(0f, 0f), new Vector2(328, 12), new Vector2(560, 88));
      for (var i = 0; i < SortieSystem.Slots; i++) {
        var idx = i;
        var btn = Ui.Button($"AirOp{idx}", _airOpsPanel, "빈 칸", () => {
          if (idx >= _airOpsSlots.Count) {
            return;
          }
          var entry = _airOpsSlots[idx];
          if (entry.Airfield == EntityRef.None) {
            return;
          }
          var input = Controller.Input;
          var picking = input.Mode == MatchInput.TapMode.Sortie && input.SortieAirfield == entry.Airfield && input.SortieSlot == entry.Slot;
          input.SortieAirfield = entry.Airfield;
          input.SortieSlot = entry.Slot;
          input.Mode = picking ? MatchInput.TapMode.None : MatchInput.TapMode.Sortie;
        }, theme.FontSmall);
        Ui.Place((RectTransform)btn.transform, new Vector2(0f, 0.5f), new Vector2(10 + idx * 136, 0), new Vector2(128, 70));
        _airOpsSlots.Add((btn, btn.GetComponentInChildren<Text>(), EntityRef.None, 0));
      }
      _airOpsPanel.gameObject.SetActive(false);
    }

    void RefreshAirOps(Frame frame) {
      if (_airOpsPanel == null || Controller.Player.IsValid == false) {
        return;
      }
      if (DoctrineOpen) {
        _airOpsPanel.gameObject.SetActive(false);
        return;
      }
      var collected = new List<(EntityRef Airfield, int Slot, HangarSlot Data)>();
      var buildings = frame.Filter<Building>();
      while (buildings.NextUnsafe(out var entity, out var b)) {
        if (b->Owner != Controller.Player || b->ConstructionRemaining > FP._0 || b->Health <= FP._0) {
          continue;
        }
        for (var i = 0; i < SortieSystem.Slots && collected.Count < SortieSystem.Slots; i++) {
          var s = b->Hangar[i];
          if (s.State != 0 && s.Spec.IsValid) {
            collected.Add((entity, i, s));
          }
        }
      }
      var show = collected.Count > 0;
      if (_airOpsPanel.gameObject.activeSelf != show) {
        _airOpsPanel.gameObject.SetActive(show);
      }
      if (show == false) {
        return;
      }
      var fps = Mathf.Max(1, frame.SessionConfig.UpdateFPS);
      var input = Controller.Input;
      for (var i = 0; i < _airOpsSlots.Count; i++) {
        var item = _airOpsSlots[i];
        if (i >= collected.Count) {
          item.Button.gameObject.SetActive(false);
          _airOpsSlots[i] = (item.Button, item.Label, EntityRef.None, 0);
          continue;
        }
        item.Button.gameObject.SetActive(true);
        var (af, slotIdx, slot) = collected[i];
        var spec = frame.FindAsset(slot.Spec);
        var role = spec.SortieKind == SortieKind.Strafe ? "기총소사" : spec.SortieKind == SortieKind.Bomb ? "폭격" : "공수투하";
        var left = slot.State == 2 ? -1 : Mathf.Max(0, Mathf.CeilToInt((slot.ReadyFrame - frame.Number) / (float)fps));
        var picking = input != null && input.Mode == MatchInput.TapMode.Sortie && input.SortieAirfield == af && input.SortieSlot == slotIdx;
        item.Button.interactable = slot.State == 1 && left == 0;
        item.Label.text = slot.State == 2 ? $"{role}\n출격 중" : picking ? $"{role}\n목표 탭" : left > 0 ? $"{role}\n재정비 {left}초" : $"{role}\n출격 가능";
        _airOpsSlots[i] = (item.Button, item.Label, af, slotIdx);
      }
    }

    public bool DoctrineOpen { get; private set; }

    void RefreshDoctrine(Frame frame) {
      if (_doctrinePanel == null || Controller.Player.IsValid == false) {
        return;
      }
      var state = FactoryWarsUtil.Player(frame, Controller.Player);
      var rules = frame.FindAsset(frame.RuntimeConfig.Rules);
      var left = rules.DoctrineSelectSeconds - frame.Number / Mathf.Max(1, frame.SessionConfig.UpdateFPS);
      var show = state != null && state->DoctrineLocked == false && left > 0;
      if (_doctrinePanel.gameObject.activeSelf != show) {
        _doctrinePanel.gameObject.SetActive(show);
      }
      DoctrineOpen = show;
      if (show) {
        // 큰 창이 겹치지 않게: 도크린 선택 중에는 오른쪽 명령 패널·선택 카드를 숨긴다.
        _contextPanel?.SetActive(false);
        _card?.gameObject.SetActive(false);
        _doctrineTitle.text = $"전술 도크린을 고르세요 ({left}초 뒤 기본값)";
      }
    }

    void BuildCard(UiTheme theme) {
      _card = Ui.Panel("SelectionCard", Root, theme.Panel);
      Ui.Place(_card, new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(660, Cell + 2 * Pad + 36));
      var frame = Ui.Rect("IconFrame", _card);
      var frameImage = frame.gameObject.AddComponent<Image>();
      frameImage.sprite = UiCatalog.SpriteFor("icon_frame");
      frameImage.type = Image.Type.Sliced;
      frameImage.raycastTarget = false;
      Ui.Place(frame, new Vector2(0, 0.5f), new Vector2(Pad, 0), new Vector2(150, 150));
      _cardIcon = Ui.Icon("Icon", frame, "", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(124, 124));
      _cardTitle = Ui.Label("Title", _card, "", theme.FontLarge - 2);
      _cardTitle.color = theme.PanelText;
      Ui.Place(_cardTitle.rectTransform, new Vector2(0, 1), new Vector2(176, -10), new Vector2(380, 44));
      _cardBadge = Ui.Capsule("SizeBadge", _card, theme.Accent);
      var badgeSprite = UiCatalog.SpriteFor("badge_small");
      if (badgeSprite != null) {
        _cardBadge.GetComponent<Image>().sprite = badgeSprite;
      }
      Ui.Place(_cardBadge, new Vector2(1, 1), new Vector2(-Pad, -12), new Vector2(92, 40));
      _cardBadgeText = Ui.Label("Text", _cardBadge, "", theme.FontSmall, TextAnchor.MiddleCenter);
      _cardBadgeText.color = Color.white;
      Ui.Stretch(_cardBadgeText.rectTransform);
      _cardHealth = Ui.Rect("HealthBar", _card);
      var back = _cardHealth.gameObject.AddComponent<Image>();
      back.sprite = UiCatalog.SpriteFor("health_bar");
      back.type = Image.Type.Sliced;
      back.color = new Color(0.2f, 0.22f, 0.26f, 0.6f);
      back.raycastTarget = false;
      Ui.Place(_cardHealth, new Vector2(0, 1), new Vector2(176, -60), new Vector2(460, 20));
      var fill = Ui.Rect("Fill", _cardHealth);
      _cardHealthFill = fill.gameObject.AddComponent<Image>();
      _cardHealthFill.sprite = UiCatalog.SpriteFor("health_fill");
      _cardHealthFill.type = Image.Type.Sliced;
      _cardHealthFill.raycastTarget = false;
      fill.anchorMin = Vector2.zero;
      fill.anchorMax = new Vector2(1, 1);
      fill.offsetMin = Vector2.zero;
      fill.offsetMax = Vector2.zero;
      _cardDetail = Ui.Label("Detail", _card, "", theme.FontSmall, TextAnchor.UpperLeft);
      _cardDetail.color = theme.PanelText;
      Ui.Place(_cardDetail.rectTransform, new Vector2(0, 1), new Vector2(176, -88), new Vector2(470, 100));
      for (var i = 0; i < QueueIcons; i++) {
        var icon = Ui.Icon($"Queue{i}", _card, "", new Vector2(0, 0), new Vector2(176 + i * 64, Pad), new Vector2(56, 56));
        _queueIcons.Add(icon);
      }
      // 여러 종류 선택: 종류별 아이콘 + 개수. 누르면 그 종류만 남긴다.
      for (var i = 0; i < MaxTypeButtons; i++) {
        var index = i;
        var button = Ui.Button($"Type{i}", _card, "", () => SelectType(index), theme.FontSmall);
        Ui.Place((RectTransform)button.transform, new Vector2(0, 0.5f), new Vector2(Pad + i * (Cell + Gap), 0), new Vector2(Cell, Cell));
        button.gameObject.SetActive(false);
        _typeButtons.Add(button);
      }
      _card.gameObject.SetActive(false);
    }

    void BuildContext(UiTheme theme) {
      var panel = Ui.Panel("ContextPanel", Root, theme.Panel);
      _contextPanel = panel.gameObject;
      Ui.Place(panel, new Vector2(1, 0), new Vector2(-12, 8), new Vector2(ContextWidth, ContextHeight));
      _status = Ui.Label("Status", panel, "", theme.FontSmall + 2);
      _status.color = theme.PanelText;
      _status.horizontalOverflow = HorizontalWrapMode.Wrap;
      _status.resizeTextForBestFit = true;
      _status.resizeTextMinSize = theme.FontSmall - 4;
      _status.resizeTextMaxSize = theme.FontSmall + 2;
      _status.rectTransform.anchorMin = new Vector2(0, 1);
      _status.rectTransform.anchorMax = Vector2.one;
      _status.rectTransform.pivot = new Vector2(0.5f, 1);
      _status.rectTransform.sizeDelta = new Vector2(-2 * Pad, HeaderHeight - 4);
      _status.rectTransform.anchoredPosition = new Vector2(0, -6);
      _context = new Button[ContextSlots];
      for (var i = 0; i < ContextSlots; i++) {
        var index = i;
        _context[i] = Ui.Button($"Context{i}", panel, "", () => PressContext(index), theme.FontSmall);
        var row = i / ContextColumns;
        var column = i % ContextColumns;
        // 빈 칸은 비워 둔다: 칸마다 고정 자리.
        Ui.Place((RectTransform)_context[i].transform, new Vector2(0, 1), new Vector2(Pad + column * (Cell + Gap), -HeaderHeight - Pad - row * (Cell + Gap)), new Vector2(Cell, Cell));
        ((RectTransform)_context[i].transform).pivot = new Vector2(0, 1);
        LongPress.Attach(_context[i], () => LongPressContext(index));
      }
      _contextPanel.SetActive(false);

      // 툴팁: 명령 카드 바로 위.
      _tooltip = Ui.Panel("Tooltip", Root, new Color(0.12f, 0.15f, 0.22f, 0.94f));
      var tooltipSprite = UiCatalog.SpriteFor("tooltip");
      if (tooltipSprite != null) {
        _tooltip.GetComponent<Image>().sprite = tooltipSprite;
      }
      Ui.Place(_tooltip, new Vector2(1, 0), new Vector2(-12, 8 + ContextHeight + Gap), new Vector2(ContextWidth, 96));
      _tooltipText = Ui.Label("Text", _tooltip, "", theme.FontSmall + 2, TextAnchor.MiddleLeft);
      _tooltipText.color = Color.white;
      Ui.Stretch(_tooltipText.rectTransform, 14f);
      _tooltip.GetComponent<Image>().raycastTarget = false;
      _tooltip.gameObject.SetActive(false);
    }

    void OnBaseAttacked(EventBaseUnderAttack e) {
      if (e.Player == Controller.Player) {
        GameAudio.Play(SoundEvent.UnderAttack);
        ShowAlert(Texts.BaseAttacked);
        PingAlert(e.Position.ToUnityVector3());
      }
    }

    int _produced = -1;

    /// <summary>온라인(항목 30): 다른 사람이 끊기면 "연결 대기"와 기권까지 남은 시간을 보인다. 그동안 AI가 대신 조종한다.</summary>
    void OnConnection(EventPlayerConnection e) {
      if (e.Player == Controller.Player) {
        return;
      }
      ShowAlert(e.Connected ? $"플레이어 {(int)e.Player + 1} 다시 연결됨" : $"플레이어 {(int)e.Player + 1} 연결 대기 중 (AI가 대신 조종)");
    }

    /// <summary>끊긴 사람 목록과 기권까지 남은 초. 화면 위 알림 줄에 계속 보인다.</summary>
    public static string ConnectionWaiting(Frame frame, PlayerRef viewer) {
      var rules = frame.FindAsset(frame.RuntimeConfig.Rules);
      var parts = new List<string>();
      for (var i = 0; i < frame.PlayerCount; i++) {
        var state = frame.Global->Players.GetPointer(i);
        if (i == (int)viewer || state->DisconnectedFrame <= 0 || state->Defeated) {
          continue;
        }
        var left = rules.DisconnectForfeitSeconds - (frame.Number - state->DisconnectedFrame) / frame.SessionConfig.UpdateFPS;
        parts.Add(rules.DisconnectForfeitSeconds > 0 ? $"플레이어 {i + 1} 연결 대기 {Mathf.Max(0, left)}초" : $"플레이어 {i + 1} 연결 끊김(AI 조종)");
      }
      return string.Join("  ", parts);
    }

    void OnCratePicked(EventCratePicked e) {
      if (e.Player == Controller.Player) {
        FloatText(e.Position.ToUnityVector3(), Texts.Reward(e.Reward));
      }
    }

    unsafe void OnCapture(EventCaptureChanged e) {
      var frame = e.Game.Frames.Verified;
      if (frame == null || frame.Exists(e.Site) == false) {
        return;
      }
      var kind = frame.Get<CaptureSite>(e.Site).Kind;
      var position = frame.Get<Transform3D>(e.Site).Position.ToUnityVector3();
      if (e.Next == Controller.Player) {
        GameAudio.Play(SoundEvent.Captured);
        ShowAlert(Texts.Captured(kind));
        PingAlert(position);
      } else if (e.Previous == Controller.Player) {
        ShowAlert(Texts.Lost(kind));
        PingAlert(position);
      }
    }

    void OnDropIncoming(EventSupplyDropIncoming e) {
      ShowAlert(Texts.DropIncoming);
      var spots = MapOf(e.Game)?.CrateSpots;
      for (var i = 0; spots != null && i < spots.Length; i++) {
        if (spots[i].Seat < 0 && spots[i].Group == e.Group) {
          PingAlert(spots[i].Position.ToUnityVector3());
        }
      }
    }

    void OnDropStage(EventSupplyDropStage e) {
      var pos = e.Position.ToUnityVector3();
      switch (e.Phase) {
        case DropPhase.Zone:
          PingAlert(pos);
          ShowAlert("보급 투하 예고(35초) — 붉은 신호탄 구역 확인");
          _minimap.Ping(pos, new Color(0.95f, 0.22f, 0.16f));
          break;
        case DropPhase.Closing:
          PingAlert(pos);
          ShowAlert("보급 투하 임박(10초) — 착지 반경 축소");
          _minimap.Ping(pos, new Color(1f, 0.45f, 0.12f));
          break;
        case DropPhase.Landed:
          PingAlert(pos);
          ShowAlert("보급 상자 착지! 신호탄 시야 공개");
          _minimap.Ping(pos, new Color(1f, 0.85f, 0.25f));
          break;
      }
    }

    void OnBarricade(EventBarricadeDestroyed e) {
      ShowAlert(Texts.BackdoorOpened);
      var barricades = MapOf(e.Game)?.Barricades;
      if (barricades != null && e.Index < barricades.Length) {
        PingAlert(barricades[e.Index].Shape.Center.ToUnityVector3());
      }
    }

    static MapSpec MapOf(QuantumGame game) {
      var frame = game?.Frames?.Verified;
      return frame?.FindAsset<MapSpec>(frame.Map.UserAsset);
    }

    const float FloatSeconds = 1.4f;
    const float FloatRise = 70f;
    readonly List<(Text Label, Vector3 World, float Start)> _floats = new List<(Text, Vector3, float)>();

    public int FloatCount => _floats.Count;

    /// <summary>월드 위치에서 떠오르는 글자.</summary>
    public void FloatText(Vector3 world, string text) {
      var label = Ui.Label("Float", Root, text, 34, TextAnchor.MiddleCenter);
      label.color = new Color(1f, 0.95f, 0.55f);
      label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
      label.rectTransform.sizeDelta = new Vector2(420, 48);
      _floats.Add((label, world, Time.unscaledTime));
      UpdateFloats();
    }

    void UpdateFloats() {
      var camera = Camera.main;
      for (var i = _floats.Count - 1; i >= 0; i--) {
        var (label, world, start) = _floats[i];
        var age = (Time.unscaledTime - start) / FloatSeconds;
        if (age >= 1f || camera == null) {
          Destroy(label.gameObject);
          _floats.RemoveAt(i);
          continue;
        }
        var screen = camera.WorldToScreenPoint(world + Vector3.up * 2f);
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)label.rectTransform.parent, screen, null, out var local);
        label.rectTransform.anchoredPosition = local + Vector2.up * (FloatRise * age);
        label.color = new Color(label.color.r, label.color.g, label.color.b, 1f - age * age);
      }
    }

    void BuildDiplomacy(Frame frame) {
      _diplomacy = Ui.Panel("Diplomacy", Root);
      Ui.Place(_diplomacy, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500, 900));
      _diplomacy.pivot = new Vector2(0.5f, 0.5f);
      var rules = frame.FindAsset(frame.RuntimeConfig.Rules);
      var row = 0;
      for (var i = 0; i < frame.PlayerCount; i++) {
        PlayerRef other = i;
        if (other == Controller.Player || frame.Global->Players.GetPointer(i)->Active == false) {
          continue;
        }
        var y = -20 - row * 150;
        var line = Ui.Rect($"Row{i}", _diplomacy);
        Ui.Place(line, new Vector2(0, 1), new Vector2(20, y), new Vector2(1460, Ui.MinButtonSize));
        var name = Ui.Label("Name", line, "", 30);
        Ui.Place(name.rectTransform, new Vector2(0, 0.5f), Vector2.zero, new Vector2(330, Ui.MinButtonSize));
        var x = 340f;
        Button Add(string label, DiplomacyAction action) {
          var button = Ui.Button(label, line, label, () => Controller.Send(new DiplomacyCommand { Action = action, Target = other }), 24);
          Ui.Place((RectTransform)button.transform, new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(Ui.MinButtonSize + 40, Ui.MinButtonSize));
          x += Ui.MinButtonSize + 50;
          return button;
        }
        var propose = Add($"동맹 제안\n철 {rules.ProposalCost.AsInt}", DiplomacyAction.Propose);
        var accept = Add($"수락\n철 {rules.AcceptCost.AsInt}", DiplomacyAction.Accept);
        var reject = Add("거절", DiplomacyAction.Reject);
        var breakButton = Add($"파기\n철 {rules.BreakCost.AsInt}", DiplomacyAction.Break);
        var note = Ui.Button("Note", line, $"쪽지\n철 {rules.NoteSingleCost.AsInt}",
          () => Controller.Send(new SendNoteCommand { PhraseId = _phrase, TargetMode = NoteTarget.Player, Receiver = other }), 24);
        Ui.Place((RectTransform)note.transform, new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(Ui.MinButtonSize + 40, Ui.MinButtonSize));
        _rows.Add((other, name, propose, accept, reject, breakButton, note));
        row++;
      }

      var phrase = Ui.Button("Phrase", _diplomacy, Texts.Phrases[0], () => {
        _phrase = (_phrase + 1) % Texts.Phrases.Length;
      }, 26);
      Ui.Place((RectTransform)phrase.transform, Vector2.zero, new Vector2(20, 20), new Vector2(520, Ui.MinButtonSize));
      var allies = Ui.Button("NoteAllies", _diplomacy, $"동맹에게\n철 {rules.NoteGroupCost.AsInt}",
        () => Controller.Send(new SendNoteCommand { PhraseId = _phrase, TargetMode = NoteTarget.Allies }), 24);
      Ui.Place((RectTransform)allies.transform, Vector2.zero, new Vector2(550, 20), new Vector2(180, Ui.MinButtonSize));
      var everyone = Ui.Button("NoteEveryone", _diplomacy, $"모두에게\n철 {rules.NoteGroupCost.AsInt}",
        () => Controller.Send(new SendNoteCommand { PhraseId = _phrase, TargetMode = NoteTarget.Everyone }), 24);
      Ui.Place((RectTransform)everyone.transform, Vector2.zero, new Vector2(740, 20), new Vector2(180, Ui.MinButtonSize));
      var close = Ui.Button("Close", _diplomacy, "닫기", ToggleDiplomacy);
      Ui.Place((RectTransform)close.transform, new Vector2(1, 0), new Vector2(-20, 20), new Vector2(Ui.MinButtonSize, Ui.MinButtonSize));
      _inbox = Ui.Label("Inbox", _diplomacy, "", 24, TextAnchor.UpperLeft);
      Ui.Place(_inbox.rectTransform, Vector2.zero, new Vector2(20, 170), new Vector2(1460, 160));
      _diplomacy.gameObject.SetActive(false);
    }

    public void ToggleDiplomacy() {
      _diplomacy.gameObject.SetActive(DiplomacyOpen == false);
    }

    public void ToggleBoxMode() {
      BoxMode = BoxMode == false;
      Ui.SetLabel(_boxButton, BoxMode ? "범위 중" : "범위");
      _boxButton.GetComponent<Image>().color = BoxMode ? UiTheme.Current.ButtonPressed : Ui.ButtonColor;
    }

    int _lastGroup = -1;
    float _lastGroupTime;
    Vector3? _alertAt;
    public const float DoubleTapSeconds = 0.4f;

    /// <summary>부대 버튼 길게 누르기: 지금 선택을 그 번호에 지정한다.</summary>
    public void AssignGroup(int index) {
      var selection = Controller.Input.Selection;
      selection.SaveGroup(index);
      ShowAlert($"{index + 1}부대 지정: {selection.GroupSize(index)}기");
    }

    /// <summary>부대 버튼: 누르면 선택. 같은 번호를 0.4초 안에 두 번 누르면 카메라가 그 부대로 간다.</summary>
    public void PressGroup(int index) {
      var selection = Controller.Input.Selection;
      selection.RecallGroup(index);
      var now = Time.unscaledTime;
      if (_lastGroup == index && now - _lastGroupTime <= DoubleTapSeconds && selection.Units.Count > 0) {
        var frame = Controller.Frame;
        var center = Vector3.zero;
        var count = 0;
        foreach (var unit in selection.Units) {
          if (frame != null && frame.Exists(unit)) {
            center += frame.Get<Transform3D>(unit).Position.ToUnityVector3();
            count++;
          }
        }
        if (count > 0) {
          center /= count;
          Controller.Camera.MoveTo(new Vector2(center.x, center.z));
        }
      }
      _lastGroup = index;
      _lastGroupTime = now;
    }

    public void SelectIdleEngineer() {
      var frame = Controller.Frame;
      if (frame != null && Controller.Input.SelectIdleEngineer(frame, Controller.Player, out var position)) {
        Controller.Camera.MoveTo(new Vector2(position.x, position.z));
      } else {
        ShowAlert("놀고 있는 공병이 없습니다");
      }
    }

    public void SelectArmy() {
      var frame = Controller.Frame;
      if (frame != null && Controller.Input.SelectArmy(frame, Controller.Player) == 0) {
        ShowAlert("전투 유닛이 없습니다");
      }
    }

    /// <summary>선택 카드의 종류 버튼: 그 종류만 남긴다.</summary>
    public void SelectType(int index) {
      var frame = Controller.Frame;
      if (frame == null || index >= _typeSpecs.Count) {
        return;
      }
      var spec = _typeSpecs[index];
      Controller.Input.Selection.Units.RemoveAll(e => frame.Exists(e) == false || frame.Get<Unit>(e).Spec != spec);
      _nextRefresh = 0f;
    }

    void PingAlert(Vector3 position) {
      _minimap.Ping(position);
      _alertAt = position;
    }

    public Vector3? AlertTarget => _alertAt;

    public void JumpToAlert() {
      if (_alertAt.HasValue) {
        Controller.Camera.MoveTo(new Vector2(_alertAt.Value.x, _alertAt.Value.z));
      }
    }

    /// <summary>명령 카드의 칸을 누른다. 잠긴 칸은 이유를 보인다.</summary>
    public void PressContext(int slot) {
      if (slot < 0 || slot >= Entries.Count) {
        return;
      }
      var entry = Entries[slot];
      if (entry.Enabled) {
        entry.Action?.Invoke();
        _nextRefresh = 0f;
        var frame = Controller.Frame;
        if (frame != null) {
          Refresh(frame);
        }
      } else if (string.IsNullOrEmpty(entry.Reason) == false) {
        ShowTooltip(entry.Reason);
      }
    }

    /// <summary>길게 누르기: 잠긴 칸은 이유, 아니면 설명.</summary>
    public void LongPressContext(int slot) {
      if (slot < 0 || slot >= Entries.Count) {
        return;
      }
      var entry = Entries[slot];
      var text = entry.Locked ? entry.Reason : entry.Detail;
      if (string.IsNullOrEmpty(text) == false) {
        ShowTooltip(text);
      }
    }

    public void ShowTooltip(string text) {
      _tooltipText.text = text;
      _tooltip.gameObject.SetActive(string.IsNullOrEmpty(text) == false);
      _tooltipUntil = Time.unscaledTime + TooltipSeconds;
    }

    public void ShowAlert(string text) {
      _alert.text = text;
      _alertAt = null;
      if (_alertBubble != null) {
        _alertBubble.gameObject.SetActive(!string.IsNullOrEmpty(text));
      }
      _alertUntil = Time.unscaledTime + AlertSeconds;
    }

    void OnRejected(EventActionRejected e) {
      if (e.Player == Controller.Player) {
        GameAudio.Play(SoundEvent.Warning);
        ShowAlert(Texts.Rejected(e.Reason, (MissingResource)e.Missing));
        // 모자란 자원 칸을 빨갛게 깜빡인다.
        for (var i = 0; i < ResourceBits.Length; i++) {
          if ((e.Missing & (int)ResourceBits[i]) != 0) {
            _flashUntil[i] = Time.unscaledTime + FlashSeconds;
          }
        }
      }
    }

    /// <summary>깜빡이는 자원 칸 수(시험용).</summary>
    public int FlashingResources => _flashUntil.Count(t => t > Time.unscaledTime);

    void OnNote(EventNoteReceived e) {
      if (e.Receiver != Controller.Player) {
        return;
      }
      var line = $"플레이어 {(int)e.Sender + 1}: {Texts.Phrases[e.PhraseId]}";
      NoteInbox.Add(line);
      _note.text = line;
      if (_noteBubble != null) {
        _noteBubble.gameObject.SetActive(!string.IsNullOrEmpty(line));
      }
      _noteUntil = Time.unscaledTime + NoteSeconds;
    }

    void OnOutage(EventPowerOutage e) {
      if (e.Player == Controller.Player) {
        ShowAlert(e.Started ? "정전" : "전기 복구");
        if (e.Started) {
          _flashUntil[3] = Time.unscaledTime + FlashSeconds;
        }
      }
    }

    void Update() {
      if (_resources.Count == 0) {
        return;
      }
      if (_alert.text.Length > 0 && Time.unscaledTime >= _alertUntil) {
        _alert.text = "";
        _alertBubble.gameObject.SetActive(false);
      }
      if (_tooltip.gameObject.activeSelf && Time.unscaledTime >= _tooltipUntil) {
        _tooltipText.text = "";
        _tooltip.gameObject.SetActive(false);
      }
      if (_floats.Count > 0) {
        UpdateFloats();
      }
      if (_note.text.Length > 0 && Time.unscaledTime >= _noteUntil) {
        _note.text = "";
        _noteBubble.gameObject.SetActive(false);
      }
      var theme = UiTheme.Current;
      for (var i = 0; i < _resources.Count; i++) {
        var flashing = _flashUntil[i] > Time.unscaledTime && Mathf.Repeat(Time.unscaledTime * 4f, 1f) < 0.5f;
        _resources[i].Back.color = flashing ? theme.Danger : theme.Panel;
      }
      if (UnityEngine.Input.GetKeyDown(KeyCode.Space)) {
        JumpToAlert();
      }
      if (Time.unscaledTime < _nextRefresh) {
        return;
      }
      _nextRefresh = Time.unscaledTime + RefreshSeconds;
      var frame = Controller.Frame;
      if (frame != null) {
        Refresh(frame);
      }
    }

    public void Refresh(Frame frame) {
      var state = frame.Global->Players.GetPointer(Controller.Player);
      TopBarText = Texts.TopBar(*state);
      var values = Texts.TopBarValues(*state);
      for (var i = 0; i < _resources.Count; i++) {
        _resources[i].Value.text = values[i];
      }
      _truce.text = FactoryWarsUtil.InTruce(frame)
        ? Texts.TruceCountdown((frame.Global->TruceEndFrame - frame.Number) / (float)frame.SessionConfig.UpdateFPS)
        : "";
      var waiting = ConnectionWaiting(frame, Controller.Player);
      if (waiting.Length > 0) {
        _truce.text = waiting;
      }
      _truceBadge.gameObject.SetActive(_truce.text.Length > 0);
      if (_produced >= 0 && state->UnitsProduced > _produced) {
        GameAudio.Play(SoundEvent.ProductionDone);
      }
      _produced = state->UnitsProduced;
      TruceTips.Update(this, frame, state);
      _minimap.Refresh(frame);
      var selection = Controller.Input.Selection;
      selection.Prune(frame);
      for (var i = 0; i < _groupButtons.Length; i++) {
        Ui.SetLabel(_groupButtons[i], selection.GroupSize(i) > 0 ? $"{i + 1}부대 {selection.GroupSize(i)}" : $"{i + 1}부대");
      }
      IdleEngineers = CountIdle(frame);
      _idleBadge.text = IdleEngineers.ToString();
      _idleBadge.transform.parent.gameObject.SetActive(IdleEngineers > 0);
      var key = SelectionKey(selection);
      if (key != _selectionKey) {
        _selectionKey = key;
        _menu = Menu.Root;
      }
      RefreshContext(frame, state);
      RefreshCard(frame, state);
      RefreshDoctrine(frame);
      RefreshAirOps(frame);
      RefreshUltimateBars(frame);
      Layout();
      if (DiplomacyOpen) {
        RefreshDiplomacy(frame);
      }
    }

    static string SelectionKey(SelectionModel selection) => selection.Building.ToString() + "|" + (selection.Units.Count > 0 ? selection.Units[0].ToString() : "") + "|" + selection.Units.Count;

    int CountIdle(Frame frame) {
      var count = 0;
      foreach (var pair in frame.GetComponentIterator<Worker>()) {
        var unit = frame.Get<Unit>(pair.Entity);
        count += unit.Owner == Controller.Player && pair.Component.Task == WorkerTask.Idle && unit.Order == UnitOrder.Stop ? 1 : 0;
      }
      return count;
    }

    /// <summary>선택 카드를 미니맵 묶음과 명령 카드 사이에 둔다. 사이가 좁으면(4:3 등) 명령 카드 위로 올린다.</summary>
    void Layout() {
      var width = Root.rect.width;
      var left = LeftClusterWidth;
      var right = width - ContextWidth - 12 - 16;
      var room = right - left;
      var cardWidth = Mathf.Min(660f, room);
      if (room >= 520f) {
        _card.anchorMin = _card.anchorMax = _card.pivot = new Vector2(0, 0);
        _card.anchoredPosition = new Vector2(left + (room - cardWidth) / 2f, 8);
        _card.sizeDelta = new Vector2(cardWidth, _card.sizeDelta.y);
      } else {
        _card.anchorMin = _card.anchorMax = _card.pivot = new Vector2(1, 0);
        _card.anchoredPosition = new Vector2(-12, 8 + ContextHeight + Gap + (_tooltip.gameObject.activeSelf ? 96 + Gap : 0));
        _card.sizeDelta = new Vector2(Mathf.Max(ContextWidth, 600f), _card.sizeDelta.y);
      }
      var innerWidth = _card.sizeDelta.x - 176 - Pad;
      _cardTitle.rectTransform.sizeDelta = new Vector2(Mathf.Max(120f, innerWidth - 100), 44);
      _cardHealth.sizeDelta = new Vector2(innerWidth, 20);
      _cardDetail.rectTransform.sizeDelta = new Vector2(innerWidth, 100);
    }

    // ───────────── 선택 카드 ─────────────

    public static string UnitIcon(string id) {
      if (Ui.IconSprite("model_" + id) != null) {
        return "model_" + id;
      }
      if (Ui.IconSprite(id) != null) {
        return id;
      }
      // 빌린 모델(ViewCatalog.TemporaryModels)의 아이콘을 쓴다.
      var borrowed = ViewCatalog.BorrowedId(id);
      if (borrowed != null && Ui.IconSprite("model_" + borrowed) != null) {
        return "model_" + borrowed;
      }
      return borrowed != null && Ui.IconSprite(borrowed) != null ? borrowed : "ui_army";
    }

    public static string BuildingIcon(BuildingSpec spec) {
      if (Ui.IconSprite("model_" + spec.name) != null) {
        return "model_" + spec.name;
      }
      var own = $"building_{spec.name.Substring(3)}";
      var borrowed = ViewCatalog.BorrowedId(spec.name);
      if (Ui.IconSprite(own) != null || borrowed == null) {
        return own;
      }
      return Ui.IconSprite("model_" + borrowed) != null ? "model_" + borrowed : $"building_{borrowed.Substring(3)}";
    }

    /// <summary>궁극 전력 칸의 그림: 항모·상륙정은 그 유닛(빌린 모델) 그림, 미사일은 공격 그림.</summary>
    static string UltimateIcon(Frame frame, UltimateSpec spec) {
      return spec.Kind == UltimateKind.Carrier ? UnitIcon(frame.FindAsset(spec.Carrier).name)
        : spec.Kind == UltimateKind.AmphibiousAssault ? UnitIcon(frame.FindAsset(spec.LandingCraft).name) : "ui_attack_move";
    }

    string UnitName(AssetRef<UnitSpec> unit) {
      var frame = Controller?.Frame;
      var spec = frame?.FindAsset(unit);
      return spec != null ? Texts.NameOf(spec.name) : "";
    }

    void ShowCard(bool single) {
      _card.gameObject.SetActive(true);
      _cardIcon.transform.parent.gameObject.SetActive(single);
      _cardTitle.gameObject.SetActive(single);
      _cardHealth.gameObject.SetActive(single);
      _cardDetail.gameObject.SetActive(single);
      if (single == false) {
        _cardBadge.gameObject.SetActive(false);
        foreach (var icon in _queueIcons) {
          icon.gameObject.SetActive(false);
        }
      }
      for (var i = 0; i < _typeButtons.Count; i++) {
        _typeButtons[i].gameObject.SetActive(single == false && i < _typeSpecs.Count);
      }
    }

    void SetHealth(float ratio) {
      ratio = Mathf.Clamp01(ratio);
      var fill = (RectTransform)_cardHealthFill.transform;
      fill.anchorMax = new Vector2(Mathf.Max(0.02f, ratio), 1);
      var theme = UiTheme.Current;
      _cardHealthFill.color = Color.Lerp(theme.HealthLow, theme.HealthHigh, ratio);
    }

    void RefreshCard(Frame frame, PlayerState* state) {
      var selection = Controller.Input.Selection;
      _typeSpecs.Clear();
      if (selection.Building != EntityRef.None && frame.Exists(selection.Building) && frame.Has<NpcTrader>(selection.Building)) {
        var trader = frame.Get<NpcTrader>(selection.Building);
        ShowCard(true);
        _cardIcon.sprite = Ui.IconSprite("building_headquarters");
        _cardTitle.text = "암시장 무기상";
        _cardBadge.gameObject.SetActive(false);
        SetHealth(trader.Health.AsFloat / Mathf.Max(1f, trader.MaxHealth.AsFloat));
        _cardDetail.text = $"체력 {trader.Health.AsInt}/{trader.MaxHealth.AsInt}";
        HideQueue();
        return;
      }
      if (selection.Building != EntityRef.None && frame.Exists(selection.Building)) {
        if (frame.Has<CityBuilding>(selection.Building)) {
          var city = frame.Get<CityBuilding>(selection.Building);
          var placement = frame.FindAsset<MapSpec>(frame.Map.UserAsset).CityBuildings[city.Index];
          ShowCard(true);
          _cardIcon.sprite = Ui.IconSprite("building_headquarters");
          _cardTitle.text = Texts.CityName(placement);
          _cardBadge.gameObject.SetActive(false);
          SetHealth(city.Health.AsFloat / Mathf.Max(1f, city.MaxHealth.AsFloat));
          _cardDetail.text = $"체력 {city.Health.AsInt}/{city.MaxHealth.AsInt}";
          HideQueue();
          return;
        }
        CardBuilding(frame, state, selection.Building);
        return;
      }
      var units = selection.Units.Where(frame.Exists).ToList();
      if (units.Count == 0) {
        _card.gameObject.SetActive(false);
        return;
      }
      var groups = units.GroupBy(e => frame.Get<Unit>(e).Spec).ToList();
      if (groups.Count == 1 && units.Count == 1) {
        CardUnit(frame, state, units[0]);
        return;
      }
      if (groups.Count == 1) {
        // 한 종류 여러 기: 한 기 카드에 수만 붙인다.
        CardUnit(frame, state, units[0]);
        _cardTitle.text += $"  x{units.Count}";
        return;
      }
      foreach (var group in groups.Take(MaxTypeButtons)) {
        _typeSpecs.Add(group.Key);
      }
      ShowCard(false);
      for (var i = 0; i < _typeSpecs.Count; i++) {
        var spec = frame.FindAsset(_typeSpecs[i]);
        var count = groups[i].Count();
        Ui.SetLabel(_typeButtons[i], $"{Texts.SizeBadge(spec.Size)} x{count}");
        Ui.SetIcon(_typeButtons[i], Ui.IconSprite(UnitIcon(spec.name)));
      }
      if (groups.Count > MaxTypeButtons) {
        Ui.SetLabel(_typeButtons[MaxTypeButtons - 1], $"외 {groups.Count - MaxTypeButtons + 1}종");
      }
    }

    void HideQueue() {
      foreach (var icon in _queueIcons) {
        icon.gameObject.SetActive(false);
      }
    }

    void CardUnit(Frame frame, PlayerState* state, EntityRef entity) {
      var unit = frame.Get<Unit>(entity);
      var spec = frame.FindAsset(unit.Spec);
      ShowCard(true);
      HideQueue();
      _cardIcon.sprite = Ui.IconSprite(UnitIcon(spec.name));
      var elite = TechSystem.Elite(frame, unit.Owner, unit.Spec) != null;
      _cardTitle.text = (elite ? "★정예 " : "") + Texts.NameOf(spec.name);
      var theme = UiTheme.Current;
      _cardBadge.gameObject.SetActive(spec.Size != UnitSize.Engineer);
      _cardBadgeText.text = spec.IsAir ? $"{Texts.SizeBadge(spec.Size)}·공중" : Texts.SizeBadge(spec.Size);
      _cardBadge.GetComponent<Image>().color = theme.SizeColor(spec.Size);
      var max = TechSystem.MaxHealth(frame, unit.Owner, unit.Spec);
      SetHealth(unit.Health.AsFloat / Mathf.Max(1f, max.AsFloat));
      var owner = frame.Global->Players.GetPointer(unit.Owner);
      var size = (int)spec.Size;
      var lines = $"체력 {unit.Health.AsInt}/{max.AsInt}";
      if (spec.Size != UnitSize.Engineer) {
        lines += $"   공격 +{owner->AttackLevels[size]}  방어 +{owner->ArmorLevels[size]}";
        lines += "\n" + Texts.CounterTip(spec, frame.FindAsset(frame.RuntimeConfig.Rules));
      }
      // 항목 33: 항모는 드론 수, 드론·상륙정은 직접 조종하지 못한다는 안내.
      if (frame.TryGet<Carrier>(entity, out var carrier)) {
        var flying = 0;
        foreach (var pair in frame.GetComponentIterator<Drone>()) {
          flying += pair.Component.Carrier == entity ? 1 : 0;
        }
        lines += $"\n드론 출격 {flying} · 격납 {carrier.Docked}  (대공 무기에 취약)";
      } else if (UltimateSystem.IsAutonomous(frame, entity)) {
        lines += "\n스스로 움직입니다(명령을 받지 않음)";
      }
      _cardDetail.text = lines;
    }

    void CardBuilding(Frame frame, PlayerState* state, EntityRef entity) {
      var building = frame.Get<Building>(entity);
      var spec = frame.FindAsset(building.Spec);
      ShowCard(true);
      _cardIcon.sprite = Ui.IconSprite(BuildingIcon(spec));
      _cardTitle.text = Texts.NameOf(spec.name);
      var tierBadge = spec.Role == BuildingRole.Headquarters;
      _cardBadge.gameObject.SetActive(tierBadge);
      if (tierBadge) {
        _cardBadgeText.text = $"{Mathf.Max(1, building.Tier)}단계";
        _cardBadge.GetComponent<Image>().color = UiTheme.Current.Accent;
      }
      SetHealth(building.Health.AsFloat / Mathf.Max(1f, spec.MaxHealth.AsFloat));
      var detail = $"체력 {building.Health.AsInt}/{spec.MaxHealth.AsInt}";
      if (building.ConstructionRemaining > FP._0) {
        detail += $"   건설 {Mathf.CeilToInt(building.ConstructionRemaining.AsFloat)}초 남음";
      }
      if (building.UpgradeTier > 0) {
        detail += $"\n{Texts.TierName(building.UpgradeTier)}로 업그레이드 {Mathf.CeilToInt(building.UpgradeRemaining.AsFloat)}초";
      }
      if (building.Research > 0) {
        var research = TechSystem.ResearchAt(frame, building.Owner, building.Research - 1);
        if (research != null) {
          detail += $"\n연구 중: {Texts.ResearchName(research, UnitName)} {Mathf.CeilToInt(building.ResearchRemaining.AsFloat)}초";
        }
      }
      if (building.Paused != PauseReason.None) {
        detail += "\n정전으로 멈춤";
      }
      if (spec.ProcessSeconds > FP._0) {
        detail += building.ProcessorOff ? "   꺼짐" : "   변환 중";
      }
      _cardDetail.text = detail;
      // 생산 대기열 5칸.
      var producer = spec.Producible != null && spec.Producible.Length > 0;
      for (var i = 0; i < _queueIcons.Count; i++) {
        var used = producer && i < building.QueueCount;
        _queueIcons[i].gameObject.SetActive(producer);
        _queueIcons[i].sprite = used ? Ui.IconSprite(UnitIcon(frame.FindAsset(building.Queue[i].Unit).name)) : UiCatalog.SpriteFor("icon_frame");
        _queueIcons[i].color = used ? Color.white : new Color(1, 1, 1, 0.35f);
      }
      if (producer && building.QueueCount > 0) {
        _cardDetail.text += $"   대기열 {building.QueueCount}/{QueueIcons}";
      }
    }

    // ───────────── 명령 카드 ─────────────

    void RefreshContext(Frame frame, PlayerState* state) {
      Entries.Clear();
      var input = Controller.Input;
      var selection = input.Selection;
      var status = "";
      var showPanel = false;
      if (selection.Building != EntityRef.None && frame.Exists(selection.Building) && frame.Has<NpcTrader>(selection.Building)) {
        status = TraderEntries(frame, state, selection.Building);
        showPanel = true;
      } else if (selection.Building != EntityRef.None && frame.Exists(selection.Building) && frame.Has<CityBuilding>(selection.Building)) {
        status = CityEntries(frame, selection.Building);
        showPanel = true;
      } else if (selection.Building != EntityRef.None && frame.Exists(selection.Building)) {
        status = BuildingEntries(frame, state, selection.Building);
        showPanel = true;
      } else if (selection.Units.Count > 0) {
        var spec = frame.FindAsset(frame.Get<Unit>(selection.Units[0]).Spec);
        status = selection.Units.Count == 1 ? Texts.NameOf(spec.name) : $"유닛 {selection.Units.Count}기";
        if (selection.Units.Any(e => frame.Has<Worker>(e))) {
          status = EngineerEntries(frame, state);
          showPanel = true;
        } else {
          UnitCommandEntries();
          showPanel = true;
        }
        if (input.Mode == MatchInput.TapMode.Jammer) {
          status = "교란 장치: 적 자원 노드를 누르세요";
        } else if (input.Mode == MatchInput.TapMode.AttackMove) {
          status = "공격 이동: 목적지를 누르세요";
        } else if (input.Mode == MatchInput.TapMode.Patrol) {
          status = "순찰: 반대쪽 끝을 누르세요";
        }
      }
      if (input.Mode == MatchInput.TapMode.Ultimate) {
        status = "궁극 전력: 목표 지점을 누르세요";
      }
      if (input.IsPlacing) {
        status = "건설할 곳을 누르세요";
        showPanel = true;
      }
      _status.text = status;
      if (Entries.Count > ContextSlots) {
        Entries.RemoveRange(ContextSlots, Entries.Count - ContextSlots);
      }
      _contextPanel.SetActive(showPanel && (Entries.Count > 0 || input.IsPlacing));
      for (var i = 0; i < _context.Length; i++) {
        // 빈 칸(자리 맞춤용)은 그리지 않는다.
        var used = i < Entries.Count && string.IsNullOrEmpty(Entries[i].Label) == false;
        _context[i].gameObject.SetActive(used);
        if (used) {
          Ui.SetLabel(_context[i], Entries[i].Label);
          Ui.SetIcon(_context[i], Entries[i].Icon != null ? Ui.IconSprite(Entries[i].Icon) : null);
          Ui.SetLocked(_context[i], Entries[i].Enabled == false);
        }
      }
    }

    void UnitCommandEntries() {
      var input = Controller.Input;
      Entries.Add(new ContextEntry { Label = input.Mode == MatchInput.TapMode.AttackMove ? "공격 이동 중" : "공격 이동", Icon = "ui_attack_move", Enabled = true,
        Detail = "땅을 누르면 가는 길의 적을 공격하며 이동",
        Action = () => input.Mode = input.Mode == MatchInput.TapMode.AttackMove ? MatchInput.TapMode.None : MatchInput.TapMode.AttackMove });
      Entries.Add(new ContextEntry { Label = "위치 고수", Icon = "ui_hold", Enabled = true, Detail = "제자리에서 사거리 안의 적만 쏩니다",
        Action = () => Controller.Send(input.Hold(Controller.Frame)) });
      Entries.Add(new ContextEntry { Label = input.Mode == MatchInput.TapMode.Patrol ? "순찰 지정 중" : "순찰", Icon = "ui_patrol", Enabled = true,
        Detail = "지금 위치와 누른 곳 사이를 오가며 적을 공격",
        Action = () => input.Mode = input.Mode == MatchInput.TapMode.Patrol ? MatchInput.TapMode.None : MatchInput.TapMode.Patrol });
      Entries.Add(new ContextEntry { Label = "정지", Icon = "ui_stop", Enabled = true, Detail = "하던 일을 멈춥니다", Action = () => Controller.Stop() });
      SniperEntries();
    }

    /// <summary>중앙 암시장 거래 패널. 적대가 되면 영구 잠김, 파괴되면 닫힘.</summary>
    string TraderEntries(Frame frame, PlayerState* state, EntityRef entity) {
      var trader = frame.Get<NpcTrader>(entity);
      var rules = frame.FindAsset(frame.RuntimeConfig.Rules);
      var index = (int)Controller.Player;
      var hostile = index >= 0 && (trader.HostileMask & (1 << index)) != 0;
      if (trader.Destroyed) {
        return "암시장: 파괴되어 영구히 닫힘";
      }
      if (hostile) {
        Entries.Add(new ContextEntry { Label = "거래 불가\n(적대)", Icon = "ui_cancel", Enabled = false, Locked = true, Reason = "이 무기상을 공격해 영구히 거래가 막혔습니다" });
        return "암시장: 적대 — 경비가 반격 중. 파괴하면 현상금";
      }
      var wait = Mathf.Max(0, state->TradeReadyFrame - frame.Number) / Mathf.Max(1, frame.SessionConfig.UpdateFPS);
      var stock = state->Stock;
      var input = Controller.Input;
      void Offer(int offer, string name, ResourceCost cost, string detail, System.Action action) {
        var ok = wait == 0 && stock.Covers(cost);
        Entries.Add(new ContextEntry {
          Label = wait > 0 ? $"{name}\n대기 {wait}초" : $"{name}\n{Texts.Cost(cost, stock)}", Icon = offer == 1 ? "ui_rally" : offer == 2 ? "ui_attack" : "ui_upgrade",
          Enabled = ok, Locked = ok == false, Reason = wait > 0 ? "다음 거래까지 기다리세요" : "자원이 부족합니다", Detail = detail, Action = action,
        });
      }
      Offer(NpcTraderSystem.OfferMercenaries, "용병 투입", rules.TraderMercCost, $"보병 {rules.TraderMercCount}분대를 암시장 출입구에 즉시 투입", () => Controller.Send(new TradeCommand { Offer = NpcTraderSystem.OfferMercenaries }));
      Offer(NpcTraderSystem.OfferDrone, input.Mode == MatchInput.TapMode.Drone ? "목표를 누르세요" : "자폭 드론", rules.TraderDroneCost, "보이는 땅을 눌러 정밀 타격(범위 피해)",
        () => input.Mode = input.Mode == MatchInput.TapMode.Drone ? MatchInput.TapMode.None : MatchInput.TapMode.Drone);
      Offer(NpcTraderSystem.OfferRefuel, "긴급 재정비", rules.TraderRefuelCost, "비행장 격납 작전기 즉시 재정비 + 긴급 출격권 1", () => Controller.Send(new TradeCommand { Offer = NpcTraderSystem.OfferRefuel }));
      return input.Mode == MatchInput.TapMode.Drone ? "자폭 드론: 목표 지점을 누르세요" : "암시장 무기상 — 공격하면 영구 적대";
    }

    /// <summary>항목 34-C: 저격수를 고르면 은신·교란 장치 버튼. 주둔은 진지 건물(도시 진지·벙커)을 누르면 된다.</summary>
    void SniperEntries() {
      var frame = Controller.Frame;
      var input = Controller.Input;
      if (frame == null) {
        return;
      }
      var snipers = input.SelectedSnipers(frame);
      if (snipers.Count == 0) {
        return;
      }
      var ground = snipers.Where(e => frame.Get<Unit>(e).Garrison == EntityRef.None).ToList();
      var ready = ground.Where(e => frame.Number >= frame.Get<Unit>(e).StealthReadyFrame && frame.Get<Unit>(e).StealthUntilFrame <= frame.Number).ToList();
      var wait = ground.Count == 0 ? 0 : ground.Min(e => Mathf.Max(0, frame.Get<Unit>(e).StealthReadyFrame - frame.Number)) / Mathf.Max(1, frame.SessionConfig.UpdateFPS);
      Entries.Add(new ContextEntry {
        Label = ready.Count > 0 ? "은신" : ground.Count == 0 ? "은신\n주둔 중" : $"은신\n{wait}초", Icon = "ui_hold", Enabled = ready.Count > 0, Locked = ready.Count == 0,
        Reason = ground.Count == 0 ? "건물에서 내려와야 합니다" : "다시 쓸 수 있을 때까지 기다리세요",
        Detail = "20초 동안 적에게 안 보입니다(이동 -30%). 적이 4m 안에 오거나 레이더·정찰차에 들키면 드러납니다",
        Action = () => Controller.Send(new StealthCommand { Units = ready.ToArray() }),
      });
      var hidden = ground.Any(e => frame.Get<Unit>(e).StealthUntilFrame > frame.Number);
      Entries.Add(new ContextEntry {
        Label = input.Mode == MatchInput.TapMode.Jammer ? "노드를 누르세요" : "교란 장치", Icon = "ui_cancel", Enabled = hidden, Locked = hidden == false,
        Reason = "은신 중에만 설치할 수 있습니다",
        Detail = "은신한 채 적 자원 노드 10m 안에서 4초 설치. 60초 동안 그 노드 채집 -40%(1인당 1개)",
        Action = () => input.Mode = input.Mode == MatchInput.TapMode.Jammer ? MatchInput.TapMode.None : MatchInput.TapMode.Jammer,
      });
    }

    /// <summary>항목 34-D: 비행장 격납 칸 4개. 대기 중인 작전기를 누르고 보이는 땅을 누르면 출격한다.</summary>
    string HangarEntries(Frame frame, EntityRef entity, Building building) {
      var input = Controller.Input;
      var rules = frame.FindAsset(frame.RuntimeConfig.Rules);
      for (var i = 0; i < SortieSystem.Slots; i++) {
        var slot = building.Hangar[i];
        var index = i;
        if (slot.State == 0) {
          Entries.Add(new ContextEntry { Label = $"격납 {i + 1}\n비어 있음", Icon = "ui_back", Enabled = false, Locked = true, Reason = "비행장에서 작전기를 생산하세요" });
          continue;
        }
        var spec = frame.FindAsset(slot.Spec);
        var name = Texts.NameOf(spec.name);
        if (slot.State == 2) {
          Entries.Add(new ContextEntry { Label = $"{name}\n작전 중", Icon = "ui_attack", Enabled = false, Locked = true, Reason = "작전기가 비행 중입니다" });
          continue;
        }
        var left = Mathf.Max(0, slot.ReadyFrame - frame.Number) / Mathf.Max(1, frame.SessionConfig.UpdateFPS);
        var picking = input.Mode == MatchInput.TapMode.Sortie && input.SortieAirfield == entity && input.SortieSlot == i;
        Entries.Add(new ContextEntry {
          Label = picking ? "목표를 누르세요" : left > 0 ? $"{name}\n재정비 {left}초" : $"{name}\n{Texts.Cost(spec.SortieCost, FactoryWarsUtil.Player(frame, Controller.Player)->Stock)}",
          Icon = spec.SortieKind == SortieKind.Drop ? "ui_rally" : "ui_attack", Enabled = left == 0, Locked = left > 0, Reason = "재정비 중입니다",
          Detail = spec.SortieKind == SortieKind.Strafe ? "기총소사: 진입~이탈 통로의 보병에 강함" : spec.SortieKind == SortieKind.Bomb ? "폭격: 폭탄 4발, 건물 피해 2배" : "공수 투하: 목표에 보병 6을 내려놓음",
          Action = () => {
            input.SortieAirfield = entity;
            input.SortieSlot = index;
            input.Mode = picking ? MatchInput.TapMode.None : MatchInput.TapMode.Sortie;
          },
        });
      }
      PadTo(ContextSlots - 1);
      Entries.Add(new ContextEntry { Label = "뒤로", Icon = "ui_back", Enabled = true, Action = () => { _menu = Menu.Root; input.Mode = MatchInput.TapMode.None; } });
      return TacticalSystem.WeatherActive(frame) ? "격납고 (기상 악화: 신호탄 구역만 출격)" : input.Mode == MatchInput.TapMode.Sortie ? "작전 목표: 보이는 땅을 누르세요" : "격납고";
    }

    /// <summary>건물이 있는 단계 목록. 지금 단계 + 1까지만 보인다(2단계 이상 높으면 숨김).</summary>
    List<int> BuildTiers(Frame frame, FactionSpec faction, int tier) {
      return faction.Buildings.Select(b => frame.FindAsset(b)).Where(b => b.Defensive == false).Select(b => Mathf.Max(1, b.RequiredTier)).Distinct().OrderBy(t => t).Where(t => t <= tier + 1).ToList();
    }

    /// <summary>건설 하위 메뉴의 건물인가. 묶음 0은 방어 건물(항목 32), 1~3은 그 단계의 방어 아닌 건물.</summary>
    static bool InBuildMenu(BuildingSpec spec, int menu) => menu == 0 ? spec.Defensive : spec.Defensive == false && Mathf.Max(1, spec.RequiredTier) == menu;

    string EngineerEntries(Frame frame, PlayerState* state) {
      var faction = frame.FindAsset(state->Faction);
      var tier = TechSystem.Tier(frame, Controller.Player);
      if (_menu == Menu.Build) {
        foreach (var buildingRef in faction.Buildings) {
          var spec = frame.FindAsset(buildingRef);
          if (InBuildMenu(spec, _menuTier) == false || spec.RequiredTier >= tier + 2) {
            continue;
          }
          var target = buildingRef;
          var reason = BuildingLock(frame, faction, spec, tier);
          Entries.Add(new ContextEntry {
            Label = $"{Texts.NameOf(spec.name)}\n{(reason == null ? Texts.Cost(spec.Cost, state->Stock) : Short(reason))}",
            Icon = BuildingIcon(spec),
            Enabled = reason == null,
            Locked = reason != null,
            Reason = reason,
            Detail = $"{Texts.NameOf(spec.name)}: {Texts.Cost(spec.Cost, state->Stock)}  건설 {spec.BuildTimeSeconds.AsInt}초",
            Action = () => Controller.Input.Placing = target,
          });
        }
        PadTo(ContextSlots - 1);
        Entries.Add(new ContextEntry { Label = "뒤로", Icon = "ui_back", Enabled = true, Action = () => _menu = Menu.Root });
        return $"건설: {(_menuTier == 0 ? "방어" : _menuTier == 1 ? "기본" : $"{_menuTier}단계")} 건물";
      }
      foreach (var buildTier in BuildTiers(frame, faction, tier)) {
        var value = buildTier;
        var locked = buildTier > tier;
        Entries.Add(new ContextEntry {
          Label = buildTier == 1 ? "기본 건물" : $"{buildTier}단계 건물",
          Icon = buildTier >= 3 ? "ui_build3" : buildTier == 2 ? "ui_build2" : "ui_build1",
          // 잠긴 단계도 열어서 무엇이 있는지 볼 수 있다(칸마다 잠김 이유).
          Enabled = true,
          Detail = locked ? Texts.TierLock(buildTier) : null,
          Action = () => {
            _menu = Menu.Build;
            _menuTier = value;
          },
        });
      }
      if (faction.Buildings.Any(b => frame.FindAsset(b).Defensive)) {
        Entries.Add(new ContextEntry {
          Label = "방어 건물", Icon = "ui_hold", Enabled = true, Detail = "방어탑·방공포대·모래주머니 진지·벙커",
          Action = () => {
            _menu = Menu.Build;
            _menuTier = 0;
          },
        });
      }
      PadTo(ContextColumns + (Entries.Count > ContextColumns ? ContextColumns : 0));
      Entries.Add(new ContextEntry { Label = "정지", Icon = "ui_stop", Enabled = true, Action = () => Controller.Stop() });
      return $"공병  {Texts.TierName(tier)}";
    }

    /// <summary>빈 칸을 채워 다음 항목을 그 칸 번호부터 놓는다(빈 칸은 보이지 않는다).</summary>
    void PadTo(int count) {
      while (Entries.Count < count) {
        Entries.Add(new ContextEntry { Label = "", Enabled = false, Icon = null, Action = null });
      }
    }

    /// <summary>칸 안의 짧은 이유: 첫 이유만, "기지 " 접두어 없이("2단계 필요", "발전소 필요"). 전체는 툴팁.</summary>
    static string Short(string reason) {
      var first = reason.Split('/')[0].Trim();
      if (first.StartsWith("기지 ")) {
        first = first.Substring(3);
      }
      return first;
    }

    string BuildingLock(Frame frame, FactionSpec faction, BuildingSpec spec, int tier) {
      var reasons = new List<string>();
      if (spec.RequiredTier > tier) {
        reasons.Add(Texts.TierLock(spec.RequiredTier));
      }
      var missing = new List<string>();
      for (var i = 0; spec.Prerequisites != null && i < spec.Prerequisites.Length; i++) {
        if (ProductionSystem.HasFinished(frame, Controller.Player, spec.Prerequisites[i]) == false) {
          missing.Add(Texts.NameOf(frame.FindAsset(spec.Prerequisites[i]).name));
        }
      }
      if (missing.Count > 0) {
        reasons.Add(string.Join("·", missing) + " 필요");
      }
      return reasons.Count > 0 ? string.Join(" / ", reasons) : null;
    }

    string UnitLock(Frame frame, UnitSpec unit) {
      if (TechSystem.CanProduce(frame, Controller.Player, unit, out var reason)) {
        return null;
      }
      if (reason == RejectReason.TierLocked) {
        return Texts.TierLock(unit.RequiredTier);
      }
      if (reason == RejectReason.SizeLimit && TechSystem.FindSizeRule(frame.FindAsset(frame.RuntimeConfig.Rules), unit.Size, out var rule)) {
        return $"{Texts.SizeWord(unit.Size)} 보유 한도 {rule.OwnedLimit}";
      }
      return Texts.Rejected(reason, MissingResource.None);
    }

    /// <summary>도시 건물: 체력과, 내 분대가 들어 있으면 "모두 나가기".</summary>
    string CityEntries(Frame frame, EntityRef entity) {
      var city = frame.Get<CityBuilding>(entity);
      var map = frame.FindAsset<MapSpec>(frame.Map.UserAsset);
      var placement = map.CityBuildings[city.Index];
      var mine = 0;
      foreach (var pair in frame.GetComponentIterator<Unit>()) {
        mine += pair.Component.Garrison == entity && pair.Component.Owner == Controller.Player ? 1 : 0;
      }
      var status = Texts.CityName(placement);
      if (placement.Garrison) {
        status += $"  진지 {mine}";
        Entries.Add(new ContextEntry {
          Label = Texts.ExitGarrison,
          Icon = "ui_back",
          Enabled = mine > 0,
          Locked = mine == 0,
          Reason = "들어가 있는 내 분대가 없습니다",
          Action = () => Controller.Send(new ExitGarrisonCommand { Building = entity }),
        });
      }
      return status;
    }

    string BuildingEntries(Frame frame, PlayerState* state, EntityRef entity) {
      var building = frame.Get<Building>(entity);
      var spec = frame.FindAsset(building.Spec);
      var status = Texts.NameOf(spec.name);
      if (building.Owner != Controller.Player) {
        return status;
      }
      if (building.ConstructionRemaining > FP._0) {
        return status + "  건설 중";
      }
      var tier = TechSystem.Tier(frame, Controller.Player);
      if (spec.Role == BuildingRole.ResearchLab) {
        return ResearchEntries(frame, state, entity, building, tier);
      }
      if (spec.Role == BuildingRole.UltimateSite) {
        UltimateEntries(frame, state);
        return status;
      }
      if (spec.Role == BuildingRole.Airfield) {
        if (_menu == Menu.Hangar) {
          return HangarEntries(frame, entity, building);
        }
        var stored = 0;
        for (var i = 0; i < SortieSystem.Slots; i++) {
          stored += building.Hangar[i].State != 0 ? 1 : 0;
        }
        Entries.Add(new ContextEntry {
          Label = $"격납고\n{stored}/{SortieSystem.Slots}", Icon = "ui_attack", Enabled = true,
          Detail = "작전기(전투기·공격기·수송기)는 격납고에 대기합니다. 눌러서 출격시키세요",
          Action = () => _menu = Menu.Hangar,
        });
      }
      // 항목 33 벙커: 안의 분대를 내보낸다.
      if (spec.GarrisonCapacity > 0) {
        var inside = 0;
        foreach (var pair in frame.GetComponentIterator<Unit>()) {
          inside += pair.Component.Garrison == entity ? 1 : 0;
        }
        Entries.Add(new ContextEntry {
          Label = $"내보내기\n{inside}/{spec.GarrisonCapacity}", Icon = "ui_rally_clear", Enabled = inside > 0, Locked = false,
          Detail = "보병 분대를 고르고 벙커를 누르면 들어갑니다. 안에서는 맞지 않고, 벙커가 부서지면 튕겨 나옵니다",
          Action = () => Controller.Send(new ExitGarrisonCommand { Building = entity }),
        });
        status += $"  수용 {inside}/{spec.GarrisonCapacity}";
      }

      var stock = state->Stock;
      foreach (var unitRef in spec.Producible ?? Array.Empty<AssetRef<UnitSpec>>()) {
        var unit = frame.FindAsset(unitRef);
        if (unit.RequiredTier >= tier + 2) {
          continue;
        }
        var target = unitRef;
        var reason = UnitLock(frame, unit);
        Entries.Add(new ContextEntry {
          Label = $"{Texts.NameOf(unit.name)}\n{(reason == null ? Texts.Cost(unit.Cost, stock) : Short(reason))}",
          Icon = UnitIcon(unit.name),
          Enabled = reason == null,
          Locked = reason != null,
          Reason = reason,
          Detail = $"{Texts.NameOf(unit.name)} [{Texts.SizeWord(unit.Size)}] {Texts.Cost(unit.Cost, stock)}\n{Texts.CounterTip(unit, frame.FindAsset(frame.RuntimeConfig.Rules))}",
          Action = () => Controller.Send(new ProduceCommand { Building = entity, Unit = target, Count = 1 }),
        });
      }
      if (building.QueueCount > 0) {
        var last = building.Queue[building.QueueCount - 1].Unit;
        Entries.Add(new ContextEntry {
          Label = "생산 취소", Icon = "ui_cancel", Enabled = true, Detail = "대기열 맨 뒤를 취소하고 환불",
          Action = () => Controller.Send(new ProduceCommand { Building = entity, Unit = last, Count = 1, Cancel = true }),
        });
      }
      if (spec.Role == BuildingRole.Headquarters) {
        UpgradeEntry(frame, state, entity, building, tier);
        // 항목 33: 3단계가 가까워지면(2단계부터) 궁극 전력 칸을 보인다.
        if (tier >= 2) {
          UltimateEntries(frame, state);
        }
      }
      if (spec.ProcessSeconds > FP._0) {
        var off = (bool)building.ProcessorOff;
        Entries.Add(new ContextEntry {
          Label = off ? "가공소 켜기" : "가공소 끄기", Icon = off ? "ui_upgrade" : "ui_stop",
          Enabled = true,
          Action = () => Controller.Send(new SetProcessorCommand { Building = entity, Enabled = off }),
        });
      }
      if (spec.Producible != null && spec.Producible.Length > 0) {
        var input = Controller.Input;
        Entries.Add(new ContextEntry {
          Label = input.Mode == MatchInput.TapMode.Rally ? "집결 지정 중" : "집결 지점", Icon = "ui_rally", Enabled = true,
          Detail = "새 유닛이 모일 곳을 땅에서 누릅니다",
          Action = () => input.Mode = input.Mode == MatchInput.TapMode.Rally ? MatchInput.TapMode.None : MatchInput.TapMode.Rally,
        });
        if (building.HasRally) {
          Entries.Add(new ContextEntry { Label = "집결 해제", Icon = "ui_rally_clear", Enabled = true, Action = () => Controller.Send(new RallyCommand { Building = entity, Clear = true }) });
        }
        if (input.Mode == MatchInput.TapMode.Rally) {
          status = "집결 지점: 땅을 누르세요";
        }
      }
      if (spec.Producible != null && spec.Producible.Length > 0 && spec.Role != BuildingRole.Headquarters) {
        PresetEntries(frame, state, entity, building);
      }
      return status;
    }

    void UpgradeEntry(Frame frame, PlayerState* state, EntityRef entity, Building building, int tier) {
      if (building.UpgradeTier > 0) {
        Entries.Add(new ContextEntry {
          Label = $"업그레이드 중\n{Mathf.CeilToInt(building.UpgradeRemaining.AsFloat)}초", Icon = "ui_upgrade", Enabled = false, Locked = true,
          Reason = $"{Texts.TierName(building.UpgradeTier)}로 올리는 중",
        });
        return;
      }
      var rules = frame.FindAsset(frame.RuntimeConfig.Rules);
      if (tier >= TechSystem.MaxTier || TechSystem.FindTierSpec(rules, tier + 1, out var next) == false) {
        return;
      }
      string reason = null;
      if (TechSystem.MeetsTierRequirement(frame, Controller.Player, next, out var missingRole) == false) {
        var faction = frame.FindAsset(state->Faction);
        var role = faction.Buildings.Select(b => frame.FindAsset(b)).FirstOrDefault(b => b.Role == missingRole);
        reason = $"{(role != null ? Texts.NameOf(role.name) : missingRole.ToString())} 필요";
      }
      var name = next.Tier == 2 ? "연대 본부" : "사단 본부";
      Entries.Add(new ContextEntry {
        Label = $"{name}\n{(reason == null ? Texts.Cost(next.Cost, state->Stock) : reason)}",
        Icon = "ui_upgrade",
        Enabled = reason == null,
        Locked = reason != null,
        Reason = reason != null ? $"{Texts.TierName(next.Tier)}: {reason}" : null,
        Detail = $"{Texts.TierName(next.Tier)}로 업그레이드: {Texts.Cost(next.Cost, state->Stock)}  {next.Seconds.AsInt}초 (그동안 생산 계속)",
        Action = () => Controller.Send(new UpgradeBaseCommand { Building = entity }),
      });
    }

    string ResearchEntries(Frame frame, PlayerState* state, EntityRef entity, Building building, int tier) {
      var faction = frame.FindAsset(state->Faction);
      var list = faction.Researches ?? Array.Empty<AssetRef<ResearchSpec>>();
      var status = "연구소";
      if (_menu == Menu.Research) {
        foreach (var kind in new[] { ResearchKind.Attack, ResearchKind.Armor }) {
          // 크기·종류별로 아직 안 한 가장 낮은 단계 하나만 보인다.
          var index = -1;
          for (var i = 0; i < list.Length; i++) {
            var r = frame.FindAsset(list[i]);
            if (r.Kind == kind && r.Size == _menuSize && TechSystem.IsResearched(frame, Controller.Player, i) == false
                && (index < 0 || r.Level < frame.FindAsset(list[index]).Level)) {
              index = i;
            }
          }
          if (index < 0) {
            Entries.Add(new ContextEntry { Label = $"{Texts.SizeWord(_menuSize)} {(kind == ResearchKind.Attack ? "공격" : "방어")}\n완료", Icon = kind == ResearchKind.Attack ? "ui_attack" : "ui_armor", Enabled = false, Locked = true, Reason = "모든 단계를 끝냈습니다" });
            continue;
          }
          AddResearch(frame, state, entity, building, list, index, tier);
        }
        PadTo(ContextSlots - 1);
        Entries.Add(new ContextEntry { Label = "뒤로", Icon = "ui_back", Enabled = true, Action = () => _menu = Menu.Root });
        return $"{Texts.SizeWord(_menuSize)} 연구";
      }
      foreach (var size in new[] { UnitSize.Small, UnitSize.Medium, UnitSize.Large }) {
        var specs = list.Select(r => frame.FindAsset(r)).Where(r => r.Kind != ResearchKind.Elite && r.Size == size).ToList();
        if (specs.Count == 0 || specs.Min(r => r.RequiredTier) >= tier + 2) {
          continue;
        }
        var value = size;
        var minTier = specs.Min(r => r.RequiredTier);
        Entries.Add(new ContextEntry {
          Label = $"{Texts.SizeWord(size)} 연구", Icon = "ui_research", Enabled = true,
          Detail = minTier > tier ? Texts.TierLock(minTier) : $"{Texts.SizeWord(size)} 유닛 공격·방어 강화",
          Action = () => {
            _menu = Menu.Research;
            _menuSize = value;
          },
        });
      }
      for (var i = 0; i < list.Length; i++) {
        if (frame.FindAsset(list[i]).Kind == ResearchKind.Elite && frame.FindAsset(list[i]).RequiredTier < tier + 2) {
          AddResearch(frame, state, entity, building, list, i, tier);
        }
      }
      if (building.Research > 0) {
        var current = TechSystem.ResearchAt(frame, Controller.Player, building.Research - 1);
        if (current != null) {
          status += $"  연구 중: {Texts.ResearchName(current, UnitName)}";
        }
      }
      return status;
    }

    void AddResearch(Frame frame, PlayerState* state, EntityRef entity, Building building, AssetRef<ResearchSpec>[] list, int index, int tier) {
      var research = frame.FindAsset(list[index]);
      string reason = null;
      if (TechSystem.IsResearched(frame, Controller.Player, index)) {
        reason = "완료";
      } else if (TechSystem.IsResearching(frame, Controller.Player, index)) {
        reason = "연구 중";
      } else if (research.RequiredTier > tier) {
        reason = Texts.TierLock(research.RequiredTier);
      } else if (research.Requires.IsValid && TechSystem.IsResearched(frame, Controller.Player, TechSystem.IndexOf(frame, Controller.Player, research.Requires)) == false) {
        reason = "앞 단계 연구 필요";
      } else if (building.Research > 0) {
        reason = "다른 연구 중";
      }
      var target = index;
      Entries.Add(new ContextEntry {
        Label = $"{Texts.ResearchName(research, UnitName)}\n{(reason == null ? Texts.Cost(research.Cost, state->Stock) : reason)}",
        Icon = research.Kind == ResearchKind.Elite ? "ui_elite" : research.Kind == ResearchKind.Attack ? "ui_attack" : "ui_armor",
        Enabled = reason == null,
        Locked = reason != null,
        Reason = reason != null ? $"{Texts.ResearchName(research, UnitName)}: {reason}" : null,
        Detail = $"{Texts.ResearchName(research, UnitName)}: {Texts.Cost(research.Cost, state->Stock)}  {research.Seconds.AsInt}초 (연구 중 전기 사용)",
        Action = () => Controller.Send(new ResearchCommand { Building = entity, Index = target }),
      });
    }

    void PresetEntries(Frame frame, PlayerState* state, EntityRef entity, Building building) {
      var modules = frame.FindAsset(state->Faction).Modules ?? Array.Empty<AssetRef<ModuleSpec>>();
      var slots = new[] { building.Preset[0], building.Preset[1], building.Preset[2] };
      for (var i = 0; i < slots.Length; i++) {
        var index = i;
        var current = frame.FindAsset(slots[i]);
        Entries.Add(new ContextEntry {
          Label = $"프리셋 {i + 1}\n{(current != null ? Texts.NameOf(current.name) : "없음")}",
          Icon = current != null ? (current.Effect == ModuleEffect.ReactiveArmor ? "ui_armor" : "ui_attack") : "ui_upgrade",
          Enabled = true,
          Detail = "새 유닛에 달 모듈을 바꿉니다(누를 때마다 다음 모듈)",
          Action = () => {
            var next = (Array.IndexOf(modules, slots[index]) + 2) % (modules.Length + 1) - 1;
            var preset = (AssetRef<ModuleSpec>[])slots.Clone();
            preset[index] = next < 0 ? default : modules[next];
            Controller.Send(new SetPresetCommand { Building = entity, Module0 = preset[0], Module1 = preset[1], Module2 = preset[2] });
          },
        });
      }
    }

    void RefreshDiplomacy(Frame frame) {
      var allowed = DiplomacySystem.IsAllowed(frame);
      foreach (var row in _rows) {
        var relation = DiplomacySystem.GetRelation(frame, Controller.Player, row.Player);
        var other = frame.Global->Players.GetPointer(row.Player);
        var incoming = DiplomacySystem.HasProposal(frame, row.Player, Controller.Player);
        var mark = $"<color={Palette.Hex(Palette.Of(frame, row.Player))}>■</color>";
        row.Name.text = $"{mark} 플레이어 {(int)row.Player + 1}\n{(other->Defeated ? "패배" : Texts.Relation(relation))}";
        Ui.SetEnabled(row.Propose, allowed && relation == Relation.Enemy && incoming == false && DiplomacySystem.HasProposal(frame, Controller.Player, row.Player) == false);
        Ui.SetEnabled(row.Accept, allowed && incoming);
        Ui.SetEnabled(row.Reject, allowed && incoming);
        Ui.SetEnabled(row.Break, allowed && relation == Relation.Allied);
      }
      _inbox.text = "받은 쪽지\n" + string.Join("\n", NoteInbox.Skip(Math.Max(0, NoteInbox.Count - 4)));
      Ui.SetLabel(_diplomacy.Find("Phrase").GetComponent<Button>(), $"문구: {Texts.Phrases[_phrase]}");
    }

    void OnDestroy() {
      QuantumEvent.UnsubscribeListener(this);
    }
  }
}
