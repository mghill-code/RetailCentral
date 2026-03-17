namespace RetailCentral.Api.Data.Enums
{
    public enum DeploymentExecuteStatus
    {
        None = 0,
        Waiting = 1,
        Executing = 2,
        Completed = 3,
        Failed = 4,
        TimedOut = 5
    }
}