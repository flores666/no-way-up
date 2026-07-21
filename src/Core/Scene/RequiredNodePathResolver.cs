using System;
using Godot;

namespace LineZero.Core.Scene;

public static class RequiredNodePathResolver
{
    public static TNode Resolve<TNode>(
        Node owner,
        NodePath path,
        string propertyName)
        where TNode : Node
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        if (string.IsNullOrWhiteSpace(path.ToString()))
        {
            throw new InvalidOperationException(
                $"{owner.GetType().Name} on '{owner.Name}' requires an explicit " +
                $"'{propertyName}' node path.");
        }

        TNode? node = owner.GetNodeOrNull<TNode>(path);
        if (node is null ||
            !GodotObject.IsInstanceValid(node) ||
            !node.IsInsideTree())
        {
            throw new InvalidOperationException(
                $"{owner.GetType().Name} on '{owner.Name}' could not resolve active " +
                $"{typeof(TNode).Name} from '{propertyName}' path '{path}'.");
        }

        return node;
    }
}
