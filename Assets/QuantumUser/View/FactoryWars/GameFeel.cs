namespace FactoryWars.View {
  using System;
  using System.Collections.Generic;
  using System.IO;
  using UnityEngine;

  /// <summary>개인 설정(항목 30): 진동·카메라 속도. 기기에 저장한다.</summary>
  public static class PlayerOptions {
    public const string VibrationKey = "fw_vibration";
    public const string CameraSpeedKey = "fw_camera_speed";
    public static readonly float[] CameraSpeeds = { 0.6f, 0.8f, 1f, 1.25f, 1.6f };

    public static bool Vibration {
      get => PlayerPrefs.GetInt(VibrationKey, 1) == 1;
      set => PlayerPrefs.SetInt(VibrationKey, value ? 1 : 0);
    }

    public static float CameraSpeed => CameraSpeeds[Mathf.Clamp(PlayerPrefs.GetInt(CameraSpeedKey, 2), 0, CameraSpeeds.Length - 1)];

    public static float NextCameraSpeed() {
      var next = (PlayerPrefs.GetInt(CameraSpeedKey, 2) + 1) % CameraSpeeds.Length;
      PlayerPrefs.SetInt(CameraSpeedKey, next);
      return CameraSpeeds[next];
    }

    public static string VibrationLabel => Vibration ? "진동: 켬" : "진동: 끔";
    public static string CameraLabel => $"카메라 속도: {CameraSpeed:0.##}배";
  }

  /// <summary>소리가 나는 게임 일(항목 30). AudioCatalog의 칸 이름과 같다.</summary>
  public enum SoundEvent { Click, ProductionDone, UnderAttack, Captured, Explosion, Collapse, Warning, Victory, Defeat }

  /// <summary>소리·진동 재생. 칸이 비었으면 소리 없이 진동 설정만 따른다.</summary>
  public static class GameAudio {
    static AudioSource _source;
    static readonly Dictionary<SoundEvent, float> Last = new Dictionary<SoundEvent, float>();
    public static int Played { get; private set; }
    public static int Vibrated { get; private set; }

    public static void Play(SoundEvent e) {
      var slot = AudioCatalog.Current?.Get(e);
      var now = Time.unscaledTime;
      var interval = slot != null ? slot.MinInterval : 0.5f;
      if (Last.TryGetValue(e, out var last) && now - last < interval) {
        return;
      }
      Last[e] = now;
      var haptic = slot != null ? slot.Haptic : e == SoundEvent.UnderAttack || e == SoundEvent.Collapse;
      if (haptic && PlayerOptions.Vibration) {
        Vibrated++;
#if UNITY_ANDROID || UNITY_IOS
        Handheld.Vibrate();
#endif
      }
      if (slot == null || slot.Clips == null || slot.Clips.Length == 0) {
        return;
      }
      var clip = slot.Clips[UnityEngine.Random.Range(0, slot.Clips.Length)];
      if (clip == null) {
        return;
      }
      if (_source == null) {
        var item = new GameObject("GameAudio");
        UnityEngine.Object.DontDestroyOnLoad(item);
        _source = item.AddComponent<AudioSource>();
        _source.playOnAwake = false;
      }
      _source.PlayOneShot(clip, slot.Volume);
      Played++;
    }

    static bool _synthInstalled;

    /// <summary>오디오 팩이 없을 때 기본 카탈로그 슬롯에 절차적 군용 효과음을 채운다(테스트의 빈 카탈로그 인스턴스에는 손대지 않음).</summary>
    public static void InstallProceduralDefaults() {
      if (_synthInstalled) {
        return;
      }
      var catalog = AudioCatalog.Current;
      if (catalog == null || catalog.Slots == null) {
        return;
      }
      _synthInstalled = true;
      foreach (var slot in catalog.Slots) {
        if (slot.Clips != null && slot.Clips.Length > 0) {
          continue;
        }
        var clip = SynthClip(slot.Event);
        if (clip != null) {
          slot.Clips = new[] { clip };
          if (slot.Event == SoundEvent.Explosion) {
            slot.Volume = 0.55f;
          } else if (slot.Event == SoundEvent.Click) {
            slot.Volume = 0.45f;
          }
        }
      }
    }

    static AudioClip SynthClip(SoundEvent e) {
      const int Rate = 22050;
      float duration = e switch {
        SoundEvent.Click => 0.04f,
        SoundEvent.ProductionDone => 0.18f,
        SoundEvent.Warning => 0.34f,
        SoundEvent.UnderAttack => 0.32f,
        SoundEvent.Captured => 0.28f,
        SoundEvent.Explosion => 0.36f,
        SoundEvent.Collapse => 0.42f,
        SoundEvent.Victory => 0.40f,
        SoundEvent.Defeat => 0.40f,
        _ => 0.15f,
      };
      var count = Mathf.Max(64, Mathf.RoundToInt(Rate * duration));
      var data = new float[count];
      uint rng = 0x9E3779B9u + (uint)e * 2654435761u;
      for (var i = 0; i < count; i++) {
        var t = (float)i / Rate;
        var norm = (float)i / count;
        var env = Mathf.Pow(1f - norm, e == SoundEvent.Explosion || e == SoundEvent.Collapse ? 1.8f : 1.2f);
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        var noise = ((rng & 0xFFFF) / 32768f) - 1f;
        float sample = 0f;
        switch (e) {
          case SoundEvent.Click:
            sample = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(920f, 1380f, norm) * t) * 0.4f;
            break;
          case SoundEvent.ProductionDone:
            sample = Mathf.Sin(2f * Mathf.PI * (norm < 0.45f ? 523.25f : 659.25f) * t) * 0.35f;
            break;
          case SoundEvent.Warning:
          case SoundEvent.UnderAttack:
            sample = Mathf.Sin(2f * Mathf.PI * (680f + 240f * Mathf.Sin(norm * Mathf.PI * 4f)) * t) * 0.35f;
            break;
          case SoundEvent.Captured:
          case SoundEvent.Victory:
            var freq = norm < 0.33f ? 440f : norm < 0.66f ? 554.37f : 659.25f;
            sample = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.35f;
            break;
          case SoundEvent.Defeat:
            sample = Mathf.Sin(2f * Mathf.PI * (norm < 0.5f ? 330f : 220f) * t) * 0.35f;
            break;
          case SoundEvent.Explosion:
          case SoundEvent.Collapse:
            sample = (noise * 0.55f + Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(115f, 38f, norm) * t) * 0.45f);
            break;
        }
        data[i] = Mathf.Clamp(sample * env, -1f, 1f);
      }
      var clip = AudioClip.Create($"proc_{e}", count, 1, Rate, false);
      clip.SetData(data, 0);
      return clip;
    }
  }

  /// <summary>
  /// 오류 로그 저장(항목 30). 오류·예외를 persistentDataPath/logs/errors-날짜.txt에 남긴다(같은 줄은 한 번만).
  /// 실기기에서 생긴 문제를 사용자가 파일로 보낼 수 있다.
  /// </summary>
  public static class ErrorLog {
    static string _path;
    static readonly HashSet<string> Seen = new HashSet<string>();
    public static string Path => _path;
    public static int Count { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() {
      if (_path != null) {
        return;
      }
      var folder = System.IO.Path.Combine(Application.persistentDataPath, "logs");
      Directory.CreateDirectory(folder);
      _path = System.IO.Path.Combine(folder, $"errors-{DateTime.Now:yyyyMMdd}.txt");
      Application.logMessageReceived += OnLog;
    }

    static void OnLog(string message, string stack, LogType type) {
      if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) {
        return;
      }
      var key = message.Length > 200 ? message.Substring(0, 200) : message;
      if (Seen.Add(key) == false || Seen.Count > 500) {
        return;
      }
      Count++;
      try {
        File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss}] {type}: {message}\n{stack}\n");
      } catch (Exception) {
        // 저장 실패는 무시한다(디스크 가득 등). 로그 처리 안에서 다시 로그를 남기면 무한 반복이 된다.
      }
    }
  }
}
