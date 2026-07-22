namespace LineZero.Gameplay.Enemies;

public readonly record struct MutantDecisionContext(
    bool IsAlive,
    bool IsTerminal,
    bool IsTargetAlive,
    bool CanSeeTarget,
    bool IsTargetInAttackRange,
    bool HasChaseGrace,
    bool HasLastKnownTarget,
    bool HasRelevantNoise,
    bool IsSearching,
    bool HasPatrolRoute);
