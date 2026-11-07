namespace FactoryWars.View {
  using System.Collections.Generic;
  using UnityEngine;

  /// <summary>
  /// 포구 화염, 탄 궤적, 피격 불꽃, 폭발, 연기, 잔해. 파티클 시스템 셋을 공유해 호출 수를 고정한다.
  /// 양은 품질 단계로 줄인다. 시뮬레이션과 무관한 화면 연출이라 UnityEngine.Random을 써도 된다.
  /// </summary>
  public class BattleFx : MonoBehaviour {
    const int MaxWrecks = 24;
    const float WreckSeconds = 14f;
    const float WreckSink = 1.2f;

    static readonly Color Muzzle = new Color(1f, 0.62f, 0.2f, 1f);
    static readonly Color Flame = new Color(1f, 0.42f, 0.08f, 1f);
    static readonly Color Spark = new Color(1f, 0.74f, 0.32f, 1f);
    static readonly Color Bullet = new Color(1f, 0.82f, 0.5f, 1f);
    static readonly Color Missile = new Color(0.72f, 0.92f, 1f, 1f);
    static readonly Color SmokeGray = new Color(0.52f, 0.52f, 0.52f, 0.5f);
    static readonly Color SmokeDark = new Color(0.16f, 0.15f, 0.14f, 0.62f);

    ParticleSystem _glow;
    ParticleSystem _tracer;
    ParticleSystem _smoke;
    Mesh _rubble;
    readonly Queue<(GameObject Item, float Born, float Y)> _wrecks = new Queue<(GameObject, float, float)>();

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
        var trail = Scaled(4);
        for (var i = 1; i <= trail; i++) {
          Emit(_smoke, Vector3.Lerp(from, to, i / (trail + 1f)), Vector3.up * 0.4f, 0.5f, 0.7f, SmokeGray);
        }
      }
    }

    public void Impact(Vector3 position, float size) {
      var count = Scaled(5);
      for (var i = 0; i < count; i++) {
        var velocity = (Random.onUnitSphere + Vector3.up) * Random.Range(2f, 6f);
        Emit(_tracer, position, velocity, 0.12f * size, Random.Range(0.15f, 0.32f), Spark);
      }
      Emit(_glow, position, Vector3.zero, size, 0.18f, Spark);
    }

    public void Explosion(Vector3 position, float size) {
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
    public void Wreck(Vector3 position, float radius) {
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
      _wrecks.Enqueue((item, Time.time, 0f));
    }

    void Update() {
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
