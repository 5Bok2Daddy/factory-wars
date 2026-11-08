namespace FactoryWars.View {
  using System.Collections.Generic;
  using Photon.Deterministic;
  using Quantum;
  using UnityEngine;
  using UnityEngine.EventSystems;
  using UnityEngine.UI;

  /// <summary>대전 화면의 입력·카메라·HUD를 묶는다. 사람 자리가 있는 경기에서만 동작한다.</summary>
  public unsafe class MatchController : MonoBehaviour {
    public MatchInput Input { get; } = new MatchInput();
    public MatchHud Hud { get; private set; }
    public RtsCamera Camera { get; private set; }
    public PlayerRef Player { get; private set; }
    public bool IsReady => Hud != null;

    public QuantumGame Game => QuantumRunner.Default?.Game;

    public Frame Frame => Game?.Frames?.Predicted;

    readonly GestureRecognizer _gestures = new GestureRecognizer();
    readonly List<Gesture> _results = new List<Gesture>();
    RectTransform _boxRect;
    bool _boxDragging;

    void Update() {
      if (IsReady == false) {
        TryStart();
        return;
      }
      ReadInput();
    }

    void TryStart() {
      var game = Game;
      var frame = game?.Frames?.Verified;
      if (frame == null) {
        return;
      }
      var players = game.GetLocalPlayers();
      if (players == null || players.Count == 0) {
        return;
      }
      Player = players[0];
      if (frame.Global->Players.GetPointer(Player)->Active == false) {
        return;
      }

      var map = frame.FindAsset<MapSpec>(frame.Map.UserAsset);
      var main = UnityEngine.Camera.main;
      Camera = main.GetComponent<RtsCamera>();
      if (Camera == null) {
        Camera = main.gameObject.AddComponent<RtsCamera>();
      }
      GraphicsQuality.Bind(main);
      // 외곽 수로(항목 24)까지 카메라가 갈 수 있다.
      var channel = TerrainKit.Channel(map);
      Camera.BoundsMin = new Vector2(map.BoundsMin.X.AsFloat - channel, map.BoundsMin.Y.AsFloat - channel);
      Camera.BoundsMax = new Vector2(map.BoundsMax.X.AsFloat + channel, map.BoundsMax.Y.AsFloat + channel);
      var start = Quantum.FactoryWars.FactoryWarsUtil.StartPosition(frame, Player);
      Camera.MoveTo(new Vector2(start.X.AsFloat, start.Z.AsFloat));

      var hud = gameObject.AddComponent<MatchHud>();
      hud.Controller = this;
      hud.Build(frame);
      Hud = hud;
      _boxRect = Ui.Panel("SelectionBox", hud.Root, new Color(1f, 1f, 1f, 0.15f));
      _boxRect.GetComponent<Image>().raycastTarget = false;
      _boxRect.gameObject.SetActive(false);
    }

    public void Send(DeterministicCommand command) {
      if (command != null) {
        Game?.SendCommand(0, command);
      }
    }

    public void Stop() {
      Send(Input.Stop(Frame));
    }

    public void TogglePause() {
      MatchFlow.Current?.TogglePause();
    }

    void ReadInput() {
      int fingers;
      Vector2 first = default;
      Vector2 second = default;
      var began = false;
      var overUi = false;
      var events = EventSystem.current;
      if (UnityEngine.Input.touchCount > 0) {
        fingers = UnityEngine.Input.touchCount;
        var touch = UnityEngine.Input.GetTouch(0);
        first = touch.position;
        second = fingers > 1 ? UnityEngine.Input.GetTouch(1).position : first;
        began = touch.phase == TouchPhase.Began;
        overUi = events != null && events.IsPointerOverGameObject(touch.fingerId);
      } else {
        // 에디터에서는 왼쪽 버튼이 손가락, 휠이 핀치다.
        fingers = UnityEngine.Input.GetMouseButton(0) ? 1 : 0;
        first = UnityEngine.Input.mousePosition;
        began = UnityEngine.Input.GetMouseButtonDown(0);
        overUi = events != null && events.IsPointerOverGameObject();
        var wheel = UnityEngine.Input.mouseScrollDelta.y;
        if (wheel != 0f && overUi == false) {
          Camera.Zoom(1f + wheel * 0.1f);
        }
      }

      if (began && overUi) {
        _gestures.IgnoreUntilRelease();
      }
      _results.Clear();
      _gestures.Update(fingers, first, second, Time.unscaledTime, _results);
      foreach (var gesture in _results) {
        Handle(gesture);
      }
    }

    /// <summary>제스처 하나를 처리한다. 테스트도 이 경로로 넣는다.</summary>
    public void Handle(Gesture gesture) {
      var frame = Frame;
      if (frame == null) {
        return;
      }
      switch (gesture.Kind) {
        case GestureKind.Tap:
          if (Camera.ToGround(gesture.Position, out var tapped)) {
            Send(Input.Tap(frame, Player, tapped));
          }
          break;
        case GestureKind.DoubleTap:
          if (Camera.ToGround(gesture.Position, out var doubled)) {
            var unit = MatchInput.PickUnit(frame, doubled);
            if (unit != EntityRef.None && frame.Get<Unit>(unit).Owner == Player) {
              Input.Selection.SelectSameType(frame, Player, unit, IsOnScreen);
            } else {
              Send(Input.Tap(frame, Player, doubled));
            }
          }
          break;
        case GestureKind.LongPress:
          if (Camera.ToGround(gesture.Position, out var pressed)) {
            Send(Input.LongPress(frame, pressed));
          }
          break;
        case GestureKind.DragBegin:
          _boxDragging = Hud.BoxMode;
          if (_boxDragging) {
            _boxRect.gameObject.SetActive(true);
          } else {
            Camera.Drag(gesture.Start, gesture.Position);
          }
          break;
        case GestureKind.Drag:
          if (_boxDragging) {
            DrawBox(gesture.Start, gesture.Position);
          } else {
            Camera.Drag(gesture.Position - gesture.Delta, gesture.Position);
          }
          break;
        case GestureKind.DragEnd:
          if (_boxDragging) {
            _boxDragging = false;
            _boxRect.gameObject.SetActive(false);
            var box = Rect.MinMaxRect(
              Mathf.Min(gesture.Start.x, gesture.Position.x), Mathf.Min(gesture.Start.y, gesture.Position.y),
              Mathf.Max(gesture.Start.x, gesture.Position.x), Mathf.Max(gesture.Start.y, gesture.Position.y));
            Input.Selection.SelectBox(frame, Player, box, world => UnityEngine.Camera.main.WorldToScreenPoint(world));
          }
          break;
        case GestureKind.Pinch:
          Camera.Zoom(gesture.Scale);
          Camera.Drag(gesture.Position - gesture.Delta, gesture.Position);
          break;
      }
    }

    static bool IsOnScreen(Vector3 world) {
      var point = UnityEngine.Camera.main.WorldToViewportPoint(world);
      return point.z > 0f && point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f;
    }

    void DrawBox(Vector2 from, Vector2 to) {
      var parent = (RectTransform)_boxRect.parent;
      RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, from, null, out var a);
      RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, to, null, out var b);
      _boxRect.anchorMin = new Vector2(0.5f, 0.5f);
      _boxRect.anchorMax = new Vector2(0.5f, 0.5f);
      _boxRect.pivot = new Vector2(0.5f, 0.5f);
      _boxRect.anchoredPosition = (a + b) / 2;
      _boxRect.sizeDelta = new Vector2(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    }
  }
}
