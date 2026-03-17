namespace RetailCentral.Api.Data.Enums
{
    public enum DeploymentDeviceStatus
    {
        Queued = 1,
        Assigned = 2,
        Downloading = 3,
        Downloaded = 4,
        WaitingForWindow = 5,
        Executing = 6,
        Completed = 7,
        FailedDownload = 8,
        FailedHash = 9,
        FailedExecution = 10,
        TimedOut = 11,
        Cancelled = 12
    }
}