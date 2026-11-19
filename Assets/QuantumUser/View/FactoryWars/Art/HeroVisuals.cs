namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>
  /// 공중 항모 · 전략 미사일 격납고 · 패트리어트 요격 포대 · 포격함(구축함급)의 전용 3D 킷배시 모델 빌더.
  /// ArtLook.Team PBR 머티리얼을 직접 입혀 흰색 플레이스홀더 없이 진영색과 다크 메탈 질감을 살린다.
  /// </summary>
  public static class HeroVisuals {
    public static void ClearExistingModel(ArtView view, float uniformScale) {
      view.Slot = null;
      var model = view.Model.transform;
      model.localPosition = Vector3.zero;
      model.localRotation = Quaternion.identity;
      model.localScale = Vector3.one * uniformScale;
      foreach (var r in model.GetComponents<Renderer>()) {
        r.enabled = false;
      }
      for (var i = model.childCount - 1; i >= 0; i--) {
        Object.DestroyImmediate(model.GetChild(i).gameObject);
      }
    }

    static Material TeamMat(int color) => ArtLook.Team(color, true) ?? ArtLook.NeutralMaterial;

    /// <summary>구축함급 포격함(ff_artillery_ship / cf_artillery_ship): 다크 해군 그레이 선체 + 진영색 함교 + 전후방 쌍열 포탑 + 레이더 마스트(길이 ~8.2m).</summary>
    public static GameObject BuildArtilleryShip(ArtView view, int color) {
      ClearExistingModel(view, 1.0f);
      var model = view.Model.transform;
      var kit = new MeshKit();
      // 하부 수선 장갑 선체(어두운 해군 그레이) + 주갑판 + 선수 경사부.
      kit.Paint(Swatch.Black).Chamfer(new Vector3(0f, 0.32f, 0f), new Vector3(2.45f, 0.68f, 7.6f), 0.25f);
      kit.Paint(Swatch.Asphalt).Chamfer(new Vector3(0f, 0.78f, -0.1f), new Vector3(2.25f, 0.28f, 6.8f), 0.14f);
      // 뾰족한 선수(Bow).
      kit.Paint(Swatch.Asphalt).Chamfer(new Vector3(0f, 0.62f, 3.85f), new Vector3(1.35f, 0.62f, 1.45f), 0.22f);
      kit.Paint(Swatch.ArmorDark).Box(new Vector3(0f, 0.94f, 0.1f), new Vector3(1.85f, 0.08f, 6.1f));
      // 진영색 띠(선체 측면) & 중앙 장갑 함교(Superstructure).
      kit.Team().Box(new Vector3(-1.16f, 0.78f, 0.2f), new Vector3(0.14f, 0.22f, 5.4f));
      kit.Team().Box(new Vector3(1.16f, 0.78f, 0.2f), new Vector3(0.14f, 0.22f, 5.4f));
      kit.Paint(Swatch.ArmorDark).Chamfer(new Vector3(0f, 1.45f, -0.4f), new Vector3(1.55f, 1.0f, 2.35f), 0.12f);
      kit.Team().Chamfer(new Vector3(0f, 2.08f, -0.2f), new Vector3(1.22f, 0.52f, 1.45f), 0.08f);
      // 함교 유리창 + 발광 항법등.
      kit.Paint(Swatch.Glass).Box(new Vector3(0f, 2.12f, 0.54f), new Vector3(0.96f, 0.22f, 0.08f));
      kit.Paint(Swatch.GlowCyan).Box(new Vector3(-0.62f, 2.22f, 0.25f), new Vector3(0.12f, 0.12f, 0.12f));
      kit.Paint(Swatch.GlowRed).Box(new Vector3(0.62f, 2.22f, 0.25f), new Vector3(0.12f, 0.12f, 0.12f));
      // 쌍열 연돌(Smokestacks) + 레이더 마스트.
      kit.Paint(Swatch.Black).Cylinder(new Vector3(-0.36f, 2.15f, -1.25f), 0.22f, 1.1f, 10);
      kit.Paint(Swatch.Black).Cylinder(new Vector3(0.36f, 2.15f, -1.25f), 0.22f, 1.1f, 10);
      kit.Paint(Swatch.MetalLight).Cylinder(new Vector3(0f, 2.85f, -0.35f), 0.09f, 1.4f, 8);
      kit.Paint(Swatch.MetalLight).Box(new Vector3(0f, 3.35f, -0.35f), new Vector3(0.95f, 0.08f, 0.14f));
      kit.Paint(Swatch.GlowCyan).Box(new Vector3(0f, 3.55f, -0.35f), new Vector3(0.62f, 0.18f, 0.08f));
      // 전방(Bow) & 후방(Stern) 쌍열 주포 포탑.
      foreach (var zPos in new[] { 2.15f, -2.55f }) {
        var dir = zPos > 0f ? 1f : -1f;
        kit.Paint(Swatch.Metal).Cylinder(new Vector3(0f, 1.06f, zPos), 0.62f, 0.26f, 12);
        kit.Team().Chamfer(new Vector3(0f, 1.32f, zPos), new Vector3(0.95f, 0.44f, 1.15f), 0.08f);
        kit.Paint(Swatch.MetalDark).Box(new Vector3(-0.22f, 1.42f, zPos + dir * 0.95f), new Vector3(0.14f, 0.14f, 1.55f));
        kit.Paint(Swatch.MetalDark).Box(new Vector3(0.22f, 1.42f, zPos + dir * 0.95f), new Vector3(0.14f, 0.14f, 1.55f));
      }
      var go = new GameObject("HeroArtilleryShip", typeof(MeshFilter), typeof(MeshRenderer));
      go.transform.SetParent(model, false);
      go.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("hero_artillery_ship");
      go.GetComponent<MeshRenderer>().sharedMaterial = TeamMat(color);
      return go;
    }

    /// <summary>공중 항모(스톰네스트) VTOL 4터빈 헬리캐리어 본체 메쉬(팀 컬러 + 청록 활주로 유도등).</summary>
    public static GameObject BuildHelicarrierCard(ArtView view, int color) {
      ClearExistingModel(view, 1.0f);
      var model = view.Model.transform;
      var kit = new MeshKit();
      kit.Team().Chamfer(new Vector3(0f, 0.6f, 0f), new Vector3(2.2f, 0.75f, 5.4f), 0.22f);
      kit.Paint(Swatch.MetalDark).Chamfer(new Vector3(0f, 1.08f, 0.1f), new Vector3(3.4f, 0.24f, 6.2f), 0.14f);
      kit.Paint(Swatch.Asphalt).Box(new Vector3(-0.35f, 1.22f, 0.1f), new Vector3(1.55f, 0.05f, 5.8f));
      kit.Team().Chamfer(new Vector3(1.18f, 1.65f, -0.35f), new Vector3(0.68f, 0.95f, 1.45f), 0.08f);
      kit.Paint(Swatch.GlowCyan).Box(new Vector3(1.18f, 1.88f, 0.39f), new Vector3(0.52f, 0.22f, 0.08f));
      for (var s = -5; s <= 5; s++) {
        var z = s * 0.5f;
        kit.Paint(Swatch.GlowCyan).Box(new Vector3(-1.02f, 1.25f, z), new Vector3(0.11f, 0.06f, 0.28f));
        kit.Paint(Swatch.GlowCyan).Box(new Vector3(0.32f, 1.25f, z), new Vector3(0.11f, 0.06f, 0.28f));
      }
      foreach (var p in new[] { new Vector3(-2.35f, 0.92f, -1.75f), new Vector3(2.35f, 0.92f, -1.75f), new Vector3(-2.45f, 0.92f, 1.85f), new Vector3(2.45f, 0.92f, 1.85f) }) {
        kit.Paint(Swatch.MetalDark).Box(new Vector3(p.x * 0.55f, p.y, p.z), new Vector3(Mathf.Abs(p.x), 0.22f, 0.42f));
        kit.Team().Cylinder(p, 0.92f, 0.34f, 14);
        kit.Paint(Swatch.Black).Cylinder(p + Vector3.up * 0.04f, 0.76f, 0.36f, 14);
        kit.Paint(Swatch.MetalLight).Box(p + Vector3.up * 0.16f, new Vector3(1.42f, 0.04f, 0.16f));
        kit.Paint(Swatch.MetalLight).Box(p + Vector3.up * 0.16f, new Vector3(0.16f, 0.04f, 1.42f));
        kit.Paint(Swatch.GlowCyan).Cylinder(p + Vector3.up * 0.08f, 0.26f, 0.38f, 10);
      }
      var go = new GameObject("HeroCarrier", typeof(MeshFilter), typeof(MeshRenderer));
      go.transform.SetParent(model, false);
      go.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("hero_carrier_card");
      go.GetComponent<MeshRenderer>().sharedMaterial = TeamMat(color);
      return go;
    }

    /// <summary>전략 미사일 격납고(방폭문 + 수직 ICBM 탄두 + 적색 경보등).</summary>
    public static GameObject BuildMissileSiloCard(ArtView view, int color) {
      ClearExistingModel(view, 1.0f);
      var model = view.Model.transform;
      var kit = new MeshKit();
      kit.Paint(Swatch.ConcreteDark).Chamfer(new Vector3(0f, 0.55f, 0f), new Vector3(4.6f, 1.1f, 4.6f), 0.35f);
      kit.Team().Chamfer(new Vector3(0f, 1.18f, 0f), new Vector3(3.6f, 0.36f, 3.6f), 0.2f);
      kit.Paint(Swatch.Black).Cylinder(new Vector3(0f, 0.9f, 0f), 1.25f, 1.4f, 16);
      kit.Paint(Swatch.MetalLight).Chamfer(new Vector3(-1.3f, 1.48f, 0f), new Vector3(1.25f, 0.24f, 2.5f), 0.06f);
      kit.Paint(Swatch.MetalLight).Chamfer(new Vector3(1.3f, 1.48f, 0f), new Vector3(1.25f, 0.24f, 2.5f), 0.06f);
      kit.Paint(Swatch.GlowRed).Box(new Vector3(-1.95f, 1.75f, 1.2f), new Vector3(0.28f, 0.28f, 0.28f));
      kit.Paint(Swatch.GlowRed).Box(new Vector3(1.95f, 1.75f, -1.2f), new Vector3(0.28f, 0.28f, 0.28f));
      kit.Paint(Swatch.White).Cylinder(new Vector3(0f, 1.9f, 0f), 0.44f, 2.8f, 12);
      kit.Paint(Swatch.GlowRed).Cone(new Vector3(0f, 3.7f, 0f), 0.45f, 0.95f, 12);
      var go = new GameObject("HeroSilo", typeof(MeshFilter), typeof(MeshRenderer));
      go.transform.SetParent(model, false);
      go.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("hero_silo_card");
      go.GetComponent<MeshRenderer>().sharedMaterial = TeamMat(color);
      return go;
    }

    static Material _beaconOn;
    static Material _beaconOff;
    static Material _beaconOverdrive;

    public static Material BeaconMaterial(bool lit, bool overdrive = false) {
      if (overdrive && lit) {
        if (_beaconOverdrive == null) {
          _beaconOverdrive = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default")) { name = "FactoryBeaconOverdrive" };
          _beaconOverdrive.color = new Color(1f, 0.22f, 0.08f, 1f);
        }
        return _beaconOverdrive;
      }
      if (lit) {
        if (_beaconOn == null) {
          _beaconOn = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default")) { name = "FactoryBeaconOn" };
          _beaconOn.color = new Color(1f, 0.72f, 0.08f, 1f);
        }
        return _beaconOn;
      }
      if (_beaconOff == null) {
        _beaconOff = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "FactoryBeaconOff" };
        _beaconOff.color = new Color(0.22f, 0.19f, 0.15f, 1f);
      }
      return _beaconOff;
    }

    static Renderer AddBeacon(Transform parent, Vector3 localPos) {
      var go = new GameObject("AmberBeacon", typeof(MeshFilter), typeof(MeshRenderer));
      go.transform.SetParent(parent, false);
      go.transform.localPosition = localPos;
      var kit = new MeshKit().Paint(Swatch.White);
      kit.Cylinder(Vector3.zero, 0.24f, 0.32f, 10);
      go.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("factory_beacon");
      var rend = go.GetComponent<MeshRenderer>();
      rend.sharedMaterial = BeaconMaterial(false);
      rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      return rend;
    }

    /// <summary>
    /// "Factory Wars" 정체성: 7대 산업 공장 + 산업 사령부(1.25x)의 전용 3D 킷배시 모델 빌더.
    /// 일반 천막/막사를 콘크리트·강철 공장, 갠트리 크레인, 쌍굴뚝·용광로, 냉각 터빈, 회전 레이더 연구소로 교체한다.
    /// </summary>
    public static bool TryBuildFactory(ArtView view, string id, int color, out Vector3[] chimneys, out Renderer beacon, out Transform radarDish) {
      chimneys = null;
      beacon = null;
      radarDish = null;
      if (string.IsNullOrEmpty(id)) return false;

      var role = id.Length > 3 && (id.StartsWith("ff_") || id.StartsWith("cf_") || id.StartsWith("tf_")) ? id.Substring(3) : id;
      var isFactory = role == "barracks" || role == "vehicle_factory" || role == "heavy_works" || role == "airfield"
        || role == "carrier_yard" || role == "processor" || role == "power_plant" || role == "research_lab"
        || role == "supply_depot" || role == "headquarters";
      if (isFactory == false) return false;

      // 1.25x 스케일 밸런스로 통일(0.8x 보병 · 1.0x 전차 대비 웅장한 산업 공장 존재감, 도로 이격 준수).
      ClearExistingModel(view, 1.25f);
      var model = view.Model.transform;
      var kit = new MeshKit();
      var beaconPos = new Vector3(0f, 3.2f, 2.55f);

      switch (role) {
        case "barracks":
          // 1) 보병 무장 공장 (Infantry Armory): 철근 콘크리트 군수 홀 + 골강판 지붕 + 셔터 출입문 + 배기 굴뚝.
          kit.Paint(Swatch.Asphalt).Box(new Vector3(0f, 0.14f, 0f), new Vector3(5.6f, 0.28f, 5.2f));
          kit.Paint(Swatch.ConcreteDark).Chamfer(new Vector3(0f, 1.45f, -0.15f), new Vector3(4.9f, 2.35f, 4.2f), 0.12f);
          kit.Paint(Swatch.MetalDark).Chamfer(new Vector3(0f, 2.75f, -0.15f), new Vector3(5.15f, 0.36f, 4.45f), 0.1f);
          kit.Team().Box(new Vector3(0f, 2.48f, 2.02f), new Vector3(4.2f, 0.38f, 0.14f));
          // 전면 강철 셔터 베이 도어 + 황색 안전선.
          kit.Paint(Swatch.MetalLight).Box(new Vector3(0f, 1.15f, 1.98f), new Vector3(2.35f, 1.85f, 0.16f));
          kit.Paint(Swatch.StripeYellow).Box(new Vector3(0f, 0.29f, 2.32f), new Vector3(2.7f, 0.06f, 0.42f));
          // 측면 탄약 보급 상자 + 지붕 배기 굴뚝.
          kit.Paint(Swatch.OliveDark).Box(new Vector3(-2.1f, 0.65f, 1.85f), new Vector3(0.85f, 0.72f, 0.85f));
          kit.Paint(Swatch.MetalDark).Cylinder(new Vector3(1.65f, 3.65f, -1.25f), 0.28f, 1.8f, 10);
          kit.Paint(Swatch.Black).Cylinder(new Vector3(1.65f, 4.55f, -1.25f), 0.24f, 0.16f, 10);
          chimneys = new[] { new Vector3(1.65f, 4.65f, -1.25f) };
          beaconPos = new Vector3(0f, 2.25f, 2.15f);
          break;

        case "vehicle_factory":
          // 2) 기동·화력 조립 공장 (Light Vehicle & Rocket Plant): 2-베이 차량 조립 차고 + 쌍셔터 도어 + 쌍배기구.
          kit.Paint(Swatch.Asphalt).Box(new Vector3(0f, 0.14f, 0f), new Vector3(6.4f, 0.28f, 5.6f));
          kit.Paint(Swatch.ConcreteDark).Chamfer(new Vector3(0f, 1.55f, -0.25f), new Vector3(5.8f, 2.6f, 4.5f), 0.14f);
          kit.Paint(Swatch.MetalDark).Chamfer(new Vector3(0f, 2.98f, -0.25f), new Vector3(6.05f, 0.34f, 4.75f), 0.1f);
          kit.Team().Box(new Vector3(0f, 2.74f, 2.06f), new Vector3(5.35f, 0.36f, 0.14f));
          // 2-베이 조립 출입문(Bay L & Bay R).
          foreach (var bx in new[] { -1.35f, 1.35f }) {
            kit.Paint(Swatch.Black).Box(new Vector3(bx, 1.18f, 2.02f), new Vector3(2.05f, 1.95f, 0.12f));
            kit.Paint(Swatch.MetalLight).Box(new Vector3(bx, 1.72f, 2.06f), new Vector3(1.92f, 0.72f, 0.10f));
            kit.Paint(Swatch.StripeYellow).Box(new Vector3(bx, 0.29f, 2.48f), new Vector3(2.25f, 0.06f, 0.55f));
          }
          // 지붕 쌍배기 스택.
          kit.Paint(Swatch.MetalDark).Cylinder(new Vector3(-1.9f, 3.85f, -1.45f), 0.28f, 1.8f, 10);
          kit.Paint(Swatch.MetalDark).Cylinder(new Vector3(1.9f, 3.85f, -1.45f), 0.28f, 1.8f, 10);
          chimneys = new[] { new Vector3(-1.9f, 4.8f, -1.45f), new Vector3(1.9f, 4.8f, -1.45f) };
          beaconPos = new Vector3(0f, 2.45f, 2.2f);
          break;

        case "heavy_works":
          // 3) 중기갑 전차 공장 (Heavy Armor Foundry): 대형 제강 홀 + 외부 오버헤드 갠트리 크레인 + 광폭 방폭문 + 쌍용광로 굴뚝.
          kit.Paint(Swatch.Asphalt).Box(new Vector3(0f, 0.16f, 0f), new Vector3(6.8f, 0.32f, 6.2f));
          kit.Paint(Swatch.ArmorDark).Chamfer(new Vector3(0f, 1.75f, -0.45f), new Vector3(5.8f, 3.0f, 4.6f), 0.16f);
          kit.Paint(Swatch.MetalDark).Chamfer(new Vector3(0f, 3.35f, -0.45f), new Vector3(6.1f, 0.42f, 4.9f), 0.12f);
          kit.Team().Box(new Vector3(0f, 3.1f, 1.92f), new Vector3(5.2f, 0.44f, 0.18f));
          // 광폭 중장갑 강철 방폭문 + 내부 용광로 주황 발광 슬릿.
          kit.Paint(Swatch.MetalLight).Box(new Vector3(-1.05f, 1.35f, 1.88f), new Vector3(1.85f, 2.25f, 0.22f));
          kit.Paint(Swatch.MetalLight).Box(new Vector3(1.05f, 1.35f, 1.88f), new Vector3(1.85f, 2.25f, 0.22f));
          kit.Paint(Swatch.GlowOrange).Box(new Vector3(0f, 1.35f, 1.92f), new Vector3(0.24f, 2.1f, 0.18f));
          // 외부 황색·흑색 오버헤드 갠트리 크레인(Gantry Crane).
          foreach (var gx in new[] { -2.85f, 2.85f }) {
            kit.Paint(Swatch.StripeYellow).Box(new Vector3(gx, 2.25f, 2.55f), new Vector3(0.32f, 4.2f, 0.32f));
            kit.Paint(Swatch.Black).Box(new Vector3(gx, 0.42f, 2.55f), new Vector3(0.48f, 0.55f, 0.48f));
          }
          kit.Paint(Swatch.StripeYellow).Box(new Vector3(0f, 4.28f, 2.55f), new Vector3(6.15f, 0.38f, 0.42f));
          kit.Paint(Swatch.MetalDark).Box(new Vector3(-0.6f, 3.92f, 2.55f), new Vector3(0.95f, 0.46f, 0.62f));
          // 후방 중형 쌍굴뚝.
          kit.Paint(Swatch.MetalDark).Cylinder(new Vector3(-1.8f, 4.45f, -1.85f), 0.38f, 2.5f, 12);
          kit.Paint(Swatch.MetalDark).Cylinder(new Vector3(1.8f, 4.45f, -1.85f), 0.38f, 2.5f, 12);
          kit.Paint(Swatch.StripeYellow).Cylinder(new Vector3(-1.8f, 5.2f, -1.85f), 0.40f, 0.25f, 12);
          kit.Paint(Swatch.StripeYellow).Cylinder(new Vector3(1.8f, 5.2f, -1.85f), 0.40f, 0.25f, 12);
          chimneys = new[] { new Vector3(-1.8f, 5.75f, -1.85f), new Vector3(1.8f, 5.75f, -1.85f) };
          beaconPos = new Vector3(0f, 2.75f, 2.08f);
          break;

        case "airfield":
        case "carrier_yard":
          // 4) 항공·드론 조립 격납고 (Aero-Drone Assembly Hangar): 활주로 유도등 아치 격납고 + 관제탑.
          kit.Paint(Swatch.Asphalt).Box(new Vector3(0f, 0.14f, 0f), new Vector3(6.6f, 0.28f, 6.4f));
          kit.Paint(Swatch.MetalDark).Chamfer(new Vector3(-0.45f, 1.55f, -0.55f), new Vector3(4.9f, 2.65f, 4.4f), 0.48f);
          kit.Team().Chamfer(new Vector3(-0.45f, 2.88f, -0.55f), new Vector3(3.6f, 0.38f, 4.25f), 0.14f);
          kit.Paint(Swatch.Black).Box(new Vector3(-0.45f, 1.25f, 1.66f), new Vector3(3.8f, 2.05f, 0.16f));
          // 활주로 청록 유도등 2열.
          for (var l = 0; l < 4; l++) {
            var lz = 1.95f + l * 0.38f;
            kit.Paint(Swatch.GlowCyan).Box(new Vector3(-2.25f, 0.32f, lz), new Vector3(0.14f, 0.08f, 0.22f));
            kit.Paint(Swatch.GlowCyan).Box(new Vector3(1.35f, 0.32f, lz), new Vector3(0.14f, 0.08f, 0.22f));
          }
          // 우현 항공 관제탑(Control Tower).
          kit.Paint(Swatch.ConcreteDark).Box(new Vector3(2.45f, 1.85f, -1.05f), new Vector3(1.25f, 3.45f, 1.35f));
          kit.Paint(Swatch.Glass).Box(new Vector3(2.45f, 3.85f, -1.05f), new Vector3(1.55f, 0.72f, 1.55f));
          kit.Team().Chamfer(new Vector3(2.45f, 4.35f, -1.05f), new Vector3(1.68f, 0.26f, 1.68f), 0.06f);
          kit.Paint(Swatch.MetalDark).Cylinder(new Vector3(-2.3f, 3.35f, -1.9f), 0.24f, 1.5f, 10);
          chimneys = new[] { new Vector3(-2.3f, 4.15f, -1.9f) };
          beaconPos = new Vector3(-0.45f, 2.55f, 1.8f);
          break;

        case "processor":
          // 5) 강철 제련소 (Steel Refinery): 쌍대형 굴뚝 + 용광로 주황 발광 통풍구(GlowOrange) + 슬래그 컨베이어.
          kit.Paint(Swatch.Asphalt).Box(new Vector3(0f, 0.14f, 0f), new Vector3(6.2f, 0.28f, 5.4f));
          kit.Paint(Swatch.ArmorDark).Chamfer(new Vector3(-0.35f, 1.48f, -0.15f), new Vector3(4.5f, 2.45f, 4.2f), 0.14f);
          kit.Team().Box(new Vector3(-0.35f, 2.65f, 1.98f), new Vector3(3.8f, 0.34f, 0.14f));
          // 용광로 주황 발광 통풍구(Molten-Orange Furnace Vents).
          for (var v = -1; v <= 1; v++) {
            kit.Paint(Swatch.Black).Box(new Vector3(-0.35f + v * 1.15f, 1.35f, 1.96f), new Vector3(0.76f, 1.25f, 0.16f));
            kit.Paint(Swatch.GlowOrange).Box(new Vector3(-0.35f + v * 1.15f, 1.35f, 2.02f), new Vector3(0.58f, 0.95f, 0.12f));
          }
          // 우측 고로 탱크 + 쌍대형 굴뚝(Twin Smokestacks).
          kit.Paint(Swatch.MetalDark).Cylinder(new Vector3(2.15f, 1.65f, 0.5f), 0.78f, 2.85f, 12);
          kit.Paint(Swatch.GlowOrange).Cylinder(new Vector3(2.15f, 2.25f, 0.5f), 0.81f, 0.22f, 12);
          kit.Paint(Swatch.MetalDark).Cylinder(new Vector3(-1.55f, 4.15f, -1.35f), 0.42f, 3.2f, 12);
          kit.Paint(Swatch.MetalDark).Cylinder(new Vector3(0.85f, 4.15f, -1.35f), 0.42f, 3.2f, 12);
          kit.Paint(Swatch.GlowOrange).Cylinder(new Vector3(-1.55f, 5.55f, -1.35f), 0.44f, 0.18f, 12);
          kit.Paint(Swatch.GlowOrange).Cylinder(new Vector3(0.85f, 5.55f, -1.35f), 0.44f, 0.18f, 12);
          chimneys = new[] { new Vector3(-1.55f, 5.8f, -1.35f), new Vector3(0.85f, 5.8f, -1.35f) };
          beaconPos = new Vector3(-0.35f, 2.3f, 2.12f);
          break;

        case "power_plant":
          // 6) 터빈 발전소 (Power Plant): 발전 터빈 홀 + 쌍냉각탑 스택 + 청록 변압기 코일.
          kit.Paint(Swatch.Asphalt).Box(new Vector3(0f, 0.14f, 0f), new Vector3(5.8f, 0.28f, 5.2f));
          kit.Paint(Swatch.ConcreteDark).Chamfer(new Vector3(0f, 1.15f, 0.85f), new Vector3(4.8f, 1.85f, 2.4f), 0.12f);
          kit.Team().Box(new Vector3(0f, 2.02f, 2.08f), new Vector3(4.2f, 0.3f, 0.12f));
          // 쌍냉각/터빈 스택(Cooling/Turbine Stacks).
          foreach (var sx in new[] { -1.35f, 1.35f }) {
            kit.Paint(Swatch.Concrete).Cylinder(new Vector3(sx, 1.85f, -1.15f), 0.98f, 3.4f, 14);
            kit.Paint(Swatch.MetalDark).Cylinder(new Vector3(sx, 3.65f, -1.15f), 0.82f, 0.42f, 14);
            kit.Paint(Swatch.GlowCyan).Cylinder(new Vector3(sx, 2.65f, -1.15f), 1.01f, 0.22f, 14);
          }
          // 전면 고전압 청록 변압기 코일 2기.
          kit.Paint(Swatch.GlowCyan).Cylinder(new Vector3(-1.2f, 1.25f, 2.15f), 0.26f, 1.35f, 10);
          kit.Paint(Swatch.GlowCyan).Cylinder(new Vector3(1.2f, 1.25f, 2.15f), 0.26f, 1.35f, 10);
          chimneys = new[] { new Vector3(-1.35f, 3.95f, -1.15f), new Vector3(1.35f, 3.95f, -1.15f) };
          beaconPos = new Vector3(0f, 1.75f, 2.18f);
          break;

        case "research_lab": {
          // 7) 군수 연구 공장 (Ordnance Tech Lab): 장갑 연구 복합동 + 청록 채광창 + 회전 레이더 접시.
          kit.Paint(Swatch.Asphalt).Box(new Vector3(0f, 0.14f, 0f), new Vector3(5.8f, 0.28f, 5.2f));
          kit.Paint(Swatch.ConcreteDark).Chamfer(new Vector3(0f, 1.45f, 0f), new Vector3(4.9f, 2.4f, 4.3f), 0.16f);
          kit.Paint(Swatch.MetalDark).Chamfer(new Vector3(0f, 2.72f, 0f), new Vector3(4.35f, 0.36f, 3.75f), 0.12f);
          kit.Team().Box(new Vector3(0f, 2.38f, 2.18f), new Vector3(4.0f, 0.34f, 0.14f));
          kit.Paint(Swatch.GlowCyan).Box(new Vector3(0f, 1.65f, 2.17f), new Vector3(3.2f, 0.42f, 0.12f));
          kit.Paint(Swatch.MetalDark).Cylinder(new Vector3(-1.75f, 3.45f, -1.25f), 0.22f, 1.4f, 10);
          // 지붕 회전 레이더 접시(RadarDish 자식 오브젝트).
          var rGo = new GameObject("RadarDish", typeof(MeshFilter), typeof(MeshRenderer));
          rGo.transform.SetParent(model, false);
          rGo.transform.localPosition = new Vector3(0.45f, 3.45f, -0.25f);
          var rKit = new MeshKit();
          rKit.Paint(Swatch.MetalDark).Cylinder(new Vector3(0f, -0.35f, 0f), 0.28f, 0.72f, 10);
          rKit.Paint(Swatch.MetalLight).Chamfer(new Vector3(0f, 0.45f, 0.18f), new Vector3(1.65f, 0.95f, 0.24f), 0.08f);
          rKit.Paint(Swatch.GlowCyan).Cylinder(new Vector3(0f, 0.48f, 0.58f), 0.14f, 0.42f, 8);
          rGo.GetComponent<MeshFilter>().sharedMesh = rKit.ToMesh("factory_radar_dish");
          rGo.GetComponent<MeshRenderer>().sharedMaterial = TeamMat(color);
          radarDish = rGo.transform;
          chimneys = new[] { new Vector3(-1.75f, 4.2f, -1.25f) };
          beaconPos = new Vector3(0f, 2.15f, 2.24f);
          break;
        }

        case "supply_depot":
          // 8) 군수·연료 보급창 (Munitions & Fuel Depot): 쌍고압 연료 탱크 + 탄약 벙커 + 황흑 폭발 위험 표식(파괴 시 연쇄 대폭발).
          kit.Paint(Swatch.Asphalt).Box(new Vector3(0f, 0.14f, 0f), new Vector3(5.8f, 0.28f, 5.2f));
          kit.Paint(Swatch.StripeYellow).Box(new Vector3(0f, 0.29f, 2.38f), new Vector3(5.1f, 0.06f, 0.32f));
          // 쌍대형 연료 저장 탱크(위험 붉은 띠 + 진영색 캡).
          foreach (var tx in new[] { -1.45f, 1.45f }) {
            kit.Paint(Swatch.Metal).Cylinder(new Vector3(tx, 1.35f, -0.75f), 0.92f, 2.35f, 14);
            kit.Paint(Swatch.GlowRed).Cylinder(new Vector3(tx, 1.65f, -0.75f), 0.95f, 0.24f, 14);
            kit.Team().Cylinder(new Vector3(tx, 2.55f, -0.75f), 0.78f, 0.26f, 14);
          }
          // 전면 강화 탄약 보급 벙커 + 연료 배관.
          kit.Paint(Swatch.OliveDark).Chamfer(new Vector3(0f, 0.95f, 1.25f), new Vector3(4.2f, 1.45f, 1.75f), 0.12f);
          kit.Paint(Swatch.MetalLight).Box(new Vector3(0f, 0.88f, 2.15f), new Vector3(1.85f, 1.15f, 0.12f));
          kit.Paint(Swatch.GlowOrange).Box(new Vector3(-1.45f, 1.25f, 2.15f), new Vector3(0.45f, 0.32f, 0.12f));
          kit.Paint(Swatch.GlowOrange).Box(new Vector3(1.45f, 1.25f, 2.15f), new Vector3(0.45f, 0.32f, 0.12f));
          kit.Paint(Swatch.MetalDark).Box(new Vector3(0f, 1.15f, -0.75f), new Vector3(2.8f, 0.22f, 0.22f));
          chimneys = new[] { new Vector3(-1.45f, 2.75f, -0.75f) };
          beaconPos = new Vector3(0f, 1.85f, 2.24f);
          break;

        case "headquarters": {
          // 산업 사령부 (Command Foundry HQ): 2단 철근 콘크리트 요새 지휘동 + 진영 장갑벽 + 통신 레이더 마스트.
          kit.Paint(Swatch.Asphalt).Box(new Vector3(0f, 0.16f, 0f), new Vector3(6.8f, 0.32f, 6.6f));
          kit.Paint(Swatch.ConcreteDark).Chamfer(new Vector3(0f, 1.45f, -0.1f), new Vector3(5.9f, 2.5f, 5.4f), 0.2f);
          kit.Team().Chamfer(new Vector3(0f, 2.95f, -0.4f), new Vector3(4.2f, 1.15f, 3.8f), 0.16f);
          kit.Paint(Swatch.MetalDark).Chamfer(new Vector3(0f, 3.62f, -0.4f), new Vector3(3.6f, 0.32f, 3.2f), 0.1f);
          kit.Paint(Swatch.GlowCyan).Box(new Vector3(0f, 2.95f, 1.54f), new Vector3(2.8f, 0.38f, 0.12f));
          kit.Paint(Swatch.MetalLight).Box(new Vector3(0f, 1.1f, 2.62f), new Vector3(2.6f, 1.85f, 0.18f));
          kit.Paint(Swatch.StripeYellow).Box(new Vector3(0f, 0.33f, 2.95f), new Vector3(3.0f, 0.06f, 0.52f));
          var hqDish = new GameObject("HqRadar", typeof(MeshFilter), typeof(MeshRenderer));
          hqDish.transform.SetParent(model, false);
          hqDish.transform.localPosition = new Vector3(1.1f, 4.35f, -0.55f);
          var hKit = new MeshKit();
          hKit.Paint(Swatch.MetalDark).Cylinder(new Vector3(0f, -0.35f, 0f), 0.22f, 0.85f, 10);
          hKit.Paint(Swatch.GlowCyan).Box(new Vector3(0f, 0.28f, 0f), new Vector3(1.45f, 0.34f, 0.16f));
          hqDish.GetComponent<MeshFilter>().sharedMesh = hKit.ToMesh("hq_radar");
          hqDish.GetComponent<MeshRenderer>().sharedMaterial = TeamMat(color);
          radarDish = hqDish.transform;
          chimneys = new[] { new Vector3(-1.85f, 3.95f, -1.65f) };
          beaconPos = new Vector3(0f, 2.25f, 2.75f);
          break;
        }
      }

      var hull = new GameObject($"IndustrialFactory_{role}", typeof(MeshFilter), typeof(MeshRenderer));
      hull.transform.SetParent(model, false);
      hull.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh($"factory_{role}");
      hull.GetComponent<MeshRenderer>().sharedMaterial = TeamMat(color);
      beacon = AddBeacon(model, beaconPos);
      return true;
    }

    /// <summary>공장 Lv.2 / Lv.3 시각적 확장: 추가 굴뚝 · 대형 갠트리 트러스 · 진영색 발광 배관 장착.</summary>
    public static GameObject AttachFactoryUpgrade(Transform model, int level, int color) {
      if (model == null || level <= 1) return null;
      var kit = new MeshKit();
      // Lv.2: 좌우 추가 증기 굴뚝 + 진영색 가압 배관 + 상단 강철 프레임.
      kit.Paint(Swatch.MetalDark).Cylinder(new Vector3(-2.65f, 2.75f, -0.25f), 0.26f, 3.8f, 10);
      kit.Team().Cylinder(new Vector3(-2.65f, 4.15f, -0.25f), 0.29f, 0.35f, 10);
      kit.Team().Box(new Vector3(-2.65f, 1.65f, 0.85f), new Vector3(0.24f, 0.24f, 2.2f));
      kit.Paint(Swatch.GlowCyan).Box(new Vector3(2.65f, 1.65f, 0.45f), new Vector3(0.22f, 0.22f, 2.6f));
      if (level >= 3) {
        // Lv.3: 쌍대형 터보 배기 스택 + 지붕 대형 오버헤드 트러스 크레인 + 황금/시안 고출력 코어 배관.
        kit.Paint(Swatch.MetalDark).Cylinder(new Vector3(2.65f, 3.25f, -0.35f), 0.32f, 4.8f, 12);
        kit.Paint(Swatch.StripeYellow).Cylinder(new Vector3(2.65f, 4.95f, -0.35f), 0.36f, 0.38f, 12);
        kit.Paint(Swatch.StripeYellow).Box(new Vector3(-2.85f, 2.65f, 0.5f), new Vector3(0.26f, 4.8f, 0.26f));
        kit.Paint(Swatch.StripeYellow).Box(new Vector3(2.85f, 2.65f, 0.5f), new Vector3(0.26f, 4.8f, 0.26f));
        kit.Paint(Swatch.StripeYellow).Box(new Vector3(0f, 5.05f, 0.5f), new Vector3(6.1f, 0.34f, 0.38f));
        kit.Paint(Swatch.GlowCyan).Box(new Vector3(0f, 5.26f, 0.5f), new Vector3(4.2f, 0.14f, 0.16f));
        kit.Paint(Swatch.GlowOrange).Box(new Vector3(0f, 4.78f, 0.5f), new Vector3(1.1f, 0.42f, 0.58f));
      }
      var go = new GameObject($"FactoryUpgrade_Lv{level}", typeof(MeshFilter), typeof(MeshRenderer));
      go.transform.SetParent(model, false);
      go.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh($"factory_upgrade_lv{level}");
      go.GetComponent<MeshRenderer>().sharedMaterial = TeamMat(color);
      return go;
    }

    static Material _pipePowerMat;
    static Material _pipeSteelMat;

    /// <summary>12m 인접 시너지 발광 지상 파이프라인(제련소=용광로 주황, 발전소=고압 시안).</summary>
    public static GameObject BuildSynergyPipeline(Transform parent, Vector3 from, Vector3 to, bool isPower) {
      var a = new Vector3(from.x, 0.28f, from.z);
      var b = new Vector3(to.x, 0.28f, to.z);
      var delta = b - a;
      var len = delta.magnitude;
      if (len < 1f) return null;
      var go = new GameObject("SynergyPipeline");
      go.transform.SetParent(parent, false);
      go.transform.position = (a + b) * 0.5f;
      go.transform.rotation = Quaternion.LookRotation(delta / len, Vector3.up);

      // 외곽 강철 파이프 하우징 + 지지대.
      var shellGo = new GameObject("PipeShell", typeof(MeshFilter), typeof(MeshRenderer));
      shellGo.transform.SetParent(go.transform, false);
      var sk = new MeshKit();
      sk.Paint(Swatch.MetalDark).Box(new Vector3(0f, 0f, 0f), new Vector3(0.56f, 0.26f, len));
      var rings = Mathf.Max(2, Mathf.FloorToInt(len / 2.6f));
      for (var i = 0; i <= rings; i++) {
        var z = -len * 0.5f + (len * i / rings);
        sk.Paint(Swatch.StripeYellow).Box(new Vector3(0f, 0.04f, z), new Vector3(0.72f, 0.34f, 0.24f));
      }
      shellGo.GetComponent<MeshFilter>().sharedMesh = sk.ToMesh("synergy_pipe_shell");
      shellGo.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;

      // 상단 발광 에너지 도관 스트립.
      var glowGo = new GameObject("PipeGlow", typeof(MeshFilter), typeof(MeshRenderer));
      glowGo.transform.SetParent(go.transform, false);
      var gk = new MeshKit().Paint(Swatch.White);
      gk.Box(new Vector3(0f, 0.16f, 0f), new Vector3(0.24f, 0.10f, len * 0.96f));
      glowGo.GetComponent<MeshFilter>().sharedMesh = gk.ToMesh("synergy_pipe_glow");
      if (isPower) {
        if (_pipePowerMat == null) {
          _pipePowerMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default")) { name = "SynergyPipePower" };
          _pipePowerMat.color = new Color(0.18f, 0.92f, 1f, 1f);
        }
        glowGo.GetComponent<MeshRenderer>().sharedMaterial = _pipePowerMat;
      } else {
        if (_pipeSteelMat == null) {
          _pipeSteelMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default")) { name = "SynergyPipeSteel" };
          _pipeSteelMat.color = new Color(1f, 0.54f, 0.08f, 1f);
        }
        glowGo.GetComponent<MeshRenderer>().sharedMaterial = _pipeSteelMat;
      }
      return go;
    }
  }
}
