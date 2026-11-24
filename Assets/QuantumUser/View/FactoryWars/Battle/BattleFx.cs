namespace FactoryWars.View {
  using System.Collections.Generic;
  using UnityEngine;

  /// <summary>
  /// 포구 화염, 탄 궤적, 피격 불꽃, 폭발, 연기, 잔해. 파티클 시스템 셋을 공유해 호출 수를 고정한다.
  /// 양은 품질 단계로 줄인다. 시뮬레이션과 무관한 화면 연출이라 UnityEngine.Random을 써도 된다.
  /// </summary>
  public class BattleFx : MonoBehaviour {
    const int MaxWrecks = 24;
    const int MaxPermanentRubble = 48;
    const float WreckSeconds = 30f;
    const float WreckSink = 1.2f;
    // 모바일 안전 링-버퍼 데칼 풀: 최대 120개, 일반 그을음은 75초 유지 + 10초 부드러운 알파 페이드아웃, 중화기/전략 미사일 크레이터는 영구 유지.
    public const int MaxDecalPool = 120;
    public const float CraterHoldSeconds = 75f;
    public const float CraterFadeSeconds = 10f;
    static int MaxCraters => GraphicsQuality.Current == QualityTier.Low ? 64 : MaxDecalPool;

    // 전투 가독성: 포구 섬광 주황(#FF6B00 계열), 소총·기관총 예광 호박색(#FFD166), 포탄 연노랑(#FFF3B0), 미사일 주황 불꽃.
    static readonly Color Muzzle = new Color(1f, 0.42f, 0f, 1f);
    static readonly Color Flame = new Color(1f, 0.42f, 0.08f, 1f);
    static readonly Color Spark = new Color(1f, 0.74f, 0.32f, 1f);
    static readonly Color Bullet = new Color(1f, 0.82f, 0.4f, 1f);
    static readonly Color Shell = new Color(1f, 0.95f, 0.69f, 1f);
    static readonly Color Missile = new Color(1f, 0.55f, 0.15f, 1f);
    static readonly Color SmokeGray = new Color(0.52f, 0.52f, 0.52f, 0.5f);
    static readonly Color SmokeDark = new Color(0.16f, 0.15f, 0.14f, 0.62f);
    static readonly Color DustBrown = new Color(0.62f, 0.54f, 0.4f, 0.45f);

    ParticleSystem _glow;
    ParticleSystem _tracer;
    ParticleSystem _smoke;
    ParticleSystem _needle;
    ParticleSystem _flash;
    static readonly Color NeedleColor = new Color(1f, 0.88f, 0.4f, 1f);
    static readonly Color FlashCore = new Color(1f, 0.78f, 0.38f, 1f);
    static readonly Color SmokeBillow = new Color(0.2f, 0.2f, 0.21f, 0.62f);
    Mesh _rubble;
    readonly Queue<(GameObject Item, float Born, Vector3 Pos, float Radius, float NextSmoke)> _wrecks = new Queue<(GameObject, float, Vector3, float, float)>();
    readonly List<GameObject> _permanentRubble = new List<GameObject>();
    readonly List<(Vector3 Position, float Size, float Born, bool Permanent)> _craters = new List<(Vector3, float, float, bool)>();
    MeshFilter _craterFilter;
    bool _cratersDirty;
    float _nextCraterFadeRebuild;
    public int CraterCount => _craters.Count;
    public int PermanentCraterCount { get { var n = 0; foreach (var c in _craters) { if (c.Permanent) n++; } return n; } }
    public int PermanentRubbleCount => _permanentRubble.Count;
    public float ShakeAmount { get; private set; }

    public int Emitted { get; private set; }
    const int MlrsImpactPoolCapacity = 8;
    sealed class PooledImpact {
      public GameObject Root;
      public ParticleSystem[] Systems;
      public Vector3 Scale;
      public float Until;
    }
    readonly List<PooledImpact> _mlrsImpacts = new List<PooledImpact>(MlrsImpactPoolCapacity);

    public void MlrsExhaust(Vector3 position, Vector3 direction, bool trail) {
      Emit(_flash, position, -direction * 2.2f, trail ? 0.50f : 1.65f, trail ? 0.10f : 0.26f, Muzzle);
      Emit(_glow, position, -direction * 5f, trail ? 0.65f : 1.6f, 0.2f, Missile);
      var puffs = trail ? 1 : Scaled(5);
      for (var i = 0; i < puffs; ++i) {
        Emit(_smoke, position - direction * (i * 0.22f), -direction * (trail ? 0.4f : 2.9f) + Vector3.up,
          trail ? 0.55f : 1.2f, 1.1f, SmokeGray);
      }
    }

    public void MlrsImpact(Vector3 position, float radius) {
      GameAudio.PlayAt(SoundEvent.Explosion, position);
      Shake(0.24f);
      var now = Time.time;
      PooledImpact item = null;
      foreach (var entry in _mlrsImpacts) {
        if (entry.Until > now) continue;
        item = entry;
        break;
      }
      if (TryResolveWarFx("explosion", out var prefab, out var scale, out var lifetime)
          && (item != null || _mlrsImpacts.Count < MlrsImpactPoolCapacity)) {
        if (item == null) {
          var root = Instantiate(prefab, transform);
          item = new PooledImpact { Root = root, Systems = root.GetComponentsInChildren<ParticleSystem>(true), Scale = root.transform.localScale };
          _mlrsImpacts.Add(item);
        }
        var rootTransform = item.Root.transform;
        rootTransform.SetPositionAndRotation(position + Vector3.up * 0.12f, Quaternion.identity);
        rootTransform.localScale = item.Scale * (scale * Mathf.Clamp(radius * 0.58f, 0.8f, 2.4f));
        item.Root.SetActive(true);
        foreach (var particles in item.Systems) {
          particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
          particles.Simulate(0.18f, false, true);
          particles.Play(false);
        }
        item.Until = now + Mathf.Max(0.25f, lifetime);
      } else {
        Emit(_flash, position + Vector3.up, Vector3.zero, radius * 1.2f, 0.30f, FlashCore);
        Emit(_smoke, position + Vector3.up, Vector3.up * 2f, radius, 1.8f, SmokeBillow);
      }
    }

    // 비행 중인 미사일: 2차 베지어(위로 솟는 조종점 + 옆 흔들림)를 따라 머리 불꽃과 연기를 뿌린다.
    class Flight {
      public Vector3 From, Control, To;
      public float T, Duration, Puff, Impact;
    }
    readonly List<Flight> _flights = new List<Flight>();
    public int MissilesInFlight => _flights.Count;
    public int MissilesLaunched { get; private set; }

    public void LaunchMissile(Vector3 from, Vector3 to, float speed, float impact) {
      var delta = to - from;
      var distance = delta.magnitude;
      if (distance < 0.5f) {
        Impact(to, impact, delta);
        return;
      }
      var side = Vector3.Cross(delta / distance, Vector3.up) * (Random.Range(-0.18f, 0.18f) * distance);
      var control = (from + to) * 0.5f + Vector3.up * (distance * 0.22f + 1.5f) + side;
      _flights.Add(new Flight { From = from, Control = control, To = to, Duration = distance * 1.15f / Mathf.Max(speed, 1f), Impact = impact });
      MissilesLaunched++;
    }

    static Vector3 Bezier(Flight f, float t) {
      var u = 1f - t;
      return u * u * f.From + 2f * u * t * f.Control + t * t * f.To;
    }

    void UpdateFlights(float dt) {
      for (var i = _flights.Count - 1; i >= 0; i--) {
        var f = _flights[i];
        f.T = Mathf.Min(1f, f.T + dt / Mathf.Max(f.Duration, 0.05f));
        var head = Bezier(f, f.T);
        var ahead = Bezier(f, Mathf.Min(1f, f.T + 0.05f)) - head;
        Emit(_glow, head, Vector3.zero, 0.55f, 0.07f, Missile);
        Emit(_glow, head - ahead.normalized * 0.25f, -ahead.normalized * 2f, 0.35f, 0.1f, Muzzle);
        f.Puff -= dt;
        if (f.Puff <= 0f) {
          f.Puff = 0.03f / Mathf.Max(GraphicsQuality.FxScale, 0.25f);
          Emit(_smoke, head, Vector3.up * 0.25f + Random.insideUnitSphere * 0.15f, 0.45f, 1.5f, SmokeGray);
        }
        if (f.T >= 1f) {
          Impact(f.To, f.Impact, f.To - f.Control);
          _flights.RemoveAt(i);
        }
      }
    }

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
      // 바늘 예광탄: 폭 0.035m, 길이 1.6m로 늘인다(속도와 무관한 길이).
      _needle = Make(transform, "Needle", "FxGlow", 300, true, 1f);
      var needle = _needle.GetComponent<ParticleSystemRenderer>();
      needle.lengthScale = 1.6f / BattleView.NeedleWidth;
      needle.velocityScale = 0f;
      // 폭발 중심 섬광: 0.25초 동안 커지며 사라진다.
      _flash = Make(transform, "Flash", "FxGlow", 60, false, 2.4f);
    }

    static int Scaled(int count) => Mathf.Max(1, Mathf.RoundToInt(count * GraphicsQuality.FxScale));

    void Emit(ParticleSystem system, Vector3 position, Vector3 velocity, float size, float life, Color color) {
      var parameters = new ParticleSystem.EmitParams {
        position = position, velocity = velocity, startSize = size, startLifetime = life, startColor = color, applyShapeToPosition = false,
      };
      system.Emit(parameters, 1);
      Emitted++;
    }

    static ViewCatalog _viewCatalog;

    static bool TryResolveWarFx(string fxName, out GameObject prefab, out float scale, out float lifetime) {
      if (VisualCatalog.TryFilled(VisualCatalog.FxId(fxName), out var slot) && slot.Prefab != null) {
        prefab = slot.Prefab;
        scale = slot.Scale > 0f ? slot.Scale : 1f;
        lifetime = slot.Lifetime > 0f ? slot.Lifetime : 4f;
        return true;
      }
      _viewCatalog ??= Resources.Load<ViewCatalog>(ViewCatalog.ResourcePath);
      if (_viewCatalog != null && _viewCatalog.TryGetWarFx(fxName, out var wfx) && wfx.Prefab != null) {
        prefab = wfx.Prefab;
        scale = wfx.Scale > 0f ? wfx.Scale : 1f;
        lifetime = wfx.Lifetime > 0f ? wfx.Lifetime : 4f;
        return true;
      }
      prefab = null;
      scale = 1f;
      lifetime = 3f;
      return false;
    }

    /// <summary>Jean Moreno War FX(WFX_*) 프리팹을 VisualCatalog/ViewCatalog에서 직접 생성하고 필요 시 simulateSeconds만큼 선행 시뮬레이션한다.</summary>
    public GameObject SpawnWarFx(string fxName, Vector3 position, Quaternion rotation, float scaleMultiplier = 1f, float simulateSeconds = 0f) {
      if (TryResolveWarFx(fxName, out var prefab, out var baseScale, out var baseLife) == false) {
        return null;
      }
      var item = Instantiate(prefab, position, rotation);
      item.transform.SetParent(transform, true);
      var s = Mathf.Max(0.05f, scaleMultiplier * baseScale);
      item.transform.localScale *= s;
      // 즉각 가시성 확보: War FX 파티클이 생성 즉시 불꽃·버섯구름·연기를 분출하도록 선행 시뮬레이션.
      var systems = item.GetComponentsInChildren<ParticleSystem>(true);
      foreach (var ps in systems) {
        if (simulateSeconds > 0f) {
          ps.Simulate(simulateSeconds, false, true);
        }
        ps.Play(false);
      }
      var life = Mathf.Max(0.18f, baseLife);
      Destroy(item, life);
      return item;
    }

    public void MuzzleFlash(Vector3 position, Vector3 forward, float size) {
      var rot = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward) : Quaternion.identity;
      var key = size >= 1.1f ? "muzzle_heavy" : "muzzle";
      if (SpawnWarFx(key, position, rot, Mathf.Clamp(size * 0.85f, 0.45f, 1.5f), 0.02f) != null) {
        return;
      }
      var count = Scaled(4);
      for (var i = 0; i < count; i++) {
        Emit(_glow, position + forward * (0.2f * i), forward * Random.Range(2f, 7f) + Random.insideUnitSphere, size * Random.Range(0.6f, 1.2f), 0.14f, Muzzle);
      }
    }

    public void FlameJet(Vector3 position, Vector3 forward, float reach) {
      var rot = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward) : Quaternion.identity;
      if (SpawnWarFx("flame", position, rot, Mathf.Clamp(reach * 0.25f, 0.7f, 1.6f), 0.12f) != null) {
        return;
      }
      var count = Scaled(10);
      for (var i = 0; i < count; i++) {
        var velocity = (forward + Random.insideUnitSphere * 0.18f) * reach * Random.Range(1.6f, 2.6f);
        Emit(_glow, position, velocity, Random.Range(0.5f, 1.1f), Random.Range(0.3f, 0.45f), Flame);
      }
    }

    /// <summary>소총 예광탄: 총구에서 표적까지 바늘처럼 가는 빛줄기(기본 #FFE066, Mk.2/Mk.3 색상 진화 지원).</summary>
    public void Needle(Vector3 from, Vector3 to, float speed, Color? tint = null) {
      var delta = to - from;
      var distance = delta.magnitude;
      if (distance < 0.5f) {
        return;
      }
      Emit(_needle, from, delta / distance * speed, BattleView.NeedleWidth, distance / speed + 0.03f, tint ?? NeedleColor);
    }

    public int NeedleCount => _needle != null ? _needle.particleCount : 0;

    /// <summary>탄 궤적. 빠른 입자 하나를 쏘고 도착 시점에 사라지게 한다(Mk.2/Mk.3 색상 진화 지원).</summary>
    public void Tracer(Vector3 from, Vector3 to, float speed, float width, bool missile, Color? tint = null) {
      var delta = to - from;
      var distance = delta.magnitude;
      if (distance < 0.5f) {
        return;
      }
      Emit(_tracer, from, delta / distance * speed, width, distance / speed + 0.04f, tint ?? (missile ? Missile : width > 0.25f ? Shell : Bullet));
      if (missile == false && width > 0.25f) {
        // 포탄: 옅은 흰·회색 연기 꼬리.
        var ribbon = Scaled(4);
        for (var i = 1; i <= ribbon; i++) {
          Emit(_smoke, Vector3.Lerp(from, to, i / (ribbon + 1f)), Vector3.up * 0.2f, 0.35f, 0.7f, SmokeGray);
        }
      }
      if (missile) {
        // 미사일 연기 꼬리: 길고 굵게, 오래 남는다.
        var trail = Scaled(10);
        for (var i = 1; i <= trail; i++) {
          var t = i / (trail + 1f);
          Emit(_smoke, Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * distance * 0.08f, Vector3.up * 0.3f, 0.6f + t * 0.4f, 1.2f + t * 0.6f, SmokeGray);
        }
      }
    }

    /// <summary>전차·MLRS·포병·보병 피격: 중화기(size>=1.0)는 War FX WFX_ExplosiveSmokeGround/WFX_Explosion, 소총탄은 WFX_BImpact Dirt 재생.</summary>
    public void Impact(Vector3 position, float size, Vector3 incoming) {
      var normal = incoming.sqrMagnitude > 0.001f ? -incoming.normalized : Vector3.up;
      if (size >= 1.0f) {
        var key = size >= 1.5f ? "explosion" : "explosion_generic";
        if (SpawnWarFx(key, position + Vector3.up * 0.12f, Quaternion.identity, Mathf.Clamp(size * 0.58f, 0.55f, 1.6f), 0.12f) != null) {
          if (size >= 1.35f) Crater(position, size * 0.72f);
          return;
        }
      } else {
        var rot = Quaternion.LookRotation(normal);
        if (SpawnWarFx("bullet_impact", position + Vector3.up * 0.25f, rot, Mathf.Clamp(size, 0.45f, 0.95f), 0.04f) != null) {
          return;
        }
      }
      Emit(_glow, position + Vector3.up * 0.3f, Vector3.zero, size * 1.2f, 0.07f, FlashCore);
      var count = Scaled(3);
      for (var i = 0; i < count; i++) {
        var velocity = (normal + Vector3.up * 0.6f + Random.insideUnitSphere * 0.6f) * Random.Range(3f, 7f);
        Emit(_needle, position, velocity, 0.03f, Random.Range(0.08f, 0.16f), Spark);
      }
      Emit(_smoke, position + Vector3.up * 0.2f, Vector3.up * 0.4f + normal * 0.3f, 0.5f * size, 0.6f, DustBrown);
    }

    public void Impact(Vector3 position, float size) {
      if (size >= 1.0f && SpawnWarFx("explosion_generic", position + Vector3.up * 0.15f, Quaternion.identity, Mathf.Clamp(size * 0.6f, 0.55f, 1.5f), 0.12f) != null) {
        return;
      }
      if (SpawnWarFx("bullet_impact", position + Vector3.up * 0.25f, Quaternion.identity, Mathf.Clamp(size, 0.5f, 1.0f), 0.04f) != null) {
        return;
      }
      // 피격 번쩍임 폴백.
      Emit(_glow, position + Vector3.up * 0.3f, Vector3.zero, size * 1.6f, 0.08f, Color.white);
      var count = Scaled(5);
      for (var i = 0; i < count; i++) {
        var velocity = (Random.onUnitSphere + Vector3.up) * Random.Range(2f, 6f);
        Emit(_tracer, position, velocity, 0.12f * size, Random.Range(0.15f, 0.32f), Spark);
      }
      Emit(_glow, position, Vector3.zero, size, 0.18f, Spark);
    }

    /// <summary>땅에 남는 그을음·크레이터 링-버퍼(최대 120개, 1 드로우콜). permanent=true면 경기 끝까지 영구 잔존.</summary>
    public void Crater(Vector3 position, float size, bool permanent = false) {
      var cap = MaxCraters;
      if (_craters.Count >= cap) {
        // 영구 크레이터는 보존하고 가장 오래된 일반 그을음부터 재활용한다.
        var evict = -1;
        for (var i = 0; i < _craters.Count; i++) {
          if (_craters[i].Permanent == false) {
            evict = i;
            break;
          }
        }
        _craters.RemoveAt(evict >= 0 ? evict : 0);
      }
      _craters.Add((new Vector3(position.x, 0f, position.z), size, Time.time, permanent));
      _cratersDirty = true;
    }

    static Material _scorchMat;
    static Texture2D _scorchTex;

    public static Material ScorchMaterial() {
      if (_scorchMat != null) {
        return _scorchMat;
      }
      const int TexSize = 64;
      _scorchTex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false) {
        name = "ScorchDecalTex",
        wrapMode = TextureWrapMode.Clamp,
        filterMode = FilterMode.Bilinear,
      };
      var pixels = new Color32[TexSize * TexSize];
      var half = (TexSize - 1) * 0.5f;
      for (var y = 0; y < TexSize; y++) {
        for (var x = 0; x < TexSize; x++) {
          var dx = (x - half) / half;
          var dy = (y - half) / half;
          var dist = Mathf.Sqrt(dx * dx + dy * dy);
          var falloff = Mathf.Clamp01(1f - dist);
          var alpha = (byte)Mathf.RoundToInt(falloff * falloff * 210f);
          // #181A1B 부드러운 어두운 그을음 데칼
          pixels[y * TexSize + x] = new Color32(0x18, 0x1A, 0x1B, alpha);
        }
      }
      _scorchTex.SetPixels32(pixels);
      _scorchTex.Apply(false);
      var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
      _scorchMat = new Material(shader) { name = "FlatScorchDecal" };
      _scorchMat.mainTexture = _scorchTex;
      if (_scorchMat.HasProperty("_BaseMap")) {
        _scorchMat.SetTexture("_BaseMap", _scorchTex);
      }
      if (_scorchMat.HasProperty("_BaseColor")) {
        _scorchMat.SetColor("_BaseColor", Color.white);
      }
      if (_scorchMat.HasProperty("_Surface")) {
        _scorchMat.SetFloat("_Surface", 1f);
      }
      if (_scorchMat.HasProperty("_ZWrite")) {
        _scorchMat.SetFloat("_ZWrite", 0f);
      }
      _scorchMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
      _scorchMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
      _scorchMat.renderQueue = 2460;
      return _scorchMat;
    }

    void RebuildCraters() {
      _cratersDirty = false;
      if (_craterFilter == null) {
        var item = new GameObject("Craters", typeof(MeshFilter), typeof(MeshRenderer));
        item.transform.SetParent(transform, false);
        _craterFilter = item.GetComponent<MeshFilter>();
        var renderer = item.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = ScorchMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
      }
      var mesh = new Mesh { name = "scorch_decals" };
      var count = _craters.Count;
      if (count > 0) {
        var now = Time.time;
        var verts = new Vector3[count * 4];
        var uvs = new Vector2[count * 4];
        var cols = new Color32[count * 4];
        var tris = new int[count * 6];
        for (var c = 0; c < count; c++) {
          var (center, size, born, permanent) = _craters[c];
          var age = now - born;
          var alpha = permanent || age <= CraterHoldSeconds ? 1f : 1f - Mathf.Clamp01((age - CraterHoldSeconds) / CraterFadeSeconds);
          var col = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
          var y = 0.02f + c * 0.0002f;
          var r = size * 1.15f;
          var v = c * 4;
          verts[v + 0] = new Vector3(center.x - r, y, center.z - r);
          verts[v + 1] = new Vector3(center.x + r, y, center.z - r);
          verts[v + 2] = new Vector3(center.x + r, y, center.z + r);
          verts[v + 3] = new Vector3(center.x - r, y, center.z + r);
          uvs[v + 0] = new Vector2(0f, 0f);
          uvs[v + 1] = new Vector2(1f, 0f);
          uvs[v + 2] = new Vector2(1f, 1f);
          uvs[v + 3] = new Vector2(0f, 1f);
          cols[v + 0] = col; cols[v + 1] = col; cols[v + 2] = col; cols[v + 3] = col;
          var t = c * 6;
          tris[t + 0] = v + 0; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
          tris[t + 3] = v + 0; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
        }
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.colors32 = cols;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
      }
      if (_craterFilter.sharedMesh != null) {
        Destroy(_craterFilter.sharedMesh);
      }
      _craterFilter.sharedMesh = count > 0 ? mesh : null;
    }

    /// <summary>손상된 건물의 연기와 불(War FX WFXMR_Fire Natural + WFX_Smoke 연동).</summary>
    public void Burning(Vector3 position, bool fire) {
      if (fire && SpawnWarFx("fire_black", position, Quaternion.identity, 0.55f, 0.25f) != null) {
        return;
      }
      if (fire == false && SpawnWarFx("smoke_black", position, Quaternion.identity, 0.55f, 0.2f) != null) {
        return;
      }
      Emit(_smoke, position + Random.insideUnitSphere * 0.6f, new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(1.2f, 2f), Random.Range(-0.3f, 0.3f)), Random.Range(0.8f, 1.3f), Random.Range(1.6f, 2.4f), SmokeDark);
      if (fire) {
        Emit(_glow, position + Random.insideUnitSphere * 0.8f, Vector3.up * Random.Range(0.6f, 1.4f), Random.Range(0.6f, 1.1f), Random.Range(0.3f, 0.5f), Flame);
      }
    }

    static readonly Color SignalSmoke = new Color(0.95f, 0.18f, 0.15f, 0.8f);

    /// <summary>보급 투하 예고: 낙하 지점에서 피어오르는 주황 신호 연기.</summary>
    public void Signal(Vector3 position) {
      Emit(_smoke, position + Random.insideUnitSphere * 0.3f, new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(2.2f, 3f), Random.Range(-0.2f, 0.2f)), Random.Range(0.7f, 1.1f), Random.Range(2.4f, 3.2f), SignalSmoke);
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

    /// <summary>항목 33: 땅 위에 잠깐 머무는 고리(미사일 착탄 반경 예고). 주기적으로 다시 부르면 계속 보인다. 기존 빛 입자를 쓴다.</summary>
    public void Ring(Vector3 position, Color color, float radius, float seconds) {
      var count = Mathf.Max(24, Scaled(56));
      for (var i = 0; i < count; i++) {
        var angle = Mathf.PI * 2f * i / count;
        Emit(_glow, position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius + Vector3.up * 0.3f, Vector3.zero, 1.1f, seconds, color);
      }
    }

    /// <summary>큰 폭발의 화면 흔들림. 설정에서 끌 수 있다.</summary>
    public void Shake(float amount) {
      if (ScreenShake.Enabled) {
        ShakeAmount = Mathf.Max(ShakeAmount, amount);
      }
    }

    public void Explosion(Vector3 position, float size) {
      GameAudio.Play(SoundEvent.Explosion);
      if (size >= 1.2f) {
        Crater(position, size * 0.9f);
      }
      if (size >= 2f) {
        Shake(size * 0.12f);
      }
      // Jean Moreno War FX(WFXMR_ExplosiveSmokeGround / WFXMR_Explosion Small) 직접 재생.
      var wfx = SpawnWarFx(size >= 1.1f ? "explosion" : "explosion_small", position + Vector3.up * 0.15f, Quaternion.identity, Mathf.Clamp(size * 0.72f, 0.55f, 2.6f), 0.15f);
      if (wfx != null) {
        if (size < 1.2f) Crater(position, size * 0.7f);
        return;
      }
      // 폴백 파티클(원시 구체·링 없음).
      Emit(_flash, position + Vector3.up * (0.6f * size), Vector3.zero, size * 1.7f, 0.25f, FlashCore);
      var flames = Scaled(4);
      for (var i = 0; i < flames; i++) {
        Emit(_glow, position + Random.insideUnitSphere * size * 0.4f, Random.insideUnitSphere * size * 1.2f + Vector3.up * size * 1.4f, size * Random.Range(0.8f, 1.3f), Random.Range(0.3f, 0.45f), Flame);
      }
      var smoke = Scaled(7);
      for (var i = 0; i < smoke; i++) {
        var drift = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0.9f, 1.8f), Random.Range(-0.5f, 0.5f));
        Emit(_smoke, position + Vector3.up * (0.5f * size) + Random.insideUnitSphere * size * 0.5f, drift, size * Random.Range(1f, 1.5f), Random.Range(2f, 3.2f), SmokeBillow);
      }
      var sparks = Scaled(4);
      for (var i = 0; i < sparks; i++) {
        var velocity = (Random.onUnitSphere + Vector3.up * 1.2f) * size * Random.Range(3f, 6f);
        Emit(_needle, position, velocity, 0.04f, Random.Range(0.1f, 0.2f), Spark);
      }
      if (size < 1.2f) {
        Crater(position, size * 0.7f);
      }
    }

    /// <summary>전략 미사일 발사 시 격납고 문 주변으로 뿜어져 나오는 하얀 배기 증기(WFX_SmokeGrenade White).</summary>
    public void SiloSteam(Vector3 position) {
      if (SpawnWarFx("smoke_white", position + Vector3.up * 0.5f, Quaternion.identity, 1.85f, 0.25f) != null) {
        Emit(_flash, position + Vector3.up * 1.4f, Vector3.zero, 3.2f, 0.35f, FlashCore);
        return;
      }
      var steamCol = new Color(0.92f, 0.94f, 0.96f, 0.85f);
      for (var i = 0; i < 14; i++) {
        var angle = i * (Mathf.PI * 2f / 14f);
        var dir = new Vector3(Mathf.Cos(angle), 0.35f, Mathf.Sin(angle)).normalized;
        Emit(_smoke, position + Vector3.up * 0.8f + dir * 1.1f, dir * Random.Range(3.5f, 6.5f) + Vector3.up * 2.2f, Random.Range(1.6f, 2.6f), Random.Range(1.1f, 1.8f), steamCol);
      }
      Emit(_flash, position + Vector3.up * 1.4f, Vector3.zero, 3.2f, 0.35f, FlashCore);
    }

    /// <summary>전략 미사일 착탄 · 공중항모 추락: 실제 War FX WFX_Nuke + WFX_ExplosiveSmokeGround Big + WFX_Fire Natural Broad + WFX_SmokeGrenade Black + 영구 크레이터.</summary>
    public void MushroomExplosion(Vector3 position, float size) {
      GameAudio.Play(SoundEvent.Explosion);
      Shake(1.15f);
      var ground = new Vector3(position.x, 0f, position.z);
      // 중심 거대 영구 크레이터 + 외곽 방사형 그을음 얼룩.
      Crater(ground, size * 2.75f, permanent: true);
      for (var c = 0; c < 5; c++) {
        var ang = c * (Mathf.PI * 2f / 5f) + 0.3f;
        Crater(ground + new Vector3(Mathf.Cos(ang) * size * 1.45f, 0f, Mathf.Sin(ang) * size * 1.45f), size * 1.15f, permanent: true);
      }
      // 실제 War FX 핵폭발(WFX_Nuke) + 다층 충격파 연기(WFX_ExplosiveSmokeGround Big) + 잔존 화염·검은 연기!
      var nukeScale = Mathf.Clamp(size * 0.26f, 0.8f, 1.55f);
      var spawnedNuke = SpawnWarFx("nuke", ground + Vector3.up * 0.1f, Quaternion.identity, nukeScale, 1.15f);
      SpawnWarFx("nuke_extra", ground + Vector3.up * 0.2f, Quaternion.identity, nukeScale * 0.9f, 0.65f);
      for (var f = 0; f < 3; f++) {
        var a = f * (Mathf.PI * 2f / 3f) + 0.4f;
        var spot = ground + new Vector3(Mathf.Cos(a) * size * 1.2f, 0.05f, Mathf.Sin(a) * size * 1.2f);
        SpawnWarFx("fire_black", spot, Quaternion.identity, 0.95f, 0.55f);
        if (f < 2) {
          SpawnWarFx("smoke_black", spot + Vector3.up * 0.4f, Quaternion.identity, 1.05f, 0.5f);
        }
      }
      if (spawnedNuke == null) {
        // War FX 미연결 환경 폴백(순수 ParticleSystem 버스트 — 원시 메쉬 없음).
        Emit(_flash, ground + Vector3.up * (0.85f * size), Vector3.zero, size * 3.4f, 0.65f, FlashCore);
        for (var i = 0; i < 20; i++) {
          var a = i * (Mathf.PI * 2f / 20f);
          var radial = new Vector3(Mathf.Cos(a), 0.18f, Mathf.Sin(a));
          Emit(_glow, ground + Vector3.up * (size * 1.4f) + radial * (size * 0.65f), radial * 2.8f + Vector3.up * 1.6f, size * 1.45f, 0.95f, Flame);
          Emit(_smoke, ground + Vector3.up * (size * 2.1f) + radial * (size * 0.95f), radial * 2.2f + Vector3.up * 1.4f, size * 1.95f, 3.2f, SmokeDark);
        }
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

    /// <summary>건설 2단계(33~99%) 철골 용접 스파크: War FX WFX_BImpact Metal + 미세 용접 아크 섬광.</summary>
    public GameObject WeldingSparks(Vector3 position, Vector3 normal) {
      var dir = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
      var rot = Quaternion.LookRotation(dir);
      var wfx = SpawnWarFx("welding_sparks", position, rot, 1.35f, 0.06f);
      // 용접 아크 중심 빛점과 가느다란 불똥 몇 가닥으로 근접 뷰 가독성 보강.
      var arcColor = new Color(1f, 0.86f, 0.42f, 1f);
      Emit(_glow, position, Vector3.zero, 0.55f, 0.14f, arcColor);
      var count = Scaled(5);
      for (var i = 0; i < count; i++) {
        var vel = (dir * 1.4f + Vector3.up * 0.9f + Random.insideUnitSphere * 0.8f) * Random.Range(1.8f, 4.2f);
        Emit(_needle, position, vel, 0.025f, Random.Range(0.14f, 0.28f), Spark);
      }
      return wfx;
    }

    /// <summary>건설 3단계(100% 완공) 비계 철거 먼지 피어오름.</summary>
    public void ConstructionCompleteDust(Vector3 position, float radius) {
      if (GraphicsQuality.Current == QualityTier.Low) {
        return;
      }
      var ring = Mathf.Max(6, Scaled(10));
      var r = Mathf.Clamp(radius * 0.85f, 1.8f, 5.5f);
      for (var i = 0; i < ring; i++) {
        var angle = (Mathf.PI * 2f * i) / ring;
        var outDir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        var p = position + outDir * r + Vector3.up * 0.25f;
        Emit(_smoke, p, outDir * Random.Range(0.7f, 1.5f) + Vector3.up * Random.Range(0.5f, 1.2f), Random.Range(1.1f, 1.7f), Random.Range(1.2f, 2.0f), DustBrown);
      }
    }

    public void ChimneySmoke(Vector3 position) {
      if (SpawnWarFx("smoke", position, Quaternion.identity, 0.55f, 0.25f) != null) {
        return;
      }
      Emit(_smoke, position, new Vector3(Random.Range(0.2f, 0.6f), Random.Range(1.4f, 2.2f), Random.Range(-0.2f, 0.2f)), Random.Range(0.5f, 0.8f), Random.Range(2.2f, 3.2f), SmokeGray);
    }

    /// <summary>어둡게 그을린 파쇄 콘크리트 슬래브 + 돌출 철근 잔해 메쉬(둥근 청회색 바위 제거).</summary>
    Mesh Rubble() {
      if (_rubble == null) {
        var kit = new MeshKit();
        // 납작하게 부서진 암회색·검은 콘크리트 바닥 파편.
        kit.Paint(Swatch.Black).Chamfer(new Vector3(0f, 0.06f, 0f), new Vector3(1.55f, 0.14f, 1.45f), 0.05f);
        for (var i = 0; i < 9; i++) {
          var angle = i * 0.72f + 0.2f;
          var dist = 0.22f + MeshKit.Hash(7, i) * 0.62f;
          var w = 0.34f + MeshKit.Hash(7, i + 10) * 0.42f;
          var h = 0.10f + MeshKit.Hash(7, i + 20) * 0.24f;
          var d = 0.30f + MeshKit.Hash(7, i + 30) * 0.38f;
          var sw = i % 3 == 0 ? Swatch.Asphalt : i % 3 == 1 ? Swatch.Rubber : Swatch.Black;
          kit.Paint(sw).Chamfer(new Vector3(Mathf.Cos(angle) * dist, h * 0.55f, Mathf.Sin(angle) * dist), new Vector3(w, h, d), 0.03f);
        }
        // 콘크리트 더미 사이로 비스듬히 솟은 검붉은 철근·H빔 파편.
        kit.Paint(Swatch.MetalDark).Box(new Vector3(-0.32f, 0.32f, 0.18f), new Vector3(0.06f, 0.64f, 0.06f));
        kit.Paint(Swatch.MetalDark).Box(new Vector3(0.28f, 0.28f, -0.24f), new Vector3(0.06f, 0.56f, 0.06f));
        kit.Paint(Swatch.Rubber).Box(new Vector3(0.05f, 0.22f, 0.35f), new Vector3(0.72f, 0.07f, 0.08f));
        kit.Paint(Swatch.MetalDark).Box(new Vector3(-0.18f, 0.25f, -0.30f), new Vector3(0.58f, 0.06f, 0.06f));
        _rubble = kit.ToMesh("charred_concrete_rubble");
      }
      return _rubble;
    }

    /// <summary>파괴된 기갑 잔해(30초간 연소·연기 후 가라앉으며 오일·그을음 자국을 남긴다).</summary>
    public void Wreck(Vector3 position, float radius) => Wreck(position, radius, WreckSeconds);

    GameObject CreateRubbleVisual(string name, Vector3 flat, float scaleXz, float scaleY) {
      if (VisualCatalog.TryFilled(VisualCatalog.FxId("rubble_pile"), out var slot) && slot.Prefab != null) {
        var go = Instantiate(slot.Prefab, flat, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
        go.name = name;
        go.transform.SetParent(transform, true);
        var s = Mathf.Clamp(scaleXz * 0.62f, 0.6f, 2.4f);
        go.transform.localScale = new Vector3(s, Mathf.Clamp(scaleY * 0.55f, 0.45f, 1.5f), s);
        return go;
      }
      var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      item.transform.SetParent(transform, false);
      item.transform.SetPositionAndRotation(flat, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
      item.transform.localScale = new Vector3(scaleXz, scaleY, scaleXz);
      item.GetComponent<MeshFilter>().sharedMesh = Rubble();
      var renderer = item.GetComponent<MeshRenderer>();
      renderer.sharedMaterial = ArtLook.NeutralMaterial;
      renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      return item;
    }

    /// <summary>A unit's own destroyed model (slot WreckPrefab) left for <paramref name="seconds"/>, with fire, in the wreck queue.</summary>
    public void ModelWreck(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale, float seconds) {
      if (_wrecks.Count >= MaxWrecks) {
        var old = _wrecks.Dequeue();
        if (old.Item != null) {
          Destroy(old.Item);
        }
      }
      var item = Instantiate(prefab, position, rotation, transform);
      item.name = "Wreck";
      item.transform.localScale = scale;
      var flat = new Vector3(position.x, 0f, position.z);
      SpawnWarFx("fire_black", flat + Vector3.up * 0.35f, Quaternion.identity, 0.65f, 0.35f);
      _wrecks.Enqueue((item, Time.time - (WreckSeconds - seconds), flat, 1.5f, Time.time + 2.8f));
    }

    public void Wreck(Vector3 position, float radius, float seconds) {
      if (_wrecks.Count >= MaxWrecks) {
        var old = _wrecks.Dequeue();
        if (old.Item != null) {
          Destroy(old.Item);
        }
      }
      var flat = new Vector3(position.x, 0f, position.z);
      var item = CreateRubbleVisual("Wreck", flat, radius, radius * 0.8f);
      SpawnWarFx("fire_black", flat + Vector3.up * 0.35f, Quaternion.identity, 0.65f, 0.35f);
      _wrecks.Enqueue((item, Time.time - (WreckSeconds - seconds), flat, radius, Time.time + 2.8f));
    }

    /// <summary>파괴된 기지 건물의 영구 붕괴 콘크리트 잔해 더미(실제 Synty SM_Prop_Rubble_Pile_01 + 영구 그을음).</summary>
    public void PermanentRubble(Vector3 position, float radius) {
      if (_permanentRubble.Count >= MaxPermanentRubble) {
        var oldest = _permanentRubble[0];
        if (oldest != null) {
          Destroy(oldest);
        }
        _permanentRubble.RemoveAt(0);
      }
      var flat = new Vector3(position.x, 0f, position.z);
      var item = CreateRubbleVisual("CollapsedBuildingRubble", flat, radius * 1.05f, radius * 0.52f);
      _permanentRubble.Add(item);
      Crater(flat, radius * 1.45f, permanent: true);
    }

    void LateUpdate() {
      var camera = Camera.main;
      if (ShakeAmount > 0.001f && camera != null) {
        camera.transform.position += Random.insideUnitSphere * ShakeAmount;
        ShakeAmount = Mathf.MoveTowards(ShakeAmount, 0f, Time.deltaTime * 1.5f);
      }
    }

    void Update() {
      UpdateFlights(Time.deltaTime);
      var now = Time.time;
      foreach (var item in _mlrsImpacts) {
        if (item.Until <= now && item.Root.activeSelf) item.Root.SetActive(false);
      }
      var maxAge = CraterHoldSeconds + CraterFadeSeconds;
      var hasFading = false;
      for (var i = _craters.Count - 1; i >= 0; i--) {
        if (_craters[i].Permanent) {
          continue;
        }
        var age = now - _craters[i].Born;
        if (age >= maxAge) {
          _craters.RemoveAt(i);
          _cratersDirty = true;
        } else if (age > CraterHoldSeconds) {
          hasFading = true;
        }
      }
      if (hasFading && now >= _nextCraterFadeRebuild) {
        _nextCraterFadeRebuild = now + 0.25f;
        _cratersDirty = true;
      }
      if (_cratersDirty) {
        RebuildCraters();
      }
      while (_wrecks.Count > 0 && now - _wrecks.Peek().Born > WreckSeconds + 2f) {
        var expired = _wrecks.Dequeue();
        if (expired.Item != null) {
          Destroy(expired.Item);
        }
        // 기갑 잔해가 가라앉은 자리에 어두운 기름·그을음 흔적 잔존.
        Crater(expired.Pos, Mathf.Max(1.1f, expired.Radius * 1.05f), permanent: false);
      }
      var wreckCount = _wrecks.Count;
      for (var w = 0; w < wreckCount; w++) {
        var wreck = _wrecks.Dequeue();
        var age = now - wreck.Born;
        if (wreck.Item != null) {
          if (age > WreckSeconds) {
            wreck.Item.transform.position += Vector3.down * (WreckSink * Time.deltaTime);
          } else if (now >= wreck.NextSmoke) {
            wreck.NextSmoke = now + 0.55f / Mathf.Max(0.35f, GraphicsQuality.FxScale);
            Burning(wreck.Pos + Vector3.up * 0.35f, age < 10f);
          }
        }
        _wrecks.Enqueue(wreck);
      }
    }
  }
}
