using System.Threading.Tasks;

namespace LineZero.Tests.Framework;

public interface IFeatureTestSuite
{
    string Id { get; }

    string Description { get; }

    Task RunAsync(FeatureTestContext context);
}
