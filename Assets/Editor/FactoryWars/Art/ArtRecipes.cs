namespace FactoryWars.EditorTools {
  using System;
  using System.Collections.Generic;
  using FactoryWars.View;
  using UnityEngine;

  /// <summary>움직이는 파츠. 메쉬는 pivot을 원점으로 만든다. parent가 비어 있으면 몸체에 붙는다.</summary>
  public class MovingPart {
    public string Name;
    public string Parent;
    public Vector3 Pivot;
    public Action<MeshKit> Build;
  }

  public class ArtRecipe {
    public string Id;
    public string Kind;
    public int MaxRenderers;
    public int MaxTriangles;
    public float DisplayScale = 1f;
    public float Altitude;
    public Vector3[] Smoke = Array.Empty<Vector3>();
    public Action<MeshKit> Body;
    public MovingPart[] Moving = Array.Empty<MovingPart>();

    public ArtRecipe Smoking(params Vector3[] points) {
      Smoke = points;
      return this;
    }

    public ArtRecipe Flying(float altitude) {
      Altitude = altitude;
      return this;
    }
  }

  /// <summary>
  /// 사령부 2종, 유닛 12종, 철광산, 다리의 파츠 표. 나머지 건물은 ArtRecipes.Buildings.cs에 있다. 앞이 +Z, 위가 +Y다. 규칙은 visual-style 스킬을 따른다.
  /// 파츠 값은 에디터에서만 쓰는 모양 데이터라 게임 밸런스 에셋이 아니다.
  /// </summary>
  public static partial class ArtRecipes {
    static readonly Vector3 Z90 = new Vector3(90, 0, 0);

    // 화면 표시 배율. 시뮬레이션 크기는 그대로다.
    const float VehicleScale = 1.3f;
    const float InfantryScale = 1.5f;

    public static IEnumerable<ArtRecipe> All() {
      yield return Unit("ff_engineer", 3, 800, FreeEngineer, new MovingPart { Name = "Arm", Pivot = new Vector3(0.42f, 0.95f, 0.35f), Build = FreeEngineerArm });
      yield return Infantry("ff_ranger_squad", 900, k => Squad(k, Rangers, Ranger));
      yield return Infantry("ff_skyguard_squad", 800, k => Squad(k, Trio, Skyguard));
      yield return Unit("ff_eagle_tank", 3, 900, EagleHull,
        new MovingPart { Name = "Turret", Pivot = new Vector3(0, 1.06f, -0.42f), Build = EagleTurret },
        new MovingPart { Name = "Barrel", Parent = "Turret", Pivot = new Vector3(0, 0.22f, 0.72f), Build = EagleBarrel });
      yield return Unit("ff_hawk_gunship", 3, 900, HawkBody,
        new MovingPart { Name = "Rotor", Pivot = new Vector3(0, 0.95f, 0.05f), Build = HawkRotor },
        new MovingPart { Name = "TailFan", Pivot = new Vector3(0, 0.3f, -2.2f), Build = HawkFan }).Flying(2.8f);
      yield return Unit("ff_skyfortress_bomber", 2, 900, BomberBody,
        new MovingPart { Name = "BombDoors", Pivot = new Vector3(0, -0.28f, 0.1f), Build = BomberDoors }).Flying(3.2f);

      yield return Unit("cf_engineer", 3, 800, ContinentalEngineer, new MovingPart { Name = "Crane", Pivot = new Vector3(0, 1.0f, -0.65f), Build = ContinentalCrane });
      yield return Infantry("cf_militia", 600, k => Squad(k, Pair, Militia));
      yield return Infantry("cf_flame_squad", 700, k => Squad(k, Pair, Flamer));
      yield return Unit("cf_dragonscale_tank", 3, 1000, DragonHull,
        new MovingPart { Name = "Turret", Pivot = new Vector3(0, 0.82f, -0.1f), Build = DragonTurret },
        new MovingPart { Name = "Barrel", Parent = "Turret", Pivot = new Vector3(0, 0.25f, 0.68f), Build = DragonBarrel });
      yield return Unit("cf_thunder_aa", 4, 1000, ThunderHull,
        new MovingPart { Name = "Turret", Pivot = new Vector3(0, 0.78f, -0.1f), Build = ThunderTurret },
        new MovingPart { Name = "Guns", Parent = "Turret", Pivot = new Vector3(0, 0.3f, 0.3f), Build = ThunderGuns },
        new MovingPart { Name = "Radar", Parent = "Turret", Pivot = new Vector3(0, 0.62f, -0.4f), Build = ThunderRadar });
      yield return Unit("cf_longwall_mlrs", 3, 1000, LongwallBody,
        new MovingPart { Name = "Launcher", Pivot = new Vector3(0, 1.0f, -0.9f), Build = LongwallBase },
        new MovingPart { Name = "Tubes", Parent = "Launcher", Pivot = new Vector3(0, 0.25f, -0.6f), Build = LongwallTubes });

      yield return new ArtRecipe {
        Id = "ff_headquarters", Kind = "건물", MaxRenderers = 5, MaxTriangles = 2000, Body = FreeHeadquarters,
        Moving = new[] { new MovingPart { Name = "Radar", Pivot = new Vector3(0, 3.55f, 0), Build = FreeRadar } },
      };
      yield return new ArtRecipe {
        Id = "cf_headquarters", Kind = "건물", MaxRenderers = 5, MaxTriangles = 2200, Body = ContinentalHeadquarters,
        Moving = new[] { new MovingPart { Name = "Flag", Pivot = new Vector3(0, 5.1f, 0.05f), Build = ContinentalFlag } },
      };
      yield return new ArtRecipe {
        Id = "iron_mine", Kind = "자원", MaxRenderers = 2, MaxTriangles = 800, Body = k => MineRocks(k, 7),
        Moving = new[] { new MovingPart { Name = "Crystals", Pivot = Vector3.zero, Build = k => MineCrystals(k, 7) } },
      };
      yield return new ArtRecipe {
        Id = "bridge", Kind = "지형", MaxRenderers = 1, MaxTriangles = 800, Body = Bridge,
      };
      foreach (var building in Buildings()) {
        yield return building;
      }
    }

    static ArtRecipe Unit(string id, int maxRenderers, int maxTriangles, Action<MeshKit> body, params MovingPart[] moving) {
      return new ArtRecipe { Id = id, Kind = "유닛", MaxRenderers = maxRenderers, MaxTriangles = maxTriangles, DisplayScale = VehicleScale, Body = body, Moving = moving };
    }

    /// <summary>분대는 메쉬 하나다.</summary>
    static ArtRecipe Infantry(string id, int maxTriangles, Action<MeshKit> body) {
      return new ArtRecipe { Id = id, Kind = "유닛", MaxRenderers = 1, MaxTriangles = maxTriangles, DisplayScale = InfantryScale, Body = body };
    }

    // 공통

    /// <summary>축이 X인 바퀴.</summary>
    static void Wheel(MeshKit k, Vector3 center, float radius, float width, int sides = 8) {
      k.Push(center, new Vector3(0, 0, 90));
      k.Paint(Swatch.Rubber);
      k.Cylinder(new Vector3(0, -width / 2, 0), radius, width, sides);
      k.Paint(Swatch.MetalDark);
      k.Disc(new Vector3(0, width / 2 + 0.01f, 0), radius * 0.55f, 6);
      k.Disc(new Vector3(0, -width / 2 - 0.01f, 0), radius * 0.55f, 6);
      k.Pop();
    }

    static void Track(MeshKit k, float x, float length, float height, float width, int wheels) {
      k.Paint(Swatch.Rubber);
      k.Chamfer(new Vector3(x, height / 2 + 0.02f, 0), new Vector3(width, height, length), height * 0.32f);
      k.Paint(Swatch.MetalDark);
      for (var i = 0; i < wheels; i++) {
        var z = -length / 2 + height * 0.5f + (length - height) * i / (wheels - 1);
        var side = Mathf.Sign(x);
        k.Push(new Vector3(x + side * (width / 2 + 0.005f), height / 2, z), new Vector3(0, 0, -side * 90));
        k.Disc(Vector3.zero, height * 0.32f, 6);
        k.Pop();
      }
    }

    static void TurnZ(MeshKit k, Vector3 position, Action body) {
      k.Push(position, Z90);
      body();
      k.Pop();
    }

    // 보병

    static readonly Vector3[] Rangers = { new Vector3(-0.48f, 0, 0.5f), new Vector3(0.5f, 0, 0.38f), new Vector3(-0.42f, 0, -0.5f), new Vector3(0.46f, 0, -0.56f) };
    static readonly Vector3[] Trio = { new Vector3(0, 0, 0.55f), new Vector3(-0.55f, 0, -0.35f), new Vector3(0.55f, 0, -0.35f) };
    static readonly Vector3[] Pair = { new Vector3(-0.42f, 0, 0.05f), new Vector3(0.42f, 0, -0.1f) };

    static void Squad(MeshKit k, Vector3[] positions, Action<MeshKit, int> soldier) {
      for (var i = 0; i < positions.Length; i++) {
        k.Push(positions[i], new Vector3(0, (MeshKit.Hash(i + 5, 1) - 0.5f) * 24f, 0));
        soldier(k, i);
        k.Pop();
      }
    }

    /// <summary>자유 연방 장갑복: 둥근 헬멧과 곡면 어깨.</summary>
    static void Armored(MeshKit k) {
      k.Paint(Swatch.MetalDark);
      k.Taper(new Vector3(-0.13f, 0, 0.03f), new Vector2(0.17f, 0.28f), new Vector2(0.13f, 0.16f), 0.62f);
      k.Taper(new Vector3(0.13f, 0, -0.03f), new Vector2(0.17f, 0.28f), new Vector2(0.13f, 0.16f), 0.62f);
      k.Paint(Swatch.Metal);
      k.Lathe(new Vector3(0, 0.58f, 0), new[] { new Vector2(0.15f, 0), new Vector2(0.2f, 0.18f), new Vector2(0.27f, 0.42f), new Vector2(0.19f, 0.54f) }, 6);
      k.Paint(Swatch.MetalDark);
      k.Push(new Vector3(0.27f, 0.97f, 0), new Vector3(58, 0, 0));
      k.Box(new Vector3(0, 0, 0.2f), new Vector3(0.11f, 0.11f, 0.46f));
      k.Pop();
      k.Push(new Vector3(-0.27f, 0.97f, 0), new Vector3(58, 24, 0));
      k.Box(new Vector3(0, 0, 0.2f), new Vector3(0.11f, 0.11f, 0.46f));
      k.Pop();
      k.Paint(Swatch.MetalLight);
      k.Lathe(new Vector3(0, 1.1f, 0), new[] { new Vector2(0.15f, 0), new Vector2(0.18f, 0.1f), new Vector2(0.15f, 0.22f), new Vector2(0, 0.3f) }, 6);
      k.Team();
      k.Dome(new Vector3(0.28f, 0.97f, 0), 0.15f, 0.13f, 5, 1);
      k.Dome(new Vector3(-0.28f, 0.97f, 0), 0.15f, 0.13f, 5, 1);
    }

    static void Ranger(MeshKit k, int index) {
      Armored(k);
      k.Paint(Swatch.Black);
      k.Box(new Vector3(0.1f, 0.74f, 0.42f), new Vector3(0.08f, 0.11f, 0.86f));
      k.Paint(Swatch.MetalDark);
      k.Box(new Vector3(0.1f, 0.64f, 0.34f), new Vector3(0.07f, 0.14f, 0.1f));
    }

    static void Skyguard(MeshKit k, int index) {
      Armored(k);
      k.Paint(Swatch.MetalDark);
      TurnZ(k, new Vector3(0.3f, 1.14f, -0.68f), () => k.Cylinder(Vector3.zero, 0.13f, 1.4f, 6));
      k.Team();
      TurnZ(k, new Vector3(0.3f, 1.14f, 0.56f), () => k.Cylinder(Vector3.zero, 0.15f, 0.16f, 6));
      TurnZ(k, new Vector3(0.3f, 1.14f, -0.7f), () => k.Cylinder(Vector3.zero, 0.15f, 0.12f, 6));
      k.Paint(Swatch.Black);
      TurnZ(k, new Vector3(0.3f, 1.14f, 0.73f), () => k.Disc(Vector3.zero, 0.1f, 6));
    }

    /// <summary>대륙 연방 군복: 각진 철모와 조끼.</summary>
    static void Fatigues(MeshKit k, Swatch main, Swatch dark) {
      k.Paint(dark);
      k.Box(new Vector3(-0.13f, 0.31f, 0.03f), new Vector3(0.15f, 0.62f, 0.18f));
      k.Box(new Vector3(0.13f, 0.31f, -0.03f), new Vector3(0.15f, 0.62f, 0.18f));
      k.Paint(Swatch.Black);
      k.Box(new Vector3(-0.13f, 0.05f, 0.07f), new Vector3(0.17f, 0.1f, 0.3f));
      k.Box(new Vector3(0.13f, 0.05f, 0.01f), new Vector3(0.17f, 0.1f, 0.3f));
      k.Paint(main);
      k.Taper(new Vector3(0, 0.6f, 0), new Vector2(0.36f, 0.22f), new Vector2(0.48f, 0.28f), 0.46f);
      k.Paint(dark);
      k.Taper(new Vector3(0, 0.7f, 0), new Vector2(0.4f, 0.3f), new Vector2(0.46f, 0.32f), 0.3f);
      k.Paint(main);
      k.Push(new Vector3(0.28f, 1.0f, 0), new Vector3(58, 0, 0));
      k.Box(new Vector3(0, 0, 0.2f), new Vector3(0.12f, 0.12f, 0.46f));
      k.Team();
      k.Box(new Vector3(0, 0, 0.02f), new Vector3(0.14f, 0.14f, 0.14f));
      k.Pop();
      k.Paint(main);
      k.Push(new Vector3(-0.28f, 1.0f, 0), new Vector3(58, 24, 0));
      k.Box(new Vector3(0, 0, 0.2f), new Vector3(0.12f, 0.12f, 0.46f));
      k.Pop();
      k.Paint(Swatch.Sand);
      k.Box(new Vector3(0, 1.17f, 0), new Vector3(0.17f, 0.18f, 0.17f));
      k.Paint(dark);
      k.Frustum(new Vector3(0, 1.2f, 0), 0.2f, 0.18f, 0.03f, 6);
      k.Frustum(new Vector3(0, 1.23f, 0), 0.17f, 0.09f, 0.13f, 6);
      k.Team();
      k.Box(new Vector3(0, 0.9f, 0.15f), new Vector3(0.24f, 0.08f, 0.02f));
    }

    static void Militia(MeshKit k, int index) {
      Fatigues(k, Swatch.Olive, Swatch.OliveDark);
      k.Paint(Swatch.Black);
      k.Box(new Vector3(0.1f, 0.76f, 0.44f), new Vector3(0.08f, 0.1f, 0.78f));
      k.Paint(Swatch.WoodDark);
      k.Box(new Vector3(0.1f, 0.72f, 0.06f), new Vector3(0.08f, 0.13f, 0.22f));
    }

    /// <summary>민병대와 게임 시점에서 구분되게, 머리 위까지 오는 큰 연료통 2개와 넓은 주황 포구를 단다.</summary>
    static void Flamer(MeshKit k, int index) {
      Fatigues(k, Swatch.ArmorDark, Swatch.MetalDark);
      foreach (var x in new[] { -0.22f, 0.22f }) {
        k.Paint(Swatch.MetalLight);
        k.Cylinder(new Vector3(x, 0.44f, -0.38f), 0.22f, 1.02f, 6);
        k.Dome(new Vector3(x, 1.46f, -0.38f), 0.22f, 0.16f, 6, 1);
        k.Paint(Swatch.Hazard);
        k.Cylinder(new Vector3(x, 1.0f, -0.38f), 0.235f, 0.16f, 6);
      }
      k.Paint(Swatch.Metal);
      k.Box(new Vector3(0.1f, 0.74f, 0.46f), new Vector3(0.12f, 0.12f, 0.86f));
      k.Paint(Swatch.MetalDark);
      TurnZ(k, new Vector3(0.1f, 0.74f, 0.82f), () => k.Frustum(Vector3.zero, 0.09f, 0.17f, 0.14f, 6));
      k.Glow(Swatch.GlowOrange);
      TurnZ(k, new Vector3(0.1f, 0.74f, 0.96f), () => k.Cone(Vector3.zero, 0.19f, 0.4f, 6));
    }

    // 자유 연방 차량

    static void FreeEngineer(MeshKit k) {
      Track(k, 0.56f, 1.8f, 0.42f, 0.34f, 3);
      Track(k, -0.56f, 1.8f, 0.42f, 0.34f, 3);
      k.Paint(Swatch.Metal);
      k.Chamfer(new Vector3(0, 0.56f, -0.05f), new Vector3(1.1f, 0.3f, 1.6f), 0.08f);
      k.Lathe(new Vector3(-0.18f, 0.7f, 0.05f), new[] { new Vector2(0.44f, 0), new Vector2(0.42f, 0.32f), new Vector2(0.28f, 0.52f), new Vector2(0f, 0.56f) }, 8);
      k.Paint(Swatch.Glass);
      k.Box(new Vector3(-0.18f, 1.02f, 0.36f), new Vector3(0.42f, 0.2f, 0.1f));
      k.Team();
      k.Box(new Vector3(0.53f, 0.82f, -0.32f), new Vector3(0.1f, 0.26f, 0.5f));
      k.Box(new Vector3(-0.18f, 1.28f, 0.02f), new Vector3(0.3f, 0.04f, 0.3f));
      // 뒤쪽 드릴
      k.Paint(Swatch.MetalDark);
      TurnZ(k, new Vector3(0, 0.62f, -1.28f), () => k.Cylinder(Vector3.zero, 0.18f, 0.44f, 6));
      k.Paint(Swatch.Metal);
      k.Push(new Vector3(0, 0.62f, -1.3f), new Vector3(-90, 0, 0));
      k.Lathe(Vector3.zero, new[] { new Vector2(0.2f, 0), new Vector2(0.15f, 0.2f), new Vector2(0.18f, 0.22f), new Vector2(0.09f, 0.42f), new Vector2(0.11f, 0.44f), new Vector2(0, 0.72f) }, 6);
      k.Pop();
    }

    static void FreeEngineerArm(MeshKit k) {
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(0, -0.12f, 0), 0.14f, 0.24f, 6);
      k.Paint(Swatch.Metal);
      k.Push(Vector3.zero, new Vector3(-38, 0, 0));
      k.Box(new Vector3(0, 0, 0.42f), new Vector3(0.16f, 0.16f, 0.96f));
      k.Pop();
      k.Push(new Vector3(0, 0.52f, 0.68f), new Vector3(58, 0, 0));
      k.Paint(Swatch.Metal);
      k.Box(new Vector3(0, 0, 0.36f), new Vector3(0.14f, 0.14f, 0.82f));
      k.Team();
      k.Box(new Vector3(0.08f, 0, 0.36f), new Vector3(0.02f, 0.09f, 0.42f));
      k.Pop();
      k.Paint(Swatch.MetalLight);
      k.Taper(new Vector3(0.12f, -0.34f, 1.15f), new Vector2(0.06f, 0.16f), new Vector2(0.09f, 0.1f), 0.34f, new Vector2(-0.05f, 0));
      k.Taper(new Vector3(-0.12f, -0.34f, 1.15f), new Vector2(0.06f, 0.16f), new Vector2(0.09f, 0.1f), 0.34f, new Vector2(0.05f, 0));
    }

    static void EagleHull(MeshKit k) {
      Track(k, 0.98f, 3.2f, 0.46f, 0.4f, 4);
      Track(k, -0.98f, 3.2f, 0.46f, 0.4f, 4);
      // 궤도 사이를 채우는 아랫배
      k.Paint(Swatch.ArmorDark);
      k.Taper(new Vector3(0, 0.16f, 0), new Vector2(1.46f, 2.7f), new Vector2(1.72f, 3.3f), 0.34f);
      // 궤도를 덮는 두꺼운 측면 장갑
      foreach (var side in new[] { -1f, 1f }) {
        k.Paint(Swatch.Armor);
        k.Taper(new Vector3(side * 0.98f, 0.5f, 0), new Vector2(0.56f, 3.28f), new Vector2(0.44f, 2.96f), 0.16f);
        k.Paint(Swatch.ArmorDark);
        k.Push(new Vector3(side * 1.22f, 0.4f, 0), new Vector3(0, 0, side * 14f));
        k.Box(Vector3.zero, new Vector3(0.08f, 0.34f, 2.9f));
        k.Pop();
      }
      // 윗차체: 앞은 눕고 뒤는 가파른 경사 장갑. 높이가 있어야 판처럼 보이지 않는다.
      k.Paint(Swatch.Armor);
      k.Taper(new Vector3(0, 0.5f, -0.04f), new Vector2(1.7f, 3.24f), new Vector2(1.3f, 1.66f), 0.54f, new Vector2(0, -0.42f));
      // 앞 경사판과 조종수 해치
      k.Paint(Swatch.ArmorDark);
      k.Push(new Vector3(0, 0.77f, 0.98f), new Vector3(24, 0, 0));
      k.Box(Vector3.zero, new Vector3(1.16f, 0.05f, 1.0f));
      k.Paint(Swatch.MetalDark);
      k.Box(new Vector3(0.3f, 0.04f, -0.1f), new Vector3(0.3f, 0.05f, 0.26f));
      k.Pop();
      k.Paint(Swatch.MetalDark);
      k.Frustum(new Vector3(0, 1.02f, -0.42f), 0.74f, 0.68f, 0.05f, 10);
      k.Team();
      foreach (var side in new[] { -1f, 1f }) {
        k.Push(new Vector3(side * 0.77f, 0.76f, -0.28f), new Vector3(0, 0, side * 20.3f));
        k.Box(Vector3.zero, new Vector3(0.03f, 0.16f, 1.6f));
        k.Pop();
      }
      k.Glow();
      k.Box(new Vector3(0, 0.76f, -1.5f), new Vector3(0.9f, 0.07f, 0.05f));
    }

    static void EagleTurret(MeshKit k) {
      k.Push(Vector3.zero, default, new Vector3(1f, 1f, 1.28f));
      k.Paint(Swatch.Armor);
      k.Dome(Vector3.zero, 0.72f, 0.46f, 10, 3);
      k.Pop();
      k.Paint(Swatch.Metal);
      k.Frustum(new Vector3(0.1f, 0.4f, -0.15f), 0.22f, 0.17f, 0.1f, 8);
      k.Team();
      k.Box(new Vector3(0.62f, 0.14f, -0.05f), new Vector3(0.05f, 0.09f, 0.72f));
      k.Box(new Vector3(-0.62f, 0.14f, -0.05f), new Vector3(0.05f, 0.09f, 0.72f));
    }

    static void EagleBarrel(MeshKit k) {
      k.Paint(Swatch.Armor);
      TurnZ(k, new Vector3(0, 0, -0.1f), () => k.Frustum(Vector3.zero, 0.19f, 0.13f, 0.34f, 8));
      k.Paint(Swatch.MetalDark);
      TurnZ(k, new Vector3(0, 0, 0.2f), () => k.Cylinder(Vector3.zero, 0.08f, 1.45f, 8));
      TurnZ(k, new Vector3(0, 0, 1.5f), () => k.Cylinder(Vector3.zero, 0.11f, 0.2f, 8));
      k.Paint(Swatch.Black);
      TurnZ(k, new Vector3(0, 0, 1.71f), () => k.Disc(Vector3.zero, 0.07f, 6));
    }

    static void HawkBody(MeshKit k) {
      // 짧고 둥근 동체. 실존 공격헬기의 길쭉한 복좌 동체를 피한다.
      k.Paint(Swatch.Armor);
      k.Push(new Vector3(0, 0.1f, -0.75f), Z90);
      k.Lathe(Vector3.zero, new[] { new Vector2(0.28f, 0), new Vector2(0.7f, 0.55f), new Vector2(0.78f, 1.05f), new Vector2(0.62f, 1.6f), new Vector2(0.26f, 2.0f), new Vector2(0, 2.08f) }, 8);
      k.Pop();
      k.Paint(Swatch.Glass);
      k.Push(new Vector3(0, 0.2f, 0.62f), new Vector3(24, 0, 0), new Vector3(1f, 1f, 1.5f));
      k.Dome(Vector3.zero, 0.5f, 0.46f, 8, 2);
      k.Pop();
      k.Paint(Swatch.MetalDark);
      k.Frustum(new Vector3(0, 0.62f, 0.05f), 0.36f, 0.16f, 0.34f, 8);
      // 꼬리 붐과 덕트
      k.Paint(Swatch.Armor);
      TurnZ(k, new Vector3(0, 0.3f, -2.05f), () => k.Frustum(Vector3.zero, 0.12f, 0.3f, 1.35f, 6));
      k.Paint(Swatch.MetalDark);
      k.Push(new Vector3(0, 0.3f, -2.48f), Z90);
      k.Lathe(Vector3.zero, new[] { new Vector2(0.62f, 0), new Vector2(0.66f, 0.14f), new Vector2(0.62f, 0.58f) }, 10);
      k.Pop();
      k.Team();
      k.Push(new Vector3(0, 0.3f, -2.28f), Z90);
      k.Cylinder(Vector3.zero, 0.68f, 0.12f, 10);
      k.Pop();
      // 짧은 날개와 큰 원통 포드
      k.Paint(Swatch.MetalDark);
      k.Taper(new Vector3(0, 0.0f, -0.25f), new Vector2(2.6f, 0.62f), new Vector2(2.4f, 0.5f), 0.16f);
      foreach (var x in new[] { -1.28f, 1.28f }) {
        k.Paint(Swatch.Metal);
        TurnZ(k, new Vector3(x, -0.12f, -0.85f), () => k.Cylinder(Vector3.zero, 0.27f, 1.25f, 8));
        k.Team();
        TurnZ(k, new Vector3(x, -0.12f, 0.02f), () => k.Cylinder(Vector3.zero, 0.285f, 0.18f, 8));
        k.Paint(Swatch.Black);
        TurnZ(k, new Vector3(x, -0.12f, 0.41f), () => k.Disc(Vector3.zero, 0.2f, 8));
      }
      k.Paint(Swatch.MetalDark);
      k.Box(new Vector3(0, -0.72f, 0.1f), new Vector3(0.1f, 0.24f, 0.12f));
    }

    static void HawkRotor(MeshKit k) {
      k.Paint(Swatch.MetalDark);
      k.Cylinder(Vector3.zero, 0.13f, 0.16f, 6);
      for (var i = 0; i < 3; i++) {
        k.Push(new Vector3(0, 0.1f, 0), new Vector3(0, i * 120f, 0));
        k.Taper(new Vector3(0, -0.015f, 1.45f), new Vector2(0.24f, 2.7f), new Vector2(0.2f, 2.7f), 0.03f);
        k.Pop();
      }
    }

    static void HawkFan(MeshKit k) {
      k.Paint(Swatch.Metal);
      k.Push(Vector3.zero, Z90);
      k.Cylinder(new Vector3(0, -0.05f, 0), 0.1f, 0.1f, 6);
      k.Pop();
      for (var i = 0; i < 5; i++) {
        k.Push(Vector3.zero, new Vector3(0, 0, i * 72f));
        k.Box(new Vector3(0.28f, 0, 0), new Vector3(0.46f, 0.1f, 0.02f));
        k.Pop();
      }
    }

    static void BomberBody(MeshKit k) {
      // 꼬리 날개 없는 삼각 전익기
      k.Paint(Swatch.Armor);
      k.Slab(new[] { new Vector2(0, 2.6f), new Vector2(0.55f, 1.6f), new Vector2(0.55f, -1.5f), new Vector2(0, -1.05f), new Vector2(-0.55f, -1.5f), new Vector2(-0.55f, 1.6f) }, -0.2f, 0.16f);
      k.Slab(new[] { new Vector2(0.5f, 1.72f), new Vector2(5.1f, -1.95f), new Vector2(4.65f, -2.4f), new Vector2(0.5f, -1.45f) }, -0.11f, 0.1f);
      k.Slab(new[] { new Vector2(-0.5f, 1.72f), new Vector2(-0.5f, -1.45f), new Vector2(-4.65f, -2.4f), new Vector2(-5.1f, -1.95f) }, -0.11f, 0.1f);
      k.Push(new Vector3(0, 0.1f, 0.2f), default, new Vector3(1f, 1f, 3.0f));
      k.Paint(Swatch.Metal);
      k.Dome(Vector3.zero, 0.72f, 0.34f, 8, 2);
      k.Pop();
      k.Paint(Swatch.Glass);
      k.Push(new Vector3(0, 0.22f, 1.45f), new Vector3(18, 0, 0));
      k.Chamfer(Vector3.zero, new Vector3(0.5f, 0.1f, 0.55f), 0.04f);
      k.Pop();
      // 후방 엔진 4개
      foreach (var x in new[] { -1.9f, -1.0f, 1.0f, 1.9f }) {
        var z = -0.75f - Mathf.Abs(x) * 0.36f;
        k.Paint(Swatch.MetalDark);
        TurnZ(k, new Vector3(x, 0.14f, z - 0.5f), () => k.Frustum(Vector3.zero, 0.2f, 0.27f, 1.0f, 8));
        k.Glow();
        TurnZ(k, new Vector3(x, 0.14f, z - 0.56f), () => k.Cylinder(Vector3.zero, 0.24f, 0.08f, 8));
        k.Push(new Vector3(x, 0.14f, z - 0.51f), new Vector3(-90, 0, 0));
        k.Disc(Vector3.zero, 0.16f, 8);
        k.Pop();
      }
      k.Team();
      k.Slab(new[] { new Vector2(4.2f, -1.2f), new Vector2(5.12f, -1.95f), new Vector2(4.68f, -2.41f), new Vector2(3.8f, -2.02f) }, 0.1f, 0.13f);
      k.Slab(new[] { new Vector2(-4.2f, -1.2f), new Vector2(-3.8f, -2.02f), new Vector2(-4.68f, -2.41f), new Vector2(-5.12f, -1.95f) }, 0.1f, 0.13f);
      k.Box(new Vector3(0, 0.17f, -0.9f), new Vector3(0.7f, 0.02f, 0.7f));
    }

    static void BomberDoors(MeshKit k) {
      k.Paint(Swatch.Black);
      k.Box(new Vector3(0, 0.05f, 0), new Vector3(0.7f, 0.06f, 1.3f));
      k.Paint(Swatch.MetalDark);
      k.Box(new Vector3(-0.2f, -0.02f, 0), new Vector3(0.32f, 0.04f, 1.25f));
      k.Box(new Vector3(0.2f, -0.02f, 0), new Vector3(0.32f, 0.04f, 1.25f));
    }

    // 대륙 연방 차량

    static void ContinentalEngineer(MeshKit k) {
      k.Paint(Swatch.Olive);
      k.Box(new Vector3(0, 0.5f, -0.1f), new Vector3(1.1f, 0.2f, 2.3f));
      foreach (var z in new[] { 0.65f, -0.75f }) {
        Wheel(k, new Vector3(0.62f, 0.33f, z), 0.33f, 0.24f);
        Wheel(k, new Vector3(-0.62f, 0.33f, z), 0.33f, 0.24f);
      }
      k.Paint(Swatch.Olive);
      k.Taper(new Vector3(0, 0.6f, 0.45f), new Vector2(1.08f, 0.95f), new Vector2(0.95f, 0.62f), 0.68f, new Vector2(0, -0.1f));
      k.Taper(new Vector3(0, 0.6f, 1.0f), new Vector2(0.98f, 0.5f), new Vector2(0.9f, 0.46f), 0.3f);
      k.Paint(Swatch.Glass);
      k.Box(new Vector3(0, 1.06f, 0.65f), new Vector3(0.78f, 0.22f, 0.04f));
      k.Paint(Swatch.OliveDark);
      k.Box(new Vector3(0, 0.74f, -0.55f), new Vector3(1.08f, 0.26f, 1.0f));
      // 앞 불도저 날
      k.Team();
      k.Taper(new Vector3(0, 0.12f, 1.48f), new Vector2(1.5f, 0.12f), new Vector2(1.5f, 0.1f), 0.62f, new Vector2(0, 0.14f));
      k.Paint(Swatch.MetalDark);
      k.Box(new Vector3(0.36f, 0.42f, 1.32f), new Vector3(0.09f, 0.09f, 0.26f));
      k.Box(new Vector3(-0.36f, 0.42f, 1.32f), new Vector3(0.09f, 0.09f, 0.26f));
      k.Glow(Swatch.GlowOrange);
      k.Box(new Vector3(0.28f, 1.31f, 0.46f), new Vector3(0.14f, 0.05f, 0.08f));
      k.Box(new Vector3(-0.28f, 1.31f, 0.46f), new Vector3(0.14f, 0.05f, 0.08f));
    }

    static void ContinentalCrane(MeshKit k) {
      k.Paint(Swatch.OliveDark);
      k.Cylinder(new Vector3(0, -0.14f, 0), 0.26f, 0.14f, 6);
      k.Paint(Swatch.Olive);
      k.Push(new Vector3(0, 0, 0), new Vector3(0, 180, 0));
      k.Push(new Vector3(0, 0.02f, 0), new Vector3(-38, 0, 0));
      k.Box(new Vector3(0, 0, 0.62f), new Vector3(0.18f, 0.2f, 1.4f));
      k.Pop();
      k.Paint(Swatch.MetalDark);
      k.Box(new Vector3(0, 0.46f, 1.14f), new Vector3(0.03f, 0.62f, 0.03f));
      k.Paint(Swatch.Metal);
      k.Taper(new Vector3(0, 0.08f, 1.14f), new Vector2(0.12f, 0.12f), new Vector2(0.06f, 0.06f), 0.14f);
      k.Pop();
    }

    static void DragonScales(MeshKit k, Vector3 origin, int rows, int columns, float width, float length) {
      for (var r = 0; r < rows; r++) {
        for (var c = 0; c < columns; c++) {
          var x = (c - (columns - 1) / 2f + (r % 2 == 0 ? 0f : 0.5f)) * width;
          var z = (r - (rows - 1) / 2f) * length * 0.7f;
          if (Mathf.Abs(x) > width * columns / 2f) {
            continue;
          }
          k.Paint((r + c) % 3 == 0 ? Swatch.ArmorDark : Swatch.Armor);
          k.Plate(origin + new Vector3(x, r * -0.002f, z), width * 1.04f, length, 0.1f);
        }
      }
    }

    static void DragonHull(MeshKit k) {
      Track(k, 0.96f, 3.0f, 0.46f, 0.4f, 4);
      Track(k, -0.96f, 3.0f, 0.46f, 0.4f, 4);
      k.Paint(Swatch.ArmorDark);
      k.Taper(new Vector3(0, 0.26f, 0), new Vector2(2.1f, 3.2f), new Vector2(1.72f, 2.5f), 0.52f);
      DragonScales(k, new Vector3(0, 0.79f, 0), 8, 5, 0.34f, 0.44f);
      foreach (var side in new[] { -1f, 1f }) {
        k.Push(new Vector3(side * 0.96f, 0.53f, 0), new Vector3(0, 0, side * -70f));
        k.Push(Vector3.zero, new Vector3(0, side * 90f, 0));
        DragonScales(k, Vector3.zero, 2, 9, 0.34f, 0.42f);
        k.Pop();
        k.Pop();
      }
      k.Paint(Swatch.Armor);
      k.Taper(new Vector3(0, 0.26f, 1.48f), new Vector2(1.96f, 0.3f), new Vector2(1.7f, 0.14f), 0.46f);
      k.Team();
      k.Box(new Vector3(0, 0.875f, -0.82f), new Vector3(1.72f, 0.03f, 0.2f));
    }

    static void DragonTurret(MeshKit k) {
      k.Paint(Swatch.ArmorDark);
      k.Taper(Vector3.zero, new Vector2(1.38f, 1.7f), new Vector2(0.98f, 1.2f), 0.44f, new Vector2(0, -0.06f));
      DragonScales(k, new Vector3(0, 0.45f, -0.05f), 4, 3, 0.31f, 0.4f);
      foreach (var side in new[] { -1f, 1f }) {
        k.Push(new Vector3(side * 0.6f, 0.22f, -0.03f), new Vector3(0, 0, side * -66f));
        k.Push(Vector3.zero, new Vector3(0, side * 90f, 0));
        DragonScales(k, Vector3.zero, 2, 5, 0.3f, 0.34f);
        k.Pop();
        k.Pop();
      }
      k.Team();
      k.Box(new Vector3(0, 0.46f, -0.5f), new Vector3(0.9f, 0.03f, 0.16f));
    }

    static void DragonBarrel(MeshKit k) {
      k.Paint(Swatch.ArmorDark);
      k.Box(new Vector3(0, 0, 0.04f), new Vector3(0.34f, 0.3f, 0.3f));
      k.Paint(Swatch.MetalDark);
      TurnZ(k, new Vector3(0, 0, 0.18f), () => k.Cylinder(Vector3.zero, 0.09f, 1.3f, 6));
      TurnZ(k, new Vector3(0, 0, 1.2f), () => k.Cylinder(Vector3.zero, 0.115f, 0.3f, 6));
      k.Paint(Swatch.Black);
      TurnZ(k, new Vector3(0, 0, 1.51f), () => k.Disc(Vector3.zero, 0.07f, 6));
    }

    static void ThunderHull(MeshKit k) {
      Track(k, 0.92f, 3.1f, 0.48f, 0.4f, 5);
      Track(k, -0.92f, 3.1f, 0.48f, 0.4f, 5);
      k.Paint(Swatch.Armor);
      k.Taper(new Vector3(0, 0.3f, 0), new Vector2(2.0f, 3.1f), new Vector2(1.7f, 2.2f), 0.46f, new Vector2(0, -0.2f));
      k.Paint(Swatch.ArmorDark);
      k.Box(new Vector3(0, 0.78f, -0.1f), new Vector3(1.0f, 0.04f, 1.1f));
      k.Team();
      foreach (var side in new[] { -1f, 1f }) {
        k.Push(new Vector3(side * 0.93f, 0.53f, 0.1f), new Vector3(0, 0, side * -36f));
        k.Box(new Vector3(0, 0, 0.5f), new Vector3(0.04f, 0.3f, 0.5f));
        k.Box(new Vector3(0, 0, -0.5f), new Vector3(0.04f, 0.3f, 0.5f));
        k.Pop();
      }
      k.Glow(Swatch.GlowOrange);
      k.Box(new Vector3(0.6f, 0.52f, 1.46f), new Vector3(0.14f, 0.07f, 0.03f));
      k.Box(new Vector3(-0.6f, 0.52f, 1.46f), new Vector3(0.14f, 0.07f, 0.03f));
    }

    static void ThunderTurret(MeshKit k) {
      k.Paint(Swatch.Armor);
      k.Taper(Vector3.zero, new Vector2(1.3f, 1.25f), new Vector2(1.0f, 0.9f), 0.56f, new Vector2(0, -0.05f));
      k.Team();
      k.Box(new Vector3(0.59f, 0.28f, 0.02f), new Vector3(0.03f, 0.22f, 0.5f));
      k.Box(new Vector3(-0.59f, 0.28f, 0.02f), new Vector3(0.03f, 0.22f, 0.5f));
    }

    static void ThunderGuns(MeshKit k) {
      foreach (var x in new[] { -0.72f, 0.72f }) {
        k.Paint(Swatch.ArmorDark);
        k.Box(new Vector3(x, 0, -0.05f), new Vector3(0.3f, 0.34f, 0.6f));
        k.Paint(Swatch.MetalDark);
        foreach (var y in new[] { -0.09f, 0.09f }) {
          TurnZ(k, new Vector3(x, y, 0.25f), () => k.Cylinder(Vector3.zero, 0.05f, 1.2f, 6));
          TurnZ(k, new Vector3(x, y, 1.3f), () => k.Cylinder(Vector3.zero, 0.07f, 0.16f, 6));
        }
      }
    }

    static void ThunderRadar(MeshKit k) {
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(0, -0.1f, 0), 0.08f, 0.34f, 6);
      k.Paint(Swatch.Metal);
      k.Push(new Vector3(0, 0.46f, 0.04f), new Vector3(70, 0, 0));
      k.Lathe(Vector3.zero, new[] { new Vector2(0.52f, 0), new Vector2(0.46f, -0.08f), new Vector2(0.1f, -0.2f), new Vector2(0, -0.2f) }, 8);
      k.Paint(Swatch.MetalDark);
      k.Disc(new Vector3(0, 0.002f, 0), 0.5f, 8);
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(0, -0.2f, 0), 0.03f, 0.36f, 5);
      k.Pop();
    }

    static void LongwallBody(MeshKit k) {
      k.Paint(Swatch.OliveDark);
      k.Box(new Vector3(0, 0.62f, -0.15f), new Vector3(1.5f, 0.22f, 4.5f));
      foreach (var z in new[] { 1.5f, 0.45f, -1.15f, -2.05f }) {
        Wheel(k, new Vector3(0.88f, 0.4f, z), 0.4f, 0.28f);
        Wheel(k, new Vector3(-0.88f, 0.4f, z), 0.4f, 0.28f);
      }
      k.Paint(Swatch.Olive);
      k.Taper(new Vector3(0, 0.72f, 1.55f), new Vector2(1.7f, 1.3f), new Vector2(1.56f, 1.02f), 1.0f, new Vector2(0, -0.08f));
      k.Paint(Swatch.Glass);
      k.Taper(new Vector3(0, 1.18f, 2.17f), new Vector2(1.34f, 0.06f), new Vector2(1.24f, 0.06f), 0.4f, new Vector2(0, -0.08f));
      k.Paint(Swatch.OliveDark);
      k.Box(new Vector3(0, 0.92f, 2.22f), new Vector3(0.8f, 0.3f, 0.05f));
      k.Team();
      k.Box(new Vector3(0, 1.74f, 1.5f), new Vector3(1.3f, 0.03f, 0.74f));
      k.Box(new Vector3(0, 1.1f, 0.86f), new Vector3(1.72f, 0.72f, 0.1f));
      k.Paint(Swatch.Olive);
      k.Box(new Vector3(0, 0.82f, -0.9f), new Vector3(1.5f, 0.2f, 2.7f));
      k.Glow(Swatch.GlowOrange);
      k.Box(new Vector3(0.6f, 0.86f, 2.23f), new Vector3(0.18f, 0.1f, 0.03f));
      k.Box(new Vector3(-0.6f, 0.86f, 2.23f), new Vector3(0.18f, 0.1f, 0.03f));
    }

    static void LongwallBase(MeshKit k) {
      k.Paint(Swatch.MetalDark);
      k.Frustum(new Vector3(0, -0.08f, 0), 0.72f, 0.6f, 0.2f, 8);
      k.Paint(Swatch.Olive);
      k.Box(new Vector3(0, 0.2f, -0.2f), new Vector3(0.9f, 0.16f, 1.5f));
    }

    static void LongwallTubes(MeshKit k) {
      k.Push(Vector3.zero, new Vector3(-22, 0, 0));
      foreach (var x in new[] { -0.46f, 0.46f }) {
        k.Paint(Swatch.Olive);
        k.Chamfer(new Vector3(x, 0.32f, 0.72f), new Vector3(0.84f, 0.76f, 2.4f), 0.14f);
        k.Team();
        k.Box(new Vector3(x, 0.71f, 0.72f), new Vector3(0.5f, 0.02f, 1.6f));
        k.Box(new Vector3(x + Mathf.Sign(x) * 0.425f, 0.32f, 0.72f), new Vector3(0.02f, 0.34f, 1.4f));
        k.Paint(Swatch.Black);
        for (var i = 0; i < 6; i++) {
          k.Push(new Vector3(x - 0.2f + (i % 3) * 0.2f, 0.16f + (i / 3) * 0.32f, 1.925f), Z90);
          k.Disc(Vector3.zero, 0.075f, 6);
          k.Pop();
        }
      }
      k.Pop();
    }

    // 사령부

    static void BasePlate(MeshKit k) {
      k.Paint(Swatch.ArmorDark);
      k.Taper(Vector3.zero, new Vector2(5.96f, 5.96f), new Vector2(5.7f, 5.7f), 0.14f);
    }

    static readonly Vector2[] HqBody = { new Vector2(2.62f, 0), new Vector2(2.6f, 0.5f), new Vector2(2.28f, 1.2f), new Vector2(1.9f, 1.55f) };
    static readonly Vector2[] HqDome = { new Vector2(1.72f, 0), new Vector2(1.49f, 0.56f), new Vector2(0.86f, 0.97f), new Vector2(0.46f, 1.04f) };

    static void FreeHeadquarters(MeshKit k) {
      BasePlate(k);
      // 낮은 돔 몸체: 납작한 곡면을 층으로 쌓는다.
      k.Paint(Swatch.Metal);
      k.Lathe(new Vector3(0, 0.14f, 0), HqBody, 12, 0.5f);
      // 멀리서도 보이는 팀 띠 2줄: 몸체와 윗돔을 한 바퀴 두른다.
      k.Team();
      k.Lathe(new Vector3(0, 0.14f, 0), new[] { new Vector2(2.61f, 0.58f), new Vector2(2.395f, 1.06f) }, 12, 0.5f);
      k.Lathe(new Vector3(0, 1.97f, 0), new[] { new Vector2(1.47f, 0.6f), new Vector2(1.07f, 0.86f) }, 12);
      // 앞문
      k.Paint(Swatch.MetalDark);
      k.Chamfer(new Vector3(0, 0.82f, 2.5f), new Vector3(1.3f, 1.36f, 0.7f), 0.2f);
      k.Paint(Swatch.Black);
      k.Box(new Vector3(0, 0.7f, 2.86f), new Vector3(0.8f, 1.08f, 0.04f));
      k.Glow();
      k.Box(new Vector3(-0.48f, 0.72f, 2.86f), new Vector3(0.07f, 1.1f, 0.05f));
      k.Box(new Vector3(0.48f, 0.72f, 2.86f), new Vector3(0.07f, 1.1f, 0.05f));
      k.Box(new Vector3(0, 1.3f, 2.86f), new Vector3(0.96f, 0.07f, 0.05f));
      // 갈빗대 사이의 낮은 창
      foreach (var yaw in new[] { 45f, 135f, 225f, 315f }) {
        k.Push(new Vector3(0, 0.34f, 0), new Vector3(0, yaw, 0));
        k.Taper(new Vector3(0, 0, 2.58f), new Vector2(1.0f, 0.06f), new Vector2(0.96f, 0.06f), 0.18f, new Vector2(0, -0.03f));
        k.Pop();
      }
      // 발광 고리와 윗돔
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(0, 1.69f, 0), 1.95f, 0.14f, 12);
      k.Glow();
      k.Cylinder(new Vector3(0, 1.83f, 0), 1.74f, 0.14f, 12);
      k.Paint(Swatch.Metal);
      k.Dome(new Vector3(0, 1.97f, 0), 1.72f, 1.12f, 12, 3);
      // 곡면 갈빗대: 몸체에서 윗돔까지 이어진다. 팀 띠를 칸으로 나눠 표면이 입체로 보인다.
      k.Paint(Swatch.MetalLight);
      for (var i = 0; i < 8; i++) {
        var yaw = 22.5f + i * 45f;
        k.Push(new Vector3(0, 0.14f, 0), new Vector3(0, yaw, 0));
        k.Rib(HqBody, 0.26f, 0.12f, 0.16f);
        k.Pop();
        k.Push(new Vector3(0, 1.97f, 0), new Vector3(0, yaw, 0));
        k.Rib(HqDome, 0.2f, 0.1f, 0.14f);
        k.Pop();
      }
      // 안테나
      k.Paint(Swatch.MetalDark);
      k.Taper(new Vector3(0, 3.0f, 0), new Vector2(0.9f, 0.9f), new Vector2(0.7f, 0.7f), 0.14f);
      k.Cylinder(new Vector3(-0.28f, 3.1f, -0.22f), 0.05f, 1.0f, 5);
      k.Cylinder(new Vector3(-0.08f, 3.1f, -0.3f), 0.04f, 0.72f, 5);
      // 옆 탑
      k.Paint(Swatch.Metal);
      k.Cylinder(new Vector3(1.9f, 1.3f, -1.5f), 0.34f, 0.66f, 8);
      k.Glow();
      k.Cylinder(new Vector3(1.9f, 1.96f, -1.5f), 0.3f, 0.1f, 8);
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(1.9f, 2.06f, -1.5f), 0.05f, 0.75f, 5);
    }

    static void FreeRadar(MeshKit k) {
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(0, -0.45f, 0), 0.16f, 0.62f, 6);
      k.Paint(Swatch.MetalLight);
      k.Push(new Vector3(0, 0.46f, 0.1f), new Vector3(58, 0, 0));
      k.Lathe(Vector3.zero, new[] { new Vector2(0.92f, 0), new Vector2(0.84f, -0.08f), new Vector2(0.24f, -0.3f), new Vector2(0, -0.32f) }, 10);
      k.Paint(Swatch.Metal);
      k.Disc(new Vector3(0, 0.002f, 0), 0.9f, 10);
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(0, -0.32f, 0), 0.05f, 0.66f, 5);
      k.Cone(new Vector3(0, 0.3f, 0), 0.1f, 0.14f, 5);
      k.Pop();
    }

    static void Rivets(MeshKit k, Vector3 from, Vector3 to, int count, Vector3 euler) {
      k.Paint(Swatch.MetalLight);
      for (var i = 0; i < count; i++) {
        k.Push(Vector3.Lerp(from, to, count > 1 ? i / (count - 1f) : 0.5f), euler);
        k.Frustum(Vector3.zero, 0.1f, 0.07f, 0.06f, 6);
        k.Pop();
      }
    }

    static void ContinentalHeadquarters(MeshKit k) {
      BasePlate(k);
      k.Paint(Swatch.ArmorDark);
      k.Taper(new Vector3(0, 0.14f, 0), new Vector2(5.2f, 5.2f), new Vector2(4.1f, 4.1f), 1.5f);
      k.Paint(Swatch.Armor);
      k.Taper(new Vector3(0, 1.64f, 0), new Vector2(4.1f, 4.1f), new Vector2(3.9f, 3.9f), 0.12f);

      // 네 면: 경사 버팀벽, 리벳, 팀 판
      for (var i = 0; i < 4; i++) {
        k.Push(Vector3.zero, new Vector3(0, i * 90f, 0));
        foreach (var x in new[] { -1.7f, 1.7f }) {
          k.Paint(Swatch.Armor);
          k.Taper(new Vector3(x, 0.14f, 2.42f), new Vector2(0.62f, 0.6f), new Vector2(0.5f, 0.32f), 1.56f, new Vector2(0, -0.52f));
          Rivets(k, new Vector3(x, 0.42f, 2.6f), new Vector3(x, 1.5f, 2.145f), 4, new Vector3(67, 0, 0));
        }
        k.Team();
        k.Push(new Vector3(0, i == 0 ? 1.34f : 0.86f, i == 0 ? 2.17f : 2.36f), new Vector3(-20, 0, 0));
        k.Box(Vector3.zero, i == 0 ? new Vector3(1.6f, 0.34f, 0.08f) : new Vector3(1.9f, 1.0f, 0.08f));
        k.Pop();
        k.Pop();
      }
      // 앞문
      k.Paint(Swatch.ArmorDark);
      k.Taper(new Vector3(0, 0.14f, 2.62f), new Vector2(1.22f, 0.5f), new Vector2(1.0f, 0.3f), 0.84f, new Vector2(0, -0.12f));
      k.Paint(Swatch.Black);
      k.Box(new Vector3(0, 0.5f, 2.84f), new Vector3(0.74f, 0.7f, 0.04f));

      // 윗층 지휘소
      k.Paint(Swatch.ArmorDark);
      k.Taper(new Vector3(0, 1.76f, 0), new Vector2(2.6f, 2.6f), new Vector2(1.9f, 1.9f), 0.9f);
      k.Paint(Swatch.Sand);
      k.Taper(new Vector3(0, 2.66f, 0), new Vector2(1.9f, 1.9f), new Vector2(1.7f, 1.7f), 0.1f);
      k.Paint(Swatch.Black);
      k.Box(new Vector3(0, 2.2f, 1.14f), new Vector3(1.2f, 0.16f, 0.04f));

      // 망루 2개
      foreach (var corner in new[] { new Vector3(-1.72f, 1.76f, -1.72f), new Vector3(1.72f, 1.76f, -1.72f) }) {
        k.Paint(Swatch.Armor);
        k.Taper(corner, new Vector2(0.92f, 0.92f), new Vector2(0.7f, 0.7f), 1.0f);
        k.Paint(Swatch.MetalDark);
        foreach (var x in new[] { -0.3f, 0.3f }) {
          foreach (var z in new[] { -0.3f, 0.3f }) {
            k.Box(corner + new Vector3(x, 1.2f, z), new Vector3(0.06f, 0.42f, 0.06f));
          }
        }
        k.Paint(Swatch.Earth);
        k.Taper(corner + new Vector3(0, 1.4f, 0), new Vector2(1.0f, 1.0f), new Vector2(0.12f, 0.12f), 0.42f);
      }

      // 깃대
      k.Paint(Swatch.ArmorDark);
      k.Taper(new Vector3(0, 2.76f, 0), new Vector2(0.6f, 0.6f), new Vector2(0.24f, 0.24f), 0.4f);
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(0, 3.1f, 0), 0.06f, 2.7f, 6);
    }

    /// <summary>깃발 문양: Art/Icons/src/cf_flag_emblem.svg의 다각형과 같다. 육각 고리와 사선 막대.</summary>
    public static readonly Vector2[][] FlagEmblem = {
      new[] { new Vector2(0.5f, 0.1f), new Vector2(0.82f, 0.28f), new Vector2(0.72f, 0.34f), new Vector2(0.5f, 0.22f) },
      new[] { new Vector2(0.82f, 0.28f), new Vector2(0.82f, 0.72f), new Vector2(0.72f, 0.66f), new Vector2(0.72f, 0.34f) },
      new[] { new Vector2(0.82f, 0.72f), new Vector2(0.5f, 0.9f), new Vector2(0.5f, 0.78f), new Vector2(0.72f, 0.66f) },
      new[] { new Vector2(0.5f, 0.9f), new Vector2(0.18f, 0.72f), new Vector2(0.28f, 0.66f), new Vector2(0.5f, 0.78f) },
      new[] { new Vector2(0.18f, 0.72f), new Vector2(0.18f, 0.28f), new Vector2(0.28f, 0.34f), new Vector2(0.28f, 0.66f) },
      new[] { new Vector2(0.18f, 0.28f), new Vector2(0.5f, 0.1f), new Vector2(0.5f, 0.22f), new Vector2(0.28f, 0.34f) },
      new[] { new Vector2(0.36f, 0.62f), new Vector2(0.58f, 0.34f), new Vector2(0.66f, 0.4f), new Vector2(0.44f, 0.68f) },
    };

    static void ContinentalFlag(MeshKit k) {
      const float Width = 1.5f;
      const float Height = 0.95f;
      k.Team();
      k.Taper(new Vector3(Width / 2 + 0.05f, -0.48f, 0), new Vector2(Width, 0.03f), new Vector2(Width, 0.03f), Height);
      k.Paint(Swatch.White);
      foreach (var side in new[] { -1f, 1f }) {
        foreach (var quad in FlagEmblem) {
          Vector3 P(Vector2 p) => new Vector3(0.05f + p.x * Width, -0.48f + (1f - p.y) * Height, side * 0.02f);
          k.Quad(P(quad[0]), P(quad[1]), P(quad[2]), P(quad[3]), new Vector3(0, 0, side));
        }
      }
    }

    // 철광산, 다리

    static void MineRocks(MeshKit k, int seed) {
      // 검정이 아니라 어두운 회색이다. 면마다 명도를 달리해 덩어리가 읽힌다.
      k.ShadeFaces(seed, Swatch.OreDark, Swatch.Ore, Swatch.OreLight);
      k.Rock(Vector3.zero, 1.25f, 2.7f, seed);
      for (var i = 0; i < 6; i++) {
        var angle = i * 1.05f + MeshKit.Hash(seed, i) * 0.5f;
        var distance = 1.45f + MeshKit.Hash(seed, i + 10) * 0.75f;
        var size = 0.42f + MeshKit.Hash(seed, i + 20) * 0.5f;
        k.Rock(new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance), size, size * 1.35f, seed + i + 1);
      }
      k.EndShade();
    }

    static void MineCrystals(MeshKit k, int seed) {
      k.Glow(Swatch.GlowCrystal);
      for (var i = 0; i < 7; i++) {
        var angle = i * 0.9f + MeshKit.Hash(seed, i + 30) * 0.6f;
        var distance = 0.85f + MeshKit.Hash(seed, i + 40) * 1.3f;
        var height = 0.9f + MeshKit.Hash(seed, i + 50) * 1.3f;
        var width = 0.24f + MeshKit.Hash(seed, i + 60) * 0.18f;
        k.Push(new Vector3(Mathf.Cos(angle) * distance, MeshKit.Hash(seed, i + 70) * 0.5f, Mathf.Sin(angle) * distance),
          new Vector3(Mathf.Sin(angle) * 22f, angle * 40f, -Mathf.Cos(angle) * 22f));
        k.Lathe(Vector3.zero, new[] { new Vector2(width * 0.6f, 0), new Vector2(width, height * 0.25f), new Vector2(width * 0.85f, height * 0.75f), new Vector2(0, height) }, 5);
        k.Pop();
      }
    }

    /// <summary>항상 내려진 나무 다리. Z축으로 건너고 원점이 땅 높이다.</summary>
    static void Bridge(MeshKit k) {
      const float Half = 5.2f;
      k.Paint(Swatch.WoodDark);
      foreach (var x in new[] { -6.2f, -2f, 2f, 6.2f }) {
        k.Box(new Vector3(x, 0.02f, 0), new Vector3(0.34f, 0.2f, Half * 2));
      }
      for (var i = 0; i < 13; i++) {
        k.Paint(i % 2 == 0 ? Swatch.Wood : Swatch.WoodDark);
        k.Box(new Vector3(0, 0.14f, -Half + 0.4f + i * 0.8f), new Vector3(12.6f, 0.08f, 0.74f));
      }
      foreach (var side in new[] { -1f, 1f }) {
        k.Paint(Swatch.Stone);
        k.Taper(new Vector3(side * 6.6f, -1f, 3.4f), new Vector2(0.9f, 0.9f), new Vector2(0.62f, 0.62f), 1.9f);
        k.Taper(new Vector3(side * 6.6f, -1f, -3.4f), new Vector2(0.9f, 0.9f), new Vector2(0.62f, 0.62f), 1.9f);
        k.Paint(Swatch.WoodDark);
        k.Box(new Vector3(side * 6.4f, 0.55f, 0), new Vector3(0.12f, 0.12f, 6.8f));
      }
      k.Paint(Swatch.MetalDark);
      foreach (var x in new[] { -3f, 3f }) {
        k.Box(new Vector3(x, -0.5f, 0), new Vector3(0.4f, 0.9f, 0.4f));
      }
    }
  }
}
