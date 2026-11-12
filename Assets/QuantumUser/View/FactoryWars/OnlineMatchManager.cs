namespace FactoryWars.View {
  using System;
  using System.Text;
  using UnityEngine;

  /// <summary>
  /// 항목 28 온라인 6자리 방 코드 대전.
  /// - 6자리 방 코드 생성·검증 (혼동 문자 I, O, 1, 0 제외)
  /// - 버전 호환성 검사
  /// 실제 방·대기실은 OnlineRoom(Photon Realtime, 항목 30).
  /// </summary>
  public static class OnlineMatchManager {
    public static string VersionHash => BuildVersion.Current;
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

    /// <summary>방 입장 버전 검사. 시뮬레이션 코드·데이터 해시(BuildVersion)가 같아야 한다.</summary>
    public static bool CheckVersion(string clientVersion) => BuildVersion.Compatible(clientVersion);
  }
}
