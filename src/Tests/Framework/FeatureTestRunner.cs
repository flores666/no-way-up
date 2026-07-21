using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;

namespace LineZero.Tests.Framework;

public sealed partial class FeatureTestRunner : Node
{
    public override void _Ready()
    {
        _ = RunSelectedSuitesAsync();
    }

    private async Task RunSelectedSuitesAsync()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            IReadOnlyList<IFeatureTestSuite> allSuites = FeatureTestSuiteCatalog.CreateAll();
            string[] arguments = OS.GetCmdlineUserArgs();
            if (ContainsArgument(arguments, "--list"))
            {
                PrintSuiteList(allSuites);
                GetTree().Quit(0);
                return;
            }

            HashSet<string>? requestedSuites = ParseRequestedSuites(arguments);
            FeatureTestContext context = new(this);
            int executedSuites = 0;
            for (int index = 0; index < allSuites.Count; index++)
            {
                IFeatureTestSuite suite = allSuites[index];
                if (requestedSuites is not null && !requestedSuites.Contains(suite.Id))
                {
                    continue;
                }

                executedSuites++;
                context.BeginSuite(suite.Id);
                GD.Print($"[TEST][SUITE] {suite.Id}: {suite.Description}");
                try
                {
                    await suite.RunAsync(context);
                }
                catch (Exception exception)
                {
                    context.Run("suite-bootstrap", () => throw exception);
                }
            }

            if (executedSuites == 0)
            {
                string requested = requestedSuites is null
                    ? "<all>"
                    : string.Join(",", requestedSuites);
                GD.PushError($"[TEST] No suites matched '{requested}'. Use --list.");
                GetTree().Quit(2);
                return;
            }

            stopwatch.Stop();
            int totalCases = context.PassedCases + context.FailedCases;
            bool succeeded = context.FailedCases == 0;
            GD.Print(
                $"[TEST][SUMMARY] suites={executedSuites} " +
                $"passed={context.PassedCases} failed={context.FailedCases}");
            GD.Print("[TEST][FINAL_SUMMARY]");
            GD.Print($"  result: {(succeeded ? "PASS" : "FAIL")}");
            GD.Print($"  suites: {executedSuites}");
            GD.Print($"  tests: {totalCases}");
            GD.Print($"  passed: {context.PassedCases}");
            GD.Print($"  failed: {context.FailedCases}");
            GD.Print($"  duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
            GD.Print(
                context.FailedCases == 0
                    ? "  failed cases: none"
                    : $"  failed cases: {string.Join(", ", context.FailedCaseNames)}");
            if (context.FailedCases > 0)
            {
                GD.PushError(
                    $"[TEST][FAILED_CASES] {string.Join(", ", context.FailedCaseNames)}");
            }

            GetTree().Quit(succeeded ? 0 : 1);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            GD.PushError($"[TEST][RUNNER_FAILURE] {exception}");
            GD.Print("[TEST][FINAL_SUMMARY]");
            GD.Print("  result: ERROR");
            GD.Print("  stage: runner");
            GD.Print($"  duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
            GetTree().Quit(2);
        }
    }

    private static HashSet<string>? ParseRequestedSuites(string[] arguments)
    {
        HashSet<string>? selected = null;
        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            const string prefix = "--suite=";
            if (!argument.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            selected ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string value = argument[prefix.Length..];
            string[] identifiers = value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int suiteIndex = 0; suiteIndex < identifiers.Length; suiteIndex++)
            {
                selected.Add(identifiers[suiteIndex]);
            }
        }

        return selected;
    }

    private static bool ContainsArgument(string[] arguments, string expected)
    {
        for (int index = 0; index < arguments.Length; index++)
        {
            if (string.Equals(arguments[index], expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void PrintSuiteList(IReadOnlyList<IFeatureTestSuite> suites)
    {
        GD.Print("[TEST][SUITES]");
        for (int index = 0; index < suites.Count; index++)
        {
            IFeatureTestSuite suite = suites[index];
            GD.Print($"  {suite.Id} - {suite.Description}");
        }
    }
}
