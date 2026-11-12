namespace FactoryWars.View {
  using System;
  using System.Globalization;
  using System.IO;
  using Unity.Profiling;
  using UnityEngine;
  using UnityEngine.UI;

  /// <summary>
  /// 항목 23 실기기 시험 장치. 경기 화면에 붙는다(화면 전용, 시뮬레이션과 무관).
  /// - 성능 오버레이: FPS, 프레임 시간, 그리기 호출, 유닛 수, 품질. 개발 빌드에서만, 설정에서 켜고 끈다.
  /// - 기록: 경기마다 평균·최저 FPS(1초 구간), 최대 그리기 호출, 최대 메모리를 persistentDataPath/perf_log.csv에 한 줄씩 남긴다.
  /// - 품질 자동 선택: 첫 실행 경기의 처음 10초 평균 FPS가 낮으면 한 단계 내린다(설정에서 직접 고르면 하지 않는다).
  /// </summary>
  public class PerfMonitor : MonoBehaviour {
    public const string OverlayKey = "fw_perf_overlay";
    public const string LogFile = "perf_log.csv";
    const string Header = "date,map,quality,device,gpu,seconds,avg_fps,min_fps,max_draw_calls,max_memory_mb";

    public static bool OverlayEnabled {
      // 항목 31: 기본은 끔(설정에서 켠다). 켜져 있어도 글자가 생기기 전에는 숨긴다(빈 어두운 상자 B5).
      get => Debug.isDebugBuild && PlayerPrefs.GetInt(OverlayKey, 0) == 1;
      set => PlayerPrefs.SetInt(OverlayKey, value ? 1 : 0);
    }

    public static string LogPath => Path.Combine(Application.persistentDataPath, LogFile);

    public MatchController Controller;
    Text _text;
    ProfilerRecorder _drawCalls;
    ProfilerRecorder _memory;
    float _elapsed;
    int _frames;
    float _windowTime;
    int _windowFrames;
    float _minFps = float.MaxValue;
    long _maxDrawCalls;
    long _maxMemory;
    float _shownAt;
    bool _saved;

    void OnEnable() {
      _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
      _memory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
    }

    void OnDisable() {
      Save();
      _drawCalls.Dispose();
      _memory.Dispose();
    }

    public void Build(RectTransform parent) {
      var panel = Ui.Panel("Perf", parent, new Color(0f, 0f, 0f, 0.55f));
      Ui.Place(panel, new Vector2(0f, 1f), new Vector2(20, -80), new Vector2(520, 150));
      panel.GetComponent<Image>().raycastTarget = false;
      _text = Ui.Label("PerfText", panel, "", 24, TextAnchor.UpperLeft);
      _text.raycastTarget = false;
      Ui.Stretch(_text.rectTransform);
      _panel = panel.gameObject;
      panel.gameObject.SetActive(false);
    }

    GameObject _panel;
    public bool PanelVisible => _panel != null && _panel.activeSelf;

    void Update() {
      var dt = Time.unscaledDeltaTime;
      if (Time.timeScale > 1.01f || dt <= 0f) {
        return;
      }
      _elapsed += dt;
      _frames++;
      _windowTime += dt;
      _windowFrames++;
      if (_windowTime >= 1f) {
        _minFps = Mathf.Min(_minFps, _windowFrames / _windowTime);
        _windowTime = 0f;
        _windowFrames = 0;
      }
      if (_drawCalls.Valid) {
        _maxDrawCalls = Math.Max(_maxDrawCalls, _drawCalls.LastValue);
      }
      if (_memory.Valid) {
        _maxMemory = Math.Max(_maxMemory, _memory.LastValue);
      }
      AutoQuality.Observe(_elapsed, _frames);
      if (_panel != null && _panel.activeSelf != (OverlayEnabled && _text.text.Length > 0)) {
        _panel.SetActive(OverlayEnabled && _text.text.Length > 0);
      }
      if (_text != null && OverlayEnabled && Time.unscaledTime >= _shownAt) {
        _shownAt = Time.unscaledTime + 0.5f;
        var units = Controller != null && Controller.Frame != null ? Controller.Frame.ComponentCount<Quantum.Unit>() : 0;
        _text.text = $"FPS {1f / Mathf.Max(0.0001f, dt):0}  ({dt * 1000f:0.0}ms)\n"
          + $"그리기 {(_drawCalls.Valid ? _drawCalls.LastValue.ToString() : "-")}  유닛 {units}\n"
          + $"품질 {GraphicsQuality.Name(GraphicsQuality.Current)}  메모리 {(_memory.Valid ? _memory.LastValue / (1024 * 1024) : 0)}MB";
      }
    }

    /// <summary>경기 한 판의 기록을 CSV 한 줄로 남긴다. 10초 미만 경기는 남기지 않는다.</summary>
    public void Save() {
      if (_saved || _elapsed < 10f) {
        return;
      }
      _saved = true;
      try {
        var line = string.Join(",",
          DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
          UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
          GraphicsQuality.Name(GraphicsQuality.Current),
          Clean(SystemInfo.deviceModel),
          Clean(SystemInfo.graphicsDeviceName),
          ((int)_elapsed).ToString(CultureInfo.InvariantCulture),
          (_frames / _elapsed).ToString("0.0", CultureInfo.InvariantCulture),
          (_minFps == float.MaxValue ? 0f : _minFps).ToString("0.0", CultureInfo.InvariantCulture),
          _maxDrawCalls.ToString(CultureInfo.InvariantCulture),
          (_maxMemory / (1024 * 1024)).ToString(CultureInfo.InvariantCulture));
        var exists = File.Exists(LogPath);
        File.AppendAllText(LogPath, (exists ? "" : Header + "\n") + line + "\n");
      } catch (Exception e) {
        Debug.LogWarning("[FW-PERF] 기록 실패: " + e.Message);
      }
    }

    static string Clean(string value) => (value ?? "").Replace(',', ' ');

    /// <summary>"기록 공유": CSV 전체를 클립보드에 복사한다. 메신저에 붙여 넣어 보낸다. 줄 수를 돌려준다.</summary>
    public static int CopyLog() {
      if (File.Exists(LogPath) == false) {
        GUIUtility.systemCopyBuffer = Header + "\n";
        return 0;
      }
      var text = File.ReadAllText(LogPath);
      GUIUtility.systemCopyBuffer = text;
      return Mathf.Max(0, text.Split('\n').Length - 2);
    }
  }

  /// <summary>
  /// 품질 자동 선택(항목 23). 첫 실행에 기기 정보로 처음 단계를 고르고, 첫 경기 처음 10초 평균 FPS로 한 번 더 고친다.
  /// 사용자가 설정에서 품질을 바꾸면(GraphicsQuality.Cycle) 더 이상 자동으로 바꾸지 않는다.
  /// </summary>
  public static class AutoQuality {
    public const string DoneKey = "fw_quality_auto_done";
    const float MeasureSeconds = 10f;
    const float LowFps = 26f;
    const float HighFps = 55f;

    /// <summary>메모리·GPU 이름으로 처음 단계를 고른다. 이름 규칙은 대략적이고 10초 측정이 고친다.</summary>
    public static QualityTier Guess() {
      if (Application.isMobilePlatform == false) {
        return QualityTier.High;
      }
      var memory = SystemInfo.systemMemorySize;
      var gpu = (SystemInfo.graphicsDeviceName ?? "").ToLowerInvariant();
      var weakGpu = gpu.Contains("mali-g5") || gpu.Contains("mali-g3") || gpu.Contains("mali-t") || gpu.Contains("adreno (tm) 5") || gpu.Contains("powervr");
      if (memory < 4000 || weakGpu) {
        return QualityTier.Low;
      }
      return memory >= 8000 ? QualityTier.High : QualityTier.Medium;
    }

    public static void Observe(float elapsed, int frames) {
      if (PlayerPrefs.GetInt(DoneKey, 0) == 1 || elapsed < MeasureSeconds) {
        return;
      }
      PlayerPrefs.SetInt(DoneKey, 1);
      var fps = frames / elapsed;
      var tier = GraphicsQuality.Current;
      if (fps < LowFps && tier > QualityTier.Low) {
        tier--;
      } else if (fps > HighFps && tier < QualityTier.High && Guess() > tier) {
        tier++;
      }
      if (tier != GraphicsQuality.Current) {
        GraphicsQuality.Apply(tier);
        PlayerPrefs.SetInt(GraphicsQuality.PrefsKey, (int)tier);
        Debug.Log($"[FW-PERF] 자동 품질: 평균 {fps:0.0}FPS → {GraphicsQuality.Name(tier)}");
      }
    }
  }
}
