using Godot;
using LineZero.Gameplay.Presentation;

namespace LineZero.World3D.Presentation;

/// <summary>
/// Configuration-only mapping from presentation states to exact imported clip
/// names. Runtime playback state is owned by PlayerVisualController3D.
/// </summary>
[GlobalClass]
public sealed partial class PlayerAnimationSet3D : Resource
{
    [Export]
    public string IdleClip { get; set; } = string.Empty;

    [Export]
    public string WalkClip { get; set; } = string.Empty;

    [Export]
    public string SprintClip { get; set; } = string.Empty;

    [Export]
    public string CrouchIdleClip { get; set; } = string.Empty;

    [Export]
    public string CrouchWalkClip { get; set; } = string.Empty;

    [Export]
    public string CrawlIdleClip { get; set; } = string.Empty;

    [Export]
    public string CrawlMoveClip { get; set; } = string.Empty;

    [Export]
    public string FireClip { get; set; } = string.Empty;

    [Export]
    public string ReloadClip { get; set; } = string.Empty;

    [Export]
    public string HitReactionClip { get; set; } = string.Empty;

    [Export]
    public string DeathClip { get; set; } = string.Empty;

    public string GetClipName(PlayerPresentationState state)
    {
        return state switch
        {
            PlayerPresentationState.Idle => IdleClip,
            PlayerPresentationState.Walk => WalkClip,
            PlayerPresentationState.Sprint => SprintClip,
            PlayerPresentationState.CrouchIdle => CrouchIdleClip,
            PlayerPresentationState.CrouchWalk => CrouchWalkClip,
            PlayerPresentationState.CrawlIdle => CrawlIdleClip,
            PlayerPresentationState.CrawlMove => CrawlMoveClip,
            PlayerPresentationState.Fire => FireClip,
            PlayerPresentationState.Reload => ReloadClip,
            PlayerPresentationState.HitReaction => HitReactionClip,
            PlayerPresentationState.Death => DeathClip,
            _ => string.Empty,
        } ?? string.Empty;
    }
}
