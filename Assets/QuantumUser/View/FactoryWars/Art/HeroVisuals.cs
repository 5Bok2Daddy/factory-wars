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

    /// <summary>공장 본체 옆에 정비동을 증설하고, 최고 단계에 별도 굴뚝을 세운다.</summary>
    public static GameObject AttachFactoryUpgrade(Transform model, int level, float footprint, float modelHeight) {
      if (model == null || level <= 1) return null;
      var root = new GameObject($"FactoryUpgrade_Lv{level}");
      root.transform.SetParent(model.parent != null ? model.parent : model, false);
      root.transform.localPosition = Vector3.zero;
      root.transform.localRotation = Quaternion.identity;
      AddFactorySection(root.transform, "factory_service_annex", "factory_lv2", "ServiceAnnex",
        new Vector3(footprint * 0.83f, 0f, footprint * 0.64f), new Vector2(footprint * 0.67f, 2.05f));
      if (level >= 3) {
        AddFactorySection(root.transform, "factory_expansion_stack", "factory_lv3", "ExpansionStack",
          new Vector3(-footprint * 0.79f, 0f, footprint * 0.69f),
          new Vector2(footprint * 0.43f, Mathf.Clamp(modelHeight + 1.45f, 4.8f, 8.8f)));
      }
      return root;
    }

    /// <summary>
    /// 승인된 사령부(HQ) 1→2→3단계 컨셉 시안 증설 비주얼:
    /// - Tier 1: 기본 야전 막사 본관(Barracks + 위장 텐트 + 모래주머니 + 보급품)
    /// - Tier 2: 좌·우 작전통제실 윙(Drone Control Room) + 비상 발전기 + 회전 이동식 레이더 + 좌측 8륜 지휘 장갑차(GLB APC)
    /// - Tier 3: 대형 관제탑(ControlTower) + 외곽 감시탑 + 강화 콘크리트 방벽 + 투광등 + 좌측 후방 헬리패드 & 지휘 헬기(GLB Little Bird)
    /// </summary>
    public static GameObject AttachHqTierUpgrade(Transform model, int tier, float footprint, float modelHeight) {
      if (model == null || tier <= 1) return null;
      var root = new GameObject($"HqTierUpgrade_T{tier}");
      root.transform.SetParent(model.parent != null ? model.parent : model, false);
      root.transform.localPosition = Vector3.zero;
      root.transform.localRotation = Quaternion.identity;

      // Tier 2 (Comms & Ops HQ): 좌·우 컨테이너 작전통제실 윙 + 비상 발전기 + 회전 레이더 + 좌측 지휘 장갑차(APC).
      MapPolishArt.Place("HqControlRoom1", root.transform, new Vector3(-5.6f, 0f, 0.8f), Quaternion.Euler(0f, 90f, 0f), new Vector3(5.0f, 2.9f, 2.8f));
      MapPolishArt.Place("HqControlRoom2", root.transform, new Vector3(5.6f, 0f, 0.8f), Quaternion.Euler(0f, -90f, 0f), new Vector3(5.0f, 2.9f, 2.8f));
      MapPolishArt.Place("HqGenerator", root.transform, new Vector3(7.0f, 0f, -2.1f), Quaternion.Euler(0f, -90f, 0f), new Vector3(2.2f, 1.8f, 1.6f));
      MapPolishArt.Place("Radar", root.transform, new Vector3(4.2f, 0f, -5.2f), Quaternion.Euler(0f, -32f, 0f), new Vector3(3.1f, 4.1f, 3.1f));
      MapPolishArt.Place("HqCmdApc", root.transform, new Vector3(-5.0f, 0f, -5.1f), Quaternion.Euler(0f, 142f, 0f), new Vector3(2.5f, 2.6f, 5.6f));

      if (tier >= 3) {
        // Tier 3 (Strategic Command HQ): 후방 좌측 대형 관제탑 + 전방 좌측 감시탑 + 전방 우측 헬리패드 & 리틀버드 지휘 헬기 + 비왜곡 방호벽/투광등.
        MapPolishArt.Place("HqControlTower", root.transform, new Vector3(6.6f, 0f, -6.4f), Quaternion.Euler(0f, 180f, 0f), new Vector3(4.2f, 10.2f, 4.2f));
        MapPolishArt.Place("Tower", root.transform, new Vector3(7.4f, 0f, 5.8f), Quaternion.Euler(0f, -20f, 0f), new Vector3(2.8f, 6.2f, 2.8f));
        MapPolishArt.Place("HqHelipad", root.transform, new Vector3(-5.0f, 0.04f, 6.2f), Quaternion.identity, new Vector3(6.6f, 0.14f, 6.6f));
        MapPolishArt.Place("HqCmdHeli", root.transform, new Vector3(-5.0f, 0.16f, 6.2f), Quaternion.Euler(0f, 138f, 0f), new Vector3(5.2f, 2.1f, 5.0f));
        // 왜곡 없는 1:1 비율 콘크리트 방호벽(Jersey) 좌·우·후면 정렬 배치.
        MapPolishArt.Place("Jersey", root.transform, new Vector3(-9.2f, 0f, 1.0f), Quaternion.Euler(0f, 90f, 0f), new Vector3(3.4f, 1.5f, 1.1f));
        MapPolishArt.Place("Jersey", root.transform, new Vector3(-9.2f, 0f, 4.8f), Quaternion.Euler(0f, 90f, 0f), new Vector3(3.4f, 1.5f, 1.1f));
        MapPolishArt.Place("Jersey", root.transform, new Vector3(9.2f, 0f, 1.0f), Quaternion.Euler(0f, -90f, 0f), new Vector3(3.4f, 1.5f, 1.1f));
        MapPolishArt.Place("Jersey", root.transform, new Vector3(9.2f, 0f, 4.8f), Quaternion.Euler(0f, -90f, 0f), new Vector3(3.4f, 1.5f, 1.1f));
        MapPolishArt.Place("HqFloodlight", root.transform, new Vector3(-8.2f, 0f, 8.2f), Quaternion.Euler(0f, 135f, 0f), new Vector3(1.4f, 4.4f, 1.4f));
        MapPolishArt.Place("HqFloodlight", root.transform, new Vector3(8.2f, 0f, 8.2f), Quaternion.Euler(0f, -135f, 0f), new Vector3(1.4f, 4.4f, 1.4f));
      }
      return root;
    }

    static void AddFactorySection(Transform parent, string id, string fallback, string name, Vector3 position, Vector2 size) {
      var fitted = VisualCatalog.TryFilled(VisualCatalog.FxId(id), out var slot);
      if (fitted == false && VisualCatalog.TryFilled(VisualCatalog.FxId(fallback), out slot) == false) return;
      var part = Object.Instantiate(slot.Prefab, parent).transform;
      part.name = name;
      if (fitted) {
        var bounds = VisualCatalog.LocalBounds(slot.Prefab);
        var width = size.x / Mathf.Max(0.1f, bounds.size.x, bounds.size.z);
        var scale = new Vector3(width, size.y / Mathf.Max(0.1f, bounds.size.y), width);
        part.localScale = scale;
        part.localPosition = position - Vector3.Scale(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), scale);
      }
      foreach (var collider in part.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
    }

    static Material _pipFullMat;
    static Material _pipEmptyMat;

    /// <summary>12m 인접 시너지 파이프라인: 100% 실제 Synty 산업 배관(SM_Prop_Pipeline_Pipe_Large_01) 연결(원시 박스 금지).</summary>
    public static GameObject BuildSynergyPipeline(Transform parent, Vector3 from, Vector3 to, bool isPower) {
      var a = new Vector3(from.x, 0.08f, from.z);
      var b = new Vector3(to.x, 0.08f, to.z);
      var delta = b - a;
      var len = delta.magnitude;
      if (len < 1f) return null;
      if (VisualCatalog.TryFilled(VisualCatalog.FxId("synergy_pipe"), out var slot) == false || slot.Prefab == null) {
        return null;
      }
      var go = new GameObject(isPower ? "SynergyPipeline_Power" : "SynergyPipeline_Steel");
      go.transform.SetParent(parent, false);
      go.transform.position = (a + b) * 0.5f;
      go.transform.rotation = Quaternion.LookRotation(delta / len, Vector3.up);
      var pipe = Object.Instantiate(slot.Prefab, go.transform);
      pipe.name = "SyntyPipelineMesh";
      var bounds = VisualCatalog.LocalBounds(slot.Prefab);
      var pipeLen = Mathf.Max(0.5f, Mathf.Max(bounds.size.x, bounds.size.z));
      var alongX = bounds.size.x > bounds.size.z;
      pipe.transform.localRotation = Quaternion.Euler(0f, alongX ? 90f : 0f, 0f);
      var s = slot.Scale > 0f ? slot.Scale : 0.55f;
      var lenScale = Mathf.Max(0.25f, (len - 2.6f) / pipeLen);
      pipe.transform.localScale = alongX ? new Vector3(lenScale, s, s) : new Vector3(s, s, lenScale);
      pipe.transform.localPosition = new Vector3(0f, -bounds.min.y * s, 0f);
      return go;
    }

    /// <summary>수륙양용 상륙정(Landing Craft) 전용 PBR 강철 화물선 + 장갑 갑판 + 후면 브리지 킷배시.</summary>
    public static void BuildLandingCraft(ArtView view, int color) {
      if (view == null || view.Model == null) return;
      view.Slot = null;
      var model = view.Model.transform;
      model.localPosition = Vector3.zero;
      model.localRotation = Quaternion.identity;
      model.localScale = Vector3.one * 1.15f;
      foreach (var r in model.GetComponents<Renderer>()) r.enabled = false;
      for (var i = model.childCount - 1; i >= 0; i--) Object.DestroyImmediate(model.GetChild(i).gameObject);

      var kit = new MeshKit();
      // 평저 강철 선체 & 측면 방탄 현측 + 화물 갑판.
      kit.Paint(Swatch.MetalDark).Chamfer(new Vector3(0f, 0.35f, 0f), new Vector3(3.1f, 0.7f, 6.0f), 0.14f);
      kit.Paint(Swatch.ConcreteDark).Box(new Vector3(0f, 0.72f, 0.3f), new Vector3(2.3f, 0.1f, 4.4f));
      kit.Team().Box(new Vector3(-1.42f, 1.05f, 0.1f), new Vector3(0.32f, 0.85f, 5.4f));
      kit.Team().Box(new Vector3(1.42f, 1.05f, 0.1f), new Vector3(0.32f, 0.85f, 5.4f));
      // 선미 장갑 조타실 + 레이더 마스트 + 안전 줄무늬.
      kit.Paint(Swatch.ArmorDark).Chamfer(new Vector3(0f, 1.45f, -2.15f), new Vector3(2.4f, 1.2f, 1.45f), 0.1f);
      kit.Paint(Swatch.GlowCyan).Box(new Vector3(0f, 1.72f, -1.42f), new Vector3(1.8f, 0.32f, 0.08f));
      kit.Paint(Swatch.StripeYellow).Box(new Vector3(0f, 0.78f, 2.55f), new Vector3(2.3f, 0.14f, 0.32f));
      var go = new GameObject("HeroLandingCraft", typeof(MeshFilter), typeof(MeshRenderer));
      go.transform.SetParent(model, false);
      go.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("hero_landing_craft");
      go.GetComponent<MeshRenderer>().sharedMaterial = TeamMat(color);
    }

    /// <summary>수송 유닛 후면 경첩식 장갑 램프(CargoRamp) 부착. APC 전용(수송헬기는 측면 도어 전개, 상륙정은 Synty SM_Veh_Attach_Ramp_01 보유).</summary>
    public static Transform AttachTransportRamp(Transform turn, bool isAir, bool isNaval, int color) {
      if (turn == null || isAir || isNaval) return null;
      var pivot = new GameObject("CargoRampPivot").transform;
      pivot.SetParent(turn, false);
      pivot.localPosition = new Vector3(0f, 0.38f, -2.42f);
      var kit = new MeshKit();
      var w = 1.55f;
      var h = 1.15f;
      kit.Paint(Swatch.ArmorDark).Box(new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, 0.18f));
      kit.Paint(Swatch.StripeYellow).Box(new Vector3(0f, h - 0.14f, 0f), new Vector3(w * 0.92f, 0.18f, 0.22f));
      kit.Paint(Swatch.GlowCyan).Box(new Vector3(0f, h * 0.35f, -0.09f), new Vector3(w * 0.55f, 0.16f, 0.08f));
      var meshGo = new GameObject("CargoRampMesh", typeof(MeshFilter), typeof(MeshRenderer));
      meshGo.transform.SetParent(pivot, false);
      meshGo.GetComponent<MeshFilter>().sharedMesh = kit.ToMesh("transport_cargo_ramp");
      meshGo.GetComponent<MeshRenderer>().sharedMaterial = TeamMat(color);
      return pivot;
    }

    public static Material PipMaterial(bool occupied) {
      if (occupied) {
        if (_pipFullMat == null) {
          _pipFullMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default")) { name = "PassengerPipFull" };
          _pipFullMat.color = new Color(0.18f, 0.96f, 1f, 1f);
        }
        return _pipFullMat;
      }
      if (_pipEmptyMat == null) {
        _pipEmptyMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default")) { name = "PassengerPipEmpty" };
        _pipEmptyMat.color = new Color(0.14f, 0.18f, 0.24f, 0.92f);
      }
      return _pipEmptyMat;
    }

    /// <summary>월드 공간 탑승 인원 핍([■][■][■][□])을 유닛 체력바 바로 아래에 생성한다.</summary>
    public static GameObject CreatePassengerPips(ArtView view, int capacity, out MeshRenderer[] pips) {
      pips = null;
      if (view == null || capacity <= 0) return null;
      var count = Mathf.Clamp(capacity, 1, 6);
      var root = new GameObject("PassengerPips");
      root.transform.SetParent(view.transform, false);
      var barY = view.Bar != null ? view.Bar.transform.localPosition.y : view.Entry.BarHeight;
      root.transform.localPosition = new Vector3(0f, barY - 0.36f, 0f);
      root.transform.localRotation = Quaternion.Euler(VisualProfile.Current.Pitch, 0f, 0f);

      const float pipSize = 0.24f;
      const float pipGap = 0.09f;
      var totalW = count * pipSize + (count - 1) * pipGap;
      // 짙은 슬레이트 외곽 프레임.
      var bg = new GameObject("PipFrame", typeof(MeshFilter), typeof(MeshRenderer));
      bg.transform.SetParent(root.transform, false);
      var bgKit = new MeshKit().Paint(Swatch.Black);
      var fw = (totalW + 0.18f) * 0.5f;
      var fh = (pipSize + 0.14f) * 0.5f;
      bgKit.Quad(new Vector3(-fw, -fh, 0.02f), new Vector3(fw, -fh, 0.02f), new Vector3(fw, fh, 0.02f), new Vector3(-fw, fh, 0.02f), Vector3.back);
      bg.GetComponent<MeshFilter>().sharedMesh = bgKit.ToMesh("passenger_pip_frame");
      var bgR = bg.GetComponent<MeshRenderer>();
      bgR.sharedMaterial = ArtLook.NeutralMaterial;
      bgR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      bgR.receiveShadows = false;

      var pipKit = new MeshKit().Paint(Swatch.White);
      var h = pipSize * 0.5f;
      pipKit.Quad(new Vector3(-h, -h, 0f), new Vector3(h, -h, 0f), new Vector3(h, h, 0f), new Vector3(-h, h, 0f), Vector3.back);
      var pipMesh = pipKit.ToMesh("passenger_pip_quad");

      pips = new MeshRenderer[count];
      for (var i = 0; i < count; i++) {
        var go = new GameObject($"Pip{i}", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(root.transform, false);
        var x = -totalW * 0.5f + pipSize * 0.5f + i * (pipSize + pipGap);
        go.transform.localPosition = new Vector3(x, 0f, 0f);
        go.GetComponent<MeshFilter>().sharedMesh = pipMesh;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = PipMaterial(false);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        pips[i] = mr;
      }
      root.SetActive(false);
      return root;
    }
  }
}
