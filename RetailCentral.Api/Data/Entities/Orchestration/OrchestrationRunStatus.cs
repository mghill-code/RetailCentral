public enum OrchestrationRunStatus
{
    Pending = 1,
    Running = 2,
    WaitingForRetry = 3,
    WaitingForDevice = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7,
    RolledBack = 8
}