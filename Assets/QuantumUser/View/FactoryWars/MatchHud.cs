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
    public bool Highlight;
    public string Reason;
    /// <summary>길게 누를 때 보일 설명(잠기지 않은 칸).</summary>
    public string Detail;
    /// <summary>Task 39 fix 5: top-right state overlay (UiCatalog id, e.g. pmc_state_sold_out).</summary>
    public string Overlay;
  }

  /// <summary>
  /// 대전 화면 HUD(항목 31 재구성). 4구역: 상단 왼쪽 자원 한 줄, 상단 가운데 휴전·경고(없으면 그리지 않음), 상단 오른쪽 외교·메뉴 아이콘,
  /// 하단 왼쪽 미니맵 + 빠른 버튼(유휴 공병·전체 군대·부대 1~3), 하단 가운데 선택 카드, 하단 오른쪽 3x3 명령 카드.
  /// 시뮬레이션은 읽기만 하고 행동은 커맨드로 보낸다. 색·그림은 UiTheme·UiCatalog.
  /// </summary>
  public unsafe class MatchHud : MonoBehaviour {
    const float RefreshSeconds = 0.2f;
    const float AlertSeconds = 2.5f;
    const float NoteSeconds = 3f;
    const float TooltipSeconds = 3f;
    const float FlashSeconds = 2f;
    public const int ContextSlots = 9;
    public const int ContextColumns = 3;
    const float Cell = Ui.MinButtonSize;
    const int ExtraBuildSlots = ContextColumns;
    const int PassengerSlots = 6;
    const float Gap = 8f;
    const float Pad = 12f;
    const float HeaderHeight = 42f;
    /// <summary>상단 자원·카운트다운 줄 높이.</summary>
    const float TopBarHeight = 48f;
    /// <summary>자주 누르지 않는 메뉴·외교 버튼.</summary>
    const float SmallButton = 120f;
    const float MinimapSize = 260f;
    /// <summary>하단 가운데 선택 카드(한 장만): 아이콘 132 + 이름·체력·상태 두세 줄.</summary>
    const float CardMaxWidth = 600f;
    const float CardHeight = 168f;
    const float CardIcon = 132f;
    const float CardText = Pad + CardIcon + 12f;
    const float TooltipHeight = 80f;
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

    enum Menu { Root, Build, Research, Hangar, HqResearch, Factory }

    static readonly (string Icon, float Width)[] ResourceSlots = {
      ("res_iron", 136), ("res_wood", 136), ("res_steel", 136), ("res_power", 236), ("res_population", 140),
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
    Button _groupsToggle;
    RectTransform _groupsFlyout;
    Button _idleButton;
    Text _idleBadge;
    Button[] _groupButtons;
    RectTransform _card;
    RectTransform _passengerStrip;
    Image _cardIcon;
    Text _cardTitle;
    RectTransform _cardBadge;
    Text _cardBadgeText;
    readonly Image[] _factoryLevelPips = new Image[3];
    RectTransform _cardHealth;
    Image _cardHealthFill;
    Text _cardDetail;
    readonly List<Image> _queueIcons = new List<Image>();
    readonly List<Button> _passengerButtons = new List<Button>();
    readonly List<EntityRef> _passengerEntities = new List<EntityRef>();
    EntityRef _passengerTransport;
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
    Button _ultimateQuickButton;
    Image _ultimateQuickFrame;
    RectTransform _ultimateQuickFill;
    Image _ultimateQuickFillImage;
    Text _ultimateQuickText;
    public int UltimateBars => _ultimateBars.Count(b => b.Root.gameObject.activeSelf);
    public string UltimateBarText(int index) => _ultimateBars.Where(b => b.Root.gameObject.activeSelf).Select(b => b.Label.text).ElementAtOrDefault(index) ?? "";
    public int MinimapUltimateMarks => _minimap.UltimateMarks;
    /// <summary>시험용: 모든 궁극 전력 막대에서 글자가 칠(Fill)보다 위 칸이고 외곽선이 있으며 칠이 반투명인지.</summary>
    public bool UltimateTextAboveFill => _ultimateBars.Count > 0 && _ultimateBars.All(b =>
      b.Label.rectTransform.GetSiblingIndex() > b.Fill.GetSiblingIndex() && b.Label.GetComponent<Outline>() != null && b.Label.color.a > 0.9f && b.Label.color.r > 0.9f && UltimateFillAlpha <= 0.65f);

    static float ContextWidth => ContextColumns * Cell + (ContextColumns - 1) * Gap + 2 * Pad;
    static float ContextHeight => ContextColumns * Cell + (ContextColumns - 1) * Gap + 2 * Pad + HeaderHeight;
    /// <summary>왼쪽 묶음(미니맵, 그 위 부대·유휴 공병 줄)의 오른쪽 끝.</summary>
    const float LeftClusterWidth = 8 + MinimapSize + 8 + 16;

    public void Build(Frame frame) {
      var theme = UiTheme.Current;
      var canvas = Ui.CreateCanvas("MatchHud");
      canvas.transform.SetParent(transform, false);
      Root = Ui.SafeArea(canvas);
      // Photon 개발용 프레임 표시(QuantumStats)는 왼쪽 위 자원 막대(철)를 가린다. 성능 표시를 켠 개발 빌드에서만 보인다.
      foreach (var stats in FindObjectsByType<QuantumStats>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
        stats.gameObject.SetActive(PerfMonitor.OverlayEnabled);
      }

      // 상단 왼쪽: 자원 한 줄.
      var top = Ui.Rect("TopBar", Root);
      top.anchorMin = new Vector2(0, 1);
      top.anchorMax = new Vector2(0, 1);
      top.pivot = new Vector2(0, 1);
      top.anchoredPosition = new Vector2(12, -8);
      top.sizeDelta = new Vector2(ResourceSlots.Sum(r => r.Width) + (ResourceSlots.Length - 1) * Gap, TopBarHeight);
      _resources.Clear();
      var x = 0f;
      foreach (var (icon, width) in ResourceSlots) {
        var capsule = Ui.Capsule($"{icon}_capsule", top, theme.Panel);
        Ui.Place(capsule, new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(width, TopBarHeight));
        var image = Ui.Icon(icon, capsule, icon, new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(34, 34));
        var slot = UiCatalog.SpriteFor(icon);
        if (slot != null) {
          image.sprite = slot;
        }
        var value = Ui.Label($"{icon}_value", capsule, "", theme.FontSmall);
        value.color = theme.PanelText;
        value.horizontalOverflow = HorizontalWrapMode.Overflow;
        Ui.Place(value.rectTransform, new Vector2(0, 0.5f), new Vector2(44, 0), new Vector2(width - 48, 40));
        _resources.Add((image, value, capsule.GetComponent<Image>()));
        x += width + Gap;
      }

      // 상단 오른쪽: 메뉴·외교는 작은 아이콘 버튼(자주 누르지 않는다). 궁극 슬롯만 정규 크기.
      var menuButton = IconButton("MenuButton", Root, "ui_menu", "메뉴", () => Controller.TogglePause());
      Ui.Place((RectTransform)menuButton.transform, Vector2.one, new Vector2(-12, -8), new Vector2(SmallButton, SmallButton));
      var diplomacyButton = IconButton("DiplomacyButton", Root, "ui_diplomacy", "외교", ToggleDiplomacy);
      Ui.Place((RectTransform)diplomacyButton.transform, Vector2.one, new Vector2(-12 - SmallButton - Gap, -8), new Vector2(SmallButton, SmallButton));

      // 상단 줄(자원 바로 오른쪽): 휴전·연결 대기 카운트다운. 글자가 없으면 통째로 숨긴다(빈 막대 B5).
      _truceBadge = Ui.Capsule("TruceBadge", Root, theme.Panel);
      Ui.Place(_truceBadge, new Vector2(0, 1), new Vector2(12 + top.sizeDelta.x + 2 * Gap, -8), new Vector2(300, TopBarHeight));
      _truce = Ui.Label("Truce", _truceBadge, "", theme.FontSmall, TextAnchor.MiddleCenter);
      _truce.resizeTextForBestFit = true;
      _truce.resizeTextMinSize = theme.FontSmall - 8;
      _truce.resizeTextMaxSize = theme.FontSmall;
      Ui.Stretch(_truce.rectTransform, 4f);
      _truce.color = theme.Warning;
      _truceBadge.gameObject.SetActive(false);

      // 알림·쪽지: 상단 줄 바로 아래 가운데, 잠깐만 보인다.
      _alertBubble = Ui.Capsule("AlertBubble", Root, theme.Panel);
      Ui.Place(_alertBubble, new Vector2(0.5f, 1), new Vector2(0, -8 - TopBarHeight - Gap), new Vector2(720, TopBarHeight));
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
      Ui.Place(_noteBubble, new Vector2(0.5f, 1), new Vector2(0, -8 - 2 * (TopBarHeight + Gap)), new Vector2(720, TopBarHeight));
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
      Ui.Place(mapFrame, Vector2.zero, new Vector2(8, 8), new Vector2(MinimapSize + 8, MinimapSize + 8));
      var map = Ui.Rect("Minimap", mapFrame);
      Ui.Place(map, Vector2.zero, new Vector2(4, 4), new Vector2(MinimapSize, MinimapSize));
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

      // 미니맵 바로 위 한 줄: 부대 펼치기 + 유휴 공병(숫자 배지). 부대 버튼 묶음은 평소에 접어 두어 왼쪽 전장을 비운다.
      var actions = Ui.Rect("Actions", Root);
      Ui.Place(actions, Vector2.zero, new Vector2(8, 8 + MinimapSize + 8 + Gap), new Vector2(2 * Cell + Gap, Cell));
      _groupsToggle = IconButton("GroupsToggle", actions, "ui_group", "부대", ToggleGroups);
      Ui.Place((RectTransform)_groupsToggle.transform, Vector2.zero, Vector2.zero, new Vector2(Cell, Cell));
      _idleButton = IconButton("IdleEngineerButton", actions, "ui_idle", "유휴 공병", SelectIdleEngineer);
      Ui.Place((RectTransform)_idleButton.transform, Vector2.zero, new Vector2(Cell + Gap, 0), new Vector2(Cell, Cell));
      _idleBadge = Badge(_idleButton.transform, theme.Danger);

      // 부대 펼침 판(누르면 선택, 두 번 누르면 카메라, 길게 누르면 지정). 고르면 저절로 접힌다.
      _groupsFlyout = Ui.Panel("GroupFlyout", Root, theme.Panel);
      Ui.Place(_groupsFlyout, Vector2.zero, new Vector2(8, 8 + MinimapSize + 8 + Gap + Cell + Gap), new Vector2(2 * Cell + Gap + 2 * Gap, 3 * Cell + 2 * Gap + 2 * Gap));
      var grid = _groupsFlyout.gameObject.AddComponent<GridLayoutGroup>();
      grid.padding = new RectOffset((int)Gap, (int)Gap, (int)Gap, (int)Gap);
      grid.cellSize = new Vector2(Cell, Cell);
      grid.spacing = new Vector2(Gap, Gap);
      grid.startCorner = GridLayoutGroup.Corner.LowerLeft;
      grid.childAlignment = TextAnchor.LowerLeft;
      grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
      grid.constraintCount = 2;
      _groupButtons = new Button[SelectionModel.GroupCount];
      for (var i = 0; i < SelectionModel.GroupCount; i++) {
        var index = i;
        _groupButtons[i] = IconButton($"Group{i + 1}", _groupsFlyout, "ui_group", $"{i + 1}부대", () => { PressGroup(index); CloseGroups(); });
        LongPress.Attach(_groupButtons[i], () => AssignGroup(index));
      }
      IconButton("ArmyButton", _groupsFlyout, "ui_army", "전체 군대", () => { SelectArmy(); CloseGroups(); });
      _boxButton = IconButton("BoxButton", _groupsFlyout, "ui_box", "범위", () => { ToggleBoxMode(); CloseGroups(); });
      _groupsFlyout.gameObject.SetActive(false);

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
      // Task 46: sea barrier / watchtower under attack (once per attack window, from the simulation).
      QuantumEvent.Subscribe<EventSeaBarrierAlert>(this, e => {
        if (e.Player != Controller.Player) return;
        SeaAlerts++;
        GameAudio.Play(SoundEvent.UnderAttack);
        ShowAlert(Texts.SeaBarrierAttacked);
        PingAlert(e.Position.ToUnityVector3());
      });
      QuantumEvent.Subscribe<EventSeaGateChanged>(this, e => {
        if (e.Destroyed) ShowAlert($"방벽 #{e.Gate + 1} 파괴됨 — 수로 개방");
      });
      QuantumEvent.Subscribe<EventCratePicked>(this, OnCratePicked);
      // Task 41: logistics alerts for the local player (delivered amount floats over the dock).
      QuantumEvent.Subscribe<EventLogisticsNotice>(this, e => {
        if (e.Player != Controller.Player) return;
        switch (e.Kind) {
          case 1: ShowAlert($"자원지 수송 도착: {Texts.Reward(e.Amount)}"); break;
          case 2: ShowAlert("수송 트럭을 잃었습니다 — 화물 일부가 상자로 떨어짐, 30초 뒤 새 트럭"); break;
          case 3: ShowAlert("수송 트럭: 경로 대기(길이 막힘)"); break;
          case 4: ShowAlert("자원지 버퍼 가득 — 수송을 기다리는 중"); break;
          case 5: ShowAlert("자원지 소유가 바뀌었습니다"); break;
          case 6: ShowAlert("수송 트럭: 적 함선 사거리를 피해 내륙으로 우회"); break;
        }
      });
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
      // 우측 전용 궁극기 터치 슬롯(골드/레드 발광 프레임 + 건조 진행/재충전 게이지 + 원탭 목표 지정).
      _ultimateQuickButton = IconButton("UltimateQuickSlot", Root, "ui_attack", "궁극 전력", PressUltimateQuickSlot);
      var qRect = (RectTransform)_ultimateQuickButton.transform;
      Ui.Place(qRect, Vector2.one, new Vector2(-12 - 2 * (SmallButton + Gap), -8), new Vector2(Cell, Cell));
      _ultimateQuickFrame = qRect.GetComponent<Image>();
      _ultimateQuickFill = Ui.Rect("QuickFill", qRect);
      _ultimateQuickFill.anchorMin = Vector2.zero;
      _ultimateQuickFill.anchorMax = new Vector2(1f, 0f);
      _ultimateQuickFill.offsetMin = Vector2.zero;
      _ultimateQuickFill.offsetMax = Vector2.zero;
      _ultimateQuickFillImage = _ultimateQuickFill.gameObject.AddComponent<Image>();
      _ultimateQuickFillImage.color = new Color(0.95f, 0.76f, 0.22f, 0.32f);
      _ultimateQuickFillImage.raycastTarget = false;
      _ultimateQuickText = qRect.GetComponentsInChildren<Text>(true).LastOrDefault();
      if (_ultimateQuickText != null) {
        _ultimateQuickText.fontStyle = FontStyle.Bold;
        _ultimateQuickText.rectTransform.SetAsLastSibling();
      }

      for (var i = 0; i < MaxUltimateBars; i++) {
        var bar = Ui.Capsule($"UltimateBar{i}", Root, theme.Panel);
        Ui.Place(bar, Vector2.one, new Vector2(-12, -8 - Cell - Gap - i * 46), new Vector2(width, 40));
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
      // 기동형 MLS 교체에 따라 전략 미사일 발사/착탄 경보 카운트다운 바를 비활성화한다.
      for (var i = 0; i < frame.PlayerCount; i++) {
        var state = frame.Global->Players.GetPointer(i);
        var spec = UltimateSystem.SpecOf(frame, i);
        if (state->Active == false || spec == null || spec.Kind == UltimateKind.StrategicMissile
            || (state->UltimatePhase != UltimatePhase.Preparing && state->UltimatePhase != UltimatePhase.Ready)) {
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
      RefreshUltimateQuickSlot(frame);
    }

    void RefreshUltimateQuickSlot(Frame frame) {
      if (_ultimateQuickButton == null) {
        return;
      }
      var state = frame.Global->Players.GetPointer(Controller.Player);
      var spec = UltimateSystem.SpecOf(frame, Controller.Player);
      if (spec == null) {
        _ultimateQuickButton.gameObject.SetActive(false);
        return;
      }
      _ultimateQuickButton.gameObject.SetActive(true);
      var shortName = spec.Kind == UltimateKind.StrategicMissile ? "기동형 MLS" : "공중항모";
      var fill = 0f;
      var frameCol = new Color(0.35f, 0.38f, 0.44f, 0.92f);
      var fillCol = new Color(0.95f, 0.76f, 0.22f, 0.45f);
      var label = shortName;
      switch (state->UltimatePhase) {
        case UltimatePhase.Preparing:
          var pTotal = Mathf.Max(1f, state->UltimateTotal.AsFloat);
          fill = Mathf.Clamp01(1f - state->UltimateRemaining.AsFloat / pTotal);
          frameCol = new Color(0.95f, 0.75f, 0.2f, 0.95f);
          label = $"{shortName}\n{Mathf.CeilToInt(state->UltimateRemaining.AsFloat)}초";
          break;
        case UltimatePhase.Ready:
          fill = 1f;
          var aiming = Controller.Input != null && Controller.Input.Mode == MatchInput.TapMode.Ultimate;
          frameCol = aiming ? new Color(0.96f, 0.22f, 0.22f, 1f) : new Color(0.98f, 0.78f, 0.18f, 1f);
          fillCol = new Color(0.95f, 0.25f, 0.2f, 0.5f);
          label = aiming ? "목표 터치" : "발사 준비!";
          break;
        case UltimatePhase.Active:
          fill = 1f;
          frameCol = new Color(0.95f, 0.78f, 0.22f, 0.96f);
          label = "보유 중\n(1/1)";
          break;
        case UltimatePhase.Cooldown:
          var cTotal = Mathf.Max(1f, state->UltimateTotal.AsFloat);
          fill = Mathf.Clamp01(1f - state->UltimateRemaining.AsFloat / cTotal);
          frameCol = new Color(0.72f, 0.24f, 0.24f, 0.92f);
          fillCol = new Color(0.92f, 0.32f, 0.22f, 0.42f);
          label = $"재충전\n{Mathf.CeilToInt(state->UltimateRemaining.AsFloat)}초";
          break;
        default:
          var locks = UltimateSystem.Locks(frame, Controller.Player);
          if (locks == UltimateLock.None) {
            fill = 1f;
            frameCol = new Color(0.92f, 0.74f, 0.22f, 0.95f);
            label = $"{shortName}\n건조 가능";
          } else {
            var sites = ProductionSystem.CountRole(frame, Controller.Player, BuildingRole.UltimateSite);
            label = sites >= 1 ? $"{shortName}\n보유(1/1)" : $"{shortName}\n3단계";
          }
          break;
      }
      _ultimateQuickFrame.color = frameCol;
      _ultimateQuickFill.anchorMax = new Vector2(1f, fill);
      _ultimateQuickFillImage.color = fillCol;
      if (_ultimateQuickText != null) {
        _ultimateQuickText.text = label;
        _ultimateQuickText.fontStyle = FontStyle.Bold;
        _ultimateQuickText.rectTransform.SetAsLastSibling();
        // 골드 배경에는 짙은 차콜(#1A1A1A) 굵은 글씨, 어두운 레드/회색 배경에는 순백 + 검은 외곽선으로 모바일 시인성 극대화.
        var brightBg = (frameCol.r * 0.299f + frameCol.g * 0.587f + frameCol.b * 0.114f) > 0.55f;
        _ultimateQuickText.color = brightBg ? new Color(0.10f, 0.10f, 0.10f, 1f) : Color.white;
        var outl = _ultimateQuickText.GetComponent<Outline>() ?? _ultimateQuickText.gameObject.AddComponent<Outline>();
        outl.enabled = brightBg == false;
        outl.effectColor = new Color(0f, 0f, 0f, 0.92f);
        outl.effectDistance = new Vector2(1.4f, -1.4f);
      }
    }

    void PressUltimateQuickSlot() {
      var frame = Controller.Frame;
      if (frame == null) {
        return;
      }
      var state = frame.Global->Players.GetPointer(Controller.Player);
      var spec = UltimateSystem.SpecOf(frame, Controller.Player);
      if (spec == null) {
        return;
      }
      if (state->UltimatePhase == UltimatePhase.Ready) {
        var aiming = Controller.Input.Mode == MatchInput.TapMode.Ultimate;
        Controller.Input.Mode = aiming ? MatchInput.TapMode.None : MatchInput.TapMode.Ultimate;
        ShowAlert(aiming ? "전략 미사일 목표 지정 취소" : "전장이나 미니맵을 터치해 전략 미사일을 발사하세요!");
        return;
      }
      if (state->UltimatePhase == UltimatePhase.Active) {
        foreach (var pair in frame.GetComponentIterator<Carrier>()) {
          if (frame.Get<Unit>(pair.Entity).Owner == Controller.Player) {
            Controller.Input.Selection.SelectUnit(pair.Entity);
            Controller.Camera.MoveTo(frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector2());
            return;
          }
        }
      }
      var site = UltimateSystem.FindSite(frame, Controller.Player, spec);
      if (site != EntityRef.None) {
        Controller.Input.Selection.SelectBuilding(site);
        if (state->UltimatePhase == UltimatePhase.None && UltimateSystem.Locks(frame, Controller.Player) == UltimateLock.None) {
          Controller.Send(new StartUltimateCommand());
        }
      } else {
        Controller.Input.Selection.SelectBuilding(Headquarters(frame, Controller.Player));
      }
    }

    static EntityRef Headquarters(Frame frame, PlayerRef player) {
      var buildings = frame.Filter<Building>();
      while (buildings.NextUnsafe(out var entity, out var b)) {
        if (b->Owner == player && frame.FindAsset(b->Spec).Role == BuildingRole.Headquarters) {
          return entity;
        }
      }
      return EntityRef.None;
    }

    /// <summary>궁극 전력 알림은 모든 플레이어에게 보인다(전략 미사일/기동형 MLS 발사 경보는 비활성화).</summary>
    void OnUltimateNotice(EventUltimateNotice e) {
      if (e.Kind == UltimateKind.StrategicMissile) {
        return;
      }
      var frame = e.Game.Frames.Verified;
      var mine = e.Player == Controller.Player;
      ShowAlert(Texts.UltimateNotice(Who(frame, e.Player), e.Stage, e.Kind, e.Value));
      if (_alertBubble != null && (e.Stage == UltimateStage.Started || e.Stage == UltimateStage.Launched || e.Stage == UltimateStage.Ready || e.Stage == UltimateStage.Impact || e.Stage == UltimateStage.Lost)) {
        // 전술 레드(#B91C1C) 배경 + 굵은 순백 텍스트 + 검은 외곽선.
        _alertBubble.GetComponent<Image>().color = new Color(0.725f, 0.110f, 0.110f, 0.96f);
        _alert.color = Color.white;
        _alert.fontStyle = FontStyle.Bold;
        var outl = _alert.GetComponent<Outline>() ?? _alert.gameObject.AddComponent<Outline>();
        outl.enabled = true;
        outl.effectColor = new Color(0f, 0f, 0f, 0.88f);
        outl.effectDistance = new Vector2(1.4f, -1.4f);
        _alertUntil = Time.unscaledTime + 5f;
      }
      if (e.Stage != UltimateStage.Cancelled || e.Position != default) {
        var pos = e.Position.ToUnityVector3();
        PingAlert(pos);
        _minimap?.Ping(pos, new Color32(240, 36, 36, 255), 5f);
      }
      if (e.Kind == UltimateKind.StrategicMissile && e.Stage == UltimateStage.Launched && mine == false) {
        GameAudio.Play(SoundEvent.MissileWarning);
      } else if (mine == false || e.Stage == UltimateStage.Lost || e.Stage == UltimateStage.Launched) {
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
          Entries.Add(new ContextEntry { Label = $"{name}\n보유 중 (1/1)", Icon = UltimateIcon(frame, spec), Enabled = false, Locked = true, Reason = $"{name} 보유 중 (1/1) — 보유 상한 {spec.OwnedLimit}" });
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

    RectTransform _doctrineOverlay;
    Text _doctrineTitle;

    static readonly (int Id, string Name, string Body, string Icon)[] Doctrines = {
      (1, "공군 타격 사령부", "출격기 재정비 -25%\n정찰차 시야·속도 +20%", "doctrine_air"),
      (2, "그림자 특수전단", "저격수 한도 +1\n은신 +10초 · 엄폐 피해 -15%", "doctrine_shadow"),
      (3, "기갑 요새화 군단", "전차·포격함 생산비 -15%\n공병 수리 +50%", "doctrine_armor"),
    };

    /// <summary>경기 시작 직후 도크린을 선택하는 카드. UiCatalog 그림을 우선 사용한다.</summary>
    void BuildDoctrinePanel(UiTheme theme) {
      _doctrineOverlay = Ui.Rect("DoctrineOverlay", Root);
      Ui.Stretch(_doctrineOverlay);
      var dim = _doctrineOverlay.gameObject.AddComponent<Image>();
      dim.color = new Color(0.04f, 0.06f, 0.10f, 0.5f);
      dim.raycastTarget = false;
      var panel = Ui.Panel("DoctrinePanel", _doctrineOverlay, theme.Panel);
      Ui.Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1180, 470));
      _doctrineTitle = Ui.Header("DoctrineHeader", panel, "전술 도크린을 고르세요", 1100f, -26f, 64f);
      for (var i = 0; i < Doctrines.Length; i++) {
        var card = Doctrines[i];
        var id = card.Id;
        var button = Ui.Button($"Doctrine{id}", panel, "", () => {
          Controller.Send(new ChooseDoctrineCommand { Doctrine = id });
          _doctrineOverlay.gameObject.SetActive(false);
        }, theme.FontMedium - 2);
        Ui.Place((RectTransform)button.transform, new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 375, -30), new Vector2(350, 300));
        button.GetComponentInChildren<Text>().gameObject.SetActive(false);
        var fallbackIcon = id == 1 ? "building_airfield" : id == 2 ? "ui_attack_move" : "ui_armor";
        var icon = Ui.Icon("RoleIcon", button.transform, fallbackIcon, new Vector2(0.5f, 1), new Vector2(0f, -24f), new Vector2(84f, 84f));
        icon.sprite = UiCatalog.SpriteFor(card.Icon) ?? icon.sprite;
        icon.preserveAspect = true;
        var title = Ui.Label("DoctrineName", button.transform, card.Name, theme.FontMedium, TextAnchor.MiddleCenter);
        title.fontStyle = FontStyle.Bold;
        title.color = theme.PanelText;
        Ui.Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0f, -115f), new Vector2(310f, 46f));
        var effect = Ui.Label("DoctrineEffect", button.transform, card.Body, theme.FontSmall - 2, TextAnchor.UpperCenter);
        effect.color = theme.PanelText;
        Ui.Place(effect.rectTransform, new Vector2(0.5f, 1), new Vector2(0f, -174f), new Vector2(305f, 95f));
      }
      _doctrineOverlay.gameObject.SetActive(false);
    }

    RectTransform _airOpsPanel;
    readonly List<(Button Button, Text Label, EntityRef Airfield, int Slot)> _airOpsSlots = new List<(Button, Text, EntityRef, int)>();

    /// <summary>항목 34-E: 비행장을 선택하지 않아도 격납고 작전기를 바로 출격·재정비 확인할 수 있는 항공 작전 패널.</summary>
    void BuildAirOpsPanel(UiTheme theme) {
      _airOpsPanel = Ui.Panel("AirOpsPanel", Root, theme.Panel);
      var buttonsWidth = SortieSystem.Slots * Cell + (SortieSystem.Slots - 1) * Gap;
      // 왼쪽 묶음(미니맵 + 부대 줄) 바로 위.
      Ui.Place(_airOpsPanel, Vector2.zero, new Vector2(8f, 8f + MinimapSize + 8f + Gap + Cell + Gap),
        new Vector2(buttonsWidth + 2f * Pad, Cell + 2f * Pad));
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
        Ui.Place((RectTransform)btn.transform, new Vector2(0f, 0.5f), new Vector2(Pad + idx * (Cell + Gap), 0f), new Vector2(Cell, Cell));
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
      if (_doctrineOverlay == null || Controller.Player.IsValid == false) {
        return;
      }
      var state = FactoryWarsUtil.Player(frame, Controller.Player);
      var rules = frame.FindAsset(frame.RuntimeConfig.Rules);
      var left = rules.DoctrineSelectSeconds - frame.Number / Mathf.Max(1, frame.SessionConfig.UpdateFPS);
      var show = state != null && state->DoctrineLocked == false && left > 0;
      if (_doctrineOverlay.gameObject.activeSelf != show) {
        _doctrineOverlay.gameObject.SetActive(show);
      }
      DoctrineOpen = show;
      if (show) {
        if (_doctrineOverlay.GetSiblingIndex() != Root.childCount - 1) {
          _doctrineOverlay.SetAsLastSibling();
        }
        // 큰 창이 겹치지 않게: 도크린 선택 중에는 오른쪽 명령 패널·선택 카드를 숨긴다.
        _contextPanel?.SetActive(false);
        _card?.gameObject.SetActive(false);
        _doctrineTitle.text = $"전술 도크린을 고르세요 ({left}초 뒤 기본값)";
      }
    }

    void BuildCard(UiTheme theme) {
      _card = Ui.Panel("SelectionCard", Root, theme.Panel);
      Ui.Place(_card, new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(CardMaxWidth, CardHeight));
      var frame = Ui.Rect("IconFrame", _card);
      var frameImage = frame.gameObject.AddComponent<Image>();
      frameImage.sprite = UiCatalog.SpriteFor("icon_frame");
      frameImage.type = Image.Type.Sliced;
      frameImage.color = theme.Button;
      frameImage.raycastTarget = false;
      Ui.Place(frame, new Vector2(0, 0.5f), new Vector2(Pad, 0), new Vector2(CardIcon, CardIcon));
      _cardIcon = Ui.Icon("Icon", frame, "", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardIcon - 20, CardIcon - 20));
      _cardTitle = Ui.Label("Title", _card, "", theme.FontLarge - 6);
      _cardTitle.color = theme.PanelText;
      Ui.Place(_cardTitle.rectTransform, new Vector2(0, 1), new Vector2(CardText, -8), new Vector2(340, 38));
      _cardBadge = Ui.Capsule("SizeBadge", _card, theme.Accent);
      var badgeSprite = UiCatalog.SpriteFor("badge_small");
      if (badgeSprite != null) {
        _cardBadge.GetComponent<Image>().sprite = badgeSprite;
      }
      Ui.Place(_cardBadge, new Vector2(1, 1), new Vector2(-Pad, -10), new Vector2(84, 34));
      _cardBadgeText = Ui.Label("Text", _cardBadge, "", theme.FontSmall, TextAnchor.MiddleCenter);
      _cardBadgeText.color = Color.white;
      Ui.Stretch(_cardBadgeText.rectTransform);
      for (var i = 0; i < _factoryLevelPips.Length; i++) {
        var pip = Ui.Capsule($"FactoryLevel{i + 1}", _card);
        Ui.Place(pip, new Vector2(1, 1), new Vector2(-Pad - (2 - i) * 30f, -50f), new Vector2(26f, 5f));
        var image = pip.GetComponent<Image>();
        image.raycastTarget = false;
        _factoryLevelPips[i] = image;
        pip.gameObject.SetActive(false);
      }
      _cardHealth = Ui.Rect("HealthBar", _card);
      var back = _cardHealth.gameObject.AddComponent<Image>();
      back.sprite = UiCatalog.SpriteFor("health_bar");
      back.type = Image.Type.Sliced;
      back.color = new Color(0.2f, 0.22f, 0.26f, 0.6f);
      back.raycastTarget = false;
      Ui.Place(_cardHealth, new Vector2(0, 1), new Vector2(CardText, -50), new Vector2(420, 14));
      var fill = Ui.Rect("Fill", _cardHealth);
      _cardHealthFill = fill.gameObject.AddComponent<Image>();
      _cardHealthFill.sprite = UiCatalog.SpriteFor("health_fill");
      _cardHealthFill.type = Image.Type.Sliced;
      _cardHealthFill.raycastTarget = false;
      fill.anchorMin = Vector2.zero;
      fill.anchorMax = new Vector2(1, 1);
      fill.offsetMin = Vector2.zero;
      fill.offsetMax = Vector2.zero;
      _cardDetail = Ui.Label("Detail", _card, "", theme.FontSmall - 4, TextAnchor.UpperLeft);
      _cardDetail.resizeTextForBestFit = true;
      _cardDetail.resizeTextMinSize = theme.FontSmall - 10;
      _cardDetail.resizeTextMaxSize = theme.FontSmall - 4;
      _cardDetail.color = theme.PanelText;
      Ui.Place(_cardDetail.rectTransform, new Vector2(0, 1), new Vector2(CardText, -70), new Vector2(430, 88));
      for (var i = 0; i < QueueIcons; i++) {
        var icon = Ui.Icon($"Queue{i}", _card, "", new Vector2(0, 0), new Vector2(CardText + i * 48, 10), new Vector2(42, 42));
        _queueIcons.Add(icon);
      }
      // 선택 카드 위의 승객 초상화. 탭하면 해당 승객만 개별 하차한다.
      _passengerStrip = Ui.Panel("PassengerStrip", _card, theme.Panel);
      var passengerWidth = PassengerSlots * (Cell + Gap) - Gap + 2 * Pad;
      Ui.Place(_passengerStrip, new Vector2(0.5f, 0), new Vector2(0f, _card.sizeDelta.y + Gap),
        new Vector2(passengerWidth, Cell + 2 * Pad));
      _passengerStrip.gameObject.SetActive(false);
      for (var i = 0; i < PassengerSlots; i++) {
        var idx = i;
        var pBtn = Ui.Button($"Passenger{i}", _passengerStrip, "", () => UnloadPassengerSlot(idx), theme.FontSmall);
        Ui.Place((RectTransform)pBtn.transform, Vector2.zero, new Vector2(Pad + i * (Cell + Gap), Pad), new Vector2(Cell, Cell));
        var bgImg = pBtn.GetComponent<Image>();
        if (bgImg != null) {
          bgImg.color = theme.Button;
        }
        var pOutline = pBtn.gameObject.AddComponent<Outline>();
        pOutline.effectColor = theme.Accent;
        pOutline.effectDistance = new Vector2(1.5f, -1.5f);
        pBtn.gameObject.SetActive(false);
        _passengerButtons.Add(pBtn);
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
      var header = Ui.Capsule("CommandHeader", panel, theme.Header);
      header.GetComponent<Image>().raycastTarget = false;
      header.anchorMin = new Vector2(0, 1);
      header.anchorMax = Vector2.one;
      header.pivot = new Vector2(0.5f, 1f);
      header.sizeDelta = new Vector2(-2 * Pad, HeaderHeight - 6f);
      header.anchoredPosition = new Vector2(0f, -8f);
      _status = Ui.Label("Status", header, "", theme.FontSmall + 2, TextAnchor.MiddleCenter);
      _status.color = Color.white;
      _status.horizontalOverflow = HorizontalWrapMode.Wrap;
      _status.resizeTextForBestFit = true;
      _status.resizeTextMinSize = theme.FontSmall - 12;
      _status.resizeTextMaxSize = theme.FontSmall;
      Ui.Stretch(_status.rectTransform, 2f);
      _context = new Button[ContextSlots + ExtraBuildSlots];
      for (var i = 0; i < _context.Length; i++) {
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
      _tooltip = Ui.Panel("Tooltip", Root, theme.Panel);
      var tooltipSprite = UiCatalog.SpriteFor("tooltip");
      if (tooltipSprite != null) {
        _tooltip.GetComponent<Image>().sprite = tooltipSprite;
      }
      Ui.Place(_tooltip, new Vector2(1, 0), new Vector2(-12, 8 + ContextHeight + Gap), new Vector2(ContextWidth, TooltipHeight));
      _tooltipText = Ui.Label("Text", _tooltip, "", theme.FontSmall - 2, TextAnchor.MiddleLeft);
      _tooltipText.resizeTextForBestFit = true;
      _tooltipText.resizeTextMinSize = theme.FontSmall - 8;
      _tooltipText.resizeTextMaxSize = theme.FontSmall - 2;
      _tooltipText.color = theme.PanelText;
      Ui.Stretch(_tooltipText.rectTransform, 10f);
      _tooltip.GetComponent<Image>().raycastTarget = false;
      _tooltip.gameObject.SetActive(false);
    }

    /// <summary>Task 46: sea barrier alerts shown to the local player (tests read it).</summary>
    public int SeaAlerts { get; private set; }

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
      Ui.Header("DiplomacyHeader", _diplomacy, "외교 · 쪽지", 1420f);
      Ui.Modal(_diplomacy);
      var rules = frame.FindAsset(frame.RuntimeConfig.Rules);
      var row = 0;
      for (var i = 0; i < frame.PlayerCount; i++) {
        PlayerRef other = i;
        if (other == Controller.Player || frame.Global->Players.GetPointer(i)->Active == false) {
          continue;
        }
        var y = -90 - row * 150;
        var line = Ui.Rect($"Row{i}", _diplomacy);
        Ui.Place(line, new Vector2(0, 1), new Vector2(20, y), new Vector2(1460, Ui.MinButtonSize));
        var name = Ui.Label("Name", line, "", 30);
        name.color = UiTheme.Current.PanelText;
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

    public bool GroupsOpen => _groupsFlyout != null && _groupsFlyout.gameObject.activeSelf;

    /// <summary>부대 버튼: 범위 선택 중이면 범위 선택을 끄고, 아니면 부대 펼침 판을 열고 닫는다.</summary>
    public void ToggleGroups() {
      if (BoxMode) {
        ToggleBoxMode();
        return;
      }
      _groupsFlyout.gameObject.SetActive(GroupsOpen == false);
      if (GroupsOpen) {
        _groupsFlyout.SetAsLastSibling();
      }
      _groupsToggle.GetComponent<Image>().color = GroupsOpen ? UiTheme.Current.ButtonPressed : Ui.ButtonColor;
    }

    void CloseGroups() {
      _groupsFlyout.gameObject.SetActive(false);
      if (BoxMode == false) {
        _groupsToggle.GetComponent<Image>().color = Ui.ButtonColor;
      }
    }

    public void ToggleBoxMode() {
      BoxMode = BoxMode == false;
      Ui.SetLabel(_boxButton, BoxMode ? "범위 중" : "범위");
      _boxButton.GetComponent<Image>().color = BoxMode ? UiTheme.Current.ButtonPressed : Ui.ButtonColor;
      // 펼침 판은 접혀 있으므로 범위 선택 상태는 부대 버튼에 보인다(다시 누르면 끈다).
      Ui.SetLabel(_groupsToggle, BoxMode ? "범위 중" : "부대");
      _groupsToggle.GetComponent<Image>().color = BoxMode ? UiTheme.Current.ButtonPressed : Ui.ButtonColor;
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
        _alertBubble.GetComponent<Image>().color = UiTheme.Current.Panel;
        _alert.color = UiTheme.Current.PanelText;
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
      var cardWidth = Mathf.Min(CardMaxWidth, room);
      if (room >= 480f) {
        _card.anchorMin = _card.anchorMax = _card.pivot = new Vector2(0, 0);
        _card.anchoredPosition = new Vector2(left + (room - cardWidth) / 2f, 8);
        _card.sizeDelta = new Vector2(cardWidth, CardHeight);
      } else {
        _card.anchorMin = _card.anchorMax = _card.pivot = new Vector2(1, 0);
        _card.anchoredPosition = new Vector2(-12, 8 + ContextHeight + Gap + (_tooltip.gameObject.activeSelf ? TooltipHeight + Gap : 0));
        _card.sizeDelta = new Vector2(Mathf.Max(ContextWidth, 520f), CardHeight);
      }
      var innerWidth = _card.sizeDelta.x - CardText - Pad;
      _cardTitle.rectTransform.sizeDelta = new Vector2(Mathf.Max(120f, innerWidth - 92), 38);
      _cardHealth.sizeDelta = new Vector2(innerWidth, 14);
      // 생산 대기열 아이콘이 보이면 상태 글은 두 줄, 아니면 세 줄까지.
      var queue = _queueIcons.Any(i => i.gameObject.activeSelf);
      _cardDetail.rectTransform.sizeDelta = new Vector2(innerWidth, queue ? 46 : 88);
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
      if (spec.Role == BuildingRole.MlrsBattery) return "cf_longwall_mlrs";
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
      foreach (var pip in _factoryLevelPips) pip.gameObject.SetActive(false);
      if (single == false) {
        _cardBadge.gameObject.SetActive(false);
        foreach (var icon in _queueIcons) {
          icon.gameObject.SetActive(false);
        }
        HidePassengers();
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
      HidePassengers();
    }

    void HidePassengers() {
      _passengerEntities.Clear();
      _passengerTransport = EntityRef.None;
      _passengerStrip.gameObject.SetActive(false);
    }

    void UnloadPassengerSlot(int index) {
      if (_passengerTransport == EntityRef.None || index < 0 || index >= _passengerEntities.Count) {
        return;
      }
      Controller.Send(new UnloadTransportCommand { Transport = _passengerTransport, SpecificUnit = _passengerEntities[index] });
      ShowAlert("지정 승객 개별 하차");
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
      // 수송 유닛(APC · 상륙정 · 수송헬기): 탑승 슬롯 현황 및 개별 하차 초상화 스트립 표시.
      if (spec.TransportCapacity > 0) {
        var passengers = TransportSystem.Passengers(frame, entity);
        var used = TransportSystem.UsedSlots(frame, entity);
        lines += $"\n탑승 {used}/{spec.TransportCapacity}  " + (passengers.Count > 0 ? "(초상화 탭: 개별 하차)" : "(아군 선택 후 탭: 탑승)");
        var shown = Mathf.Min(passengers.Count, PassengerSlots);
        _passengerStrip.gameObject.SetActive(shown > 0);
        _passengerStrip.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, shown * (Cell + Gap) - Gap + 2 * Pad);
        _passengerTransport = entity;
        _passengerEntities.Clear();
        for (var i = 0; i < _passengerButtons.Count; i++) {
          var show = i < passengers.Count;
          _passengerButtons[i].gameObject.SetActive(show);
          if (show) {
            var pe = passengers[i];
            var pUnit = frame.Get<Unit>(pe);
            var pSpec = frame.FindAsset(pUnit.Spec);
            _passengerEntities.Add(pe);
            Ui.SetIcon(_passengerButtons[i], Ui.IconSprite(UnitIcon(pSpec.name)));
            var hpPct = Mathf.RoundToInt(100f * pUnit.Health.AsFloat / Mathf.Max(1f, TechSystem.MaxHealth(frame, pUnit.Owner, pUnit.Spec).AsFloat));
            Ui.SetLabel(_passengerButtons[i], $"{hpPct}%");
          }
        }
      }
      if (TechSystem.HasSprintUpgrade(frame, unit.Owner, spec)) {
        var cd = unit.AbilityCooldown > FP._0 ? $" (수류탄 {Mathf.CeilToInt(unit.AbilityCooldown.AsFloat)}초)" : " · 수류탄 준비";
        lines += $"\n기동 훈련 · 전력질주 해금{cd}";
      }
      _cardDetail.text = lines;
    }

    public string CardDetailText => _cardDetail != null ? _cardDetail.text : "";

    void CardBuilding(Frame frame, PlayerState* state, EntityRef entity) {
      var building = frame.Get<Building>(entity);
      var spec = frame.FindAsset(building.Spec);
      ShowCard(true);
      _cardIcon.sprite = Ui.IconSprite(BuildingIcon(spec));
      _cardTitle.text = Texts.NameOf(spec.name);
      var tierBadge = spec.Role == BuildingRole.Headquarters;
      var factory = FactorySystem.IsUpgradeableFactory(spec.Role) && building.ConstructionRemaining <= FP._0;
      var factoryLevel = FactorySystem.LevelOf(&building);
      _cardBadge.gameObject.SetActive(tierBadge || factory);
      if (tierBadge || factory) {
        _cardBadgeText.text = factory ? $"Lv. {factoryLevel}" : $"{Mathf.Max(1, building.Tier)}단계";
        _cardBadge.GetComponent<Image>().color = UiTheme.Current.Accent;
      }
      if (factory) {
        for (var i = 0; i < _factoryLevelPips.Length; i++) {
          var pip = _factoryLevelPips[i];
          pip.gameObject.SetActive(true);
          pip.color = i < factoryLevel ? UiTheme.Current.Accent : new Color(0.64f, 0.70f, 0.78f, 1f);
        }
      }
      SetHealth(building.Health.AsFloat / Mathf.Max(1f, spec.MaxHealth.AsFloat));
      var detail = $"체력 {building.Health.AsInt}/{spec.MaxHealth.AsInt}";
      if (factory && FactorySystem.IsProductionFactory(spec.Role)) {
        detail += $"   증축 보너스 +{FactoryUpgradeBonus(frame, factoryLevel)}%";
      }
      if (building.ConstructionRemaining > FP._0) {
        var paving = AirfieldWorkSystem.InPavingPhase(frame, entity, &building, spec, out _);
        var phase = paving ? AirfieldWorkName(spec.Role) + " 공사" : "건설";
        detail += $"   {phase} {Mathf.CeilToInt(building.ConstructionRemaining.AsFloat)}초 남음";
      }
      if (frame.TryGet<MlrsMagazine>(entity, out var magazine)) {
        var status = magazine.Status switch {
          MlrsStatus.Searching => "적 탐색",
          MlrsStatus.Aiming => "조준 중",
          MlrsStatus.Firing => "발사 중",
          MlrsStatus.Reloading => "재장전 중",
          MlrsStatus.Disabled => "작동 불가",
          _ => "대기",
        };
        detail += $"\n탄약 {magazine.Ammo}/{spec.RocketCapacity}   사거리 {spec.MinRange.AsInt}–{spec.Range.AsInt}   {status}";
        if (magazine.ReloadRemaining > 0 && magazine.ReloadTotal > 0) {
          var percent = 100 * (magazine.ReloadTotal - magazine.ReloadRemaining) / magazine.ReloadTotal;
          detail += $"   재장전 {percent}%";
        }
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
          UnitCommandEntries(frame);
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
        var placeSpec = frame.FindAsset(input.Placing);
        status = placeSpec != null && placeSpec.RequiresNeutralOutpost
          ? "건설: 점령한 중립 거점·암시장 18m 이내(녹색 구역)를 누르세요"
          : "건설할 곳을 누르세요";
        showPanel = true;
      }
      // 제목줄이 비면 지금 메뉴 이름(빈 청록 막대 방지).
      _status.text = status.Length > 0 ? status : MenuTitle();
      var limit = _menu == Menu.Build ? _context.Length : ContextSlots;
      if (Entries.Count > limit) {
        Entries.RemoveRange(limit, Entries.Count - limit);
      }
      // 쓰는 줄 수만큼만 높인다(명령이 적으면 전장을 덜 가린다).
      var rows = Mathf.Max(1, Mathf.CeilToInt(Entries.Count / (float)ContextColumns));
      var panelHeight = HeaderHeight + 2 * Pad + rows * Cell + (rows - 1) * Gap;
      ((RectTransform)_contextPanel.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelHeight);
      _tooltip.anchoredPosition = new Vector2(-12, 8 + panelHeight + Gap);
      _contextPanel.SetActive(showPanel && (Entries.Count > 0 || input.IsPlacing));
      for (var i = 0; i < _context.Length; i++) {
        // 빈 칸(자리 맞춤용)은 그리지 않는다.
        var used = i < Entries.Count && string.IsNullOrEmpty(Entries[i].Label) == false;
        _context[i].gameObject.SetActive(used);
        if (used) {
          Ui.SetLabel(_context[i], Entries[i].Label);
          var icon = Entries[i].Icon;
          Ui.SetIcon(_context[i], icon == null ? null : icon.StartsWith("pmc_") ? UiCatalog.SpriteFor(icon) ?? Ui.IconSprite("ui_rally") : Ui.IconSprite(icon));
          Ui.SetLocked(_context[i], Entries[i].Enabled == false, Entries[i].Highlight);
          Ui.SetOverlay(_context[i], Entries[i].Overlay != null ? UiCatalog.SpriteFor(Entries[i].Overlay) : null);
        }
      }
    }

    string MenuTitle() {
      switch (_menu) {
        case Menu.Build: return "건설";
        case Menu.Research: return "연구";
        case Menu.HqResearch: return "사령부 연구";
        case Menu.Hangar: return "격납고";
        case Menu.Factory: return "공장";
        default: return "명령";
      }
    }

    void UnitCommandEntries(Frame frame) {
      var input = Controller.Input;
      var selectedUnits = input.Selection.Units.ToArray();
      // 수송차량(물류 트럭/지상 수송 차량)이 선택되면 '자원채취' 버튼을 맨 앞에 배치해 가장 가까운 적치 자원지 수거 -> 사령부 납품을 실행한다.
      var pickupTrucks = selectedUnits.Where(e => {
        if (!frame.Exists(e) || !frame.Has<Unit>(e)) return false;
        var s = frame.FindAsset(frame.Get<Unit>(e).Spec);
        return !s.IsAir && !s.IsNaval && (s.name == "merc_cargo_truck" || s.name.Contains("truck") || s.TransportCapacity > 0 || frame.Has<LogisticsTruck>(e));
      }).ToArray();
      if (pickupTrucks.Length > 0) {
        Entries.Add(new ContextEntry {
          Label = "자원채취",
          Icon = "ui_rally",
          Enabled = true,
          Highlight = true,
          Detail = "가장 가까운 적치 자원지로 이동해 자원을 싣고 사령부로 운반합니다.",
          Action = () => {
            Controller.Send(new GatherCommand { Units = pickupTrucks, Node = EntityRef.None });
            ShowAlert("수송차량 자원채취 출동!");
          }
        });
      }
      // 선택한 수송 유닛이 있으면 우측 하단 엄지 바 맨 앞에 '하차 (전원)' 버튼을 배치한다.
      var transports = selectedUnits.Where(e => frame.Exists(e) && frame.Has<Unit>(e) && frame.FindAsset(frame.Get<Unit>(e).Spec).TransportCapacity > 0).ToArray();
      if (transports.Length > 0) {
        var totalUsed = transports.Sum(t => TransportSystem.UsedSlots(frame, t));
        Entries.Add(new ContextEntry {
          Label = totalUsed > 0 ? $"하차 (전원 {totalUsed})" : "하차 (비어있음)",
          Icon = "ui_garrison",
          Enabled = totalUsed > 0,
          Detail = "수송기 후면 램프를 열고 탑승 중인 아군 전원을 겹침 없이 하차",
          Action = () => {
            foreach (var t in transports) {
              Controller.Send(new UnloadTransportCommand { Transport = t, SpecificUnit = EntityRef.None });
            }
            ShowAlert("탑승 부대 전원 하차!");
          }
        });
      }
      // 모바일 엄지 전술 명령 클러스터(핵심 4종 우선 배치: 공격 이동 · 대형 정렬 · 산개 · 정지).
      Entries.Add(new ContextEntry { Label = input.Mode == MatchInput.TapMode.AttackMove ? "공격 이동 중" : "공격 이동", Icon = "ui_attack_move", Enabled = true,
        Detail = "땅을 누르면 가는 길의 적을 공격하며 자동 정렬 진격",
        Action = () => input.Mode = input.Mode == MatchInput.TapMode.AttackMove ? MatchInput.TapMode.None : MatchInput.TapMode.AttackMove });
      Entries.Add(new ContextEntry { Label = "대형 정렬", Icon = "ui_group", Enabled = selectedUnits.Length > 0,
        Detail = "전차 앞열·소총수 중간·저격/지원 뒷열로 즉시 전술 대형 정렬",
        Action = () => {
          Controller.Send(new MoveUnitsCommand { Units = selectedUnits, FormationMode = MoveUnitsCommand.ModeFormUp });
          ShowAlert("전술 대형 정렬 완료");
        } });
      Entries.Add(new ContextEntry { Label = "산개", Icon = "ui_box", Enabled = selectedUnits.Length > 0,
        Detail = "포격·폭발 회피를 위해 바깥쪽으로 빠르게 산개",
        Action = () => {
          Controller.Send(new MoveUnitsCommand { Units = selectedUnits, FormationMode = MoveUnitsCommand.ModeSpread });
          ShowAlert("유닛 긴급 산개!");
        } });
      Entries.Add(new ContextEntry { Label = "정지", Icon = "ui_hold", Enabled = true, Detail = "제자리에서 위치 고수 및 사격",
        Action = () => Controller.Send(input.Hold(Controller.Frame)) });
      Entries.Add(new ContextEntry { Label = input.Mode == MatchInput.TapMode.Patrol ? "순찰 지정 중" : "순찰", Icon = "ui_patrol", Enabled = true,
        Detail = "지금 위치와 누른 곳 사이를 오가며 적을 공격",
        Action = () => input.Mode = input.Mode == MatchInput.TapMode.Patrol ? MatchInput.TapMode.None : MatchInput.TapMode.Patrol });
      Entries.Add(new ContextEntry { Label = "이동 중단", Icon = "ui_stop", Enabled = true, Detail = "하던 일을 완전히 멈춥니다", Action = () => Controller.Stop() });
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
      // Task 39: the PMC camp panel (diplomacy stage, message, cargo dispatch, shop with shared stock) on camp maps.
      var camp = MercenaryCamp.Spec(frame);
      if (camp != null) {
        return CampEntries(frame, state, trader, camp, index);
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

    /// <summary>Task 39 fix 5: shop line → PMC icon (Codex kit). Helicopters have no icon in the kit yet → generic.</summary>
    static string PmcIcon(Frame frame, MercOffer line) {
      if (line.Unit.IsValid == false) return "pmc_supply_trade";
      var name = frame.FindAsset(line.Unit).name;
      if (name.Contains("drone")) return "pmc_attack_drone";
      if (name.Contains("operator")) return "pmc_operator_squad";
      if (name.Contains("marksman")) return "pmc_marksman";
      if (name.Contains("tank")) return "pmc_tank";
      if (name.Contains("suv") || name.Contains("mrap") || name.Contains("armor")) return "pmc_armoured_suv";
      return "ui_rally";
    }

    /// <summary>Task 39: PMC camp panel. Stage (적대 → 접촉 → 합의), message, cargo truck, then the shop lines from MercenarySpec.</summary>
    string CampEntries(Frame frame, PlayerState* state, NpcTrader trader, MercenarySpec camp, int index) {
      var bit = index >= 0 && index < 4 ? 1 << index : 0;
      var stock = state->Stock;
      var contacted = (trader.ContactMask & bit) != 0;
      var agreed = (trader.AgreementMask & bit) != 0;
      var waiting = bit != 0 && trader.ReplyFrames[index] > 0;
      var truck = bit != 0 && frame.Exists(trader.Trucks[index]);
      if (agreed == false) {
        Entries.Add(new ContextEntry {
          Label = waiting ? "외교 메시지\n답신 대기" : contacted ? "외교 메시지\n답신 받음" : "외교 메시지\n보내기", Icon = "pmc_diplomacy",
          Overlay = waiting ? "pmc_state_cooldown" : null,
          Enabled = contacted == false && waiting == false, Locked = contacted || waiting, Reason = "이미 보냈습니다",
          Detail = "용병단에 통행·거래를 요청합니다(무료)", Action = () => Controller.Send(new TradeCommand { Offer = MercenaryCamp.OfferMessage }),
        });
        var canSend = contacted && truck == false && stock.Covers(camp.Cargo);
        Entries.Add(new ContextEntry {
          Label = truck ? "교역 물자\n수송 중" : $"교역 물자 발송\n{Texts.Cost(camp.Cargo, stock)}", Icon = "pmc_cargo_truck",
          Overlay = contacted == false ? "pmc_state_locked" : truck ? "pmc_state_cooldown" : null,
          Enabled = canSend, Locked = canSend == false, Reason = contacted == false ? "먼저 메시지를 보내세요" : truck ? "트럭이 가는 중입니다" : "자원이 부족합니다",
          Detail = "트럭이 정문까지 스스로 갑니다. 파괴되면 교역 실패(물자 일부가 상자로 떨어짐)",
          Action = () => Controller.Send(new TradeCommand { Offer = MercenaryCamp.OfferDispatch }),
        });
        return truck ? "용병단: 트럭 호송 중 — 도착하면 합의" : contacted ? "용병단: 교역 물자를 보내면 통행을 허가한다" : "용병단 기지 — 합의 없이 들어오면 경비대가 공격";
      }
      for (var i = 0; camp.Offers != null && i < camp.Offers.Length && i < MercenaryCamp.MaxOffers; i++) {
        var line = camp.Offers[i];
        var left = trader.Stock[i];
        var ready = trader.ReadyFrames[index * MercenaryCamp.MaxOffers + i];
        var wait = Mathf.Max(0, ready - frame.Number) / Mathf.Max(1, frame.SessionConfig.UpdateFPS);
        var ok = left > 0 && wait == 0 && stock.Covers(line.Cost);
        var offer = MercenaryCamp.OfferShop + i;
        Entries.Add(new ContextEntry {
          Label = wait > 0 ? $"{line.Name}\n대기 {wait}초" : $"{line.Name} ({left})\n{Texts.Cost(line.Cost, stock)}", Icon = PmcIcon(frame, line),
          Overlay = left <= 0 ? "pmc_state_sold_out" : wait > 0 ? "pmc_state_cooldown" : null,
          Enabled = ok, Locked = ok == false, Reason = left <= 0 ? "재고가 없습니다(공유 재고, 시간이 지나면 채워짐)" : wait > 0 ? "다음 구매까지 기다리세요" : "자원이 부족합니다",
          Detail = line.Unit.IsValid ? "정문에서 인도(내 부대)" : "자원 교환", Action = () => Controller.Send(new TradeCommand { Offer = offer }),
        });
      }
      return "용병 거래소 — 합의됨(공격하면 합의 취소)";
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

    public const int ExtractionBuildMenu = 3;

    /// <summary>건설 하위 메뉴: 방어·지원, 병기 공장, 산업·보급, 자원 채굴.</summary>
    public static bool InBuildMenu(BuildingSpec spec, int menu) {
      if (menu == ExtractionBuildMenu || spec.RequiresResourceNode) {
        return menu == ExtractionBuildMenu && spec.RequiresResourceNode;
      }
      if (menu == 0) {
        return spec.Defensive || spec.Role == BuildingRole.MedicTent || spec.Role == BuildingRole.RepairBay
          || spec.Role == BuildingRole.Radar || spec.Role == BuildingRole.Artillery;
      }
      if (menu == 1) {
        return spec.Role == BuildingRole.Barracks || spec.Role == BuildingRole.VehicleFactory || spec.Role == BuildingRole.HeavyWorks
          || spec.Role == BuildingRole.Airfield || spec.Role == BuildingRole.UltimateSite || spec.Role == BuildingRole.Shipyard
          || spec.Role == BuildingRole.Helipad;
      }
      return spec.Role == BuildingRole.Processor || spec.Role == BuildingRole.PowerPlant || spec.Role == BuildingRole.ResearchLab
        || spec.Role == BuildingRole.SupplyDepot || spec.Role == BuildingRole.CollectionYard || spec.Role == BuildingRole.Headquarters;
    }

    string EngineerEntries(Frame frame, PlayerState* state) {
      var faction = frame.FindAsset(state->Faction);
      var tier = TechSystem.Tier(frame, Controller.Player);
      if (_menu == Menu.Build) {
        foreach (var buildingRef in faction.Buildings) {
          var spec = frame.FindAsset(buildingRef);
          if (InBuildMenu(spec, _menuTier) == false) {
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
        var menuLabel = _menuTier switch {
          1 => "병기 공장",
          2 => "산업·보급",
          ExtractionBuildMenu => "자원 채굴",
          _ => "방어·지원",
        };
        return $"건설: {menuLabel}";
      }
      Entries.Add(new ContextEntry {
        Label = "병기 공장", Icon = "ui_build1", Enabled = true,
        Detail = "보병 무장 · 기동·화력 · 중기갑 전차 · 항공·드론 조립 공장",
        Action = () => { _menu = Menu.Build; _menuTier = 1; },
      });
      Entries.Add(new ContextEntry {
        Label = "산업·보급", Icon = "ui_build2", Enabled = true,
        Detail = "강철 제련소 · 터빈 발전소 · 군수·연료 보급창 · 군수 연구 공장 · 사령부",
        Action = () => { _menu = Menu.Build; _menuTier = 2; },
      });
      Entries.Add(new ContextEntry {
        Label = "방어·지원", Icon = "ui_hold", Enabled = true,
        Detail = "방어탑 · 방공포대 · 광역 요격 포대 · 벙커 · 레이더 · 의무/정비",
        Action = () => { _menu = Menu.Build; _menuTier = 0; },
      });
      PadTo(ContextColumns + (Entries.Count > ContextColumns ? ContextColumns : 0));
      Entries.Add(new ContextEntry { Label = "정지", Icon = "ui_stop", Enabled = true, Action = () => Controller.Stop() });
      if (faction.Buildings.Any(reference => frame.FindAsset(reference).RequiresResourceNode)) {
        Entries.Add(new ContextEntry {
          Label = "자원 채굴", Icon = "ui_build2", Enabled = true,
          Detail = "제재소·철 채굴 공장을 자원 노드 위에 건설합니다.",
          Action = () => { _menu = Menu.Build; _menuTier = ExtractionBuildMenu; },
        });
      }
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
      if (spec.Role == BuildingRole.UltimateSite && ProductionSystem.CountRole(frame, Controller.Player, BuildingRole.UltimateSite) >= 1) {
        return "보유 중 (1/1)";
      }
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

    static int FactoryUpgradeBonus(Frame frame, int level) {
      if (level <= 1) return 0;
      var rules = FactoryWarsUtil.Rules(frame);
      var bonus = level == 2 ? rules.FactoryLv2SpeedBonus : rules.FactoryLv3SpeedBonus;
      return bonus > FP._0 ? Mathf.RoundToInt(bonus.AsFloat * 100f) : level == 2 ? 25 : 50;
    }

    string FactoryEntries(Frame frame, PlayerState* state, EntityRef entity, Building* building, BuildingSpec spec, int level) {
      var status = $"공장 관리 · Lv. {level}";
      var production = FactorySystem.IsProductionFactory(spec.Role);
      if (level < 3) {
        var next = level + 1;
        var cost = FactorySystem.UpgradeCost(frame, next);
        var stock = state->Stock;
        var affordable = stock.Covers(cost);
        var addition = next == 2 ? "측면 정비동" : "산업 굴뚝";
        var effect = production ? $" · 기본 대비 생산 +{FactoryUpgradeBonus(frame, next)}%" : "";
        Entries.Add(new ContextEntry {
          Label = $"Lv. {next} 증축\n{Texts.Cost(cost, stock, "\n")}", Icon = "ui_build2",
          Enabled = affordable, Locked = !affordable, Highlight = true,
          Reason = affordable ? null : "자원 부족",
          Detail = $"{addition} 추가{effect}",
          Action = () => Controller.Send(new UpgradeFactoryCommand { Building = entity }),
        });
      } else {
        status += " · 최대 단계";
      }
      if (production) {
        if (FactorySystem.HasAdjacencySynergy(frame, entity, building)) {
          var configuredSynergy = FactoryWarsUtil.Rules(frame).FactorySynergyBonus;
          var synergy = configuredSynergy > FP._0 ? Mathf.RoundToInt(configuredSynergy.AsFloat * 100f) : 15;
          status += $" · 시너지 +{synergy}%";
        }
        var active = FactorySystem.IsOverdriving(frame, building);
        var fps = (float)frame.SessionConfig.UpdateFPS;
        var cooldown = Mathf.Max(0, Mathf.CeilToInt((building->OverdriveCooldownFrame - frame.Number) / fps));
        var remaining = Mathf.Max(0, Mathf.CeilToInt((building->OverdriveUntilFrame - frame.Number) / fps));
        var configuredBonus = FactoryWarsUtil.Rules(frame).OverdriveSpeedBonus;
        var bonus = configuredBonus > FP._0 ? Mathf.RoundToInt(configuredBonus.AsFloat * 100f) : 50;
        Entries.Add(new ContextEntry {
          Label = active ? $"과부하 중지\n{remaining}초" : cooldown > 0 ? $"과부하 대기\n{cooldown}초" : $"과부하\n생산 +{bonus}%",
          Icon = "ui_attack", Enabled = active || cooldown == 0, Locked = !active && cooldown > 0,
          Reason = cooldown > 0 && !active ? $"과부하 재사용까지 {cooldown}초" : null,
          Detail = $"일시적으로 생산 +{bonus}%. 전력을 더 소비하고 시설이 과열됩니다.",
          Action = () => Controller.Send(new OverdriveFactoryCommand { Building = entity }),
        });
      }
      PadTo(ContextSlots - 1);
      Entries.Add(new ContextEntry { Label = "뒤로", Icon = "ui_back", Enabled = true, Action = () => _menu = Menu.Root });
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
        if (AirfieldWorkSystem.InPavingPhase(frame, entity, &building, spec, out _)) {
          return $"{AirfieldWorkName(spec.Role)} 공사 중 · 생산 잠김";
        }
        return status + "  건설 중";
      }
      var tier = TechSystem.Tier(frame, Controller.Player);
      if (FactorySystem.IsUpgradeableFactory(spec.Role)) {
        var level = FactorySystem.LevelOf(&building);
        if (_menu == Menu.Factory) {
          return FactoryEntries(frame, state, entity, &building, spec, level);
        }
        if (_menu == Menu.Root) {
          Entries.Add(new ContextEntry {
            Label = $"공장 관리\nLv. {level}", Icon = "ui_build2", Enabled = true, Highlight = true,
            Detail = "공장 증축과 과부하 상태를 확인합니다.", Action = () => _menu = Menu.Factory,
          });
        }
      }
      if (spec.Role == BuildingRole.ResearchLab) {
        return ResearchEntries(frame, state, entity, building, tier, false);
      }
      if (spec.Role == BuildingRole.Headquarters && (_menu == Menu.HqResearch || _menu == Menu.Research)) {
        return ResearchEntries(frame, state, entity, building, tier, true);
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
      var isWorkerSite = spec.RequiresResourceNode || spec.Role == BuildingRole.PowerPlant;
      var capacity = isWorkerSite ? spec.WorkerSlotCapacity : spec.GarrisonCapacity;
      if (capacity > 0) {
        var inside = 0;
        foreach (var pair in frame.GetComponentIterator<Unit>()) {
          inside += pair.Component.Garrison == entity ? 1 : 0;
        }
        Entries.Add(new ContextEntry {
          Label = isWorkerSite ? $"작업자 빼기\n{inside}/{capacity}" : $"내보내기\n{inside}/{capacity}",
          Icon = "ui_rally_clear", Enabled = inside > 0, Locked = false,
          Detail = isWorkerSite ? "배치된 작업자를 한 명 내보냅니다. 최대 3명까지 배치할 수 있습니다."
            : "보병 분대를 고르고 벙커를 누르면 들어갑니다. 안에서는 맞지 않고, 벙커가 부서지면 튕겨 나옵니다",
          Action = () => Controller.Send(new ExitGarrisonCommand { Building = entity }),
        });
        status += isWorkerSite ? $"  작업자 {inside}/{capacity}" : $"  수용 {inside}/{capacity}";
      }

      var stock = state->Stock;
      foreach (var unitRef in spec.Producible ?? Array.Empty<AssetRef<UnitSpec>>()) {
        var unit = frame.FindAsset(unitRef);
        if (unit.RequiredTier >= tier + 2) {
          continue;
        }
        var target = unitRef;
        var reason = UnitLock(frame, unit);
        // Task 46: canal shipyards cannot build warships (low canal bridges); say so on the disabled button.
        if (reason == null && frame.TryGet<Transform3D>(entity, out var yardAt) && SeaGateSystem.TooTallForYard(frame, yardAt.Position, unit)) {
          reason = Texts.CanalYard;
        }
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
        Entries.Add(new ContextEntry {
          Label = "유닛 연구\nMk.2·Mk.3", Icon = "ui_research", Enabled = true,
          Detail = "보병·기갑·공군 Mk.2/Mk.3 진화 및 진영 특화 업그레이드",
          Action = () => _menu = Menu.HqResearch,
        });
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

    static string AirfieldWorkName(BuildingRole role) => role == BuildingRole.Helipad ? "착륙장" : "활주로";

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

    string ResearchEntries(Frame frame, PlayerState* state, EntityRef entity, Building building, int tier, bool fromHq = false) {
      var faction = frame.FindAsset(state->Faction);
      var list = faction.Researches ?? Array.Empty<AssetRef<ResearchSpec>>();
      var status = fromHq ? "사령부 연구" : "연구소";
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
            Entries.Add(new ContextEntry { Label = $"{Texts.SizeWord(_menuSize)} {(kind == ResearchKind.Attack ? "공격" : "방어")}\nMk.3 완료", Icon = kind == ResearchKind.Attack ? "ui_attack" : "ui_armor", Enabled = false, Locked = true, Reason = "모든 단계를 끝냈습니다" });
            continue;
          }
          AddResearch(frame, state, entity, building, list, index, tier);
        }
        PadTo(ContextSlots - 1);
        Entries.Add(new ContextEntry { Label = "뒤로", Icon = "ui_back", Enabled = true, Action = () => _menu = fromHq ? Menu.HqResearch : Menu.Root });
        return $"{Texts.SizeWord(_menuSize)} Mk 업그레이드";
      }
      foreach (var size in new[] { UnitSize.Small, UnitSize.Medium, UnitSize.Large }) {
        var specs = list.Select(r => frame.FindAsset(r)).Where(r => (r.Kind == ResearchKind.Attack || r.Kind == ResearchKind.Armor) && r.Size == size).ToList();
        if (specs.Count == 0 || specs.Min(r => r.RequiredTier) >= tier + 2) {
          continue;
        }
        var value = size;
        var minTier = specs.Min(r => r.RequiredTier);
        Entries.Add(new ContextEntry {
          Label = $"{Texts.SizeWord(size)} 연구", Icon = "ui_research", Enabled = true,
          Detail = minTier > tier ? Texts.TierLock(minTier) : $"{Texts.SizeWord(size)} 유닛 Mk.2/Mk.3 공격·방어 진화",
          Action = () => {
            _menu = Menu.Research;
            _menuSize = value;
          },
        });
      }
      for (var i = 0; i < list.Length; i++) {
        var r = frame.FindAsset(list[i]);
        if ((r.Kind == ResearchKind.Elite || r.Kind == ResearchKind.Special) && r.RequiredTier < tier + 2) {
          AddResearch(frame, state, entity, building, list, i, tier);
        }
      }
      if (fromHq) {
        if (Entries.Count < ContextSlots) {
          PadTo(ContextSlots - 1);
        }
        Entries.Add(new ContextEntry { Label = "뒤로", Icon = "ui_back", Enabled = true, Action = () => _menu = Menu.Root });
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
      var extra = research.Kind == ResearchKind.Special ? "\n" + Texts.SpecialDetail(research.Special) : "";
      Entries.Add(new ContextEntry {
        Label = $"{Texts.ResearchName(research, UnitName)}\n{(reason == null ? Texts.Cost(research.Cost, state->Stock) : reason)}",
        Icon = research.Kind == ResearchKind.Elite ? "ui_elite" : research.Kind == ResearchKind.Special ? "ui_upgrade" : research.Kind == ResearchKind.Attack ? "ui_attack" : "ui_armor",
        Enabled = reason == null,
        Locked = reason != null,
        Reason = reason != null ? $"{Texts.ResearchName(research, UnitName)}: {reason}" : null,
        Detail = $"{Texts.ResearchName(research, UnitName)}: {Texts.Cost(research.Cost, state->Stock)}  {research.Seconds.AsInt}초{extra}",
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
