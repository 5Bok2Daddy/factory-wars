namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>큰 폭발 화면 흔들림 설정. 기본은 켬이고 설정 화면에서 끈다.</summary>
  public static class ScreenShake {
    public const string PrefsKey = "fw_shake";

    public static bool Enabled {
      get => PlayerPrefs.GetInt(PrefsKey, 1) == 1;
      set => PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0);
    }

    public static string Label => Enabled ? "화면 흔들림: 켬" : "화면 흔들림: 끔";
  }
}
