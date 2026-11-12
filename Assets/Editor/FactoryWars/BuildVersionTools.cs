namespace FactoryWars.EditorTools {
  using System.IO;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEditor.Build;
  using UnityEditor.Build.Reporting;
  using UnityEngine;

  /// <summary>빌드 직전에 온라인 버전 해시(BuildVersion)를 Resources에 써서 빌드에 넣는다(항목 30).</summary>
  public class BuildVersionTools : IPreprocessBuildWithReport {
    public const string FilePath = "Assets/QuantumUser/Resources/FactoryWars/BuildVersion.txt";

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report) => Write();

    [MenuItem("Tools/Factory Wars/Online/Write Build Version")]
    public static string Write() {
      BuildVersion.Reset();
      var version = BuildVersion.Compute(Application.dataPath);
      var full = Path.Combine(Application.dataPath, "..", FilePath);
      if (File.Exists(full) == false || File.ReadAllText(full).Trim() != version) {
        File.WriteAllText(full, version + "\n");
        AssetDatabase.ImportAsset(FilePath);
      }
      Debug.Log($"[FW-Online] 빌드 버전 {version}");
      return version;
    }
  }
}
