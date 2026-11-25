namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>
  /// URP 17.0.4 (Unity 6000.0.85f2) compatible particle VFX driven by Unity Technologies "Particle Pack | Starter Assets"
  /// textures (Resources/FactoryWars/Vfx/ParticlePack/):
  /// - Naval wake: stern foam trail (WaterMistSoftParticle), horizontal wave ripples (WaterRipplesParticle), bow splash (ImpactSplashParticle).
  /// - Fire / damage: flames (Fireball), billowing smoke (SmokePuff02), rising embers (RoundSoftParticle).
  /// All materials use "Universal Render Pipeline/Particles/Unlit" with proper alpha/additive blending (zero pink shaders).
  /// </summary>
  public static class ParticlePackVfx {
    const string TexRoot = "FactoryWars/Vfx/ParticlePack/";

    static Material _foamMat;
    static Material _rippleMat;
    static Material _splashMat;
    static Material _fireMat;
    static Material _smokeMat;
    static Material _emberMat;
    static Material _jetGlowMat;
    static Material _contrailMat;
    static Material _downwashMat;

    static Material MakeMat(string texName, bool additive, Color tint) {
      var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
        ?? Shader.Find("Particles/Standard Unlit")
        ?? Shader.Find("Universal Render Pipeline/Unlit");
      var mat = new Material(shader) { name = "VFX_" + texName, hideFlags = HideFlags.HideAndDontSave };
      var tex = Resources.Load<Texture2D>(TexRoot + texName);
      if (tex != null) {
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
      }
      if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
      if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
      mat.SetFloat("_Surface", 1f); // Transparent
      mat.SetFloat("_Blend", additive ? 2f : 0f); // 0=Alpha, 2=Additive
      mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
      mat.SetInt("_DstBlend", (int)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
      mat.SetInt("_ZWrite", 0);
      mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
      if (additive) mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
      mat.renderQueue = 3050;
      return mat;
    }

    public static Material FoamMat => _foamMat != null ? _foamMat : (_foamMat = MakeMat("WaterMistSoftParticle", true, new Color(0.82f, 0.95f, 1f, 0.88f)));
    public static Material RippleMat => _rippleMat != null ? _rippleMat : (_rippleMat = MakeMat("WaterRipplesParticle", true, new Color(0.78f, 0.94f, 1f, 0.82f)));
    public static Material SplashMat => _splashMat != null ? _splashMat : (_splashMat = MakeMat("ImpactSplashParticle", true, new Color(0.90f, 0.98f, 1f, 0.92f)));
    public static Material FireMat => _fireMat != null ? _fireMat : (_fireMat = MakeMat("Fireball", true, new Color(1f, 0.62f, 0.18f, 0.92f)));
    public static Material SmokeMat => _smokeMat != null ? _smokeMat : (_smokeMat = MakeMat("SmokePuff02", false, new Color(0.24f, 0.24f, 0.25f, 0.70f)));
    public static Material EmberMat => _emberMat != null ? _emberMat : (_emberMat = MakeMat("RoundSoftParticle", true, new Color(1f, 0.76f, 0.24f, 0.95f)));
    public static Material JetGlowMat => _jetGlowMat != null ? _jetGlowMat : (_jetGlowMat = MakeMat("RoundSoftParticle", true, new Color(1f, 0.76f, 0.28f, 0.96f)));
    public static Material ContrailMat => _contrailMat != null ? _contrailMat : (_contrailMat = MakeMat("SmokePuff02", false, new Color(0.93f, 0.96f, 1f, 0.62f)));
    public static Material DownwashMat => _downwashMat != null ? _downwashMat : (_downwashMat = MakeMat("WaterMistSoftParticle", false, new Color(0.84f, 0.81f, 0.74f, 0.50f)));

    static Gradient AlphaFade(Color start, Color end) {
      var g = new Gradient();
      g.SetKeys(
        new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
        new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(start.a, 0.18f), new GradientAlphaKey(end.a, 0.72f), new GradientAlphaKey(0f, 1f) }
      );
      return g;
    }

    static ParticleSystem AddPs(Transform parent, string name, Material mat, ParticleSystemRenderMode renderMode = ParticleSystemRenderMode.Billboard) {
      var go = new GameObject(name);
      go.transform.SetParent(parent, false);
      var ps = go.AddComponent<ParticleSystem>();
      ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
      var r = go.GetComponent<ParticleSystemRenderer>();
      r.sharedMaterial = mat;
      r.renderMode = renderMode;
      r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      r.receiveShadows = false;
      return ps;
    }

    public sealed class NavalWakeHandle {
      public readonly Transform Root;
      readonly ParticleSystem _foam;
      readonly ParticleSystem _ripples;
      readonly ParticleSystem _splash;
      readonly Transform _bowAnchor;
      readonly Transform _sternAnchor;

      public NavalWakeHandle(Transform parent, float hullHalfLen) {
        Root = new GameObject("NavalParticleWake").transform;
        Root.SetParent(parent, false);

        _sternAnchor = new GameObject("SternAnchor").transform;
        _sternAnchor.SetParent(Root, false);
        _sternAnchor.localPosition = new Vector3(0f, 0.08f, -hullHalfLen * 0.78f);

        _bowAnchor = new GameObject("BowAnchor").transform;
        _bowAnchor.SetParent(Root, false);
        _bowAnchor.localPosition = new Vector3(0f, 0.12f, hullHalfLen * 0.68f);

        // 1. Stern foam trail (World space so wake curves naturally behind turning ships)
        _foam = AddPs(_sternAnchor, "SternFoamTrail", FoamMat, ParticleSystemRenderMode.HorizontalBillboard);
        var fm = _foam.main;
        fm.simulationSpace = ParticleSystemSimulationSpace.World;
        fm.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.5f);
        fm.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        fm.startSize = new ParticleSystem.MinMaxCurve(2.2f, 3.6f);
        fm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        fm.maxParticles = 64;
        var fs = _foam.shape;
        fs.shapeType = ParticleSystemShapeType.Cone;
        fs.angle = 24f;
        fs.radius = 0.95f;
        fs.rotation = new Vector3(0f, 180f, 0f);
        var fc = _foam.colorOverLifetime;
        fc.enabled = true;
        fc.color = AlphaFade(new Color(0.90f, 0.98f, 1f, 0.85f), new Color(0.65f, 0.88f, 0.98f, 0.18f));
        var fsz = _foam.sizeOverLifetime;
        fsz.enabled = true;
        fsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.75f, 1f, 2.1f));

        // 2. Waterline concentric wave ripples around hull
        _ripples = AddPs(Root, "WaterlineRipples", RippleMat, ParticleSystemRenderMode.HorizontalBillboard);
        var rm = _ripples.main;
        rm.simulationSpace = ParticleSystemSimulationSpace.World;
        rm.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
        rm.startSpeed = 0f;
        rm.startSize = new ParticleSystem.MinMaxCurve(3.4f, 5.2f);
        rm.maxParticles = 28;
        var rs = _ripples.shape;
        rs.shapeType = ParticleSystemShapeType.Box;
        rs.scale = new Vector3(1.8f, 0.05f, hullHalfLen * 1.15f);
        var rc = _ripples.colorOverLifetime;
        rc.enabled = true;
        rc.color = AlphaFade(new Color(0.84f, 0.96f, 1f, 0.65f), new Color(0.60f, 0.85f, 0.98f, 0.12f));
        var rsz = _ripples.sizeOverLifetime;
        rsz.enabled = true;
        rsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.65f, 1f, 1.75f));

        // 3. Bow water spray splashes when underway
        _splash = AddPs(_bowAnchor, "BowWaterSplash", SplashMat, ParticleSystemRenderMode.Billboard);
        var sm = _splash.main;
        sm.simulationSpace = ParticleSystemSimulationSpace.World;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 2.8f);
        sm.startSize = new ParticleSystem.MinMaxCurve(1.1f, 2.0f);
        sm.gravityModifier = 0.65f;
        sm.maxParticles = 36;
        var ss = _splash.shape;
        ss.shapeType = ParticleSystemShapeType.Cone;
        ss.angle = 40f;
        ss.radius = 0.75f;
        ss.rotation = new Vector3(-55f, 0f, 0f);
        var sc = _splash.colorOverLifetime;
        sc.enabled = true;
        sc.color = AlphaFade(new Color(0.95f, 0.99f, 1f, 0.88f), new Color(0.75f, 0.92f, 1f, 0.18f));

        _foam.Play();
        _ripples.Play();
        _splash.Play();
      }

      public void Update(float speed, float yaw) {
        Root.localPosition = new Vector3(0f, 0.30f, 0f);
        Root.localRotation = Quaternion.Euler(0f, yaw, 0f);
        var moveFactor = Mathf.Clamp01(speed / 3.0f);
        var fe = _foam.emission;
        fe.rateOverTime = Mathf.Lerp(4f, 22f, moveFactor);
        var re = _ripples.emission;
        re.rateOverTime = Mathf.Lerp(3f, 10f, moveFactor);
        var se = _splash.emission;
        se.rateOverTime = speed > 0.25f ? Mathf.Lerp(6f, 18f, moveFactor) : 0f;
      }
    }

    public sealed class DamageFireHandle {
      public readonly Transform Root;
      readonly ParticleSystem _flames;
      readonly ParticleSystem _smoke;
      readonly ParticleSystem _embers;
      bool _active;

      public DamageFireHandle(Transform parent, float scale) {
        Root = new GameObject("DamageFireVfx").transform;
        Root.SetParent(parent, false);
        Root.localPosition = new Vector3(0f, 0.95f * scale, 0f);

        // 1. Flames (Fireball texture, additive)
        _flames = AddPs(Root, "FireFlames", FireMat, ParticleSystemRenderMode.Billboard);
        var fm = _flames.main;
        fm.simulationSpace = ParticleSystemSimulationSpace.World;
        fm.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
        fm.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        fm.startSize = new ParticleSystem.MinMaxCurve(0.95f * scale, 1.65f * scale);
        fm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        fm.maxParticles = 28;
        var fs = _flames.shape;
        fs.shapeType = ParticleSystemShapeType.Sphere;
        fs.radius = 0.55f * scale;
        var fc = _flames.colorOverLifetime;
        fc.enabled = true;
        fc.color = AlphaFade(new Color(1f, 0.78f, 0.22f, 0.95f), new Color(0.92f, 0.22f, 0.04f, 0.25f));

        // 2. Dark billowing smoke (SmokePuff02 texture, alpha)
        _smoke = AddPs(Root, "FireSmoke", SmokeMat, ParticleSystemRenderMode.Billboard);
        var sm = _smoke.main;
        sm.simulationSpace = ParticleSystemSimulationSpace.World;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.3f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(1.3f, 2.4f);
        sm.startSize = new ParticleSystem.MinMaxCurve(1.1f * scale, 2.1f * scale);
        sm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        sm.maxParticles = 24;
        var ss = _smoke.shape;
        ss.shapeType = ParticleSystemShapeType.Cone;
        ss.angle = 18f;
        ss.radius = 0.45f * scale;
        ss.rotation = new Vector3(-90f, 0f, 0f);
        var sc = _smoke.colorOverLifetime;
        sc.enabled = true;
        sc.color = AlphaFade(new Color(0.22f, 0.22f, 0.24f, 0.65f), new Color(0.35f, 0.35f, 0.38f, 0.15f));
        var ssz = _smoke.sizeOverLifetime;
        ssz.enabled = true;
        ssz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.85f));

        // 3. Rising glowing embers (RoundSoftParticle, additive)
        _embers = AddPs(Root, "FireEmbers", EmberMat, ParticleSystemRenderMode.Billboard);
        var em = _embers.main;
        em.simulationSpace = ParticleSystemSimulationSpace.World;
        em.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
        em.startSpeed = new ParticleSystem.MinMaxCurve(2.0f, 3.8f);
        em.startSize = new ParticleSystem.MinMaxCurve(0.14f * scale, 0.26f * scale);
        em.gravityModifier = -0.25f;
        em.maxParticles = 24;
        var es = _embers.shape;
        es.shapeType = ParticleSystemShapeType.Cone;
        es.angle = 28f;
        es.radius = 0.5f * scale;
        es.rotation = new Vector3(-90f, 0f, 0f);
        var ec = _embers.colorOverLifetime;
        ec.enabled = true;
        ec.color = AlphaFade(new Color(1f, 0.85f, 0.30f, 0.95f), new Color(1f, 0.35f, 0.05f, 0.1f));

        Root.gameObject.SetActive(false);
      }

      public void Update(float hpRatio) {
        var want = hpRatio > 0f && hpRatio < 0.60f;
        if (want != _active) {
          _active = want;
          Root.gameObject.SetActive(want);
          if (want) {
            _flames.Play();
            _smoke.Play();
            _embers.Play();
          } else {
            _flames.Stop();
            _smoke.Stop();
            _embers.Stop();
          }
        }
        if (!want) return;
        var severity = Mathf.InverseLerp(0.60f, 0.15f, hpRatio);
        var fe = _flames.emission;
        fe.rateOverTime = Mathf.Lerp(5f, 14f, severity);
        var se = _smoke.emission;
        se.rateOverTime = Mathf.Lerp(4f, 10f, severity);
        var ee = _embers.emission;
        ee.rateOverTime = Mathf.Lerp(4f, 12f, severity);
      }
    }

    public sealed class AirFlightHandle {
      public readonly Transform FlightRoot;
      public readonly Transform GroundRoot;
      readonly bool _isRotor;
      readonly ParticleSystem _primary;
      readonly ParticleSystem _secondary;
      readonly ParticleSystem _downwash;
      bool _active = true;

      public AirFlightHandle(Transform airTurnParent, Transform groundRootParent, bool isRotor, float scale) {
        _isRotor = isRotor;
        FlightRoot = new GameObject("AirFlightVfx").transform;
        FlightRoot.SetParent(airTurnParent, false);

        if (isRotor) {
          // 1. Helicopter turbine exhaust wisps (World space behind aft fuselage)
          var exhaustAnchor = new GameObject("TurbineExhaustAnchor").transform;
          exhaustAnchor.SetParent(FlightRoot, false);
          exhaustAnchor.localPosition = new Vector3(0f, 0.35f * scale, -1.15f * scale);
          _primary = AddPs(exhaustAnchor, "TurbineExhaust", ContrailMat, ParticleSystemRenderMode.Billboard);
          var pm = _primary.main;
          pm.simulationSpace = ParticleSystemSimulationSpace.World;
          pm.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.15f);
          pm.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
          pm.startSize = new ParticleSystem.MinMaxCurve(0.65f * scale, 1.15f * scale);
          pm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
          pm.maxParticles = 32;
          var psShape = _primary.shape;
          psShape.shapeType = ParticleSystemShapeType.Cone;
          psShape.angle = 16f;
          psShape.radius = 0.25f * scale;
          psShape.rotation = new Vector3(0f, 180f, 0f);
          var pc = _primary.colorOverLifetime;
          pc.enabled = true;
          pc.color = AlphaFade(new Color(0.86f, 0.90f, 0.94f, 0.42f), new Color(0.76f, 0.80f, 0.86f, 0.08f));
          var psz = _primary.sizeOverLifetime;
          psz.enabled = true;
          psz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.65f, 1f, 1.9f));

          // 2. Subtle rotor wash ring shimmer at rotor plane
          var rotorAnchor = new GameObject("RotorWashAnchor").transform;
          rotorAnchor.SetParent(FlightRoot, false);
          rotorAnchor.localPosition = new Vector3(0f, 1.05f * scale, 0f);
          _secondary = AddPs(rotorAnchor, "RotorAirShimmer", DownwashMat, ParticleSystemRenderMode.HorizontalBillboard);
          var sm = _secondary.main;
          sm.simulationSpace = ParticleSystemSimulationSpace.Local;
          sm.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.48f);
          sm.startSpeed = 0f;
          sm.startSize = new ParticleSystem.MinMaxCurve(2.8f * scale, 3.6f * scale);
          sm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
          sm.maxParticles = 10;
          var sc = _secondary.colorOverLifetime;
          sc.enabled = true;
          sc.color = AlphaFade(new Color(0.92f, 0.96f, 1f, 0.24f), new Color(0.85f, 0.92f, 0.98f, 0.04f));

          // 3. Ground downwash ring directly beneath the helicopter (visual altitude anchor)
          GroundRoot = new GameObject("AirDownwashVfx").transform;
          GroundRoot.SetParent(groundRootParent, false);
          GroundRoot.localPosition = new Vector3(0f, 0.25f, 0f);
          _downwash = AddPs(GroundRoot, "GroundDownwashRing", DownwashMat, ParticleSystemRenderMode.HorizontalBillboard);
          var dm = _downwash.main;
          dm.simulationSpace = ParticleSystemSimulationSpace.World;
          dm.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.45f);
          dm.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 2.5f);
          dm.startSize = new ParticleSystem.MinMaxCurve(1.4f * scale, 2.3f * scale);
          dm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
          dm.maxParticles = 24;
          var ds = _downwash.shape;
          ds.shapeType = ParticleSystemShapeType.Circle;
          ds.radius = 1.1f * scale;
          ds.rotation = new Vector3(90f, 0f, 0f);
          var dc = _downwash.colorOverLifetime;
          dc.enabled = true;
          dc.color = AlphaFade(new Color(0.84f, 0.80f, 0.72f, 0.34f), new Color(0.75f, 0.72f, 0.66f, 0.04f));
          var dsz = _downwash.sizeOverLifetime;
          dsz.enabled = true;
          dsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.75f, 1f, 2.15f));

          _primary.Play();
          _secondary.Play();
          _downwash.Play();
        } else {
          // 1. Fixed-wing / Drone thruster exhaust glow (World space)
          var nozzle = new GameObject("ThrusterAnchor").transform;
          nozzle.SetParent(FlightRoot, false);
          nozzle.localPosition = new Vector3(0f, 0.08f * scale, -1.25f * scale);
          _primary = AddPs(nozzle, "ThrusterGlow", JetGlowMat, ParticleSystemRenderMode.Billboard);
          var pm = _primary.main;
          pm.simulationSpace = ParticleSystemSimulationSpace.World;
          pm.startLifetime = new ParticleSystem.MinMaxCurve(0.24f, 0.42f);
          pm.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
          pm.startSize = new ParticleSystem.MinMaxCurve(0.55f * scale, 0.95f * scale);
          pm.maxParticles = 32;
          var psShape = _primary.shape;
          psShape.shapeType = ParticleSystemShapeType.Cone;
          psShape.angle = 10f;
          psShape.radius = 0.14f * scale;
          psShape.rotation = new Vector3(0f, 180f, 0f);
          var pc = _primary.colorOverLifetime;
          pc.enabled = true;
          pc.color = AlphaFade(new Color(1f, 0.80f, 0.34f, 0.90f), new Color(1f, 0.42f, 0.10f, 0.10f));
          var psz = _primary.sizeOverLifetime;
          psz.enabled = true;
          psz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.35f));

          // 2. High-altitude contrail / exhaust stream (World space)
          var trailAnchor = new GameObject("ContrailAnchor").transform;
          trailAnchor.SetParent(FlightRoot, false);
          trailAnchor.localPosition = new Vector3(0f, 0.10f * scale, -1.45f * scale);
          _secondary = AddPs(trailAnchor, "FlightContrail", ContrailMat, ParticleSystemRenderMode.Billboard);
          var sm = _secondary.main;
          sm.simulationSpace = ParticleSystemSimulationSpace.World;
          sm.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
          sm.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.85f);
          sm.startSize = new ParticleSystem.MinMaxCurve(0.95f * scale, 1.55f * scale);
          sm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
          sm.maxParticles = 64;
          var ss = _secondary.shape;
          ss.shapeType = ParticleSystemShapeType.Cone;
          ss.angle = 12f;
          ss.radius = 0.24f * scale;
          ss.rotation = new Vector3(0f, 180f, 0f);
          var sc = _secondary.colorOverLifetime;
          sc.enabled = true;
          sc.color = AlphaFade(new Color(0.95f, 0.98f, 1f, 0.68f), new Color(0.85f, 0.91f, 0.97f, 0.10f));
          var ssz = _secondary.sizeOverLifetime;
          ssz.enabled = true;
          ssz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.65f, 1f, 2.25f));

          _primary.Play();
          _secondary.Play();
        }
      }

      public void Update(float speed, bool active, float groundSurfaceY) {
        if (active != _active) {
          _active = active;
          FlightRoot.gameObject.SetActive(active);
          if (GroundRoot != null) GroundRoot.gameObject.SetActive(active);
          if (active) {
            _primary.Play();
            _secondary.Play();
            if (_downwash != null) _downwash.Play();
          } else {
            _primary.Stop();
            _secondary.Stop();
            if (_downwash != null) _downwash.Stop();
          }
        }
        if (!active) return;
        var moveFactor = Mathf.Clamp01(speed / 5.5f);
        if (_isRotor) {
          var pe = _primary.emission;
          pe.rateOverTime = Mathf.Lerp(5f, 16f, moveFactor);
          var se = _secondary.emission;
          se.rateOverTime = 8f;
          if (_downwash != null && GroundRoot != null) {
            GroundRoot.localPosition = new Vector3(0f, groundSurfaceY + 0.28f, 0f);
            var de = _downwash.emission;
            de.rateOverTime = Mathf.Lerp(8f, 14f, moveFactor);
          }
        } else {
          var pe = _primary.emission;
          pe.rateOverTime = Mathf.Lerp(10f, 26f, moveFactor);
          var se = _secondary.emission;
          se.rateOverTime = Mathf.Lerp(7f, 22f, moveFactor);
        }
      }
    }
  }
}
