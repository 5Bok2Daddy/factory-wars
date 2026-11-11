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

  /// <summary>우하단 패널의 버튼 하나.</summary>
  public struct ContextEntry {
    public string Label;
    public bool Enabled;
    public Action Action;
    public string Icon;
  }

  /// <summary>대전 화면의 HUD. 시뮬레이션은 읽기만 하고 행동은 커맨드로 보낸다.</summary>
  public unsafe class MatchHud : MonoBehaviour {
    const float RefreshSeconds = 0.2f;
    const float AlertSeconds = 3f;
    const float NoteSeconds = 3f;
    // 29 2-5: 4열 3행 12슬롯 단일 화면 그리드 패널 (다음 페이지 넘김 버튼 제거, 화면 1/3 이하 보장, 9mm 규격)
    const int ContextSlots = 12;
    const int ContextColumns = 4;

    public readonly List<ContextEntry> Entries = new List<ContextEntry>();
    public readonly List<string> NoteInbox = new List<string>();

    public MatchController Controller;
    public bool BoxMode { get; private set; }
    public int ContextPage { get; private set; }
    /// <summary>4x3 12칸 단일 화면 구성으로 다음 쪽 버튼 없이 전 항목이 한눈에 보인다.</summary>
    public int ContextPages => 1;
    public string TopBarText { get; private set; } = "";
    public int ResourceIcons => _resources.Count(r => r.Icon.sprite != null);
    public string AlertText => _alert.text;
    public string NoteText => _note.text;
    public string TruceText => _truce.text;
    public bool DiplomacyOpen => _diplomacy.gameObject.activeSelf;
    public RectTransform Root { get; private set; }

    static readonly (string Icon, float X, float Width)[] ResourceSlots = {
      ("res_iron", 12, 200), ("res_wood", 220, 200), ("res_steel", 428, 200), ("res_power", 636, 500), ("res_population", 1144, 220),
    };

    readonly List<(Image Icon, Text Value)> _resources = new List<(Image, Text)>();
    RectTransform _alertBubble;
    RectTransform _noteBubble;
    Text _alert;
    Text _note;
    Text _status;
    Text _truce;
    Minimap _minimap;
    GameObject _contextPanel;
    Button[] _context;
    Button _boxButton;
    Button _saveButton;
    Button[] _groupButtons;
    bool _saveMode;
    RectTransform _diplomacy;
    Text _inbox;
    readonly List<(PlayerRef Player, Text Name, Button Propose, Button Accept, Button Reject, Button Break, Button Note)> _rows =
      new List<(PlayerRef, Text, Button, Button, Button, Button, Button)>();
    int _phrase;
    float _nextRefresh;
    float _alertUntil;
    float _noteUntil;

    public void Build(Frame frame) {
      var canvas = Ui.CreateCanvas("MatchHud");
      canvas.transform.SetParent(transform, false);
      Root = Ui.SafeArea(canvas);

      var top = Ui.Rect("TopBar", Root);
      top.anchorMin = new Vector2(0, 1);
      top.anchorMax = Vector2.one;
      top.pivot = new Vector2(0.5f, 1);
      top.sizeDelta = new Vector2(0, 166);
      _resources.Clear();
      foreach (var (icon, x, width) in ResourceSlots) {
        var capsule = Ui.Capsule($"{icon}_capsule", top, new Color(0.96f, 0.98f, 1f, 0.95f));
        Ui.Place(capsule, new Vector2(0, 0.5f), new Vector2(x, -6), new Vector2(width, 56));
        var image = Ui.Icon(icon, capsule, icon, new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(44, 44));
        var value = Ui.Label($"{icon}_value", capsule, "", 30);
        value.color = new Color(0.16f, 0.20f, 0.30f, 1f);
        Ui.Place(value.rectTransform, new Vector2(0, 0.5f), new Vector2(54, 0), new Vector2(width - 58, 48));
        _resources.Add((image, value));
      }

      var diplomacyButton = Ui.Button("DiplomacyButton", top, "외교", ToggleDiplomacy);
      Ui.Place((RectTransform)diplomacyButton.transform, Vector2.one, new Vector2(-180, -6), new Vector2(160, 154));
      var menuButton = Ui.Button("MenuButton", top, "메뉴", () => Controller.TogglePause());
      Ui.Place((RectTransform)menuButton.transform, Vector2.one, new Vector2(-12, -6), new Vector2(160, 154));

      // 중앙 7분 휴전 타이머 캡슐 배지
      var truceBadge = Ui.Capsule("TruceBadge", top, new Color(0.96f, 0.98f, 1f, 0.95f));
      Ui.Place(truceBadge, new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(360, 56));
      _truce = Ui.Label("Truce", truceBadge, "", 28, TextAnchor.MiddleCenter);
      Ui.Stretch(_truce.rectTransform);
      _truce.color = new Color(0.16f, 0.20f, 0.30f, 1f);

      // 알림·쪽지: 둥근 말풍선 팝 캡슐
      _alertBubble = Ui.Capsule("AlertBubble", Root, new Color(0.96f, 0.98f, 1f, 0.95f));
      Ui.Place(_alertBubble, new Vector2(0.5f, 1), new Vector2(0, -136), new Vector2(800, 56));
      _alert = Ui.Label("Alert", _alertBubble, "", 32, TextAnchor.MiddleCenter);
      Ui.Stretch(_alert.rectTransform, 4f);
      _alert.color = new Color(0.16f, 0.20f, 0.30f, 1f);
      _alertBubble.gameObject.SetActive(false);

      _noteBubble = Ui.Capsule("NoteBubble", Root, new Color(0.96f, 0.98f, 1f, 0.95f));
      Ui.Place(_noteBubble, new Vector2(0.5f, 1), new Vector2(0, -198), new Vector2(800, 56));
      _note = Ui.Label("Note", _noteBubble, "", 30, TextAnchor.MiddleCenter);
      Ui.Stretch(_note.rectTransform, 4f);
      _note.color = new Color(0.16f, 0.20f, 0.30f, 1f);
      _noteBubble.gameObject.SetActive(false);

      // 왼쪽 아래 미니맵: 둥근 프레임 + 미니맵 (Kenney 둥근 팝 UI).
      var mapFrame = Ui.Panel("MinimapFrame", Root, new Color(0.96f, 0.98f, 1f, 0.92f));
      Ui.Place(mapFrame, Vector2.zero, new Vector2(8, 8), new Vector2(308, 308));
      var map = Ui.Rect("Minimap", mapFrame);
      Ui.Place(map, Vector2.zero, new Vector2(4, 4), new Vector2(300, 300));
      map.gameObject.AddComponent<RawImage>();
      _minimap = map.gameObject.AddComponent<Minimap>();
      _minimap.Init(frame.FindAsset<MapSpec>(frame.Map.UserAsset));
      _minimap.OnPick = world => Controller.Camera.MoveTo(world);

      // 왼쪽 아래 미니맵 오른쪽: 선택·부대 버튼 (동글동글한 팝 UI, 9mm 이상 규격, 2x3 배치로 태블릿/모바일 어떤 화면비에서도 ContextPanel과 겹침 없음).
      var actions = Ui.Rect("Actions", Root);
      Ui.Place(actions, Vector2.zero, new Vector2(324, 12), new Vector2(2 * 154 + 10, 3 * 154 + 20));
      var grid = actions.gameObject.AddComponent<GridLayoutGroup>();
      grid.cellSize = new Vector2(154, 154);
      grid.spacing = new Vector2(10, 10);
      grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
      grid.constraintCount = 2;
      _boxButton = Ui.Button("BoxButton", actions, "범위\n선택", ToggleBoxMode);
      Ui.Button("StopButton", actions, "정지", () => Controller.Stop());
      _saveButton = Ui.Button("SaveButton", actions, "부대\n지정", () => {
        _saveMode = _saveMode == false;
        Ui.SetLabel(_saveButton, _saveMode ? "번호를\n누르세요" : "부대\n지정");
      });
      _groupButtons = new Button[SelectionModel.GroupCount];
      for (var i = 0; i < SelectionModel.GroupCount; i++) {
        var index = i;
        _groupButtons[i] = Ui.Button($"Group{i + 1}", actions, $"{i + 1}부대", () => PressGroup(index));
      }

      // 오른쪽 아래: 한눈에 보기 건설·생산 패널 (화이트/파스텔 톤 둥근 패널, 4x3 12슬롯 단일 화면 그리드, 다음 쪽 버튼 없이 전 항목 노출, 1/3 초과 금지).
      var panel = Ui.Panel("ContextPanel", Root, new Color(0.96f, 0.98f, 1f, 0.92f));
      _contextPanel = panel.gameObject;
      const float cellW = 154f;
      const float cellH = 154f;
      const float space = 8f;
      const float padX = 24f;
      const float padY = 64f;
      var totalW = ContextColumns * cellW + (ContextColumns - 1) * space + padX;
      var totalH = 3 * cellH + 2 * space + padY;
      Ui.Place(panel, new Vector2(1, 0), new Vector2(-12, 12), new Vector2(totalW, totalH));
      _status = Ui.Label("Status", panel, "", 28);
      _status.color = new Color(0.16f, 0.20f, 0.30f, 1f);
      _status.rectTransform.anchorMin = new Vector2(0, 1);
      _status.rectTransform.anchorMax = Vector2.one;
      _status.rectTransform.pivot = new Vector2(0.5f, 1);
      _status.rectTransform.sizeDelta = new Vector2(-24, 46);
      _status.rectTransform.anchoredPosition = new Vector2(0, -6);
      var slots = Ui.Rect("Slots", panel);
      Ui.Place(slots, Vector2.zero, new Vector2(12, 12), new Vector2(totalW - padX, totalH - padY));
      var slotGrid = slots.gameObject.AddComponent<GridLayoutGroup>();
      slotGrid.cellSize = new Vector2(cellW, cellH);
      slotGrid.spacing = new Vector2(space, space);
      slotGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
      slotGrid.constraintCount = ContextColumns;
      _context = new Button[ContextSlots];
      for (var i = 0; i < ContextSlots; i++) {
        var index = i;
        _context[i] = Ui.Button($"Context{i}", slots, "", () => PressContext(index), 22);
        ((RectTransform)_context[i].transform).sizeDelta = new Vector2(cellW, cellH);
      }

      BuildDiplomacy(frame);

      QuantumEvent.Subscribe<EventActionRejected>(this, OnRejected);
      QuantumEvent.Subscribe<EventNoteReceived>(this, OnNote);
      QuantumEvent.Subscribe<EventPowerOutage>(this, OnOutage);
      QuantumEvent.Subscribe<EventBarrierOpened>(this, e => ShowAlert(Texts.BarrierAlert));
      QuantumEvent.Subscribe<EventCaptureChanged>(this, OnCapture);
      QuantumEvent.Subscribe<EventSupplyDropIncoming>(this, OnDropIncoming);
      QuantumEvent.Subscribe<EventBarricadeDestroyed>(this, OnBarricade);
      QuantumEvent.Subscribe<EventBaseUnderAttack>(this, OnBaseAttacked);
      QuantumEvent.Subscribe<EventCratePicked>(this, OnCratePicked);
    }

    void OnBaseAttacked(EventBaseUnderAttack e) {
      if (e.Player == Controller.Player) {
        ShowAlert(Texts.BaseAttacked);
        _minimap.Ping(e.Position.ToUnityVector3());
      }
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
        ShowAlert(Texts.Captured(kind));
        _minimap.Ping(position);
      } else if (e.Previous == Controller.Player) {
        ShowAlert(Texts.Lost(kind));
        _minimap.Ping(position);
      }
    }

    void OnDropIncoming(EventSupplyDropIncoming e) {
      ShowAlert(Texts.DropIncoming);
      var spots = MapOf(e.Game)?.CrateSpots;
      for (var i = 0; spots != null && i < spots.Length; i++) {
        if (spots[i].Seat < 0 && spots[i].Group == e.Group) {
          _minimap.Ping(spots[i].Position.ToUnityVector3());
        }
      }
    }

    void OnBarricade(EventBarricadeDestroyed e) {
      ShowAlert(Texts.BackdoorOpened);
      var barricades = MapOf(e.Game)?.Barricades;
      if (barricades != null && e.Index < barricades.Length) {
        _minimap.Ping(barricades[e.Index].Shape.Center.ToUnityVector3());
      }
    }

    static MapSpec MapOf(QuantumGame game) {
      var frame = game?.Frames?.Verified;
      return frame?.FindAsset<MapSpec>(frame.Map.UserAsset);
    }

    const float FloatSeconds = 1.4f;
    const float FloatRise = 70f;
    readonly System.Collections.Generic.List<(Text Label, Vector3 World, float Start)> _floats = new System.Collections.Generic.List<(Text, Vector3, float)>();

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
      Ui.SetLabel(_boxButton, BoxMode ? "범위\n선택 중" : "범위\n선택");
    }

    void PressGroup(int index) {
      var selection = Controller.Input.Selection;
      if (_saveMode) {
        selection.SaveGroup(index);
        _saveMode = false;
        Ui.SetLabel(_saveButton, "부대\n지정");
      } else {
        selection.RecallGroup(index);
      }
    }

    /// <summary>패널의 칸 번호로 누른다. 12칸 단일 화면 구성으로 전 항목 직접 실행.</summary>
    public void PressContext(int slot) {
      if (slot < Entries.Count && Entries[slot].Enabled) {
        Entries[slot].Action?.Invoke();
        _nextRefresh = 0f;
      }
    }

    public void ShowAlert(string text) {
      _alert.text = text;
      if (_alertBubble != null) {
        _alertBubble.gameObject.SetActive(!string.IsNullOrEmpty(text));
      }
      _alertUntil = Time.unscaledTime + AlertSeconds;
    }

    void OnRejected(EventActionRejected e) {
      if (e.Player == Controller.Player) {
        ShowAlert(Texts.Rejected(e.Reason, (MissingResource)e.Missing));
      }
    }

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
      }
    }

    void Update() {
      if (_resources.Count == 0) {
        return;
      }
      if (_alert.text.Length > 0 && Time.unscaledTime >= _alertUntil) {
        _alert.text = "";
        if (_alertBubble != null) {
          _alertBubble.gameObject.SetActive(false);
        }
      }
      if (_floats.Count > 0) {
        UpdateFloats();
      }
      if (_note.text.Length > 0 && Time.unscaledTime >= _noteUntil) {
        _note.text = "";
        if (_noteBubble != null) {
          _noteBubble.gameObject.SetActive(false);
        }
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
      _minimap.Refresh(frame);
      var selection = Controller.Input.Selection;
      selection.Prune(frame);
      for (var i = 0; i < _groupButtons.Length; i++) {
        Ui.SetLabel(_groupButtons[i], selection.GroupSize(i) > 0 ? $"{i + 1}부대\n{selection.GroupSize(i)}기" : $"{i + 1}부대");
      }
      RefreshContext(frame, state);
      if (DiplomacyOpen) {
        RefreshDiplomacy(frame);
      }
    }

    void RefreshContext(Frame frame, PlayerState* state) {
      Entries.Clear();
      var input = Controller.Input;
      var selection = input.Selection;
      var status = "";
      var showPanel = false;
      if (selection.Building != EntityRef.None && frame.Has<CityBuilding>(selection.Building)) {
        status = CityEntries(frame, selection.Building);
        showPanel = true;
      } else if (selection.Building != EntityRef.None) {
        status = BuildingEntries(frame, state, selection.Building);
        showPanel = true;
      } else if (selection.Units.Count > 0) {
        var spec = frame.FindAsset(frame.Get<Unit>(selection.Units[0]).Spec);
        status = selection.Units.Count == 1 ? $"{Texts.NameOf(spec.name)}  체력 {frame.Get<Unit>(selection.Units[0]).Health.AsInt}/{spec.MaxHealth.AsInt}" : $"유닛 {selection.Units.Count}기";
        // 29 2-5: 건설 패널은 공병(Worker) 선택 시에만 열림
        if (selection.Units.Any(e => frame.Has<Worker>(e))) {
          BuildEntries(frame, state);
          showPanel = true;
        }
      }
      if (input.IsPlacing) {
        status = "건설할 곳을 누르세요";
        showPanel = true;
      }
      _status.text = status;
      var hasContext = showPanel && (Entries.Count > 0 || input.IsPlacing);
      if (_contextPanel != null) {
        _contextPanel.SetActive(hasContext);
      }
      if (ContextPage >= ContextPages) {
        ContextPage = 0;
      }
      ApplyContextPage();
    }

    void ApplyContextPage() {
      for (var i = 0; i < _context.Length; i++) {
        var used = i < Entries.Count;
        _context[i].gameObject.SetActive(used);
        if (used) {
          Ui.SetLabel(_context[i], Entries[i].Label);
          Ui.SetIcon(_context[i], Entries[i].Icon != null ? Ui.IconSprite(Entries[i].Icon) : null);
          Ui.SetEnabled(_context[i], Entries[i].Enabled);
        }
      }
    }

    void BuildEntries(Frame frame, PlayerState* state) {
      var faction = frame.FindAsset(state->Faction);
      var stock = state->Stock;
      foreach (var buildingRef in faction.Buildings) {
        var spec = frame.FindAsset(buildingRef);
        var unlocked = ProductionSystem.IsUnlocked(frame, Controller.Player, spec);
        var target = buildingRef;
        Entries.Add(new ContextEntry {
          Label = $"{Texts.NameOf(spec.name)}\n{(unlocked ? Texts.Cost(spec.Cost, stock) : "잠김")}",
          Icon = $"building_{spec.name.Substring(3)}",
          Enabled = unlocked,
          Action = () => Controller.Input.Placing = target,
        });
      }
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
      var status = $"{Texts.CityName(placement)}  체력 {city.Health.AsInt}/{city.MaxHealth.AsInt}";
      if (placement.Garrison) {
        status += $"  진지 {mine}";
        Entries.Add(new ContextEntry {
          Label = Texts.ExitGarrison,
          Enabled = mine > 0,
          Action = () => Controller.Send(new ExitGarrisonCommand { Building = entity }),
        });
      }
      return status;
    }

    string BuildingEntries(Frame frame, PlayerState* state, EntityRef entity) {
      var building = frame.Get<Building>(entity);
      var spec = frame.FindAsset(building.Spec);
      var status = $"{Texts.NameOf(spec.name)}  체력 {building.Health.AsInt}/{spec.MaxHealth.AsInt}";
      if (building.ConstructionRemaining > FP._0) {
        return status + $"  건설 {Mathf.CeilToInt(building.ConstructionRemaining.AsFloat)}초 남음";
      }
      if (building.Paused != PauseReason.None) {
        status += "  정전으로 멈춤";
      }

      var stock = state->Stock;
      foreach (var unitRef in spec.Producible ?? Array.Empty<AssetRef<UnitSpec>>()) {
        var unit = frame.FindAsset(unitRef);
        var target = unitRef;
        Entries.Add(new ContextEntry {
          Label = $"{Texts.NameOf(unit.name)}\n{Texts.Cost(unit.Cost, stock)}",
          Icon = unit.name,
          Enabled = true,
          Action = () => Controller.Send(new ProduceCommand { Building = entity, Unit = target, Count = 1 }),
        });
      }
      if (building.QueueCount > 0) {
        var last = building.Queue[building.QueueCount - 1].Unit;
        status += $"  대기열 {building.QueueCount}";
        Entries.Add(new ContextEntry {
          Label = "생산\n취소",
          Enabled = true,
          Action = () => Controller.Send(new ProduceCommand { Building = entity, Unit = last, Count = 1, Cancel = true }),
        });
      }
      if (spec.ProcessSeconds > FP._0) {
        var off = (bool)building.ProcessorOff;
        status += off ? "  꺼짐" : "  변환 중";
        Entries.Add(new ContextEntry {
          Label = off ? "가공소\n켜기" : "가공소\n끄기",
          Enabled = true,
          Action = () => Controller.Send(new SetProcessorCommand { Building = entity, Enabled = off }),
        });
      }
      if (spec.Producible != null && spec.Producible.Length > 0 && spec.Role != BuildingRole.Headquarters) {
        PresetEntries(frame, state, entity, building);
      }
      return status;
    }

    void PresetEntries(Frame frame, PlayerState* state, EntityRef entity, Building building) {
      var modules = frame.FindAsset(state->Faction).Modules ?? Array.Empty<AssetRef<ModuleSpec>>();
      var slots = new[] { building.Preset[0], building.Preset[1], building.Preset[2] };
      for (var i = 0; i < slots.Length; i++) {
        var index = i;
        var current = frame.FindAsset(slots[i]);
        Entries.Add(new ContextEntry {
          Label = $"프리셋 {i + 1}\n{(current != null ? Texts.NameOf(current.name) : "없음")}",
          Enabled = true,
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
