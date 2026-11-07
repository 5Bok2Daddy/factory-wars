namespace FactoryWars.EditorTools {
  using System.IO;
  using System.Linq;
  using UnityEditor;
  using UnityEditor.Build;
  using UnityEditor.Build.Reporting;
  using UnityEngine;

  /// <summary>
  /// Android 개발 빌드. 배치 모드에서
  /// -buildTarget Android -executeMethod FactoryWars.EditorTools.AndroidBuild.Run 으로 실행한다.
  /// 결과는 저장소 밖 Builds 폴더에 쌓인다.
  /// </summary>
  public static class AndroidBuild {
    const string FileName = "FactoryWars-dev.apk";

    [MenuItem("Tools/Factory Wars/Build Android (Development)")]
    public static void Run() {
      PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
      PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
      PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
      PlayerSettings.allowedAutorotateToPortrait = false;
      PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
      PlayerSettings.allowedAutorotateToLandscapeLeft = true;
      PlayerSettings.allowedAutorotateToLandscapeRight = true;
      EditorUserBuildSettings.buildAppBundle = false;

      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Builds"));
      Directory.CreateDirectory(folder);
      var options = new BuildPlayerOptions {
        scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
        locationPathName = Path.Combine(folder, FileName),
        target = BuildTarget.Android,
        targetGroup = BuildTargetGroup.Android,
        options = BuildOptions.Development,
      };
      var report = BuildPipeline.BuildPlayer(options);
      var summary = report.summary;
      Debug.Log($"AndroidBuild: {summary.result}, 크기 {summary.totalSize / (1024f * 1024f):0.0}MB, 시간 {summary.totalTime}, 경고 {summary.totalWarnings}, 오류 {summary.totalErrors}, 경로 {summary.outputPath}");
      if (Application.isBatchMode) {
        EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
      }
    }
  }
}
