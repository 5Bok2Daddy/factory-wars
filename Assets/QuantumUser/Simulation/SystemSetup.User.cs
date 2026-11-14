namespace Quantum
{
    using System;
    using System.Collections.Generic;

    public static partial class DeterministicSystemSetup
    {
        static partial void AddSystemsUser(ICollection<SystemBase> systems, RuntimeConfig gameConfig, SimulationConfig simulationConfig, SystemsConfig systemsConfig)
        {
            systems.Add(new FactoryWars.BootstrapSystem());
            systems.Add(new FactoryWars.FogSystem());
            systems.Add(new FactoryWars.TruceSystem());
            systems.Add(new FactoryWars.CommandSystem());
            systems.Add(new FactoryWars.BotSystem());
            systems.Add(new FactoryWars.DiplomacySystem());
            systems.Add(new FactoryWars.PowerSystem());
            systems.Add(new FactoryWars.ProductionSystem());
            systems.Add(new FactoryWars.TechSystem());
            systems.Add(new FactoryWars.UltimateSystem());
            systems.Add(new FactoryWars.WorkerSystem());
            systems.Add(new FactoryWars.ModuleSystem());
            systems.Add(new FactoryWars.CombatSystem());
            systems.Add(new FactoryWars.CoverSystem());
            systems.Add(new FactoryWars.SupportSystem());
            systems.Add(new FactoryWars.MovementSystem());
            systems.Add(new FactoryWars.BattlefieldSystem());
            systems.Add(new FactoryWars.CitySystem());
            systems.Add(new FactoryWars.VictorySystem());
        }
    }
}
