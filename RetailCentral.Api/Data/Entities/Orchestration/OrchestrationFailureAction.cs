public enum OrchestrationFailureAction
{
    FailRun = 1,
    RetryStep = 2,
    Continue = 3,
    Rollback = 4,
    Escalate = 5
}