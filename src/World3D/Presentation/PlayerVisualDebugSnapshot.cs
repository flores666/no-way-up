using Godot;
using LineZero.Gameplay.Presentation;

namespace LineZero.World3D.Presentation;

public enum PlayerVisualSource
{
    DevelopmentFallback,
    ImportedModel,
}

public readonly record struct PlayerVisualDebugSnapshot(
    PlayerPresentationState State,
    PlayerPresentationAction Action,
    PlayerPresentationProfile Profile,
    Vector2 LocalLocomotionBlend,
    float VisualYawDegrees,
    PlayerVisualSource Source,
    bool SocketsValid,
    int MissingClipCount);
