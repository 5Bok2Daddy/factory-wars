namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    public static partial class DeterministicCommandSetup
    {
        static partial void AddCommandFactoriesUser(ICollection<IDeterministicCommandFactory> factories, RuntimeConfig gameConfig, SimulationConfig simulationConfig)
        {
            factories.Add(new FactoryWars.MoveUnitsCommand());
            factories.Add(new FactoryWars.StopUnitsCommand());
            factories.Add(new FactoryWars.AttackTargetCommand());
            factories.Add(new FactoryWars.BuildCommand());
            factories.Add(new FactoryWars.GatherCommand());
            factories.Add(new FactoryWars.RepairCommand());
            factories.Add(new FactoryWars.ProduceCommand());
            factories.Add(new FactoryWars.SetPresetCommand());
            factories.Add(new FactoryWars.SetProcessorCommand());
            factories.Add(new FactoryWars.DiplomacyCommand());
            factories.Add(new FactoryWars.SendNoteCommand());
        }
    }
}
