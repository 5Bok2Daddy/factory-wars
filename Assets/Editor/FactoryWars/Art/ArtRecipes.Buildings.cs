namespace FactoryWars.EditorTools {
  using System;
  using System.Collections.Generic;
  using FactoryWars.View;
  using UnityEngine;

  /// <summary>
  /// 사령부를 뺀 건물 10종의 두 진영 모델. 건물 칸은 6x6이고 바닥판 위쪽 면은 y 0.14다. 앞이 +Z다.
  /// 자유 연방은 아치·돔·원통, 대륙 연방은 각진 창고·리벳·팔각 구조다.
  /// </summary>
  public static partial class ArtRecipes {
    const float Floor = 0.14f;

    public static IEnumerable<ArtRecipe> Buildings() {
      yield return Building("ff_collection_yard", FreeYard, new MovingPart { Name = "Forklift", Pivot = new Vector3(-0.3f, Floor, 1.5f), Build = Forklift });
      yield return Building("ff_supply_depot", FreeDepot, new MovingPart { Name = "Door", Pivot = new Vector3(-1.45f, Floor, 1.72f), Build = FreeDepotDoor });
      yield return Building("ff_barracks", FreeBarracks, new MovingPart { Name = "Antenna", Pivot = new Vector3(-1.05f, 1.56f, -1.15f), Build = SmallDish });
      yield return Building("ff_power_plant", FreePower, new MovingPart { Name = "CoreRing", Pivot = new Vector3(0.5f, 1.05f, 1.6f), Build = CoreRing }).Smoking(new Vector3(1.55f, 4.2f, -1.7f));
      yield return Building("ff_processor", FreeProcessor, new MovingPart { Name = "Ingot", Pivot = new Vector3(0.9f, 0.72f, 1.5f), Build = BeltIngot }).Smoking(new Vector3(0.45f, 3.6f, -0.95f));
      yield return Building("ff_research_lab", FreeLab, new MovingPart { Name = "Dish", Pivot = new Vector3(0.5f, 3.2f, -0.4f), Build = LargeDish });
      yield return Building("ff_vehicle_factory", FreeFactory, new MovingPart { Name = "Crane", Pivot = new Vector3(0, 3.2f, -0.6f), Build = RoofCrane }).Smoking(new Vector3(-2.62f, 2.7f, 2.68f));
      yield return Building("ff_ground_tower", FreeTower,
        new MovingPart { Name = "Turret", Pivot = new Vector3(0, 2.5f, 0), Build = FreeTowerTurret },
        new MovingPart { Name = "Barrel", Parent = "Turret", Pivot = new Vector3(0, 0.5f, 0.55f), Build = FreeTowerBarrel });
      yield return Building("ff_air_defense", FreeAirDefense,
        new MovingPart { Name = "Turret", Pivot = new Vector3(0, 2.2f, 0), Build = FreeAirTurret },
        new MovingPart { Name = "Barrel", Parent = "Turret", Pivot = new Vector3(0, 0.62f, 0), Build = FreeAirMissiles });
      yield return Building("ff_airfield", FreeAirfield, new MovingPart { Name = "Radar", Pivot = new Vector3(2.0f, 3.0f, -0.8f), Build = SmallDish });
      yield return Building("ff_heavy_works", FreeHeavy, new MovingPart { Name = "Hoist", Pivot = new Vector3(0.4f, 4.3f, -2.45f), Build = Hoist });
      yield return Building("ff_shipyard", FreeShipyard, new MovingPart { Name = "Crane", Pivot = new Vector3(-1.8f, 2.8f, 0.5f), Build = RoofCrane });

      yield return Building("cf_collection_yard", ContinentalYard, new MovingPart { Name = "Forklift", Pivot = new Vector3(0.5f, Floor, 0.9f), Build = Forklift });
      yield return Building("cf_supply_depot", ContinentalDepot, new MovingPart { Name = "Door", Pivot = new Vector3(-0.5f, Floor, 1.16f), Build = ContinentalDepotDoor });
      yield return Building("cf_barracks", ContinentalBarracks, new MovingPart { Name = "Flag", Pivot = new Vector3(-1.75f, 3.1f, -2.05f), Build = SmallFlag });
      yield return Building("cf_power_plant", ContinentalPower, new MovingPart { Name = "Coil", Pivot = new Vector3(0.6f, 1.75f, 1.0f), Build = CoreRing }).Smoking(new Vector3(1.55f, 4.1f, -1.7f));
      yield return Building("cf_processor", ContinentalProcessor, new MovingPart { Name = "Ingot", Pivot = new Vector3(1.3f, 0.72f, 1.3f), Build = BeltIngot }).Smoking(new Vector3(-0.5f, 3.7f, -0.5f));
      yield return Building("cf_research_lab", ContinentalLab, new MovingPart { Name = "Dish", Pivot = new Vector3(-0.9f, 3.0f, -1.2f), Build = LargeDish });
      yield return Building("cf_vehicle_factory", ContinentalFactory, new MovingPart { Name = "Crane", Pivot = new Vector3(2.35f, 3.6f, 1.9f), Build = TowerCrane }).Smoking(new Vector3(-1.8f, 4.25f, -2.3f), new Vector3(-0.9f, 4.25f, -2.3f));
      yield return Building("cf_ground_tower", ContinentalTower,
        new MovingPart { Name = "Turret", Pivot = new Vector3(0, 2.3f, 0), Build = ContinentalTowerTurret },
        new MovingPart { Name = "Barrel", Parent = "Turret", Pivot = new Vector3(0, 0.42f, 0.6f), Build = ContinentalTowerBarrel });
      yield return Building("cf_air_defense", ContinentalAirDefense,
        new MovingPart { Name = "Turret", Pivot = new Vector3(0, 2.0f, 0), Build = ContinentalAirTurret },
        new MovingPart { Name = "Barrel", Parent = "Turret", Pivot = new Vector3(0, 0.5f, 0.3f), Build = ContinentalAirGuns });
      yield return Building("cf_airfield", ContinentalAirfield, new MovingPart { Name = "Radar", Pivot = new Vector3(2.0f, 3.1f, -1.3f), Build = SmallDish });
      yield return Building("cf_heavy_works", ContinentalHeavy, new MovingPart { Name = "Hoist", Pivot = new Vector3(0.6f, 4.35f, 1.6f), Build = Hoist });
      yield return Building("cf_shipyard", ContinentalShipyard, new MovingPart { Name = "Crane", Pivot = new Vector3(-1.8f, 3.2f, 0.5f), Build = TowerCrane });
    }

    static ArtRecipe Building(string id, Action<MeshKit> body, params MovingPart[] moving) {
      return new ArtRecipe { Id = id, Kind = "건물", MaxRenderers = 5, MaxTriangles = 2000, Body = body, Moving = moving };
    }

    // 단면

    static Vector2[] Arch(float halfWidth, float height, int steps = 6) {
      var points = new Vector2[steps + 1];
      for (var i = 0; i <= steps; i++) {
        var angle = i * Mathf.PI / steps;
        points[i] = new Vector2(Mathf.Cos(angle) * halfWidth, Mathf.Sin(angle) * height);
      }
      return points;
    }

    static Vector2[] Angular(float halfWidth, float height) => new[] {
      new Vector2(halfWidth, 0), new Vector2(halfWidth, height * 0.62f), new Vector2(halfWidth * 0.6f, height),
      new Vector2(-halfWidth * 0.6f, height), new Vector2(-halfWidth, height * 0.62f), new Vector2(-halfWidth, 0),
    };

    // 자유 연방 공용

    /// <summary>뒤에서 앞으로 뻗은 아치 격납고. 앞면에 문틀과 어두운 입구가 있다.</summary>
    static void Hangar(MeshKit k, Vector3 origin, float halfWidth, float height, float back, float front, Swatch skin = Swatch.Metal) {
      k.Push(origin);
      k.Paint(skin);
      k.Prism(Arch(halfWidth, height), back, front);
      k.Paint(Swatch.MetalDark);
      k.Prism(Arch(halfWidth + 0.1f, height + 0.1f), front - 0.16f, front + 0.05f);
      k.Paint(Swatch.Black);
      k.Prism(Arch(halfWidth * 0.74f, height * 0.74f), front + 0.05f, front + 0.07f);
      k.Pop();
    }

    static void HangarRibs(MeshKit k, Vector3 origin, float halfWidth, float height, params float[] positions) {
      k.Push(origin);
      k.Paint(Swatch.MetalLight);
      foreach (var z in positions) {
        k.Prism(Arch(halfWidth + 0.05f, height + 0.05f), z, z + 0.14f);
      }
      k.Pop();
    }

    static void HangarBand(MeshKit k, Vector3 origin, float halfWidth, float height, float back, float front) {
      k.Push(origin);
      k.Team();
      k.Prism(Arch(halfWidth + 0.04f, height + 0.04f), back, front);
      k.Pop();
    }

    static void ShutterSlats(MeshKit k, Vector3 origin, float halfWidth, float height, float z) {
      k.Paint(Swatch.MetalLight);
      for (var i = 0; i < 4; i++) {
        var y = height * (0.16f + i * 0.2f);
        var width = halfWidth * Mathf.Sqrt(Mathf.Max(0.05f, 1f - y * y / (height * height))) * 2f;
        k.Box(origin + new Vector3(0, y, z), new Vector3(width, 0.05f, 0.02f));
      }
    }

    static void Hut(MeshKit k, Vector3 origin, float yaw, float radius, float height) {
      k.Push(origin, new Vector3(0, yaw, 0));
      k.Paint(Swatch.MetalDark);
      k.Frustum(Vector3.zero, radius + 0.1f, radius + 0.04f, 0.12f, 10);
      k.Paint(Swatch.Metal);
      k.Dome(new Vector3(0, 0.1f, 0), radius, height, 10, 3);
      k.Team();
      k.Lathe(new Vector3(0, 0.1f, 0), new[] { new Vector2(radius * 0.968f + 0.03f, height * 0.12f), new Vector2(radius * 0.893f + 0.03f, height * 0.4f) }, 10);
      // 입구 터널
      var door = radius * 0.8f;
      k.Paint(Swatch.Metal);
      k.Prism(Arch(radius * 0.4f, height * 0.62f), door - 0.4f, door + 0.32f);
      k.Team();
      k.Prism(Arch(radius * 0.46f, height * 0.68f), door + 0.2f, door + 0.34f);
      k.Paint(Swatch.Black);
      k.Prism(Arch(radius * 0.3f, height * 0.5f), door + 0.34f, door + 0.36f);
      k.Pop();
    }

    // 소품

    static void Crate(MeshKit k, Vector3 bottom, float size) {
      k.Paint(Swatch.Wood);
      k.Box(bottom + Vector3.up * (size / 2), Vector3.one * size);
      k.Paint(Swatch.WoodDark);
      k.Box(bottom + Vector3.up * (size * 0.92f), new Vector3(size * 1.05f, size * 0.12f, size * 1.05f));
      k.Box(bottom + Vector3.up * (size * 0.08f), new Vector3(size * 1.05f, size * 0.12f, size * 1.05f));
    }

    static void Logs(MeshKit k, Vector3 bottom, float length) {
      const float Radius = 0.17f;
      for (var row = 0; row < 3; row++) {
        for (var i = 0; i < 3 - row; i++) {
          var center = bottom + new Vector3((i - (2 - row) / 2f) * Radius * 2f, Radius + row * Radius * 1.72f, -length / 2);
          k.Paint(Swatch.Bark);
          TurnZ(k, center, () => k.Cylinder(Vector3.zero, Radius, length, 6));
          k.Paint(Swatch.Wood);
          TurnZ(k, center + Vector3.forward * (length + 0.005f), () => k.Disc(Vector3.zero, Radius * 0.8f, 6));
        }
      }
    }

    static void OrePile(MeshKit k, Vector3 bottom, int seed, float size) {
      k.ShadeFaces(seed, Swatch.OreDark, Swatch.Ore, Swatch.OreLight);
      k.Rock(bottom, size, size * 1.1f, seed);
      k.Rock(bottom + new Vector3(size * 1.1f, 0, size * 0.5f), size * 0.7f, size * 0.7f, seed + 1);
      k.Rock(bottom + new Vector3(-size * 0.5f, 0, size * 1.1f), size * 0.55f, size * 0.55f, seed + 2);
      k.EndShade();
    }

    static void Ingots(MeshKit k, Vector3 bottom) {
      k.Paint(Swatch.MetalLight);
      for (var i = 0; i < 3; i++) {
        k.Taper(bottom + new Vector3((i - 1) * 0.3f, 0, 0), new Vector2(0.27f, 0.62f), new Vector2(0.2f, 0.54f), 0.14f);
      }
      for (var i = 0; i < 2; i++) {
        k.Taper(bottom + new Vector3((i - 0.5f) * 0.3f, 0.14f, 0), new Vector2(0.27f, 0.62f), new Vector2(0.2f, 0.54f), 0.14f);
      }
    }

    static void Conveyor(MeshKit k, Vector3 center, float length) {
      k.Paint(Swatch.Rubber);
      k.Box(center, new Vector3(0.84f, 0.1f, length));
      k.Paint(Swatch.MetalDark);
      k.Box(center + new Vector3(0.44f, 0, 0), new Vector3(0.05f, 0.16f, length));
      k.Box(center + new Vector3(-0.44f, 0, 0), new Vector3(0.05f, 0.16f, length));
      foreach (var z in new[] { -0.4f, 0.4f }) {
        foreach (var x in new[] { -0.38f, 0.38f }) {
          var top = center + new Vector3(x, -0.05f, z * length);
          k.Box(new Vector3(top.x, (top.y + Floor) / 2, top.z), new Vector3(0.06f, top.y - Floor, 0.06f));
        }
      }
    }

    static void Barrier(MeshKit k, Vector3 bottom, float length, float yaw) {
      k.Push(bottom, new Vector3(0, yaw, 0));
      k.Paint(Swatch.Stone);
      k.Taper(Vector3.zero, new Vector2(length, 0.26f), new Vector2(length, 0.1f), 0.3f);
      k.Pop();
    }

    /// <summary>네 기둥과 대각 가새로 된 사각 철탑.</summary>
    static void Lattice(MeshKit k, Vector3 bottom, float height, float width, int bays) {
      var h = width / 2;
      k.Paint(Swatch.MetalDark);
      var corners = new[] { new Vector3(-h, 0, -h), new Vector3(h, 0, -h), new Vector3(h, 0, h), new Vector3(-h, 0, h) };
      for (var i = 0; i < 4; i++) {
        k.Beam(bottom + corners[i], bottom + corners[i] + Vector3.up * height, 0.07f);
      }
      for (var bay = 0; bay < bays; bay++) {
        var low = height * bay / bays;
        var high = height * (bay + 1) / bays;
        for (var i = 0; i < 4; i++) {
          var from = corners[(i + (bay % 2)) % 4];
          var to = corners[(i + 1 - (bay % 2) + 4) % 4];
          k.Beam(bottom + from + Vector3.up * low, bottom + to + Vector3.up * high, 0.035f);
        }
      }
    }

    /// <summary>위아래 현재와 지그재그 가새로 된 수평 트러스.</summary>
    static void Girder(MeshKit k, Vector3 from, Vector3 to, float depth, int bays) {
      k.Paint(Swatch.MetalDark);
      var drop = Vector3.down * depth;
      k.Beam(from, to, 0.07f);
      k.Beam(from + drop, to + drop, 0.07f);
      for (var i = 0; i < bays; i++) {
        var a = Vector3.Lerp(from, to, i / (float)bays);
        var b = Vector3.Lerp(from, to, (i + 1) / (float)bays);
        k.Beam(i % 2 == 0 ? a : a + drop, i % 2 == 0 ? b + drop : b, 0.035f);
      }
    }

    // 움직이는 공용 파츠

    static void Forklift(MeshKit k) {
      k.Paint(Swatch.Hazard);
      k.Box(new Vector3(0, 0.27f, -0.05f), new Vector3(0.54f, 0.22f, 0.86f));
      foreach (var x in new[] { -0.31f, 0.31f }) {
        foreach (var z in new[] { -0.32f, 0.26f }) {
          Wheel(k, new Vector3(x, 0.14f, z), 0.14f, 0.1f, 6);
        }
      }
      k.Paint(Swatch.MetalDark);
      foreach (var x in new[] { -0.22f, 0.22f }) {
        k.Beam(new Vector3(x, 0.36f, -0.34f), new Vector3(x, 0.84f, -0.34f), 0.05f);
        k.Beam(new Vector3(x, 0.36f, 0.16f), new Vector3(x, 0.84f, 0.16f), 0.05f);
        k.Beam(new Vector3(x, 0.02f, 0.42f), new Vector3(x, 1.0f, 0.42f), 0.06f);
        k.Box(new Vector3(x, 0.08f, 0.68f), new Vector3(0.08f, 0.04f, 0.52f));
      }
      k.Team();
      k.Box(new Vector3(0, 0.86f, -0.09f), new Vector3(0.56f, 0.05f, 0.6f));
    }

    static void SmallDish(MeshKit k) {
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(0, -0.04f, 0), 0.07f, 0.32f, 6);
      k.Push(new Vector3(0, 0.42f, 0.05f), new Vector3(60, 0, 0));
      k.Paint(Swatch.MetalLight);
      k.Lathe(Vector3.zero, new[] { new Vector2(0.46f, 0), new Vector2(0.4f, -0.08f), new Vector2(0.1f, -0.18f), new Vector2(0, -0.18f) }, 8);
      k.Paint(Swatch.Metal);
      k.Disc(new Vector3(0, 0.002f, 0), 0.44f, 8);
      k.Paint(Swatch.MetalDark);
      k.Cone(new Vector3(0, 0.02f, 0), 0.05f, 0.22f, 5);
      k.Pop();
    }

    static void LargeDish(MeshKit k) {
      k.Paint(Swatch.MetalDark);
      k.Frustum(new Vector3(0, -0.06f, 0), 0.3f, 0.14f, 0.3f, 8);
      k.Cylinder(new Vector3(0, 0.2f, 0), 0.08f, 0.32f, 6);
      k.Push(new Vector3(0, 0.66f, 0.08f), new Vector3(58, 0, 0));
      k.Paint(Swatch.MetalLight);
      k.Lathe(Vector3.zero, new[] { new Vector2(0.72f, 0), new Vector2(0.64f, -0.08f), new Vector2(0.18f, -0.26f), new Vector2(0, -0.28f) }, 10);
      k.Paint(Swatch.Metal);
      k.Disc(new Vector3(0, 0.002f, 0), 0.7f, 10);
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(0, -0.02f, 0), 0.03f, 0.44f, 5);
      k.Cone(new Vector3(0, 0.42f, 0), 0.08f, 0.12f, 5);
      k.Pop();
    }

    /// <summary>코어를 감싸 도는 고리.</summary>
    static void CoreRing(MeshKit k) {
      const int Sides = 8;
      const float Radius = 0.74f;
      k.Paint(Swatch.MetalLight);
      for (var i = 0; i < Sides; i++) {
        var a = i * Mathf.PI * 2f / Sides;
        var b = (i + 1) * Mathf.PI * 2f / Sides;
        k.Beam(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Radius, new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * Radius, 0.1f);
      }
      k.Team();
      k.Box(new Vector3(Radius, 0, 0), new Vector3(0.14f, 0.16f, 0.26f));
      k.Box(new Vector3(-Radius, 0, 0), new Vector3(0.14f, 0.16f, 0.26f));
    }

    static void BeltIngot(MeshKit k) {
      k.Paint(Swatch.MetalLight);
      k.Taper(Vector3.zero, new Vector2(0.4f, 0.7f), new Vector2(0.32f, 0.6f), 0.15f);
    }

    static void Hoist(MeshKit k) {
      k.Team();
      k.Box(new Vector3(0, 0, 0), new Vector3(0.4f, 0.3f, 0.5f));
      k.Paint(Swatch.MetalDark);
      k.Box(new Vector3(0, -0.5f, 0), new Vector3(0.03f, 0.74f, 0.03f));
      k.Paint(Swatch.Metal);
      k.Taper(new Vector3(0, -1.0f, 0), new Vector2(0.08f, 0.08f), new Vector2(0.2f, 0.2f), 0.14f);
      k.Cone(new Vector3(0, -1.0f, 0), 0.06f, -0.14f, 5);
    }

    // 자유 연방

    static void FreeYard(MeshKit k) {
      BasePlate(k);
      var hangar = new Vector3(0.6f, Floor, 0);
      Hangar(k, hangar, 2.1f, 2.15f, -2.75f, 0.1f);
      HangarBand(k, hangar, 2.1f, 2.15f, -1.75f, -1.05f);
      HangarRibs(k, hangar, 2.1f, 2.15f, -2.5f, -0.4f);
      OrePile(k, new Vector3(-2.15f, Floor, 0.9f), 3, 0.42f);
      OrePile(k, new Vector3(-0.5f, Floor, 2.35f), 9, 0.3f);
      Logs(k, new Vector3(1.9f, Floor, 2.1f), 1.5f);
      k.Paint(Swatch.Stone);
      k.Cylinder(new Vector3(2.65f, Floor, 0.4f), 0.14f, 0.5f, 6);
    }

    static void FreeDepot(MeshKit k) {
      BasePlate(k);
      var rows = new[] {
        (new Vector3(-1.45f, Floor, 0), -2.8f, -0.55f, false),
        (new Vector3(1.45f, Floor, 0), -2.8f, -0.55f, true),
        (new Vector3(-1.45f, Floor, 0), -0.25f, 1.7f, true),
        (new Vector3(1.45f, Floor, 0), -0.25f, 1.7f, false),
      };
      foreach (var (origin, back, front, team) in rows) {
        Hangar(k, origin, 1.22f, 1.5f, back, front);
        if (team) {
          HangarBand(k, origin, 1.22f, 1.5f, back + 0.35f, front - 0.35f);
        }
        HangarRibs(k, origin, 1.22f, 1.5f, back + 0.1f, (back + front) / 2);
      }
      Crate(k, new Vector3(-2.3f, Floor, 2.35f), 0.56f);
      Crate(k, new Vector3(-1.62f, Floor, 2.42f), 0.46f);
      Crate(k, new Vector3(-2.26f, Floor + 0.56f, 2.36f), 0.42f);
      Crate(k, new Vector3(0.6f, Floor, 2.4f), 0.5f);
      Crate(k, new Vector3(2.1f, Floor, 2.3f), 0.6f);
      Crate(k, new Vector3(1.42f, Floor, 2.5f), 0.42f);
    }

    static void FreeDepotDoor(MeshKit k) {
      k.Paint(Swatch.Metal);
      k.Prism(Arch(0.9f, 1.1f), 0.07f, 0.1f);
      ShutterSlats(k, Vector3.zero, 0.9f, 1.1f, 0.11f);
    }

    static void FreeBarracks(MeshKit k) {
      BasePlate(k);
      Hut(k, new Vector3(-1.05f, Floor, -1.15f), 0, 1.45f, 1.3f);
      Hut(k, new Vector3(1.5f, Floor, -0.45f), 0, 1.25f, 1.15f);
      Hut(k, new Vector3(-2.15f, Floor, 1.35f), -30, 0.68f, 0.7f);
      k.Glow();
      k.Push(new Vector3(-1.05f, Floor, -1.15f), new Vector3(0, -62, 0));
      k.Box(new Vector3(0, 0.62f, 1.28f), new Vector3(0.62f, 0.26f, 0.05f));
      k.Pop();
      k.Push(new Vector3(1.5f, Floor, -0.45f), new Vector3(0, 58, 0));
      k.Box(new Vector3(0, 0.56f, 1.1f), new Vector3(0.56f, 0.24f, 0.05f));
      k.Pop();
      // 훈련장
      Barrier(k, new Vector3(-0.7f, Floor, 2.5f), 0.8f, 20);
      Barrier(k, new Vector3(1.9f, Floor, 2.1f), 0.8f, -30);
      k.Paint(Swatch.MetalDark);
      foreach (var x in new[] { 0.1f, 0.8f }) {
        k.Beam(new Vector3(x, Floor, 1.5f), new Vector3(x, Floor + 0.5f, 1.5f), 0.05f);
      }
      k.Beam(new Vector3(0.05f, Floor + 0.46f, 1.5f), new Vector3(0.85f, Floor + 0.46f, 1.5f), 0.05f);
      foreach (var x in new[] { -0.1f, 0.5f, 1.1f }) {
        k.Paint(Swatch.Stone);
        k.Cone(new Vector3(x, Floor, 2.4f), 0.13f, 0.34f, 5);
      }
    }

    static void CoolingTower(MeshKit k, Vector3 origin, float radius, float height, int sides, Swatch skin) {
      k.Paint(Swatch.MetalDark);
      k.Frustum(origin, radius + 0.14f, radius + 0.06f, 0.14f, sides);
      k.Paint(skin);
      var y = origin + Vector3.up * 0.12f;
      k.Lathe(y, new[] { new Vector2(radius, 0), new Vector2(radius * 0.72f, height * 0.55f), new Vector2(radius * 0.68f, height * 0.72f), new Vector2(radius * 0.74f, height) }, sides);
      k.Paint(Swatch.Black);
      k.Disc(y + Vector3.up * (height + 0.005f), radius * 0.64f, sides);
      k.Team();
      k.Lathe(y, new[] { new Vector2(radius * 0.694f + 0.03f, height * 0.77f), new Vector2(radius * 0.726f + 0.03f, height * 0.92f) }, sides);
    }

    static void FreePower(MeshKit k) {
      BasePlate(k);
      CoolingTower(k, new Vector3(-1.6f, Floor, -1.5f), 1.15f, 3.1f, 10, Swatch.Metal);
      CoolingTower(k, new Vector3(1.55f, Floor, -1.7f), 1.15f, 3.9f, 10, Swatch.Metal);
      CoolingTower(k, new Vector3(-1.9f, Floor, 1.5f), 0.95f, 2.5f, 10, Swatch.Metal);
      // 청록 코어
      var core = new Vector3(0.5f, Floor, 1.6f);
      k.Paint(Swatch.MetalDark);
      k.Frustum(core, 0.92f, 0.78f, 0.24f, 8);
      k.Glow();
      k.Cylinder(core + Vector3.up * 0.24f, 0.56f, 1.26f, 8);
      k.Paint(Swatch.Metal);
      k.Frustum(core + Vector3.up * 1.5f, 0.68f, 0.42f, 0.2f, 8);
      k.Paint(Swatch.MetalDark);
      foreach (var yaw in new[] { 45f, 135f, 225f, 315f }) {
        k.Push(core, new Vector3(0, yaw, 0));
        k.Taper(new Vector3(0, 0, 0.72f), new Vector2(0.3f, 0.34f), new Vector2(0.18f, 0.12f), 0.66f, new Vector2(0, -0.1f));
        k.Beam(new Vector3(0, 0.6f, 0.68f), new Vector3(0, 1.56f, 0.56f), 0.06f);
        k.Pop();
      }
    }

    static void FreeProcessor(MeshKit k) {
      BasePlate(k);
      k.Paint(Swatch.Metal);
      k.Chamfer(new Vector3(0.45f, 1.14f, -0.95f), new Vector3(3.5f, 2.0f, 3.5f), 0.26f);
      k.Paint(Swatch.MetalDark);
      k.Chamfer(new Vector3(0.45f, 2.2f, -0.95f), new Vector3(2.7f, 0.16f, 2.7f), 0.1f);
      k.Paint(Swatch.Metal);
      k.Lathe(new Vector3(2.3f, Floor, -1.8f), new[] { new Vector2(0.5f, 0), new Vector2(0.5f, 1.1f), new Vector2(0.34f, 1.46f), new Vector2(0, 1.5f) }, 8);
      // 굴뚝
      var stack = new Vector3(0.45f, 2.28f, -0.95f);
      k.Paint(Swatch.MetalDark);
      k.Frustum(stack, 0.52f, 0.36f, 1.3f, 8);
      k.Team();
      k.Cylinder(stack + Vector3.up * 0.42f, 0.5f, 0.3f, 8);
      k.Paint(Swatch.Black);
      k.Disc(stack + Vector3.up * 1.305f, 0.3f, 8);
      // 주황 쇳물 문
      var door = new Vector3(0.9f, Floor, 0);
      k.Push(door);
      k.Team();
      k.Prism(Arch(0.78f, 1.5f), 0.76f, 0.94f);
      k.Glow(Swatch.GlowOrange);
      k.Prism(Arch(0.6f, 1.28f), 0.94f, 0.96f);
      k.Pop();
      k.Team();
      k.Box(new Vector3(-1.34f, 1.1f, -0.9f), new Vector3(0.05f, 0.5f, 2.6f));
      // 쇳물 도가니와 관
      var pool = new Vector3(-1.75f, Floor, 1.65f);
      k.Paint(Swatch.MetalDark);
      k.Lathe(pool, new[] { new Vector2(1.0f, 0), new Vector2(1.05f, 0.42f), new Vector2(0.86f, 0.5f) }, 10);
      k.Glow(Swatch.GlowOrange);
      k.Disc(pool + Vector3.up * 0.505f, 0.84f, 10);
      k.Paint(Swatch.Metal);
      k.Beam(new Vector3(-1.1f, 1.5f, 0.3f), new Vector3(-1.5f, 1.3f, 1.05f), 0.2f);
      k.Beam(new Vector3(-1.5f, 1.3f, 1.05f), new Vector3(-1.6f, 0.8f, 1.35f), 0.2f);
      Conveyor(k, new Vector3(0.9f, 0.65f, 1.85f), 1.8f);
      Ingots(k, new Vector3(2.25f, Floor, 2.1f));
    }

    static void GlowPanels(MeshKit k, Vector3 origin, float distance, float y, Vector2 size, params float[] yaws) {
      k.Glow();
      foreach (var yaw in yaws) {
        k.Push(origin, new Vector3(0, yaw, 0));
        k.Box(new Vector3(0, y, distance), new Vector3(size.x, size.y, 0.06f));
        k.Pop();
      }
    }

    static void FreeLab(MeshKit k) {
      BasePlate(k);
      var main = new Vector3(0.5f, Floor, -0.4f);
      k.Paint(Swatch.Metal);
      k.Cylinder(main, 2.15f, 1.1f, 12);
      k.Paint(Swatch.MetalDark);
      k.Cylinder(main + Vector3.up * 1.1f, 2.2f, 0.12f, 12);
      k.Paint(Swatch.Metal);
      k.Dome(main + Vector3.up * 1.22f, 2.0f, 1.76f, 12, 3);
      k.Team();
      k.Lathe(main + Vector3.up * 1.22f, new[] { new Vector2(2.0f * 0.968f + 0.03f, 0.2f), new Vector2(2.0f * 0.89f + 0.03f, 0.72f) }, 12);
      GlowPanels(k, main, 2.1f, 0.62f, new Vector2(0.78f, 0.46f), -100f, -62f, 22f, 60f, 98f);
      // 앞 부속동
      var wing = new Vector3(-1.35f, Floor, 1.5f);
      k.Paint(Swatch.Metal);
      k.Cylinder(wing, 1.3f, 0.82f, 10);
      k.Dome(wing + Vector3.up * 0.82f, 1.3f, 0.8f, 10, 2);
      k.Paint(Swatch.MetalDark);
      k.Cylinder(wing + Vector3.up * 0.78f, 1.34f, 0.08f, 10);
      GlowPanels(k, wing, 1.27f, 0.46f, new Vector2(0.6f, 0.38f), -60f, 60f);
      k.Push(wing);
      k.Team();
      k.Prism(Arch(0.46f, 0.82f), 1.2f, 1.36f);
      k.Glow();
      k.Prism(Arch(0.32f, 0.66f), 1.36f, 1.38f);
      k.Pop();
      // 안테나
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(2.3f, 2.0f, -1.2f), 0.04f, 1.5f, 5);
      k.Box(new Vector3(2.3f, 3.2f, -1.2f), new Vector3(0.5f, 0.03f, 0.03f));
      k.Box(new Vector3(2.3f, 3.0f, -1.2f), new Vector3(0.36f, 0.03f, 0.03f));
      k.Cylinder(new Vector3(-1.7f, 2.2f, 0.2f), 0.035f, 1.1f, 5);
    }

    static void FreeFactory(MeshKit k) {
      BasePlate(k);
      var hangar = new Vector3(0, Floor, 0);
      Hangar(k, hangar, 2.7f, 3.0f, -2.8f, 2.35f);
      HangarRibs(k, hangar, 2.7f, 3.0f, -2.55f, -0.9f, 0.75f);
      ShutterSlats(k, new Vector3(0, Floor, 0), 2.0f, 2.22f, 2.43f);
      k.Paint(Swatch.Glass);
      k.Box(new Vector3(0, 3.16f, -1.8f), new Vector3(1.0f, 0.06f, 1.5f));
      k.Box(new Vector3(0, 3.16f, 1.4f), new Vector3(1.0f, 0.06f, 1.2f));
      // 옆면 팀 패널
      foreach (var side in new[] { -1f, 1f }) {
        k.Team();
        k.Taper(new Vector3(side * 2.72f, Floor, -0.1f), new Vector2(0.16f, 1.5f), new Vector2(0.1f, 1.5f), 1.45f, new Vector2(-side * 0.14f, 0));
      }
      // 굴뚝
      k.Paint(Swatch.Metal);
      k.Frustum(new Vector3(-2.62f, Floor, 2.68f), 0.26f, 0.17f, 2.5f, 8);
      k.Team();
      k.Cylinder(new Vector3(-2.62f, 2.1f, 2.68f), 0.2f, 0.22f, 8);
      k.Paint(Swatch.Black);
      k.Disc(new Vector3(-2.62f, Floor + 2.505f, 2.68f), 0.12f, 8);
    }

    static void RoofCrane(MeshKit k) {
      k.Paint(Swatch.MetalDark);
      k.Taper(new Vector3(0, -0.06f, 0), new Vector2(0.9f, 0.7f), new Vector2(0.6f, 0.5f), 0.26f);
      Girder(k, new Vector3(-2.3f, 0.64f, 0), new Vector3(2.3f, 0.64f, 0), 0.3f, 8);
      k.Team();
      k.Box(new Vector3(0, 0.46f, 0), new Vector3(0.5f, 0.52f, 0.34f));
    }

    static void FreeTower(MeshKit k) {
      BasePlate(k);
      k.Paint(Swatch.MetalDark);
      k.Frustum(new Vector3(0, Floor, 0), 2.1f, 1.98f, 0.16f, 12);
      k.Paint(Swatch.Metal);
      k.Lathe(new Vector3(0, Floor + 0.14f, 0), new[] { new Vector2(1.9f, 0), new Vector2(1.8f, 0.6f), new Vector2(1.42f, 1.65f), new Vector2(1.36f, 2.0f) }, 12);
      k.Paint(Swatch.MetalDark);
      foreach (var yaw in new[] { 45f, 135f, 225f, 315f }) {
        k.Push(new Vector3(0, Floor, 0), new Vector3(0, yaw, 0));
        k.Taper(new Vector3(0, 0, 1.9f), new Vector2(0.54f, 0.5f), new Vector2(0.3f, 0.2f), 0.9f, new Vector2(0, -0.2f));
        k.Pop();
      }
      k.Team();
      k.Lathe(new Vector3(0, Floor + 0.14f, 0), new[] { new Vector2(1.5f, 1.42f), new Vector2(1.41f, 1.76f) }, 12);
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(0, 2.26f, 0), 1.44f, 0.24f, 12);
      k.Push(new Vector3(0, Floor, 0));
      k.Team();
      k.Prism(Arch(0.56f, 1.2f), 1.5f, 1.98f);
      k.Glow();
      k.Prism(Arch(0.4f, 1.0f), 1.98f, 2.0f);
      k.Pop();
    }

    static void FreeTowerTurret(MeshKit k) {
      k.Paint(Swatch.Metal);
      k.Dome(Vector3.zero, 1.14f, 1.08f, 10, 3);
      k.Paint(Swatch.MetalDark);
      k.Prism(Arch(0.36f, 0.82f), 0.5f, 1.06f);
      k.Team();
      k.Lathe(Vector3.zero, new[] { new Vector2(1.14f * 0.968f + 0.03f, 0.13f), new Vector2(1.14f * 0.89f + 0.03f, 0.42f) }, 10);
    }

    static void FreeTowerBarrel(MeshKit k) {
      k.Paint(Swatch.Metal);
      TurnZ(k, new Vector3(0, 0, 0.2f), () => k.Frustum(Vector3.zero, 0.26f, 0.2f, 0.5f, 8));
      k.Paint(Swatch.MetalDark);
      TurnZ(k, new Vector3(0, 0, 0.66f), () => k.Cylinder(Vector3.zero, 0.13f, 1.34f, 8));
      TurnZ(k, new Vector3(0, 0, 1.76f), () => k.Cylinder(Vector3.zero, 0.18f, 0.26f, 8));
      k.Paint(Swatch.Black);
      TurnZ(k, new Vector3(0, 0, 2.025f), () => k.Disc(Vector3.zero, 0.11f, 6));
    }

    static void FreeAirDefense(MeshKit k) {
      BasePlate(k);
      k.Paint(Swatch.MetalDark);
      k.Frustum(new Vector3(0, Floor, 0), 2.0f, 1.88f, 0.16f, 12);
      k.Paint(Swatch.Metal);
      k.Lathe(new Vector3(0, Floor + 0.14f, 0), new[] { new Vector2(1.8f, 0), new Vector2(1.7f, 0.5f), new Vector2(1.34f, 1.5f), new Vector2(1.28f, 1.78f) }, 12);
      k.Team();
      k.Lathe(new Vector3(0, Floor + 0.14f, 0), new[] { new Vector2(1.5f, 1.05f), new Vector2(1.39f, 1.42f) }, 12);
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(0, 2.05f, 0), 1.34f, 0.15f, 12);
      k.Push(new Vector3(0, Floor, 0));
      k.Team();
      k.Prism(Arch(0.52f, 1.1f), 1.4f, 1.86f);
      k.Glow();
      k.Prism(Arch(0.38f, 0.92f), 1.86f, 1.88f);
      k.Pop();
    }

    static void FreeAirTurret(MeshKit k) {
      k.Paint(Swatch.Metal);
      k.Cylinder(Vector3.zero, 0.92f, 0.86f, 10);
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(0, 0.86f, 0), 0.96f, 0.08f, 10);
      k.Paint(Swatch.MetalLight);
      k.Dome(new Vector3(0, 0.94f, 0), 0.7f, 0.3f, 10, 2);
      k.Team();
      k.Cylinder(new Vector3(0, 0.3f, 0), 0.95f, 0.2f, 10);
    }

    /// <summary>원통 포탑 양옆의 미사일 발사관. 위로 들려 있다.</summary>
    static void FreeAirMissiles(MeshKit k) {
      k.Push(Vector3.zero, new Vector3(-28, 0, 0));
      k.Paint(Swatch.MetalDark);
      k.Box(Vector3.zero, new Vector3(2.5f, 0.12f, 0.2f));
      foreach (var side in new[] { -1f, 1f }) {
        foreach (var offset in new[] { 1.02f, 1.46f }) {
          var x = side * offset;
          k.Paint(Swatch.MetalLight);
          TurnZ(k, new Vector3(x, 0.02f, -0.95f), () => k.Cylinder(Vector3.zero, 0.2f, 1.9f, 6));
          k.Team();
          TurnZ(k, new Vector3(x, 0.02f, 0.95f), () => k.Cone(Vector3.zero, 0.2f, 0.46f, 6));
          k.Paint(Swatch.MetalDark);
          k.Box(new Vector3(x, 0.02f, -0.8f), new Vector3(0.56f, 0.04f, 0.36f));
        }
      }
      k.Pop();
    }

    static void Runway(MeshKit k, Vector3 center, Vector2 size) {
      k.Paint(Swatch.Rubber);
      k.Box(center, new Vector3(size.x, 0.03f, size.y));
      k.Paint(Swatch.White);
      for (var i = 0; i < 4; i++) {
        k.Box(center + new Vector3((i - 1.5f) * size.x / 4.4f, 0.02f, 0), new Vector3(size.x / 8f, 0.01f, 0.08f));
      }
      k.Box(center + new Vector3(0, 0.02f, size.y / 2 - 0.08f), new Vector3(size.x - 0.2f, 0.01f, 0.04f));
      k.Box(center + new Vector3(0, 0.02f, -size.y / 2 + 0.08f), new Vector3(size.x - 0.2f, 0.01f, 0.04f));
    }

    static void FreeAirfield(MeshKit k) {
      BasePlate(k);
      var hangar = new Vector3(-0.9f, Floor, 0);
      Hangar(k, hangar, 1.95f, 2.0f, -2.8f, 0.5f);
      HangarBand(k, hangar, 1.95f, 2.0f, -1.65f, -0.85f);
      HangarRibs(k, hangar, 1.95f, 2.0f, -2.55f, -0.3f);
      k.Paint(Swatch.Glass);
      k.Box(new Vector3(-0.9f, 2.16f, -1.2f), new Vector3(0.7f, 0.05f, 2.4f));
      Runway(k, new Vector3(-0.3f, Floor + 0.015f, 1.9f), new Vector2(5.0f, 1.7f));
      // 관제탑
      var tower = new Vector3(2.0f, Floor, -0.8f);
      k.Paint(Swatch.Metal);
      k.Lathe(tower, new[] { new Vector2(0.86f, 0), new Vector2(0.8f, 0.5f), new Vector2(0.46f, 0.72f), new Vector2(0.44f, 1.78f) }, 8);
      k.Team();
      k.Lathe(tower, new[] { new Vector2(0.84f, 0.2f), new Vector2(0.82f, 0.46f) }, 8);
      k.Paint(Swatch.MetalDark);
      k.Frustum(tower + Vector3.up * 1.78f, 0.46f, 0.72f, 0.16f, 8);
      k.Glow();
      k.Cylinder(tower + Vector3.up * 1.94f, 0.68f, 0.4f, 8);
      k.Paint(Swatch.Metal);
      k.Dome(tower + Vector3.up * 2.34f, 0.78f, 0.46f, 8, 2);
    }

    static void FreeHeavy(MeshKit k) {
      BasePlate(k);
      var hall = new Vector3(0, Floor, 0.2f);
      Hangar(k, hall, 2.65f, 3.5f, -2.9f, 1.95f);
      HangarRibs(k, hall, 2.65f, 3.5f, -1.5f, 0.1f);
      // 앞 차고
      var porch = new Vector3(0, Floor, 0);
      Hangar(k, porch, 1.5f, 2.1f, 1.7f, 2.72f);
      ShutterSlats(k, porch, 1.1f, 1.56f, 2.8f);
      foreach (var side in new[] { -1f, 1f }) {
        k.Team();
        k.Taper(new Vector3(side * 2.68f, Floor, -1.2f), new Vector2(0.18f, 0.9f), new Vector2(0.1f, 0.9f), 2.0f, new Vector2(-side * 0.26f, 0));
        k.Taper(new Vector3(side * 2.68f, Floor, 0.8f), new Vector2(0.18f, 0.9f), new Vector2(0.1f, 0.9f), 2.0f, new Vector2(-side * 0.26f, 0));
      }
      // 뒤쪽 철탑과 수평 트러스
      Lattice(k, new Vector3(-2.5f, Floor, -2.45f), 4.2f, 0.56f, 5);
      Lattice(k, new Vector3(2.5f, Floor, -2.45f), 4.2f, 0.56f, 5);
      Girder(k, new Vector3(-2.7f, 4.7f, -2.45f), new Vector3(2.7f, 4.7f, -2.45f), 0.4f, 10);
      k.Team();
      k.Box(new Vector3(-2.5f, 4.5f, -2.45f), new Vector3(0.66f, 0.3f, 0.66f));
      k.Box(new Vector3(2.5f, 4.5f, -2.45f), new Vector3(0.66f, 0.3f, 0.66f));
    }

    // 대륙 연방 공용

    /// <summary>각진 철판 창고. 처마를 따라 리벳을 박는다.</summary>
    static void Shed(MeshKit k, Vector3 origin, float halfWidth, float height, float back, float front, Swatch skin = Swatch.ArmorDark) {
      k.Push(origin);
      k.Paint(skin);
      k.Prism(Angular(halfWidth, height), back, front);
      k.Paint(Swatch.Armor);
      k.Prism(Angular(halfWidth + 0.1f, height + 0.1f), front - 0.2f, front + 0.05f);
      k.Prism(Angular(halfWidth + 0.08f, height + 0.08f), back, back + 0.2f);
      k.Paint(Swatch.Black);
      k.Prism(Angular(halfWidth * 0.68f, height * 0.7f), front + 0.05f, front + 0.07f);
      var eave = height * 0.62f + 0.05f;
      var edge = halfWidth + 0.06f;
      Rivets(k, new Vector3(-edge, eave, back + 0.4f), new Vector3(-edge, eave, front - 0.4f), 5, new Vector3(0, 0, 90));
      Rivets(k, new Vector3(edge, eave, back + 0.4f), new Vector3(edge, eave, front - 0.4f), 5, new Vector3(0, 0, -90));
      Rivets(k, new Vector3(-halfWidth * 0.8f, height * 0.86f, front + 0.05f), new Vector3(halfWidth * 0.8f, height * 0.86f, front + 0.05f), 5, Z90);
      k.Pop();
    }

    static void TeamSign(MeshKit k, Vector3 center, Vector2 size) {
      k.Team();
      k.Box(center, new Vector3(size.x, size.y, 0.05f));
    }

    static void Container(MeshKit k, Vector3 bottom, Vector3 size, bool team) {
      if (team) {
        k.Team();
      } else {
        k.Paint(Swatch.Metal);
      }
      k.Box(bottom + Vector3.up * (size.y / 2), size);
      k.Paint(Swatch.MetalDark);
      for (var i = 0; i < 4; i++) {
        var z = (i - 1.5f) * size.z / 4f;
        k.Box(bottom + new Vector3(0, size.y / 2, z), new Vector3(size.x + 0.04f, size.y - 0.08f, 0.05f));
      }
    }

    // 대륙 연방

    static void ContinentalYard(MeshKit k) {
      BasePlate(k);
      // 뒤와 오른쪽의 낮은 옹벽
      k.Paint(Swatch.Stone);
      k.Taper(new Vector3(0, Floor, -2.65f), new Vector2(5.5f, 0.5f), new Vector2(5.4f, 0.24f), 0.72f);
      k.Taper(new Vector3(-2.65f + 5.3f, Floor, 0), new Vector2(0.5f, 5.5f), new Vector2(0.24f, 5.4f), 0.72f);
      k.Team();
      k.Box(new Vector3(-1.0f, 0.55f, -2.5f), new Vector3(1.6f, 0.34f, 0.05f));
      k.Box(new Vector3(2.52f, 0.55f, 0.8f), new Vector3(0.05f, 0.34f, 1.6f));
      OrePile(k, new Vector3(-1.5f, Floor, -1.7f), 13, 0.62f);
      OrePile(k, new Vector3(0.6f, Floor, -1.65f), 21, 0.5f);
      Logs(k, new Vector3(1.75f, Floor, 1.95f), 1.8f);
      Logs(k, new Vector3(-2.0f, Floor, 2.1f), 1.5f);
      Barrier(k, new Vector3(-0.6f, Floor, 2.6f), 1.0f, 0);
      OrePile(k, new Vector3(-2.2f, Floor, 0.1f), 31, 0.34f);
    }

    static void ContinentalDepot(MeshKit k) {
      BasePlate(k);
      var shed = new Vector3(-0.5f, Floor, 0);
      Shed(k, shed, 2.1f, 2.5f, -2.8f, 1.1f);
      TeamSign(k, new Vector3(-0.5f, 2.12f, 1.19f), new Vector2(1.1f, 0.3f));
      Container(k, new Vector3(2.32f, Floor, -1.4f), new Vector3(0.96f, 0.9f, 2.6f), false);
      Container(k, new Vector3(2.32f, Floor + 0.9f, -1.4f), new Vector3(0.96f, 0.9f, 2.6f), true);
      Crate(k, new Vector3(-2.3f, Floor, 2.0f), 0.62f);
      Crate(k, new Vector3(-1.56f, Floor, 2.2f), 0.5f);
      Crate(k, new Vector3(-2.26f, Floor + 0.62f, 2.0f), 0.46f);
      Crate(k, new Vector3(1.0f, Floor, 2.1f), 0.56f);
      Crate(k, new Vector3(1.75f, Floor, 2.3f), 0.48f);
      Crate(k, new Vector3(2.3f, Floor, 1.5f), 0.6f);
    }

    static void ContinentalDepotDoor(MeshKit k) {
      k.Paint(Swatch.MetalDark);
      k.Box(new Vector3(0, 0.86f, 0.03f), new Vector3(2.7f, 1.7f, 0.05f));
      k.Paint(Swatch.Armor);
      for (var i = 0; i < 4; i++) {
        k.Box(new Vector3(0, 0.3f + i * 0.4f, 0.07f), new Vector3(2.66f, 0.06f, 0.02f));
      }
    }

    static void ContinentalBarracks(MeshKit k) {
      BasePlate(k);
      k.Paint(Swatch.ArmorDark);
      k.Taper(new Vector3(0.1f, Floor, -1.0f), new Vector2(5.2f, 3.5f), new Vector2(5.0f, 3.3f), 0.4f);
      k.Paint(Swatch.Armor);
      k.Chamfer(new Vector3(0.1f, 1.34f, -1.0f), new Vector3(4.7f, 1.6f, 3.2f), 0.26f);
      k.Paint(Swatch.ArmorDark);
      k.Chamfer(new Vector3(0.1f, 2.18f, -1.0f), new Vector3(3.8f, 0.16f, 2.4f), 0.08f);
      k.Paint(Swatch.Armor);
      k.Box(new Vector3(-0.7f, 2.36f, -1.0f), new Vector3(1.3f, 0.2f, 1.2f));
      k.Box(new Vector3(1.1f, 2.34f, -1.0f), new Vector3(1.0f, 0.16f, 1.0f));
      k.Paint(Swatch.Black);
      foreach (var x in new[] { -1.6f, -0.5f, 1.0f, 1.9f }) {
        k.Box(new Vector3(x, 1.4f, 0.61f), new Vector3(0.5f, 0.28f, 0.03f));
      }
      foreach (var z in new[] { -1.8f, -0.8f, 0.1f }) {
        k.Box(new Vector3(-2.26f, 1.4f, z), new Vector3(0.03f, 0.28f, 0.5f));
      }
      TeamSign(k, new Vector3(0.1f, 0.74f, 0.62f), new Vector2(4.3f, 0.12f));
      k.Team();
      k.Box(new Vector3(-2.27f, 0.74f, -1.0f), new Vector3(0.05f, 0.12f, 3.0f));
      // 현관
      k.Paint(Swatch.ArmorDark);
      k.Taper(new Vector3(0.25f, Floor, 1.08f), new Vector2(1.3f, 1.0f), new Vector2(1.1f, 0.78f), 1.1f);
      k.Paint(Swatch.Black);
      k.Box(new Vector3(0.25f, 0.6f, 1.58f), new Vector3(0.66f, 0.86f, 0.03f));
      Rivets(k, new Vector3(-0.3f, 1.1f, 1.53f), new Vector3(0.8f, 1.1f, 1.53f), 4, Z90);
      // 연병장 담
      k.Paint(Swatch.Stone);
      k.Taper(new Vector3(0.7f, Floor, 2.72f), new Vector2(4.4f, 0.24f), new Vector2(4.4f, 0.12f), 0.42f);
      k.Taper(new Vector3(2.8f, Floor, 1.7f), new Vector2(0.24f, 2.2f), new Vector2(0.12f, 2.2f), 0.42f);
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(-1.75f, 2.26f, -2.05f), 0.05f, 1.3f, 5);
      foreach (var x in new[] { -1.4f, -2.0f }) {
        k.Beam(new Vector3(x, Floor, 1.6f), new Vector3(x, Floor + 0.6f, 1.6f), 0.06f);
      }
      k.Beam(new Vector3(-2.06f, Floor + 0.56f, 1.6f), new Vector3(-1.34f, Floor + 0.56f, 1.6f), 0.06f);
    }

    static void SmallFlag(MeshKit k) {
      k.Push(new Vector3(0, 0.1f, 0), default, Vector3.one * 0.62f);
      ContinentalFlag(k);
      k.Pop();
    }

    static void ContinentalPower(MeshKit k) {
      BasePlate(k);
      k.Paint(Swatch.ArmorDark);
      k.Frustum(new Vector3(0, Floor, 0), 2.84f, 2.64f, 0.26f, 8);
      CoolingTower(k, new Vector3(-1.5f, 0.38f, -1.45f), 1.2f, 2.9f, 8, Swatch.Armor);
      CoolingTower(k, new Vector3(1.55f, 0.38f, -1.7f), 1.1f, 3.6f, 8, Swatch.Armor);
      // 팔각 반응로: 코어는 청록이다.
      var core = new Vector3(0.6f, Floor, 1.0f);
      k.Paint(Swatch.ArmorDark);
      k.Frustum(core, 1.5f, 1.32f, 0.3f, 8);
      k.Paint(Swatch.Armor);
      k.Lathe(core + Vector3.up * 0.3f, new[] { new Vector2(1.28f, 0), new Vector2(1.05f, 1.0f), new Vector2(0.92f, 1.1f) }, 8);
      k.Glow();
      k.Disc(core + Vector3.up * 1.405f, 0.86f, 8);
      k.Cylinder(core + Vector3.up * 1.42f, 0.5f, 0.42f, 8);
      k.Paint(Swatch.MetalDark);
      k.Frustum(core + Vector3.up * 1.84f, 0.56f, 0.34f, 0.14f, 8);
      // 앞면 청록 틈
      k.Glow();
      foreach (var yaw in new[] { -60f, -15f, 30f }) {
        k.Push(core, new Vector3(0, yaw, 0));
        k.Push(new Vector3(0, 0.82f, 1.2f), new Vector3(-13, 0, 0));
        k.Box(Vector3.zero, new Vector3(0.18f, 0.72f, 0.05f));
        k.Pop();
        k.Pop();
      }
      Rivets(k, new Vector3(-2.6f, 0.4f, 1.2f), new Vector3(-2.6f, 0.4f, -1.4f), 4, default);
      // 작은 저장 탱크
      k.Paint(Swatch.Armor);
      k.Lathe(new Vector3(-2.0f, Floor, 1.7f), new[] { new Vector2(0.62f, 0), new Vector2(0.62f, 0.9f), new Vector2(0.4f, 1.14f) }, 8);
      k.Team();
      k.Cylinder(new Vector3(-2.0f, Floor + 0.4f, 1.7f), 0.65f, 0.2f, 8);
    }

    static void ContinentalProcessor(MeshKit k) {
      BasePlate(k);
      var furnace = new Vector3(-0.5f, Floor, -0.5f);
      k.Paint(Swatch.ArmorDark);
      k.Frustum(furnace, 2.35f, 2.25f, 0.3f, 8);
      k.Paint(Swatch.Armor);
      k.Lathe(furnace + Vector3.up * 0.3f, new[] { new Vector2(2.14f, 0), new Vector2(2.1f, 1.1f), new Vector2(1.46f, 2.05f), new Vector2(1.18f, 2.16f) }, 8);
      Rivets(k, new Vector3(-2.4f, 0.9f, -1.4f), new Vector3(-2.4f, 0.9f, 0.4f), 4, new Vector3(0, 0, 90));
      // 굴뚝
      var stack = furnace + Vector3.up * 2.46f;
      k.Paint(Swatch.MetalDark);
      k.Frustum(stack, 0.9f, 0.5f, 0.66f, 8);
      k.Cylinder(stack + Vector3.up * 0.66f, 0.54f, 0.42f, 8);
      k.Paint(Swatch.Black);
      k.Disc(stack + Vector3.up * 1.085f, 0.4f, 8);
      k.Team();
      k.Cylinder(stack + Vector3.up * 0.68f, 0.57f, 0.16f, 8);
      // 쇳물 문 2개
      foreach (var (x, yaw) in new[] { (-1.5f, -22.5f), (0.65f, 22.5f) }) {
        k.Push(new Vector3(x, Floor, 1.02f), new Vector3(0, yaw, 0));
        k.Paint(Swatch.ArmorDark);
        k.Prism(Angular(0.74f, 1.52f), -0.2f, 0.26f);
        k.Glow(Swatch.GlowOrange);
        k.Prism(Angular(0.54f, 1.24f), 0.26f, 0.28f);
        k.Pop();
      }
      // 도가니 수레와 컨베이어
      k.Paint(Swatch.MetalDark);
      k.Taper(new Vector3(-1.7f, Floor + 0.12f, 2.05f), new Vector2(1.1f, 0.9f), new Vector2(1.4f, 1.16f), 0.52f);
      k.Glow(Swatch.GlowOrange);
      k.Box(new Vector3(-1.7f, Floor + 0.645f, 2.05f), new Vector3(1.16f, 0.02f, 0.92f));
      Wheel(k, new Vector3(-2.2f, 0.26f, 1.74f), 0.12f, 0.08f, 6);
      Wheel(k, new Vector3(-1.2f, 0.26f, 1.74f), 0.12f, 0.08f, 6);
      Conveyor(k, new Vector3(1.3f, 0.65f, 1.65f), 2.2f);
      Ingots(k, new Vector3(2.45f, Floor, 2.0f));
      k.Team();
      k.Box(new Vector3(1.3f, 0.58f, 2.74f), new Vector3(0.9f, 0.3f, 0.05f));
      // 뒤 호퍼
      k.Paint(Swatch.ArmorDark);
      k.Taper(new Vector3(2.0f, Floor, -1.9f), new Vector2(1.5f, 1.5f), new Vector2(1.2f, 1.2f), 1.6f);
      k.Team();
      k.Box(new Vector3(2.0f, Floor + 1.62f, -1.9f), new Vector3(1.16f, 0.05f, 1.16f));
    }

    static void ContinentalLab(MeshKit k) {
      BasePlate(k);
      k.Paint(Swatch.ArmorDark);
      k.Taper(new Vector3(-0.4f, Floor, -0.9f), new Vector2(4.6f, 3.6f), new Vector2(4.5f, 3.5f), 0.28f);
      k.Paint(Swatch.Armor);
      k.Chamfer(new Vector3(-0.4f, 1.62f, -0.9f), new Vector3(4.2f, 2.68f, 3.2f), 0.14f);
      k.Paint(Swatch.ArmorDark);
      k.Box(new Vector3(-0.4f, 3.0f, -0.9f), new Vector3(3.9f, 0.08f, 2.9f));
      TeamSign(k, new Vector3(-0.4f, 1.72f, 0.72f), new Vector2(4.2f, 0.24f));
      k.Team();
      k.Box(new Vector3(-2.52f, 1.72f, -0.9f), new Vector3(0.05f, 0.24f, 3.2f));
      // 청록 창
      k.Glow();
      foreach (var x in new[] { -1.8f, -1.0f, 1.0f }) {
        k.Box(new Vector3(x, 2.3f, 0.72f), new Vector3(0.42f, 0.42f, 0.03f));
      }
      foreach (var z in new[] { -2.0f, -0.9f, 0.1f }) {
        k.Box(new Vector3(-2.52f, 2.3f, z), new Vector3(0.03f, 0.42f, 0.5f));
      }
      // 앞 부속동
      k.Paint(Swatch.ArmorDark);
      k.Taper(new Vector3(0.9f, Floor, 1.65f), new Vector2(3.8f, 2.2f), new Vector2(3.7f, 2.1f), 0.24f);
      k.Paint(Swatch.Armor);
      k.Chamfer(new Vector3(0.9f, 0.96f, 1.65f), new Vector3(3.5f, 1.4f, 1.9f), 0.12f);
      k.Paint(Swatch.ArmorDark);
      k.Box(new Vector3(0.9f, 1.68f, 1.65f), new Vector3(3.2f, 0.06f, 1.6f));
      k.Glow();
      foreach (var x in new[] { -0.3f, 0.5f, 1.3f, 2.1f }) {
        k.Box(new Vector3(x, 1.0f, 2.61f), new Vector3(0.6f, 0.44f, 0.03f));
      }
      foreach (var z in new[] { 1.2f, 2.05f }) {
        k.Box(new Vector3(-0.86f, 1.0f, z), new Vector3(0.03f, 0.44f, 0.56f));
      }
      TeamSign(k, new Vector3(0.9f, 1.5f, 2.61f), new Vector2(3.4f, 0.14f));
      // 입구
      k.Paint(Swatch.ArmorDark);
      k.Taper(new Vector3(-2.0f, Floor, 1.65f), new Vector2(0.9f, 1.0f), new Vector2(0.76f, 0.8f), 1.0f);
      k.Paint(Swatch.Black);
      k.Box(new Vector3(-2.0f, 0.56f, 2.16f), new Vector3(0.5f, 0.78f, 0.03f));
      // 안테나 묶음
      k.Paint(Swatch.MetalDark);
      k.Taper(new Vector3(1.0f, 3.04f, -0.9f), new Vector2(0.56f, 0.56f), new Vector2(0.36f, 0.36f), 0.3f);
      k.Cylinder(new Vector3(1.0f, 3.34f, -0.9f), 0.05f, 1.1f, 5);
      k.Box(new Vector3(1.0f, 4.24f, -0.9f), new Vector3(0.14f, 0.5f, 0.14f));
      k.Cylinder(new Vector3(0.4f, 3.04f, -2.0f), 0.04f, 0.8f, 5);
      k.Team();
      k.Box(new Vector3(1.0f, 3.72f, -0.9f), new Vector3(0.16f, 0.2f, 0.16f));
    }

    static void ContinentalFactory(MeshKit k) {
      BasePlate(k);
      var shed = new Vector3(-0.3f, Floor, 0);
      Shed(k, shed, 2.45f, 3.0f, -2.8f, 2.5f);
      // 대각 보강을 댄 두 짝 문
      k.Push(shed);
      k.Paint(Swatch.MetalDark);
      k.Prism(Angular(2.45f * 0.68f, 3.0f * 0.7f), 2.57f, 2.6f);
      k.Pop();
      k.Paint(Swatch.Armor);
      k.Box(new Vector3(-0.3f, 1.1f, 2.62f), new Vector3(0.08f, 1.9f, 0.04f));
      k.Box(new Vector3(-0.3f, 0.24f, 2.62f), new Vector3(3.2f, 0.1f, 0.04f));
      k.Beam(new Vector3(-1.8f, 0.3f, 2.62f), new Vector3(-0.36f, 1.9f, 2.62f), 0.08f);
      k.Beam(new Vector3(1.2f, 0.3f, 2.62f), new Vector3(-0.24f, 1.9f, 2.62f), 0.08f);
      // 지붕 팀 판
      k.Team();
      k.Box(new Vector3(-0.3f, 3.17f, 0.9f), new Vector3(1.6f, 0.06f, 1.1f));
      k.Box(new Vector3(-0.3f, 3.17f, -0.9f), new Vector3(1.6f, 0.06f, 1.1f));
      TeamSign(k, new Vector3(-0.3f, 2.62f, 2.58f), new Vector2(1.0f, 0.26f));
      // 굴뚝 2개
      foreach (var x in new[] { -1.8f, -0.9f }) {
        k.Paint(Swatch.MetalDark);
        k.Frustum(new Vector3(x, 2.6f, -2.3f), 0.32f, 0.24f, 1.6f, 8);
        k.Team();
        k.Cylinder(new Vector3(x, 3.7f, -2.3f), 0.29f, 0.16f, 8);
        k.Paint(Swatch.Black);
        k.Disc(new Vector3(x, 4.205f, -2.3f), 0.16f, 8);
      }
      // 크레인 철탑
      Lattice(k, new Vector3(2.35f, Floor, 1.9f), 3.5f, 0.42f, 5);
      Crate(k, new Vector3(2.4f, Floor, 0.4f), 0.5f);
      Crate(k, new Vector3(2.45f, Floor, -0.3f), 0.42f);
    }

    static void TowerCrane(MeshKit k) {
      k.Team();
      k.Box(new Vector3(0, 0.1f, 0), new Vector3(0.5f, 0.4f, 0.5f));
      Girder(k, new Vector3(0, 0.54f, -2.2f), new Vector3(0, 0.54f, 0.75f), 0.24f, 8);
      k.Paint(Swatch.MetalDark);
      k.Box(new Vector3(0, 0.18f, 0.62f), new Vector3(0.03f, 0.56f, 0.03f));
      k.Paint(Swatch.Metal);
      k.Taper(new Vector3(0, -0.2f, 0.62f), new Vector2(0.06f, 0.06f), new Vector2(0.16f, 0.16f), 0.12f);
    }

    static void ContinentalTower(MeshKit k) {
      BasePlate(k);
      k.Paint(Swatch.ArmorDark);
      k.Frustum(new Vector3(0, Floor, 0), 2.6f, 2.36f, 0.34f, 8);
      k.Paint(Swatch.Armor);
      k.Lathe(new Vector3(0, Floor + 0.32f, 0), new[] { new Vector2(2.1f, 0), new Vector2(1.46f, 1.5f), new Vector2(1.38f, 1.78f) }, 8);
      k.Team();
      k.Lathe(new Vector3(0, Floor + 0.32f, 0), new[] { new Vector2(1.6f, 1.22f), new Vector2(1.47f, 1.53f) }, 8);
      k.Paint(Swatch.ArmorDark);
      foreach (var yaw in new[] { 22.5f, 112.5f, 202.5f, 292.5f }) {
        k.Push(new Vector3(0, Floor + 0.3f, 0), new Vector3(0, yaw, 0));
        k.Taper(new Vector3(0, 0, 1.96f), new Vector2(0.66f, 0.56f), new Vector2(0.46f, 0.24f), 0.82f, new Vector2(0, -0.26f));
        k.Pop();
      }
      for (var i = 0; i < 4; i++) {
        var a = (45f + i * 90f) * Mathf.Deg2Rad;
        var direction = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
        Rivets(k, direction * 1.92f + Vector3.up * 0.74f, direction * 1.6f + Vector3.up * 1.5f, 3, new Vector3(68f * Mathf.Cos(a), 0, -68f * Mathf.Sin(a)));
      }
      // 문은 팔각의 면에 붙인다. 정면 +Z는 모서리다.
      k.Push(new Vector3(0, Floor + 0.32f, 0), new Vector3(0, -22.5f, 0));
      k.Push(new Vector3(0, 0.46f, 1.74f), new Vector3(-23, 0, 0));
      k.Paint(Swatch.ArmorDark);
      k.Box(Vector3.zero, new Vector3(0.86f, 0.98f, 0.1f));
      k.Paint(Swatch.Black);
      k.Box(new Vector3(0, -0.04f, 0.06f), new Vector3(0.6f, 0.8f, 0.03f));
      k.Pop();
      k.Pop();
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(0, 2.22f, 0), 1.2f, 0.1f, 8);
    }

    static void ContinentalTowerTurret(MeshKit k) {
      k.Paint(Swatch.Armor);
      k.Taper(Vector3.zero, new Vector2(1.9f, 2.0f), new Vector2(1.34f, 1.4f), 0.84f, new Vector2(0, -0.08f));
      k.Paint(Swatch.ArmorDark);
      k.Taper(new Vector3(0, 0.1f, 0.68f), new Vector2(1.1f, 0.56f), new Vector2(0.86f, 0.4f), 0.62f);
      k.Team();
      k.Box(new Vector3(0, 0.86f, -0.3f), new Vector3(0.9f, 0.04f, 0.5f));
      Rivets(k, new Vector3(-0.92f, 0.42f, -0.6f), new Vector3(-0.92f, 0.42f, 0.6f), 3, new Vector3(0, 0, 66));
      Rivets(k, new Vector3(0.92f, 0.42f, -0.6f), new Vector3(0.92f, 0.42f, 0.6f), 3, new Vector3(0, 0, -66));
    }

    static void ContinentalTowerBarrel(MeshKit k) {
      foreach (var x in new[] { -0.24f, 0.24f }) {
        k.Paint(Swatch.ArmorDark);
        k.Box(new Vector3(x, 0, 0.3f), new Vector3(0.28f, 0.28f, 0.34f));
        k.Paint(Swatch.MetalDark);
        TurnZ(k, new Vector3(x, 0, 0.46f), () => k.Cylinder(Vector3.zero, 0.09f, 1.3f, 6));
        TurnZ(k, new Vector3(x, 0, 1.58f), () => k.Cylinder(Vector3.zero, 0.13f, 0.24f, 6));
        k.Paint(Swatch.Black);
        TurnZ(k, new Vector3(x, 0, 1.825f), () => k.Disc(Vector3.zero, 0.08f, 6));
      }
    }

    static void ContinentalAirDefense(MeshKit k) {
      BasePlate(k);
      k.Paint(Swatch.ArmorDark);
      k.Frustum(new Vector3(0, Floor, 0), 2.55f, 2.3f, 0.3f, 8);
      k.Paint(Swatch.Armor);
      k.Lathe(new Vector3(0, Floor + 0.28f, 0), new[] { new Vector2(2.0f, 0), new Vector2(1.42f, 1.34f), new Vector2(1.34f, 1.56f) }, 8);
      k.Team();
      k.Lathe(new Vector3(0, Floor + 0.28f, 0), new[] { new Vector2(1.56f, 1.06f), new Vector2(1.44f, 1.34f) }, 8);
      k.Push(new Vector3(0, Floor + 0.28f, 0), new Vector3(0, -22.5f, 0));
      k.Push(new Vector3(0, 0.42f, 1.68f), new Vector3(-23, 0, 0));
      k.Paint(Swatch.ArmorDark);
      k.Box(Vector3.zero, new Vector3(0.8f, 0.9f, 0.1f));
      k.Paint(Swatch.Black);
      k.Box(new Vector3(0, -0.04f, 0.06f), new Vector3(0.56f, 0.74f, 0.03f));
      k.Pop();
      k.Pop();
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(0, 1.94f, 0), 1.16f, 0.08f, 8);
      Rivets(k, new Vector3(-1.86f, 0.62f, 0.7f), new Vector3(-1.5f, 1.4f, 0.55f), 3, new Vector3(0, 0, 66));
      Rivets(k, new Vector3(0.7f, 0.62f, 1.86f), new Vector3(0.55f, 1.4f, 1.5f), 3, new Vector3(66, 0, 0));
    }

    static void ContinentalAirTurret(MeshKit k) {
      k.Paint(Swatch.Armor);
      k.Taper(Vector3.zero, new Vector2(1.8f, 1.8f), new Vector2(1.36f, 1.34f), 0.74f, new Vector2(0, -0.06f));
      k.Paint(Swatch.ArmorDark);
      k.Box(new Vector3(0, 0.82f, -0.3f), new Vector3(0.9f, 0.14f, 0.7f));
      k.Team();
      k.Box(new Vector3(-0.8f, 0.34f, 0), new Vector3(0.05f, 0.24f, 0.9f));
      k.Box(new Vector3(0.8f, 0.34f, 0), new Vector3(0.05f, 0.24f, 0.9f));
    }

    /// <summary>각진 포가에 두 줄로 놓은 다연장 기관포.</summary>
    static void ContinentalAirGuns(MeshKit k) {
      k.Push(Vector3.zero, new Vector3(-24, 0, 0));
      k.Paint(Swatch.ArmorDark);
      k.Taper(new Vector3(0, -0.3f, 0.08f), new Vector2(1.24f, 0.74f), new Vector2(1.1f, 0.6f), 0.6f);
      k.Paint(Swatch.MetalDark);
      foreach (var y in new[] { -0.13f, 0.13f }) {
        foreach (var x in new[] { -0.4f, -0.14f, 0.14f, 0.4f }) {
          TurnZ(k, new Vector3(x, y, 0.42f), () => k.Cylinder(Vector3.zero, 0.055f, 1.5f, 5));
        }
      }
      k.Paint(Swatch.Armor);
      k.Box(new Vector3(0, 0, 1.2f), new Vector3(1.06f, 0.46f, 0.1f));
      k.Pop();
    }

    static void ContinentalAirfield(MeshKit k) {
      BasePlate(k);
      var shed = new Vector3(-0.85f, Floor, 0);
      Shed(k, shed, 2.0f, 2.3f, -2.8f, 0.55f);
      TeamSign(k, new Vector3(-0.85f, 2.0f, 0.63f), new Vector2(1.1f, 0.26f));
      Runway(k, new Vector3(-0.4f, Floor + 0.015f, 1.9f), new Vector2(4.9f, 1.7f));
      // 팔각 관제탑
      var tower = new Vector3(2.0f, Floor, -1.3f);
      k.Team();
      k.Lathe(tower, new[] { new Vector2(0.95f, 0), new Vector2(0.92f, 0.58f), new Vector2(0.56f, 0.86f) }, 8);
      k.Paint(Swatch.Armor);
      k.Frustum(tower + Vector3.up * 0.86f, 0.56f, 0.5f, 1.04f, 8);
      k.Paint(Swatch.ArmorDark);
      k.Frustum(tower + Vector3.up * 1.9f, 0.52f, 0.8f, 0.18f, 8);
      k.Paint(Swatch.Glass);
      k.Frustum(tower + Vector3.up * 2.08f, 0.76f, 0.84f, 0.42f, 8);
      k.Paint(Swatch.Armor);
      k.Frustum(tower + Vector3.up * 2.5f, 0.88f, 0.42f, 0.36f, 8);
      Crate(k, new Vector3(2.4f, Floor, 0.3f), 0.5f);
      Crate(k, new Vector3(1.75f, Floor, 0.35f), 0.42f);
    }

    static void ContinentalHeavy(MeshKit k) {
      BasePlate(k);
      var hall = new Vector3(0, Floor, 0);
      Shed(k, hall, 2.3f, 3.5f, -2.8f, 2.55f);
      TeamSign(k, new Vector3(0, 2.88f, 2.63f), new Vector2(1.5f, 0.54f));
      // 셔터 문
      k.Push(hall);
      k.Paint(Swatch.MetalDark);
      k.Prism(Angular(2.3f * 0.68f, 3.5f * 0.7f), 2.62f, 2.65f);
      k.Pop();
      k.Paint(Swatch.Armor);
      for (var i = 0; i < 5; i++) {
        k.Box(new Vector3(0, 0.36f + i * 0.3f, 2.67f), new Vector3(3.1f, 0.06f, 0.02f));
      }
      k.Team();
      k.Box(new Vector3(-2.42f, 1.1f, 0), new Vector3(0.05f, 1.0f, 2.6f));
      k.Box(new Vector3(2.42f, 1.1f, 0), new Vector3(0.05f, 1.0f, 2.6f));
      // 사각 문틀 게이트
      foreach (var x in new[] { -2.7f, 2.7f }) {
        k.Paint(Swatch.ArmorDark);
        k.Taper(new Vector3(x, Floor, 1.6f), new Vector2(0.5f, 0.7f), new Vector2(0.36f, 0.5f), 0.7f);
        k.Taper(new Vector3(x, Floor, -1.6f), new Vector2(0.5f, 0.7f), new Vector2(0.36f, 0.5f), 0.7f);
        Lattice(k, new Vector3(x, 0.84f, 1.6f), 3.2f, 0.34f, 4);
        Lattice(k, new Vector3(x, 0.84f, -1.6f), 3.2f, 0.34f, 4);
        k.Paint(Swatch.ArmorDark);
        k.Box(new Vector3(x, 4.15f, 0), new Vector3(0.36f, 0.26f, 3.7f));
      }
      foreach (var z in new[] { -1.6f, 1.6f }) {
        k.Paint(Swatch.ArmorDark);
        k.Box(new Vector3(0, 4.55f, z), new Vector3(5.72f, 0.26f, 0.36f));
        k.Box(new Vector3(-2.7f, 4.35f, z), new Vector3(0.36f, 0.3f, 0.36f));
        k.Box(new Vector3(2.7f, 4.35f, z), new Vector3(0.36f, 0.3f, 0.36f));
      }

    }

    static void FreeShipyard(MeshKit k) {
      BasePlate(k);
      k.Paint(Swatch.Armor);
      k.Box(new Vector3(0, Floor + 0.1f, 1.2f), new Vector3(4.8f, 0.2f, 2.6f));
      k.Paint(Swatch.MetalDark);
      k.Box(new Vector3(0, Floor + 0.05f, 1.6f), new Vector3(2.4f, 0.12f, 2.4f));
      var hangar = new Vector3(0, Floor, -1.2f);
      Hangar(k, hangar, 2.2f, 2.4f, -1.8f, 1.8f);
      k.Team();
      k.Box(new Vector3(0, 2.6f, -1.2f), new Vector3(1.2f, 0.08f, 2.4f));
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(-2.0f, Floor, 0.8f), 0.2f, 0.5f, 8);
      k.Cylinder(new Vector3(2.0f, Floor, 0.8f), 0.2f, 0.5f, 8);
      k.Paint(Swatch.Glass);
      k.Box(new Vector3(1.8f, Floor + 0.6f, -1.0f), new Vector3(1.0f, 1.2f, 1.2f));
      k.Paint(Swatch.GlowCyan);
      k.Disc(new Vector3(-1.8f, Floor + 1.2f, -1.0f), 0.12f, 8);
    }

    static void ContinentalShipyard(MeshKit k) {
      BasePlate(k);
      k.Paint(Swatch.Armor);
      k.Box(new Vector3(0, Floor + 0.15f, 0.8f), new Vector3(5.0f, 0.3f, 3.2f));
      k.Paint(Swatch.MetalDark);
      k.Box(new Vector3(0, Floor + 0.05f, 1.2f), new Vector3(2.6f, 0.16f, 2.8f));
      var shed = new Vector3(0, Floor, -1.3f);
      Shed(k, shed, 2.4f, 2.5f, -1.6f, 1.6f);
      k.Team();
      k.Box(new Vector3(0, 2.7f, -1.3f), new Vector3(2.2f, 0.08f, 1.4f));
      k.Paint(Swatch.MetalDark);
      k.Box(new Vector3(1.9f, Floor + 0.4f, 0.8f), new Vector3(0.9f, 0.8f, 1.6f));
      k.Paint(Swatch.GlowCyan);
      k.Disc(new Vector3(-2.1f, Floor + 1.2f, -1.3f), 0.14f, 8);
    }
  }
}
