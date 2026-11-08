namespace FactoryWars.View {
  using System.Collections.Generic;
  using UnityEngine;

  /// <summary>
  /// 포구 화염, 탄 궤적, 피격 불꽃, 폭발, 연기, 잔해. 파티클 시스템 셋을 공유해 호출 수를 고정한다.
  /// 양은 품질 단계로 줄인다. 시뮬레이션과 무관한 화면 연출이라 UnityEngine.Random을 써도 된다.
  /// </summary>
  public class BattleFx : MonoBehaviour {
    const int MaxWrecks = 24;
    const float WreckSeconds = 30f;
    const float WreckSink = 1.2f;
    const int MaxCraters = 64;
    const float CraterSeconds = 60f;

    static readonly Color Muzzle = new Color(1f, 0.62f, 0.2f, 1f);
    static readonly Color Flame = new Color(1f, 0.42f, 0.08f, 1f);
    static readonly Color Spark = new Color(1f, 0.74f, 0.32f, 1f);
    static readonly Color Bullet = new Color(1f, 0.82f, 0.5f, 1f);
    static readonly Color Missile = new Color(0.72f, 0.92f, 1f, 1f);
    static readonly Color SmokeGray = new Color(0.52f, 0.52f, 0.52f, 0.5f);
    static readonly Color SmokeDark = new Color(0.16f, 0.15f, 0.14f, 0.62f);
    static readonly Color DustBrown = new Color(0.62f, 0.54f, 0.4f, 0.45f);

    ParticleSystem _glow;
    ParticleSystem _tracer;
    ParticleSystem _smoke;
    Mesh _rubble;
    readonly Queue<(GameObject Item, float Born, float Y)> _wrecks = new Queue<(GameObject, float, float)>();
    readonly List<(Vector3 Position, float Size, float Born)> _craters = new List<(Vector3, float, float)>();
    MeshFilter _craterFilter;
    bool _cratersDirty;
    public int CraterCount => _craters.Count;
    public float ShakeAmount { get; private set; }

    public int Emitted { get; private set; }

    static ParticleSystem Make(Transform parent, string name, string material, int max, bool stretch, float growth) {
      var item = new GameObject(name);
      item.transform.SetParent(parent, false);
      var system = item.AddComponent<ParticleSystem>();
      system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
      var main = system.main;
      main.simulationSpace = ParticleSystemSimulationSpace.World;
      main.maxParticles = max;
      main.playOnAwake = false;
      main.loop = true;
      main.startSpeed = 0f;
      main.startLifetime = 1f;
      var emission = system.emission;
      emission.enabled = false;
      var shape = system.shape;
      shape.enabled = false;

      var fade = system.colorOverLifetime;
      fade.enabled = true;
      var gradient = new Gradient();
      gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
      fade.color = gradient;
      var size = system.sizeOverLifetime;
      size.enabled = true;
      size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, growth));

      var renderer = item.GetComponent<ParticleSystemRenderer>();
      renderer.sharedMaterial = Resources.Load<Material>($"FactoryWars/Art/{material}");
      renderer.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
      renderer.lengthScale = 1f;
      renderer.velocityScale = stretch ? 0.05f : 0f;
      renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      renderer.receiveShadows = false;
      system.Play();
      return system;
    }

    void Awake() {
      _glow = Make(transform, "Glow", "FxGlow", 400, false, 0.3f);
      _tracer = Make(transform, "Tracer", "FxGlow", 200, true, 1f);
      _smoke = Make(transform, "Smoke", "FxSmoke", 300, false, 3f);
    }

    static int Scaled(int count) => Mathf.Max(1, Mathf.RoundToInt(count * GraphicsQuality.FxScale));

    void Emit(ParticleSystem system, Vector3 position, Vector3 velocity, float size, float life, Color color) {
      var parameters = new ParticleSystem.EmitParams {
        position = position, velocity = velocity, startSize = size, startLifetime = life, startColor = color, applyShapeToPosition = false,
      };
      system.Emit(parameters, 1);
      Emitted++;
    }

    public void MuzzleFlash(Vector3 position, Vector3 forward, float size) {
      var count = Scaled(4);
      for (var i = 0; i < count; i++) {
        Emit(_glow, position + forward * (0.2f * i), forward * Random.Range(2f, 7f) + Random.insideUnitSphere, size * Random.Range(0.6f, 1.2f), 0.14f, Muzzle);
      }
    }

    public void FlameJet(Vector3 position, Vector3 forward, float reach) {
      var count = Scaled(10);
      for (var i = 0; i < count; i++) {
        var velocity = (forward + Random.insideUnitSphere * 0.18f) * reach * Random.Range(1.6f, 2.6f);
        Emit(_glow, position, velocity, Random.Range(0.5f, 1.1f), Random.Range(0.3f, 0.45f), Flame);
      }
    }

    /// <summary>탄 궤적. 빠른 입자 하나를 쏘고 도착 시점에 사라지게 한다.</summary>
    public void Tracer(Vector3 from, Vector3 to, float speed, float width, bool missile) {
      var delta = to - from;
      var distance = delta.magnitude;
      if (distance < 0.5f) {
        return;
      }
      Emit(_tracer, from, delta / distance * speed, width, distance / speed + 0.04f, missile ? Missile : Bullet);
      if (missile) {
        // 미사일 연기 꼬리: 길고 굵게, 오래 남는다.
        var trail = Scaled(10);
        for (var i = 1; i <= trail; i++) {
          var t = i / (trail + 1f);
          Emit(_smoke, Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * distance * 0.08f, Vector3.up * 0.3f, 0.6f + t * 0.4f, 1.2f + t * 0.6f, SmokeGray);
        }
      }
    }

    public void Impact(Vector3 position, float size) {
      // 피격 번쩍임.
      Emit(_glow, position + Vector3.up * 0.3f, Vector3.zero, size * 1.6f, 0.08f, Color.white);
      var count = Scaled(5);
      for (var i = 0; i < count; i++) {
        var velocity = (Random.onUnitSphere + Vector3.up) * Random.Range(2f, 6f);
        Emit(_tracer, position, velocity, 0.12f * size, Random.Range(0.15f, 0.32f), Spark);
      }
      Emit(_glow, position, Vector3.zero, size, 0.18f, Spark);
    }

    /// <summary>땅에 남는 크레이터. 최대 64개를 메쉬 하나로 합쳐 그린다. 60초 뒤 사라진다.</summary>
    public void Crater(Vector3 position, float size) {
      if (_craters.Count >= MaxCraters) {
        _craters.RemoveAt(0);
      }
      _craters.Add((new Vector3(position.x, 0f, position.z), size, Time.time));
      _cratersDirty = true;
    }

    void RebuildCraters() {
      _cratersDirty = false;
      if (_craterFilter == null) {
        var item = new GameObject("Craters", typeof(MeshFilter), typeof(MeshRenderer));
        item.transform.SetParent(transform, false);
        _craterFilter = item.GetComponent<MeshFilter>();
        var renderer = item.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = ArtLook.NeutralMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      }
      var kit = new MeshKit();
      const int Sides = 9;
      for (var c = 0; c < _craters.Count; c++) {
        var (center, size, _) = _craters[c];
        var lift = 0.07f + c * 0.0004f;
        for (var ring = 0; ring < 2; ring++) {
          kit.Paint(ring == 0 ? Swatch.DirtDark : Swatch.Black);
          var radius = size * (ring == 0 ? 1f : 0.55f);
          for (var i = 0; i < Sides; i++) {
            var a = Mathf.PI * 2f * i / Sides + c;
            var b = Mathf.PI * 2f * (i + 1) / Sides + c;
            var wobbleA = 0.8f + 0.4f * MeshKit.Hash(c * 31 + ring, i);
            var wobbleB = 0.8f + 0.4f * MeshKit.Hash(c * 31 + ring, (i + 1) % Sides);
            kit.Tri(center + Vector3.up * (lift + ring * 0.01f), center + new Vector3(Mathf.Cos(a) * radius * wobbleA, lift + ring * 0.01f, Mathf.Sin(a) * radius * wobbleA),
              center + new Vector3(Mathf.Cos(b) * radius * wobbleB, lift + ring * 0.01f, Mathf.Sin(b) * radius * wobbleB), Vector3.up);
          }
        }
      }
      if (_craterFilter.sharedMesh != null) {
        Destroy(_craterFilter.sharedMesh);
      }
      _craterFilter.sharedMesh = _craters.Count > 0 ? kit.ToMesh("craters") : null;
    }

    /// <summary>손상된 건물의 연기와 불. fire면 불꽃을 더한다.</summary>
    public void Burning(Vector3 position, bool fire) {
      Emit(_smoke, position + Random.insideUnitSphere * 0.6f, new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(1.2f, 2f), Random.Range(-0.3f, 0.3f)), Random.Range(0.8f, 1.3f), Random.Range(1.6f, 2.4f), SmokeDark);
      if (fire) {
        Emit(_glow, position + Random.insideUnitSphere * 0.8f, Vector3.up * Random.Range(0.6f, 1.4f), Random.Range(0.6f, 1.1f), Random.Range(0.3f, 0.5f), Flame);
      }
    }

    /// <summary>획득·점령 같은 좋은 소식의 반짝임.</summary>
    public void Burst(Vector3 position, Color color, float size) {
      var count = Scaled(14);
      for (var i = 0; i < count; i++) {
        var velocity = (Random.onUnitSphere + Vector3.up * 1.5f) * Random.Range(2f, 5f) * size;
        Emit(_tracer, position + Vector3.up, velocity, 0.18f, Random.Range(0.4f, 0.8f), color);
      }
      Emit(_glow, position + Vector3.up, Vector3.zero, size * 3f, 0.3f, color);
    }

    /// <summary>점령 완료: 땅을 따라 퍼지는 팀 색 고리.</summary>
    public void Pulse(Vector3 position, Color color, float radius) {
      var count = Scaled(24);
      for (var i = 0; i < count; i++) {
        var angle = Mathf.PI * 2f * i / count;
        var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        Emit(_glow, position + direction * 1.5f + Vector3.up * 0.3f, direction * radius * 1.6f, 0.9f, 0.6f, color);
      }
    }

    /// <summary>큰 폭발의 화면 흔들림. 설정에서 끌 수 있다.</summary>
    public void Shake(float amount) {
      if (ScreenShake.Enabled) {
        ShakeAmount = Mathf.Max(ShakeAmount, amount);
      }
    }

    public void Explosion(Vector3 position, float size) {
      if (size >= 2f) {
        Shake(size * 0.12f);
      }
      var flames = Scaled(8);
      for (var i = 0; i < flames; i++) {
        Emit(_glow, position + Random.insideUnitSphere * size * 0.5f, Random.insideUnitSphere * size * 2f + Vector3.up * size, size * Random.Range(1f, 1.8f), Random.Range(0.3f, 0.5f), Flame);
      }
      var sparks = Scaled(10);
      for (var i = 0; i < sparks; i++) {
        var velocity = (Random.onUnitSphere + Vector3.up * 1.2f) * size * Random.Range(3f, 8f);
        Emit(_tracer, position, velocity, 0.16f, Random.Range(0.3f, 0.6f), Spark);
      }
      var smoke = Scaled(6);
      for (var i = 0; i < smoke; i++) {
        Emit(_smoke, position + Random.insideUnitSphere * size * 0.6f, Vector3.up * Random.Range(1f, 2.4f), size * Random.Range(0.9f, 1.5f), Random.Range(1.2f, 2.2f), SmokeDark);
      }
    }

    /// <summary>울타리가 내려갈 때 바닥 먼지. 낮음 품질에서는 끈다.</summary>
    public void GroundDust(Vector3 position) {
      if (GraphicsQuality.Current == QualityTier.Low) {
        return;
      }
      var count = Scaled(3);
      for (var i = 0; i < count; i++) {
        var velocity = new Vector3(Random.Range(-1f, 1f), Random.Range(0.3f, 0.9f), Random.Range(-1f, 1f));
        Emit(_smoke, position + Random.insideUnitSphere * 0.6f, velocity, Random.Range(0.8f, 1.4f), Random.Range(0.9f, 1.5f), DustBrown);
      }
    }

    public void ChimneySmoke(Vector3 position) {
      Emit(_smoke, position, new Vector3(Random.Range(0.2f, 0.6f), Random.Range(1.4f, 2.2f), Random.Range(-0.2f, 0.2f)), Random.Range(0.5f, 0.8f), Random.Range(2.2f, 3.2f), SmokeGray);
    }

    Mesh Rubble() {
      if (_rubble == null) {
        var kit = new MeshKit();
        kit.ShadeFaces(5, Swatch.BoulderDark, Swatch.Black, Swatch.Rubber);
        for (var i = 0; i < 7; i++) {
          var angle = i * 0.9f;
          var distance = 0.25f + MeshKit.Hash(5, i) * 0.7f;
          var size = 0.2f + MeshKit.Hash(5, i + 10) * 0.3f;
          kit.Rock(new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance), size, size * 0.9f, i + 2);
        }
        kit.EndShade();
        _rubble = kit.ToMesh("rubble");
      }
      return _rubble;
    }

    /// <summary>파괴된 자리에 잠시 남는 잔해. 오래되면 땅으로 가라앉는다.</summary>
    public void Wreck(Vector3 position, float radius) => Wreck(position, radius, WreckSeconds);

    public void Wreck(Vector3 position, float radius, float seconds) {
      if (_wrecks.Count >= MaxWrecks) {
        Destroy(_wrecks.Dequeue().Item);
      }
      var item = new GameObject("Wreck", typeof(MeshFilter), typeof(MeshRenderer));
      item.transform.SetParent(transform, false);
      item.transform.SetPositionAndRotation(new Vector3(position.x, 0, position.z), Quaternion.Euler(0, Random.Range(0f, 360f), 0));
      item.transform.localScale = new Vector3(radius, radius * 0.8f, radius);
      item.GetComponent<MeshFilter>().sharedMesh = Rubble();
      var renderer = item.GetComponent<MeshRenderer>();
      renderer.sharedMaterial = ArtLook.NeutralMaterial;
      renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      _wrecks.Enqueue((item, Time.time - (WreckSeconds - seconds), 0f));
    }

    void LateUpdate() {
      var camera = Camera.main;
      if (ShakeAmount > 0.001f && camera != null) {
        camera.transform.position += Random.insideUnitSphere * ShakeAmount;
        ShakeAmount = Mathf.MoveTowards(ShakeAmount, 0f, Time.deltaTime * 1.5f);
      }
    }

    void Update() {
      if (_craters.Count > 0 && Time.time - _craters[0].Born > CraterSeconds) {
        _craters.RemoveAll(c => Time.time - c.Born > CraterSeconds);
        _cratersDirty = true;
      }
      if (_cratersDirty) {
        RebuildCraters();
      }
      while (_wrecks.Count > 0 && Time.time - _wrecks.Peek().Born > WreckSeconds + 2f) {
        Destroy(_wrecks.Dequeue().Item);
      }
      foreach (var wreck in _wrecks) {
        var age = Time.time - wreck.Born;
        if (age > WreckSeconds) {
          wreck.Item.transform.position += Vector3.down * (WreckSink * Time.deltaTime);
        }
      }
    }
  }
}
