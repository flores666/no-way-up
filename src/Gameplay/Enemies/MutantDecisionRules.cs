namespace LineZero.Gameplay.Enemies;

public static class MutantDecisionRules
{
    public static MutantState Decide(MutantDecisionContext context)
    {
        if (!context.IsAlive || context.IsTerminal)
        {
            return MutantState.Dead;
        }

        if (context.IsTargetAlive && context.CanSeeTarget)
        {
            return context.IsTargetInAttackRange
                ? MutantState.Attack
                : MutantState.Chase;
        }

        if (context.IsTargetAlive && context.HasChaseGrace)
        {
            return MutantState.Chase;
        }

        if (context.IsTargetAlive && context.HasLastKnownTarget)
        {
            return MutantState.ChaseLastKnownPosition;
        }

        if (context.IsTargetAlive && context.HasRelevantNoise)
        {
            return MutantState.Investigate;
        }

        if (context.IsTargetAlive && context.IsSearching)
        {
            return MutantState.ChaseLastKnownPosition;
        }

        return context.HasPatrolRoute
            ? MutantState.Patrol
            : MutantState.Idle;
    }
}
