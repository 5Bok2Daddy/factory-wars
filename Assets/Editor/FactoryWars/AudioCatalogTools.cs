namespace FactoryWars.EditorTools {
  using System.IO;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  /// <summary>소리 칸 에셋(항목 30). 칸은 비어 있고, 소리 파일을 넣으면 그 일에 재생된다.</summary>
  public static class AudioCatalogTools {
    public const string AssetPath = "Assets/QuantumUser/Resources/FactoryWars/Audio/AudioCatalog.asset";

    [MenuItem("Tools/Factory Wars/Assets/Build Audio Slots")]
    public static void Build() {
      var catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(AssetPath);
      if (catalog == null) {
        Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
        catalog = ScriptableObject.CreateInstance<AudioCatalog>();
        AssetDatabase.CreateAsset(catalog, AssetPath);
      }
      foreach (var slot in AudioCatalog.DefaultSlots()) {
        if (catalog.Get(slot.Event) == null) {
          catalog.Slots.Add(slot);
        }
      }
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
      Debug.Log($"[FW-Assets] 소리 칸 {catalog.Slots.Count}개");
    }
  }
}
