namespace FactoryWars.View {
  using System;
  using System.Collections.Generic;
  using UnityEngine;

  /// <summary>
  /// 게임 이벤트별 음량과 재생 간격. 프로젝트 카탈로그와 로컬 음원을 함께 사용한다.
  /// 같은 소리가 너무 자주 겹치지 않게 칸마다 최소 간격을 둔다.
  /// </summary>
  [CreateAssetMenu(menuName = "FactoryWars/Audio Catalog")]
  public class AudioCatalog : ScriptableObject {
    public const string ResourcePath = "FactoryWars/Audio/AudioCatalog";
    const string LocalPath = "FactoryWars/Audio/Local/";
    static readonly Dictionary<SoundEvent, AudioClip> LocalClips = new Dictionary<SoundEvent, AudioClip>();

    public static AudioClip LocalClip(SoundEvent sound) {
      if (LocalClips.TryGetValue(sound, out var clip) == false) {
        clip = Resources.Load<AudioClip>(LocalPath + sound);
        LocalClips.Add(sound, clip);
      }
      return clip;
    }

    [Serializable]
    public class Slot {
      public SoundEvent Event;
      public AudioClip[] Clips = new AudioClip[0];
      [Range(0f, 1f)] public float Volume = 1f;
      public float MinInterval = 0.1f;
      /// <summary>이 일에 진동도 줄지(공격받음·붕괴).</summary>
      public bool Haptic;
    }

    public List<Slot> Slots = new List<Slot>();

    public Slot Get(SoundEvent e) => Slots.Find(s => s.Event == e);

    static AudioCatalog _current;
    static bool _loaded;

    public static AudioCatalog Current {
      get {
        if (_loaded == false) {
          _loaded = true;
          _current = Resources.Load<AudioCatalog>(ResourcePath);
        }
        return _current;
      }
      set {
        _current = value;
        _loaded = true;
      }
    }

    /// <summary>기본 칸(소리 없음). 공격받음·붕괴는 진동.</summary>
    public static List<Slot> DefaultSlots() {
      var slots = new List<Slot>();
      foreach (SoundEvent e in Enum.GetValues(typeof(SoundEvent))) {
        var (volume, interval) = e switch {
          SoundEvent.Click => (1f, 0.05f),
          SoundEvent.RifleFire => (0.44f, 0.23f),
          SoundEvent.TankCannon => (0.55f, 0.25f),
          SoundEvent.SniperFire => (0.58f, 0.55f),
          SoundEvent.RocketFire => (0.54f, 0.45f),
          SoundEvent.MissileWarning => (0.48f, 2f),
          SoundEvent.JetFlyby => (0.30f, 2.2f),
          SoundEvent.GroundEngine => (0.31f, 0f),
          SoundEvent.RotorEngine => (0.24f, 0f),
          SoundEvent.AirEngine => (0.23f, 0f),
          SoundEvent.CombatAmbience => (0.075f, 0f),
          _ => (1f, 0.5f),
        };
        slots.Add(new Slot { Event = e, Volume = volume, MinInterval = interval, Haptic = e == SoundEvent.UnderAttack || e == SoundEvent.Collapse });
      }
      return slots;
    }
  }
}
