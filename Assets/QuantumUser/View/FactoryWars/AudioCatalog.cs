namespace FactoryWars.View {
  using System;
  using System.Collections.Generic;
  using UnityEngine;

  /// <summary>
  /// 일마다 소리 칸. 소리 파일은 나중에 넣는다(비어 있으면 무음). Resources/FactoryWars/Audio/AudioCatalog.
  /// 같은 소리가 너무 자주 겹치지 않게 칸마다 최소 간격을 둔다.
  /// </summary>
  [CreateAssetMenu(menuName = "FactoryWars/Audio Catalog")]
  public class AudioCatalog : ScriptableObject {
    public const string ResourcePath = "FactoryWars/Audio/AudioCatalog";

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
        slots.Add(new Slot { Event = e, Haptic = e == SoundEvent.UnderAttack || e == SoundEvent.Collapse, MinInterval = e == SoundEvent.Click ? 0.05f : 0.5f });
      }
      return slots;
    }
  }
}
