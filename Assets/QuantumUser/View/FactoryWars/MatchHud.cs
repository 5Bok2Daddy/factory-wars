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
    const int ContextSlots = 10;
    const int PagedSlots = ContextSlots - 1;

    public readonly List<ContextEntry> Entries = new List<ContextEntry>();
    public readonly List<string> NoteInbox = new List<string>();

    public MatchController Controller;
    public bool BoxMode { get; private set; }
    public int ContextPage { get; private set; }
    /// <summary>10개까지는 한 쪽에 다 보인다. 넘치면 마지막 칸이 다음 쪽 버튼이 된다.</summary>
    public int ContextPages => Entries.Count <= ContextSlots ? 1 : (Entries.Count + PagedSlots - 1) / PagedSlots;
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
    Text _alert;
    Text _note;
    Text _status;
    Text _truce;
    Minimap _minimap;
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

      var top = Ui.Panel("TopBar", Root);
      top.anchorMin = new Vector2(0, 1);
      top.anchorMax = Vector2.one;
      top.pivot = new Vector2(0.5f, 1);
      top.sizeDelta = new Vector2(0, 76);
      foreach (var (icon, x, width) in ResourceSlots) {
        var image = Ui.Icon(icon, top, icon, new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(60, 60));
        var rect = (RectTransform)image.transform;
        rect.pivot = new Vector2(0, 0.5f);
        rect.anchoredPosition = new Vector2(x, 0);
        var value = Ui.Label($"{icon}_value", top, "", 34);
        Ui.Place(value.rectTransform, new Vector2(0, 0.5f), new Vector2(x + 66, 0), new Vector2(width - 66, 60));
        value.rectTransform.pivot = new Vector2(0, 0.5f);
        value.rectTransform.anchoredPosition = new Vector2(x + 66, 0);
        _resources.Add((image, value));
      }

      var diplomacyButton = Ui.Button("DiplomacyButton", top, "외교", ToggleDiplomacy);
      Ui.Place((RectTransform)diplomacyButton.transform, Vector2.one, new Vector2(-170, 0), new Vector2(160, 76));
      var menuButton = Ui.Button("MenuButton", top, "메뉴", () => Controller.TogglePause());
      Ui.Place((RectTransform)menuButton.transform, Vector2.one, Vector2.zero, new Vector2(160, 76));

      _truce = Ui.Label("Truce", Root, "", 34, TextAnchor.MiddleCenter);
      Ui.Place(_truce.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -84), new Vector2(1200, 50));
      _alert = Ui.Label("Alert", Root, "", 36, TextAnchor.MiddleCenter);
      Ui.Place(_alert.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -136), new Vector2(1200, 56));
      _note = Ui.Label("Note", Root, "", 34, TextAnchor.MiddleCenter);
      Ui.Place(_note.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -194), new Vector2(1200, 56));

      var map = Ui.Rect("Minimap", Root);
      Ui.Place(map, Vector2.zero, new Vector2(12, 12), new Vector2(300, 300));
      map.gameObject.AddComponent<RawImage>();
      _minimap = map.gameObject.AddComponent<Minimap>();
      _minimap.Init(frame.FindAsset<MapSpec>(frame.Map.UserAsset));
      _minimap.OnPick = world => Controller.Camera.MoveTo(world);

      // 왼쪽 아래 미니맵 오른쪽: 선택·부대 버튼.
      var actions = Ui.Rect("Actions", Root);
      Ui.Place(actions, Vector2.zero, new Vector2(324, 12), new Vector2(3 * 150, 2 * 150));
      var grid = actions.gameObject.AddComponent<GridLayoutGroup>();
      grid.cellSize = new Vector2(Ui.MinButtonSize, Ui.MinButtonSize);
      grid.spacing = new Vector2(10, 10);
      grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
      grid.constraintCount = 3;
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

      // 오른쪽 아래: 건설·생산 패널.
      var panel = Ui.Panel("ContextPanel", Root);
      Ui.Place(panel, new Vector2(1, 0), new Vector2(-12, 12), new Vector2(5 * 150 + 10, 2 * 150 + 70));
      _status = Ui.Label("Status", panel, "", 28);
      _status.rectTransform.anchorMin = new Vector2(0, 1);
      _status.rectTransform.anchorMax = Vector2.one;
      _status.rectTransform.pivot = new Vector2(0.5f, 1);
      _status.rectTransform.sizeDelta = new Vector2(-20, 60);
      _status.rectTransform.anchoredPosition = new Vector2(0, -4);
      var slots = Ui.Rect("Slots", panel);
      Ui.Place(slots, Vector2.zero, new Vector2(10, 10), new Vector2(5 * 150, 2 * 150));
      var slotGrid = slots.gameObject.AddComponent<GridLayoutGroup>();
      slotGrid.cellSize = new Vector2(Ui.MinButtonSize, Ui.MinButtonSize);
      slotGrid.spacing = new Vector2(10, 10);
      slotGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
      slotGrid.constraintCount = 5;
      _context = new Button[ContextSlots];
      for (var i = 0; i < ContextSlots; i++) {
        var index = i;
        _context[i] = Ui.Button($"Context{i}", slots, "", () => PressContext(index), 22);
      }

      BuildDiplomacy(frame);

      QuantumEvent.Subscribe<EventActionRejected>(this, OnRejected);
      QuantumEvent.Subscribe<EventNoteReceived>(this, OnNote);
      QuantumEvent.Subscribe<EventPowerOutage>(this, OnOutage);
      QuantumEvent.Subscribe<EventBarrierOpened>(this, e => ShowAlert(Texts.BarrierAlert));
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

    int EntryAt(int slot) => ContextPages > 1 ? ContextPage * PagedSlots + slot : slot;

    /// <summary>패널의 칸 번호로 누른다. 쪽을 나눴으면 마지막 칸은 다음 쪽이다.</summary>
    public void PressContext(int slot) {
      if (ContextPages > 1 && slot == PagedSlots) {
        ContextPage = (ContextPage + 1) % ContextPages;
        _nextRefresh = 0f;
        ApplyContextPage();
        return;
      }
      var index = EntryAt(slot);
      if (index < Entries.Count && Entries[index].Enabled) {
        Entries[index].Action?.Invoke();
        _nextRefresh = 0f;
      }
    }

    public void ShowAlert(string text) {
      _alert.text = text;
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
      }
      if (_note.text.Length > 0 && Time.unscaledTime >= _noteUntil) {
        _note.text = "";
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
      if (selection.Building != EntityRef.None) {
        status = BuildingEntries(frame, state, selection.Building);
      } else if (selection.Units.Count > 0) {
        var spec = frame.FindAsset(frame.Get<Unit>(selection.Units[0]).Spec);
        status = selection.Units.Count == 1 ? $"{Texts.NameOf(spec.name)}  체력 {frame.Get<Unit>(selection.Units[0]).Health.AsInt}/{spec.MaxHealth.AsInt}" : $"유닛 {selection.Units.Count}기";
        if (selection.Units.Any(e => frame.Has<Worker>(e))) {
          BuildEntries(frame, state);
        }
      }
      if (input.IsPlacing) {
        status = "건설할 곳을 누르세요";
      }
      _status.text = status;
      if (ContextPage >= ContextPages) {
        ContextPage = 0;
      }
      ApplyContextPage();
    }

    void ApplyContextPage() {
      var paged = ContextPages > 1;
      for (var i = 0; i < _context.Length; i++) {
        if (paged && i == PagedSlots) {
          _context[i].gameObject.SetActive(true);
          Ui.SetLabel(_context[i], $"다음\n{ContextPage + 1}/{ContextPages}");
          Ui.SetIcon(_context[i], null);
          Ui.SetEnabled(_context[i], true);
          continue;
        }
        var index = EntryAt(i);
        var used = index < Entries.Count;
        _context[i].gameObject.SetActive(used);
        if (used) {
          Ui.SetLabel(_context[i], Entries[index].Label);
          Ui.SetIcon(_context[i], Entries[index].Icon != null ? Ui.IconSprite(Entries[index].Icon) : null);
          Ui.SetEnabled(_context[i], Entries[index].Enabled);
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
