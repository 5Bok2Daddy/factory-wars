namespace FactoryWars.View {
  using System;
  using UnityEngine;
  using UnityEngine.EventSystems;

  /// <summary>누르면 동작만 하는 영역(버튼 모양·터치 크기 규칙 대상이 아닌 알림 말풍선 등).</summary>
  public class ClickRelay : MonoBehaviour, IPointerClickHandler {
    public Action OnClick;

    public void OnPointerClick(PointerEventData data) => OnClick?.Invoke();
  }
}
