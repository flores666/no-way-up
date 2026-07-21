using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace LineZero.Tests.Framework;

public sealed class FeatureTestContext
{
    private readonly Node _host;
    private readonly List<string> _failedCases = new();
    private Node? _activeCaseRoot;

    public FeatureTestContext(Node host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    public int PassedCases { get; private set; }

    public int FailedCases => _failedCases.Count;

    public IReadOnlyList<string> FailedCaseNames => _failedCases;

    public string CurrentSuiteId { get; private set; } = string.Empty;

    public void BeginSuite(string suiteId)
    {
        if (string.IsNullOrWhiteSpace(suiteId))
        {
            throw new ArgumentException("Suite ID must be non-empty.", nameof(suiteId));
        }

        CurrentSuiteId = suiteId;
    }

    public void Run(string caseName, Action test)
    {
        ArgumentNullException.ThrowIfNull(test);
        EnsureNoActiveCase();
        string fullName = BuildCaseName(caseName);
        try
        {
            test();
            PassedCases++;
            GD.Print($"[TEST][PASS] {fullName}");
        }
        catch (Exception exception)
        {
            RecordFailure(fullName, exception);
        }
    }

    public async Task RunAsync(string caseName, Func<Task> test)
    {
        ArgumentNullException.ThrowIfNull(test);
        EnsureNoActiveCase();
        string fullName = BuildCaseName(caseName);
        Node caseRoot = new()
        {
            Name = BuildCaseRootName(fullName),
        };
        _host.AddChild(caseRoot);
        _activeCaseRoot = caseRoot;

        try
        {
            await test();
            PassedCases++;
            GD.Print($"[TEST][PASS] {fullName}");
        }
        catch (Exception exception)
        {
            RecordFailure(fullName, exception);
        }
        finally
        {
            _activeCaseRoot = null;
            if (GodotObject.IsInstanceValid(caseRoot))
            {
                caseRoot.QueueFree();
                await WaitProcessFramesAsync();
            }
        }
    }

    public T InstantiateScene<T>(string resourcePath)
        where T : Node
    {
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            throw new ArgumentException(
                "Scene resource path must be non-empty.",
                nameof(resourcePath));
        }

        PackedScene scene = ResourceLoader.Load<PackedScene>(resourcePath)
            ?? throw new InvalidOperationException(
                $"Could not load test scene '{resourcePath}'.");
        Node instance = scene.Instantiate();
        if (instance is not T typedInstance)
        {
            instance.QueueFree();
            throw new InvalidOperationException(
                $"Scene '{resourcePath}' instantiated as {instance.GetType().Name}, " +
                $"not {typeof(T).Name}.");
        }

        GetNodeParentForCurrentCase().AddChild(typedInstance);
        return typedInstance;
    }

    public T AddNode<T>(T node)
        where T : Node
    {
        ArgumentNullException.ThrowIfNull(node);
        GetNodeParentForCurrentCase().AddChild(node);
        return node;
    }

    public async Task WaitProcessFramesAsync(int frameCount = 1)
    {
        if (frameCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameCount),
                "Frame count must be at least one.");
        }

        SceneTree tree = _host.GetTree();
        for (int index = 0; index < frameCount; index++)
        {
            await _host.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    public async Task WaitPhysicsFramesAsync(int frameCount = 1)
    {
        if (frameCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameCount),
                "Frame count must be at least one.");
        }

        SceneTree tree = _host.GetTree();
        for (int index = 0; index < frameCount; index++)
        {
            await _host.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
    }

    public async Task WaitSecondsAsync(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                "Wait duration must be finite and positive.");
        }

        SceneTreeTimer timer = _host.GetTree().CreateTimer(seconds);
        await _host.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
    }

    public async Task DisposeNodeAsync(Node? node)
    {
        if (node is null || !GodotObject.IsInstanceValid(node))
        {
            return;
        }

        node.QueueFree();
        await WaitProcessFramesAsync();
    }

    private Node GetNodeParentForCurrentCase()
    {
        return _activeCaseRoot
            ?? throw new InvalidOperationException(
                "Scene nodes may only be created inside an asynchronous test case.");
    }

    private void EnsureNoActiveCase()
    {
        if (_activeCaseRoot is not null)
        {
            throw new InvalidOperationException(
                "Feature test cases cannot be nested or run concurrently.");
        }
    }

    private void RecordFailure(string fullName, Exception exception)
    {
        _failedCases.Add(fullName);
        string message = $"[TEST][FAIL] {fullName}: {exception}";
        GD.Print(message);
    }

    private string BuildCaseName(string caseName)
    {
        if (string.IsNullOrWhiteSpace(caseName))
        {
            throw new ArgumentException("Case name must be non-empty.", nameof(caseName));
        }

        if (string.IsNullOrWhiteSpace(CurrentSuiteId))
        {
            throw new InvalidOperationException("No active feature-test suite.");
        }

        return $"{CurrentSuiteId}.{caseName}";
    }

    private static string BuildCaseRootName(string fullName)
    {
        char[] characters = fullName.ToCharArray();
        for (int index = 0; index < characters.Length; index++)
        {
            char value = characters[index];
            if (!char.IsLetterOrDigit(value) && value != '_')
            {
                characters[index] = '_';
            }
        }

        return $"Case_{new string(characters)}";
    }
}
