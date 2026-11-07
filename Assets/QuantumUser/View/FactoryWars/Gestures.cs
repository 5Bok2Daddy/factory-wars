namespace FactoryWars.View {
  using System.Collections.Generic;
  using UnityEngine;

  public enum GestureKind { Tap, DoubleTap, LongPress, DragBegin, Drag, DragEnd, Pinch }

  public struct Gesture {
    public GestureKind Kind;
    public Vector2 Position;
    public Vector2 Start;
    public Vector2 Delta;
    /// <summary>핀치의 이번 프레임 거리 비율. 1보다 크면 벌어진 것이다.</summary>
    public float Scale;
  }

  /// <summary>손가락 수와 위치만 받아 제스처로 바꾼다. 입력 장치와 분리해 화면 없이 시험한다.</summary>
  public class GestureRecognizer {
    enum Phase { Idle, Pressed, Dragging, Pinching, Ignored }

    public float MoveThreshold = 16f;
    public float LongPressSeconds = 0.5f;
    public float DoubleTapSeconds = 0.3f;
    public float DoubleTapDistance = 48f;

    Phase _phase;
    Vector2 _start;
    Vector2 _last;
    float _pressTime;
    bool _longFired;
    float _pinchDistance;
    Vector2 _pinchCenter;
    float _lastTapTime = float.NegativeInfinity;
    Vector2 _lastTapPosition;

    public void Update(int fingers, Vector2 first, Vector2 second, float time, List<Gesture> results) {
      if (fingers == 0) {
        Release(time, results);
        return;
      }
      if (fingers >= 2) {
        Pinch(first, second, results);
        return;
      }
      switch (_phase) {
        case Phase.Idle:
          _phase = Phase.Pressed;
          _start = first;
          _last = first;
          _pressTime = time;
          _longFired = false;
          break;
        case Phase.Pressed:
          if ((first - _start).magnitude > MoveThreshold) {
            _phase = Phase.Dragging;
            results.Add(new Gesture { Kind = GestureKind.DragBegin, Position = first, Start = _start, Delta = first - _start });
            _last = first;
          } else if (_longFired == false && time - _pressTime >= LongPressSeconds) {
            _longFired = true;
            results.Add(new Gesture { Kind = GestureKind.LongPress, Position = _start, Start = _start });
          }
          break;
        case Phase.Dragging:
          results.Add(new Gesture { Kind = GestureKind.Drag, Position = first, Start = _start, Delta = first - _last });
          _last = first;
          break;
        case Phase.Pinching:
          // 두 손가락 중 하나만 뗐다. 남은 손가락은 드래그로 이어지지 않게 끝까지 무시한다.
          _phase = Phase.Ignored;
          break;
      }
    }

    /// <summary>UI 위에서 시작한 입력처럼 손을 뗄 때까지 무시한다.</summary>
    public void IgnoreUntilRelease() {
      _phase = Phase.Ignored;
    }

    void Pinch(Vector2 first, Vector2 second, List<Gesture> results) {
      var distance = (first - second).magnitude;
      var center = (first + second) / 2;
      if (_phase == Phase.Dragging) {
        results.Add(new Gesture { Kind = GestureKind.DragEnd, Position = _last, Start = _start });
      }
      if (_phase == Phase.Pinching && _pinchDistance > 0f) {
        results.Add(new Gesture { Kind = GestureKind.Pinch, Position = center, Delta = center - _pinchCenter, Scale = distance / _pinchDistance });
      }
      if (_phase != Phase.Ignored) {
        _phase = Phase.Pinching;
      }
      _pinchDistance = distance;
      _pinchCenter = center;
    }

    void Release(float time, List<Gesture> results) {
      if (_phase == Phase.Dragging) {
        results.Add(new Gesture { Kind = GestureKind.DragEnd, Position = _last, Start = _start });
      } else if (_phase == Phase.Pressed && _longFired == false) {
        var isDouble = time - _lastTapTime <= DoubleTapSeconds && (_start - _lastTapPosition).magnitude <= DoubleTapDistance;
        results.Add(new Gesture { Kind = isDouble ? GestureKind.DoubleTap : GestureKind.Tap, Position = _start, Start = _start });
        _lastTapTime = isDouble ? float.NegativeInfinity : time;
        _lastTapPosition = _start;
      }
      _phase = Phase.Idle;
    }
  }
}
