namespace FactoryWars.View {
  using System;
  using UnityEngine;
  using UnityEngine.EventSystems;
  using UnityEngine.UI;

  /// <summary>
  /// 버튼 길게 누르기(항목 31). Seconds 이상 누르면 OnLongPress를 부르고, 그 손뗌에서는 버튼 클릭을 막는다.
  /// 잠긴 칸의 이유 보기, 부대 버튼의 지정에 쓴다.
  /// </summary>
  public class LongPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler {
    public const float Seconds = 0.5f;
    public Action OnLongPress;
    float _downAt = -1f;
    bool _fired;
    Button _button;

    void Awake() {
      _button = GetComponent<Button>();
    }

    public void OnPointerDown(PointerEventData eventData) {
      _downAt = Time.unscaledTime;
      _fired = false;
    }

    public void OnPointerUp(PointerEventData eventData) {
      _downAt = -1f;
      if (_fired && _button != null) {
        // 길게 누른 손뗌은 클릭으로 치지 않는다: 이번 프레임만 끈다.
        _button.enabled = false;
        Invoke(nameof(Restore), 0f);
      }
    }

    public void OnPointerExit(PointerEventData eventData) {
      _downAt = -1f;
    }

    void Restore() {
      if (_button != null) {
        _button.enabled = true;
      }
    }

    void Update() {
      if (_downAt >= 0f && _fired == false && Time.unscaledTime - _downAt >= Seconds) {
        _fired = true;
        OnLongPress?.Invoke();
      }
    }

    public static LongPress Attach(Component target, Action action) {
      var press = target.GetComponent<LongPress>();
      if (press == null) {
        press = target.gameObject.AddComponent<LongPress>();
      }
      press.OnLongPress = action;
      return press;
    }
  }
}
