namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>경기 화면에 보이는 유닛 소리를 종류별로 한 채널씩 섞는다.</summary>
  internal sealed class BattleAudio {
    static readonly SoundEvent[] Layers = {
      SoundEvent.GroundEngine, SoundEvent.RotorEngine, SoundEvent.AirEngine, SoundEvent.CombatAmbience,
    };

    readonly AudioSource[] _sources = new AudioSource[Layers.Length];
    readonly float[] _volumes = new float[Layers.Length];
    readonly float[] _gains = new float[Layers.Length];
    readonly float[] _pans = new float[Layers.Length];
    Camera _camera;
    float _combatUntil;

    public BattleAudio(GameObject stage) {
      for (var i = 0; i < Layers.Length; i++) {
        var slot = AudioCatalog.Current?.Get(Layers[i]);
        var clip = GameAudio.ClipFor(slot);
        if (clip == null) continue;
        var source = stage.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.clip = clip;
        source.volume = 0f;
        _sources[i] = source;
        _volumes[i] = slot.Volume;
      }
    }

    public void Begin(Camera camera) {
      _camera = camera;
      System.Array.Clear(_gains, 0, _gains.Length);
    }

    public void Engine(SoundEvent sound, Vector3 position, float strength) {
      var layer = (int)sound - (int)SoundEvent.GroundEngine;
      if (_sources[layer] == null || strength <= 0f) return;
      var gain = GameAudio.ScreenGain(_camera, position, out var pan) * strength;
      if (gain <= _gains[layer]) return;
      _gains[layer] = gain;
      _pans[layer] = pan;
    }

    public void NoteCombat(bool heard) {
      if (heard) _combatUntil = Time.unscaledTime + 3f;
    }

    public void Mix(float dt) {
      _gains[Layers.Length - 1] = Mathf.Clamp01(_combatUntil - Time.unscaledTime);
      for (var i = 0; i < Layers.Length; i++) {
        var source = _sources[i];
        if (source == null) continue;
        var target = _volumes[i] * _gains[i];
        if (target > 0.003f && source.isPlaying == false) source.Play();
        var blend = 1f - Mathf.Exp(-dt * (target > source.volume ? 5f : 2.6f));
        source.volume = Mathf.Lerp(source.volume, target, blend);
        source.panStereo = Mathf.Lerp(source.panStereo, _pans[i], blend);
        if (target <= 0.003f && source.volume < 0.003f && source.isPlaying) source.Stop();
      }
    }
  }
}
