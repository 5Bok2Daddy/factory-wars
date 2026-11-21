namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>Optional View-only overrides on a building model. Points must be on walkable ground.</summary>
  public sealed class CivilianActivityPoints : MonoBehaviour {
    public Transform Entrance;
    public Transform Work;
    public Transform Rest;
    [Min(0)] public int ExtraWorkers = 1;
  }
}
