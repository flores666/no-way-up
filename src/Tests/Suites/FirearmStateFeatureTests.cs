using System.Threading.Tasks;
using LineZero.Gameplay.Combat;
using LineZero.Tests.Fixtures;
using LineZero.Tests.Framework;

namespace LineZero.Tests.Suites;

public sealed class FirearmStateFeatureTests : IFeatureTestSuite
{
    public string Id => "firearm-state";

    public string Description => "Magazine, fire rejection, reload, and exact round accounting";

    public Task RunAsync(FeatureTestContext context)
    {
        context.Run("successful-shot-consumes-one-round", () =>
        {
            FirearmState state = new(TestDataFactory.CreateFirearmDefinition(), 3);
            int changed = 0;
            state.Changed += () => changed++;

            FirearmShotResult result = state.TryConsumeRound();

            TestAssert.True(result.Success && result.RoundConsumed,
                "Valid firearm shot was rejected.");
            TestAssert.Equal(3, result.MagazineAmmoBefore,
                "Shot reported wrong pre-shot ammunition.");
            TestAssert.Equal(2, state.CurrentMagazineAmmo,
                "Shot did not consume exactly one round.");
            TestAssert.Equal(1, changed, "Shot state-change count was incorrect.");
        });

        context.Run("empty-and-reloading-shots-change-nothing", () =>
        {
            FirearmState empty = new(TestDataFactory.CreateFirearmDefinition(), 0);
            FirearmShotResult emptyResult = empty.TryConsumeRound();
            TestAssert.Equal(FirearmShotStatus.EmptyMagazine, emptyResult.Status,
                "Empty magazine returned the wrong status.");
            TestAssert.Equal(0, empty.CurrentMagazineAmmo,
                "Empty-magazine input changed ammunition.");

            FirearmState reloading = new(TestDataFactory.CreateFirearmDefinition(), 2);
            TestAssert.Equal(
                ReloadStatus.Started,
                reloading.TryBeginReload(6).Status,
                "Reload could not start.");
            FirearmShotResult reloadResult = reloading.TryConsumeRound();
            TestAssert.Equal(FirearmShotStatus.Reloading, reloadResult.Status,
                "Firing while reloading returned the wrong status.");
            TestAssert.Equal(2, reloading.CurrentMagazineAmmo,
                "Firing while reloading consumed ammunition.");
        });

        context.Run("reload-clamps-and-cancel-preserves-ammo", () =>
        {
            FirearmState state = new(TestDataFactory.CreateFirearmDefinition(), 3);
            state.TryBeginReload(100);
            ReloadResult completed = state.CompleteReload(100);

            TestAssert.Equal(ReloadStatus.Completed, completed.Status,
                "Reload did not complete.");
            TestAssert.Equal(5, completed.LoadedRounds,
                "Reload did not clamp to magazine capacity.");
            TestAssert.Equal(8, state.CurrentMagazineAmmo,
                "Reload produced the wrong magazine count.");

            state.TryConsumeRound();
            state.TryBeginReload(1);
            ReloadResult canceled = state.CancelReload();
            TestAssert.Equal(ReloadStatus.Canceled, canceled.Status,
                "Reload cancel returned the wrong status.");
            TestAssert.Equal(7, state.CurrentMagazineAmmo,
                "Canceling reload changed magazine ammunition.");
        });

        return Task.CompletedTask;
    }
}
