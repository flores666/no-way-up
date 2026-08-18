using LineZero.Data;
using LineZero.Gameplay.Combat;
using LineZero.Gameplay.Flashlight;
using LineZero.Gameplay.Items;

namespace LineZero.Tests.Fixtures;

public static class TestDataFactory
{
    public static ItemDefinition CreateItem(
        string id,
        int maxStackSize = 10,
        ItemUseEffectDefinition? useEffect = null)
    {
        return new ItemDefinition
        {
            Id = id,
            DisplayName = id,
            Description = $"Test item {id}.",
            MaxStackSize = maxStackSize,
            UseEffect = useEffect,
        };
    }

    public static ItemDefinition CreateBattery(int maxStackSize = 5)
    {
        return CreateItem(FlashlightDefinition.RequiredBatteryItemId, maxStackSize);
    }

    public static ItemDefinition CreateReplacementFuse()
    {
        return CreateItem("replacement_fuse", maxStackSize: 1);
    }

    public static FlashlightDefinition CreateFlashlightDefinition(
        double restoredPerBattery = 100.0)
    {
        return new FlashlightDefinition
        {
            Id = "test_flashlight",
            DisplayName = "Test Flashlight",
            MaximumCharge = 100.0,
            DrainPerSecond = 1.0,
            LowChargeThreshold = 25.0,
            CriticalChargeThreshold = 10.0,
            BatteryItemDefinition = CreateBattery(),
            ChargeRestoredPerBattery = restoredPerBattery,
        };
    }

    public static FirearmDefinition CreateFirearmDefinition()
    {
        return new FirearmDefinition
        {
            Id = "test_pistol",
            DisplayName = "Test Pistol",
            AmmoItemDefinition = CreateItem("pistol_ammo", maxStackSize: 50),
            MagazineCapacity = 8,
            Damage = 25,
            FireIntervalSeconds = 0.25,
            ReloadDurationSeconds = 1.2,
            Range = 700.0f,
        };
    }


    public static FirearmDefinition CreateAutomaticRifleDefinition()
    {
        return new FirearmDefinition
        {
            Id = "test_ak",
            DisplayName = "Test AK",
            FireMode = FirearmFireMode.Automatic,
            ReloadMechanism = FirearmReloadMechanism.DetachableMagazine,
            MagazineCapacity = 30,
            Damage = 34,
            FireIntervalSeconds = 0.1,
            ReloadDurationSeconds = 2.2,
            AimedSpreadDegrees = 1.25f,
            HipFireSpreadDegrees = 7.5f,
            Range = 900.0f,
        };
    }

    public static PlayerMovementSettings CreateMovementSettings()
    {
        return new PlayerMovementSettings
        {
            WalkSpeed = 198.0f,
            SprintSpeed = 272.8f,
            Acceleration = 1250.0f,
            Deceleration = 1550.0f,
            SprintStaminaCostPerSecond = 25.0,
            StaminaRecoveryPerSecond = 18.0,
            StaminaRecoveryDelaySeconds = 0.75,
            MinimumStaminaToStartSprint = 10.0,
            MaximumStamina = 100.0,
            MinimumActualMovementDistance = 0.05f,
        };
    }
}
