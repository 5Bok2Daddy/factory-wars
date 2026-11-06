namespace Quantum {
  using System;
  using Photon.Deterministic;

  /// <summary>철·목재·강철·전기 묶음. 비용과 보유량에 같이 쓴다. 필드는 FactoryWars.qtn에 있다.</summary>
  [Serializable]
  public partial struct ResourceCost {
    /// <summary>이 보유량으로 cost를 낼 수 있으면 true.</summary>
    public bool Covers(ResourceCost cost) {
      return Iron >= cost.Iron && Wood >= cost.Wood && Steel >= cost.Steel && Power >= cost.Power;
    }

    /// <summary>이 보유량으로 cost를 낼 때 모자란 자원 전부.</summary>
    public MissingResource Missing(ResourceCost cost) {
      var missing = MissingResource.None;
      if (Iron < cost.Iron) {
        missing |= MissingResource.Iron;
      }
      if (Wood < cost.Wood) {
        missing |= MissingResource.Wood;
      }
      if (Steel < cost.Steel) {
        missing |= MissingResource.Steel;
      }
      if (Power < cost.Power) {
        missing |= MissingResource.Power;
      }
      return missing;
    }

    public static ResourceCost operator +(ResourceCost a, ResourceCost b) {
      return new ResourceCost { Iron = a.Iron + b.Iron, Wood = a.Wood + b.Wood, Steel = a.Steel + b.Steel, Power = a.Power + b.Power };
    }

    public static ResourceCost operator -(ResourceCost a, ResourceCost b) {
      return new ResourceCost { Iron = a.Iron - b.Iron, Wood = a.Wood - b.Wood, Steel = a.Steel - b.Steel, Power = a.Power - b.Power };
    }

    public static ResourceCost operator *(ResourceCost a, FP scale) {
      return new ResourceCost { Iron = a.Iron * scale, Wood = a.Wood * scale, Steel = a.Steel * scale, Power = a.Power * scale };
    }

    public static ResourceCost operator /(ResourceCost a, FP divisor) {
      return new ResourceCost { Iron = a.Iron / divisor, Wood = a.Wood / divisor, Steel = a.Steel / divisor, Power = a.Power / divisor };
    }
  }
}
