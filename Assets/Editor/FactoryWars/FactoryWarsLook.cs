namespace FactoryWars.EditorTools {
  using System.IO;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  public static partial class FactoryWarsSetup {
    const string VisualRoot = DataRoot + "/Visual";

    /// <summary>
    /// 룩 후보 프로필 에셋을 VisualPresets 값으로 다시 쓴다. 1안이 기본 프로필이고 2안·3안은 _B·_C로 남는다(항목 19A, LOOKDEV.md).
    /// </summary>
    [MenuItem("Tools/Factory Wars/Build Visual Profiles")]
    public static void BuildVisualProfiles() {
      Directory.CreateDirectory(VisualRoot);
      foreach (var name in VisualPresets.Names) {
        var path = $"{VisualRoot}/{(name == "A" ? "VisualProfile" : "VisualProfile_" + name)}.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VisualProfile>(path);
        if (profile == null) {
          profile = VisualPresets.Create(name);
          AssetDatabase.CreateAsset(profile, path);
        } else {
          VisualPresets.Apply(profile, name);
          EditorUtility.SetDirty(profile);
        }
      }
      AssetDatabase.SaveAssets();
    }
  }
}
