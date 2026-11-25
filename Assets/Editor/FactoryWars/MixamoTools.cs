namespace FactoryWars.EditorTools {
  using System;
  using System.IO;
  using System.Linq;
  using UnityEditor;
  using UnityEditor.Animations;
  using UnityEngine;

  public static class MixamoTools {
    const string Root = "Assets/ThirdParty/MixamoAnimations";
    const string Controllers = Root + "/Generated/Resources/FactoryWarsMixamo";

    [Serializable] class Manifest {
      public Entry[] Clips;
    }

    [Serializable] class Entry {
      public string Path, Role, Key;
      public bool Loop;
    }

    [MenuItem("Tools/Factory Wars/Assets/Prepare Mixamo Clips")]
    public static void Prepare() {
      var entries = JsonUtility.FromJson<Manifest>(File.ReadAllText(Root + "/clips.json")).Clips;
      Directory.CreateDirectory(Controllers);
      AssetDatabase.Refresh();
      var clips = entries.Select(entry => (Entry: entry, Clip: Import(entry))).ToArray();
      var worker = Controller(SyntyAssign.WorkerControllerPath, "Worker");
      var infantry = Controller(SyntyAssign.ControllerPath, "Infantry");
      PrepareAxe();
      PrepareTacticalPack();
      foreach (var (entry, clip) in clips) {
        var model = entry.Role == "Infantry_Combat" ? infantry : worker;
        var machine = model.layers[0].stateMachine;
        var name = "Mixamo" + entry.Key;
        var state = machine.states.Select(item => item.state).FirstOrDefault(item => item.name == name)
          ?? machine.AddState(name);
        state.motion = clip;
        if (entry.Key == "CoverIdle") {
          var cover = machine.states.Select(item => item.state).FirstOrDefault(item => item.name == "CoverIdle");
          if (cover != null) cover.motion = clip;
        }
        EditorUtility.SetDirty(model);
      }
      AssetDatabase.SaveAssets();
      Debug.Log($"[FW-Mixamo] {clips.Length} Humanoid clips prepared");
    }

    static void PrepareAxe() {
      var path = Controllers + "/Axe.prefab";
      if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
      const string source = "Assets/ExternalAssets/Synty/PolygonGeneric/Prefabs/Weapons/SM_Gen_Wep_Axe_01.prefab";
      if (!AssetDatabase.CopyAsset(source, path)) throw new InvalidOperationException($"Could not copy {source}");
    }

    static void PrepareTacticalPack() {
      var path = Controllers + "/TacticalPack.prefab";
      if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
      const string source = "Assets/ExternalAssets/Synty/PolygonBattleRoyale/Prefabs/Characters/Attachments/SM_Chr_Attach_Bag_Lvl1.prefab";
      if (!AssetDatabase.CopyAsset(source, path)) throw new InvalidOperationException($"Could not copy {source}");
    }

    static AnimationClip Import(Entry entry) {
      var path = $"{Root}/{entry.Path}";
      var importer = AssetImporter.GetAtPath(path) as ModelImporter
        ?? throw new InvalidOperationException($"Missing animation source {path}");
      importer.animationType = ModelImporterAnimationType.Human;
      importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
      importer.materialImportMode = ModelImporterMaterialImportMode.None;
      importer.importAnimation = true;
      importer.SaveAndReimport();
      var clip = importer.defaultClipAnimations.Single();
      clip.name = entry.Key;
      clip.loopTime = entry.Loop;
      clip.loopPose = entry.Loop;
      clip.lockRootRotation = true;
      clip.lockRootHeightY = true;
      clip.lockRootPositionXZ = true;
      clip.keepOriginalOrientation = true;
      clip.keepOriginalPositionY = true;
      clip.keepOriginalPositionXZ = true;
      importer.clipAnimations = new[] { clip };
      importer.SaveAndReimport();
      var assets = AssetDatabase.LoadAllAssetsAtPath(path);
      if (!assets.OfType<Avatar>().Any(a => a.isValid && a.isHuman)) {
        throw new InvalidOperationException($"No valid Humanoid avatar in {path}");
      }
      var result = assets.OfType<AnimationClip>().Single(a => a.name == entry.Key);
      if (!result.humanMotion) throw new InvalidOperationException($"Not a Humanoid motion: {path}");
      return result;
    }

    static AnimatorController Controller(string source, string name) {
      var output = $"{Controllers}/{name}.controller";
      var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(output);
      if (existing != null) return existing;
      if (!AssetDatabase.CopyAsset(source, output)) throw new InvalidOperationException($"Could not copy {source}");
      return AssetDatabase.LoadAssetAtPath<AnimatorController>(output);
    }
  }
}
