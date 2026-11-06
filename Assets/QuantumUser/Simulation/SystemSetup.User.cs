namespace Quantum
{
    using System;
    using System.Collections.Generic;

    public static partial class DeterministicSystemSetup
    {
        static partial void AddSystemsUser(ICollection<SystemBase> systems, RuntimeConfig gameConfig, SimulationConfig simulationConfig, SystemsConfig systemsConfig)
        {
            systems.Add(new FactoryWars.BootstrapSystem());
            systems.Add(new FactoryWars.CommandSystem());
            systems.Add(new FactoryWars.DiplomacySystem());
            systems.Add(new FactoryWars.ProductionSystem());
            systems.Add(new FactoryWars.ModuleSystem());
            systems.Add(new FactoryWars.CombatSystem());
            systems.Add(new FactoryWars.MovementSystem());
            systems.Add(new FactoryWars.VictorySystem());
        }
    }
}
