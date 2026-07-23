namespace LineZero.Gameplay.Presentation;

public enum PlayerPresentationState
{
    Idle,
    Walk,
    Sprint,
    CrouchIdle,
    CrouchWalk,
    CrawlIdle,
    CrawlMove,
    Fire,
    Reload,
    HitReaction,
    Death,
    Disabled,
}

public enum PlayerPresentationAction
{
    None,
    Fire,
    Reload,
    HitReaction,
}

public enum PlayerPresentationProfile
{
    Standing,
    Crouch,
    Crawl,
}
