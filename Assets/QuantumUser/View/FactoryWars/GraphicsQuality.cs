namespace FactoryWars.View {
  using UnityEngine;
  using UnityEngine.Rendering;
  using UnityEngine.Rendering.Universal;

  /// <summary>Unity 품질 단계의 순서와 같다.</summary>
  public enum QualityTier { Low, Medium, High }

  /// <summary>
  /// 낮음·보통·높음. 그림자·HDR·해상도는 단계별 URP 에셋에 있고, 여기서는 단계를 고르고 카메라와 Volume을 맞춘다.
  /// 낮음은 모바일 기준이다: 후처리 없음, 유닛 그림자 없음.
  /// </summary>
  public static class GraphicsQuality {
    public const string PrefsKey = "fw_quality";
    const string Root = "FactoryWars/Art/";

    static readonly string[] Names = { "낮음", "보통", "높음" };

    public static QualityTier Current { get; private set; } = QualityTier.High;

    public static QualityTier Default => Application.isMobilePlatform ? QualityTier.Low : QualityTier.High;
    public static bool PostProcessing => Current != QualityTier.Low;
    public static bool UnitShadows => Current != QualityTier.Low;
    /// <summary>파티클 양 배율.</summary>
    public static float FxScale => Current == QualityTier.Low ? 0.35f : Current == QualityTier.Medium ? 0.7f : 1f;
    public static string Name(QualityTier tier) => Names[(int)tier];
    public static string ProfileName(QualityTier tier) => tier == QualityTier.High ? "PostHigh" : "PostMedium";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot() {
      Apply((QualityTier)Mathf.Clamp(PlayerPrefs.GetInt(PrefsKey, (int)Default), 0, Names.Length - 1));
    }

    public static void Apply(QualityTier tier) {
      Current = tier;
      QualitySettings.SetQualityLevel((int)tier, true);
      Bind(Camera.main);
      ArtView.RefreshAll();
    }

    /// <summary>설정 화면의 버튼. 다음 단계로 넘기고 저장한다.</summary>
    public static QualityTier Cycle() {
      var next = (QualityTier)(((int)Current + 1) % Names.Length);
      Apply(next);
      PlayerPrefs.SetInt(PrefsKey, (int)next);
      return next;
    }

    /// <summary>씬이 바뀌어 카메라가 새로 생기면 다시 부른다.</summary>
    public static void Bind(Camera camera) {
      if (camera == null) {
        return;
      }
      RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
      RenderSettings.customReflectionTexture = Resources.Load<Cubemap>(Root + "SkyReflection");

      camera.GetUniversalAdditionalCameraData().renderPostProcessing = PostProcessing;
      var volume = Object.FindAnyObjectByType<Volume>(FindObjectsInactive.Include);
      if (PostProcessing == false) {
        if (volume != null) {
          volume.enabled = false;
        }
        return;
      }
      if (volume == null) {
        volume = new GameObject("Post Processing").AddComponent<Volume>();
        volume.isGlobal = true;
      }
      volume.enabled = true;
      volume.sharedProfile = BuildVolume(Current, VisualProfile.Current);
      ApplySsao(Current, VisualProfile.Current);
    }

    /// <summary>
    /// SSAO는 렌더러 기능이다. 높음은 켜고, 보통은 절반 해상도로 켜고, 낮음은 끈다. 세기·반경은 프로필에서 읽는다.
    /// 설정 필드가 공개되어 있지 않아 이름으로 찾는다. 찾지 못하면 켜고 끄기만 한다.
    /// </summary>
    static void ApplySsao(QualityTier tier, VisualProfile profile) {
      var on = profile.Ssao && tier != QualityTier.Low;
      foreach (var feature in Resources.FindObjectsOfTypeAll<ScreenSpaceAmbientOcclusion>()) {
        feature.SetActive(on);
        var field = typeof(ScreenSpaceAmbientOcclusion).GetField("m_Settings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var settings = field?.GetValue(feature);
        if (settings == null) {
          continue;
        }
        void Set(string name, object value) {
          var member = settings.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
          member?.SetValue(settings, value);
        }
        Set("Downsample", tier == QualityTier.Medium);
        Set("Intensity", profile.SsaoIntensity);
        Set("Radius", profile.SsaoRadius);
        field.SetValue(feature, settings);
      }
    }

    static VolumeProfile _volume;

    /// <summary>품질 단계와 화면 프로필로 Volume을 만든다. 저장하지 않는 실행용 에셋이다.</summary>
    public static VolumeProfile BuildVolume(QualityTier tier, VisualProfile profile) {
      if (_volume != null) {
        foreach (var component in _volume.components) {
          Object.Destroy(component);
        }
        Object.Destroy(_volume);
      }
      _volume = ScriptableObject.CreateInstance<VolumeProfile>();
      var bloom = _volume.Add<Bloom>(true);
      bloom.intensity.Override(profile.BloomIntensity);
      bloom.threshold.Override(profile.BloomThreshold);
      bloom.scatter.Override(profile.BloomScatter);
      bloom.downscale.Override(tier == QualityTier.High ? BloomDownscaleMode.Half : BloomDownscaleMode.Quarter);
      bloom.highQualityFiltering.Override(tier == QualityTier.High);
      bloom.maxIterations.Override(tier == QualityTier.High ? 6 : 4);
      var tone = _volume.Add<Tonemapping>(true);
      tone.mode.Override(profile.Tone == ToneMode.Aces ? TonemappingMode.ACES : TonemappingMode.Neutral);
      var color = _volume.Add<ColorAdjustments>(true);
      color.saturation.Override(profile.Saturation);
      color.contrast.Override(profile.Contrast);
      if (profile.Vignette > 0f) {
        var vignette = _volume.Add<Vignette>(true);
        vignette.intensity.Override(profile.Vignette);
        vignette.smoothness.Override(0.5f);
      }
      return _volume;
    }
  }
}
