namespace FactoryWars.EditorTools {
  using FactoryWars.View;
  using UnityEngine;

  public static partial class ArtRecipes {
    public static ArtRecipe MlrsBattery() => new ArtRecipe {
      Id = "MLRS_Battery",
      Kind = "건물",
      MaxRenderers = 5,
      MaxTriangles = 2400,
      Body = MlrsCompound,
      Moving = new[] {
        new MovingPart { Name = "Turret", Pivot = new Vector3(0f, 1.1f, 0f), Build = MlrsTurntable },
        new MovingPart { Name = "PortRack", Parent = "Turret", Pivot = new Vector3(-0.93f, 0.55f, 0f), Build = MlrsRack },
        new MovingPart { Name = "StarboardRack", Parent = "Turret", Pivot = new Vector3(0.93f, 0.55f, 0f), Build = MlrsRack },
        new MovingPart { Name = "LoadingHatch", Pivot = new Vector3(0f, 1.12f, -3.68f), Build = MlrsLoadingHatch },
      },
    };

    static void MlrsCompound(MeshKit k) {
      k.Paint(Swatch.ConcreteDark);
      k.Chamfer(new Vector3(0f, 0.33f, 0f), new Vector3(4.45f, 0.62f, 4.7f), 0.15f);
      k.Paint(Swatch.MetalDark);
      k.Cylinder(new Vector3(0f, 0.62f, 0f), 1.58f, 0.48f, 12);
      k.Paint(Swatch.MetalLight);
      k.Cylinder(new Vector3(0f, 0.89f, 0f), 1.66f, 0.08f, 12);
      for (var side = -1; side <= 1; side += 2) {
        k.Paint(Swatch.ConcreteDark);
        k.Chamfer(new Vector3(side * 3.78f, 0.34f, 0f), new Vector3(0.58f, 0.64f, 7.7f), 0.11f);
        for (var i = -1; i <= 1; ++i) {
          k.Paint(Swatch.MetalDark);
          k.Box(new Vector3(side * 3.78f, 0.96f, i * 3.6f), new Vector3(0.2f, 0.82f, 0.18f));
        }
        k.Paint(Swatch.Hazard);
        k.Box(new Vector3(side * 3.78f, 1.24f, 0f), new Vector3(0.13f, 0.11f, 7.44f));
        k.Paint(Swatch.Olive);
        k.Chamfer(new Vector3(side * 2.93f, 0.55f, 2.6f), new Vector3(1.05f, 1.02f, 1.5f), 0.09f);
        k.Paint(Swatch.MetalDark);
        k.Box(new Vector3(side * 2.93f, 0.94f, 2.6f), new Vector3(1.10f, 0.17f, 1.51f));
        k.Team();
        k.Box(new Vector3(side * 2.93f, 0.60f, 3.36f), new Vector3(0.63f, 0.21f, 0.02f));
      }
      k.Paint(Swatch.OliveDark);
      k.Chamfer(new Vector3(0f, 0.58f, -3.17f), new Vector3(3.6f, 1.06f, 1.27f), 0.10f);
      for (var i = -2; i <= 2; ++i) {
        k.Push(new Vector3(i * 0.59f, 0.98f, -3.6f), Z90);
        k.Paint(Swatch.Metal);
        k.Cylinder(Vector3.zero, 0.22f, 1.06f, 8);
        k.Paint(Swatch.Hazard);
        k.Cylinder(new Vector3(0f, 0.12f, 0f), 0.23f, 0.10f, 8);
        k.Pop();
      }
    }

    static void MlrsTurntable(MeshKit k) {
      k.Paint(Swatch.MetalDark);
      k.Chamfer(new Vector3(0f, 0.20f, 0f), new Vector3(3.15f, 0.40f, 1.88f), 0.11f);
      k.Paint(Swatch.MetalLight);
      k.Box(new Vector3(0f, 0.38f, 0f), new Vector3(0.40f, 0.56f, 0.69f));
    }

    static void MlrsRack(MeshKit k) {
      k.Paint(Swatch.OliveDark);
      k.Chamfer(new Vector3(0f, 0f, -0.1f), new Vector3(1.27f, 0.95f, 2.75f), 0.08f);
      k.Team();
      k.Box(new Vector3(0f, 0.46f, -0.07f), new Vector3(0.95f, 0.07f, 1.8f));
      for (var row = 0; row < 2; ++row) {
        for (var col = -1; col <= 1; ++col) {
          k.Push(new Vector3(col * 0.33f, row == 0 ? -0.18f : 0.18f, 1.18f), Z90);
          k.Paint(Swatch.MetalLight);
          k.Cylinder(Vector3.zero, 0.15f, 0.43f, 8);
          k.Paint(Swatch.Black);
          k.Disc(new Vector3(0f, 0.443f, 0f), 0.113f, 8);
          k.Pop();
        }
      }
    }

    static void MlrsLoadingHatch(MeshKit k) {
      k.Paint(Swatch.Armor);
      k.Chamfer(new Vector3(0f, 0.04f, 0.56f), new Vector3(3.59f, 0.14f, 1.26f), 0.035f);
      k.Paint(Swatch.Hazard);
      k.Box(new Vector3(0f, 0.12f, 0.52f), new Vector3(3.42f, 0.02f, 0.13f));
    }
  }
}
