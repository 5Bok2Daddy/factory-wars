namespace FactoryWars.View {
  using System;
  using System.Text;
  using UnityEngine;

  /// <summary>
  /// 항목 28 온라인 6자리 방 코드 대전.
  /// - 6자리 방 코드 생성·검증 (혼동 문자 I, O, 1, 0 제외)
  /// - 버전 호환성 검사
  /// - 대기실 슬롯 (2~4인, 방장, 봇 채우기)
  /// </summary>
  public static class OnlineMatchManager {
    public const string VersionHash = "FW-1.0.0";
    public const int CodeLength = 6;
    const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string GenerateRoomCode(int? seed = null) {
      var rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
      var sb = new StringBuilder(CodeLength);
      for (var i = 0; i < CodeLength; i++) {
        sb.Append(CodeChars[rng.Next(CodeChars.Length)]);
      }
      return sb.ToString();
    }

    public static bool ValidateRoomCode(string code) {
      if (string.IsNullOrEmpty(code) || code.Length != CodeLength) {
        return false;
      }
      foreach (var c in code.ToUpperInvariant()) {
        if (CodeChars.IndexOf(c) < 0) {
          return false;
        }
      }
      return true;
    }

    public static bool CheckVersion(string clientVersion) {
      return string.Equals(clientVersion, VersionHash, StringComparison.OrdinalIgnoreCase);
    }
  }

  public enum LobbySlotType { Empty, Human, BotNormal, BotEasy }

  public class LobbySlotInfo {
    public int Index;
    public LobbySlotType Type;
    public string Nickname;
    public int FactionIndex;
    public int ColorIndex;
    public bool IsReady;
    public bool IsHost;
  }
}
