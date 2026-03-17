namespace RetailCentral.Api.Data.Enums
{
    public enum DeploymentStatus
    {
        Draft = 1,
        Queued = 2,
        Active = 3,
        Completed = 4,
        PartialFailure = 5,
        Cancelled = 6
    }
}