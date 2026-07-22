using System.Collections.Generic;
using LineZero.Tests.Suites;

namespace LineZero.Tests.Framework;

public static class FeatureTestSuiteCatalog
{
    public static IReadOnlyList<IFeatureTestSuite> CreateAll()
    {
        return new IFeatureTestSuite[]
        {
            new CoreEventsFeatureTests(),
            new HealthFeatureTests(),
            new StaminaFeatureTests(),
            new InventoryFeatureTests(),
            new ItemUseFeatureTests(),
            new FirearmStateFeatureTests(),
            new FlashlightFeatureTests(),
            new ObjectivePowerFeatureTests(),
            new VisibilityFeatureTests(),
            new MutantPerceptionFeatureTests(),
            new NoiseFeatureTests(),
            new HazardFeatureTests(),
            new FootstepFeatureTests(),
            new WeaponIntegrationFeatureTests(),
            new InteractionFeatureTests(),
            new EmergencyExitFeatureTests(),
            new PrototypeFlowFeatureTests(),
            new MovementCrawlFeatureTests(),
            new HudFeatureTests(),
            new SceneContractFeatureTests(),
            new Foundation3DFeatureTests(),
            new PlayerFoundation3DFeatureTests(),
            new LightingOcclusion3DFeatureTests(),
            new World3DGameplayFeatureTests(),
            new World3DStealthFeatureTests(),
            new World3DCombatFeatureTests(),
            new World3DMutantFeatureTests(),
            new World3DObjectiveFeatureTests(),
        };
    }
}
