namespace FactoryWars.View {
  using System;
  using System.IO;
  using System.Linq;
  using System.Security.Cryptography;
  using System.Text;
  using UnityEngine;

  /// <summary>
  /// 온라인 버전 해시(항목 30). 시뮬레이션 코드(.cs·.qtn)와 데이터 에셋(Resources/FactoryWars의 .asset, 그래픽 폴더 제외)의 내용으로 만든다.
  /// 둘 중 하나라도 다르면 같은 입력에 다른 결과가 나와 디싱크가 나므로 방 입장을 막는다.
  /// 빌드에서는 빌드 직전 에디터가 쓴 Resources/FactoryWars/BuildVersion.txt를 읽고, 에디터에서는 파일에서 바로 계산한다.
  /// </summary>
  public static class BuildVersion {
    public const string ResourcePath = "FactoryWars/BuildVersion";
    public const string Prefix = "FW3-";

    /// <summary>해시에 넣는 폴더(Assets 기준). 그래픽 폴더(Art·Visual·UI·Icons·Materials·Shaders)는 화면 전용이라 뺀다.</summary>
    public static readonly string[] SimulationFolders = { "QuantumUser/Simulation" };
    public const string DataFolder = "QuantumUser/Resources/FactoryWars";
    public static readonly string[] ViewOnlyFolders = { "Art", "Visual", "UI", "Icons", "Materials", "Shaders" };

    static string _current;

    public static string Current {
      get {
        if (_current != null) {
          return _current;
        }
#if UNITY_EDITOR
        _current = Compute(Application.dataPath);
#else
        var stored = Resources.Load<TextAsset>(ResourcePath);
        _current = stored != null ? stored.text.Trim() : Prefix + "unknown";
#endif
        return _current;
      }
    }

    /// <summary>시뮬레이션 파일 목록(상대 경로, 정렬). 줄바꿈 차이(CRLF)는 무시한다.</summary>
    public static string[] Files(string assets) {
      var code = SimulationFolders.SelectMany(folder => Directory.GetFiles(Path.Combine(assets, folder), "*.*", SearchOption.AllDirectories))
        .Where(path => path.EndsWith(".cs") || path.EndsWith(".qtn"));
      var dataRoot = Path.Combine(assets, DataFolder);
      var data = Directory.GetFiles(dataRoot, "*.asset", SearchOption.AllDirectories)
        .Where(path => ViewOnlyFolders.Any(v => Relative(dataRoot, path).StartsWith(v + "/")) == false);
      return code.Concat(data).Select(path => Relative(assets, path)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    static string Relative(string root, string path) => path.Substring(root.Length).Replace('\\', '/').TrimStart('/');

    public static string Compute(string assets) {
      using (var sha = SHA256.Create()) {
        foreach (var file in Files(assets)) {
          var name = Encoding.UTF8.GetBytes(file + "\n");
          sha.TransformBlock(name, 0, name.Length, null, 0);
          var text = File.ReadAllText(Path.Combine(assets, file)).Replace("\r\n", "\n");
          var bytes = Encoding.UTF8.GetBytes(text);
          sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(new byte[0], 0, 0);
        return Prefix + BitConverter.ToString(sha.Hash, 0, 6).Replace("-", "").ToLowerInvariant();
      }
    }

    /// <summary>테스트용: 다음 Current 호출에서 다시 계산한다.</summary>
    public static void Reset() => _current = null;

    public static bool Compatible(string other) => string.Equals(other, Current, StringComparison.Ordinal);
  }
}
