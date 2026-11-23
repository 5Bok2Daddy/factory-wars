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
            factories.Add(new FactoryWars.EnterGarrisonCommand());
            factories.Add(new FactoryWars.ExitGarrisonCommand());
            factories.Add(new FactoryWars.BuildCommand());
            factories.Add(new FactoryWars.GatherCommand());
            factories.Add(new FactoryWars.RepairCommand());
            factories.Add(new FactoryWars.ProduceCommand());
            factories.Add(new FactoryWars.SetPresetCommand());
            factories.Add(new FactoryWars.SetProcessorCommand());
            factories.Add(new FactoryWars.DiplomacyCommand());
            factories.Add(new FactoryWars.SendNoteCommand());
            factories.Add(new FactoryWars.SurrenderCommand());
            factories.Add(new FactoryWars.HoldUnitsCommand());
            factories.Add(new FactoryWars.PatrolUnitsCommand());
            factories.Add(new FactoryWars.RallyCommand());
            factories.Add(new FactoryWars.UpgradeBaseCommand());
            factories.Add(new FactoryWars.ResearchCommand());
            factories.Add(new FactoryWars.StartUltimateCommand());
            factories.Add(new FactoryWars.LaunchUltimateCommand());
            factories.Add(new FactoryWars.CancelUltimateCommand());
            factories.Add(new FactoryWars.StealthCommand());
            factories.Add(new FactoryWars.PlaceJammerCommand());
            factories.Add(new FactoryWars.OrderSortieCommand());
            factories.Add(new FactoryWars.ChooseDoctrineCommand());
            factories.Add(new FactoryWars.TradeCommand());
            factories.Add(new FactoryWars.WorkBridgeCommand());
            factories.Add(new FactoryWars.SalvageCommand());
            factories.Add(new FactoryWars.UpgradeFactoryCommand());
            factories.Add(new FactoryWars.OverdriveFactoryCommand());
            factories.Add(new FactoryWars.BoardTransportCommand());
            factories.Add(new FactoryWars.UnloadTransportCommand());
            factories.Add(new FactoryWars.RebuildSeaBarrierCommand());
        }
    }
}
