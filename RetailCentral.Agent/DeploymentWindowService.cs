public sealed class DeploymentWindowService
{
    public bool IsInWindow(TimeSpan currentLocalTime, TimeSpan start, TimeSpan end)
    {
        if (start <= end)
            return currentLocalTime >= start && currentLocalTime <= end;

        return currentLocalTime >= start || currentLocalTime <= end;
    }
}