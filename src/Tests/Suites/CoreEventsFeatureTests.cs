using System;
using System.Threading.Tasks;
using LineZero.Core.Events;
using LineZero.Tests.Framework;

namespace LineZero.Tests.Suites;

public sealed class CoreEventsFeatureTests : IFeatureTestSuite
{
    public string Id => "core-events";

    public string Description => "Safe event publication and subscriber isolation";

    public Task RunAsync(FeatureTestContext context)
    {
        context.Run("failing-subscriber-does-not-stop-healthy-subscriber", () =>
        {
            int healthyCalls = 0;
            Action subscribers = () => throw new InvalidOperationException("Expected test failure.");
            subscribers += () => healthyCalls++;

            int failures = SafeEventPublisher.Publish(subscribers, "test-event");

            TestAssert.Equal(1, failures, "Failure count was incorrect.");
            TestAssert.Equal(1, healthyCalls, "Healthy subscriber was not invoked.");
        });

        context.Run("typed-event-preserves-arguments", () =>
        {
            int observedFirst = 0;
            string? observedSecond = null;
            Action<int, string> subscribers = (first, second) =>
            {
                observedFirst = first;
                observedSecond = second;
            };

            int failures = SafeEventPublisher.Publish(
                subscribers,
                42,
                "payload",
                "typed-test-event");

            TestAssert.Equal(0, failures, "Healthy typed event reported failures.");
            TestAssert.Equal(42, observedFirst, "First event argument was changed.");
            TestAssert.Equal("payload", observedSecond, "Second event argument was changed.");
        });

        context.Run("empty-event-name-is-rejected", () =>
        {
            TestAssert.Throws<ArgumentException>(
                () => SafeEventPublisher.Publish(null, " "),
                "Safe event publisher accepted an empty event name.");
        });

        return Task.CompletedTask;
    }
}
