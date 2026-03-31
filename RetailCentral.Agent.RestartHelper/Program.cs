using System.Diagnostics;
using System.ServiceProcess;

/*
===============================================================================
RetailCentral.Agent.RestartHelper
-------------------------------------------------------------------------------
Purpose:
- Safely restart the RetailCentral.Agent Windows service from a detached helper.
- This avoids having the agent attempt to stop/start its own service inline.

How it works:
1. Helper starts detached from the agent.
2. Waits briefly so the calling agent can finish responding/logging.
3. Stops the configured Windows service.
4. Waits for the service to stop.
5. Starts the service again.
6. Returns a process exit code that the caller can log.

Recommended usage:
- Configure AgentRestart execution profile to point to this EXE
- Pass the service name as the first argument, e.g.
  RetailCentral.Agent.RestartHelper.exe "RetailCentral.Agent"
===============================================================================
*/

internal static class Program
{
    private const int Success = 0;
    private const int InvalidArgs = 2;
    private const int ServiceNotFound = 3;
    private const int StopFailed = 4;
    private const int StartFailed = 5;
    private const int UnexpectedFailure = 10;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
            {
                Console.Error.WriteLine("Usage: RetailCentral.Agent.RestartHelper.exe <ServiceName> [DelaySeconds]");
                return InvalidArgs;
            }

            var serviceName = args[0].Trim();
            var delaySeconds = 3;

            if (args.Length >= 2 && int.TryParse(args[1], out var parsedDelay) && parsedDelay >= 0 && parsedDelay <= 60)
            {
                delaySeconds = parsedDelay;
            }

            Console.WriteLine($"Restart helper starting for service '{serviceName}'.");
            Console.WriteLine($"Delay before restart: {delaySeconds}s");

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

            using var sc = new ServiceController(serviceName);

            try
            {
                _ = sc.Status;
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"Service '{serviceName}' was not found. {ex.Message}");
                return ServiceNotFound;
            }

            if (sc.Status != ServiceControllerStatus.Stopped &&
                sc.Status != ServiceControllerStatus.StopPending)
            {
                Console.WriteLine($"Stopping service '{serviceName}'...");
                sc.Stop();
            }
            else
            {
                Console.WriteLine($"Service '{serviceName}' is already stopping/stopped.");
            }

            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(45));
            sc.Refresh();

            if (sc.Status != ServiceControllerStatus.Stopped)
            {
                Console.Error.WriteLine($"Service '{serviceName}' did not reach Stopped state.");
                return StopFailed;
            }

            Console.WriteLine($"Starting service '{serviceName}'...");
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(45));
            sc.Refresh();

            if (sc.Status != ServiceControllerStatus.Running)
            {
                Console.Error.WriteLine($"Service '{serviceName}' did not reach Running state.");
                return StartFailed;
            }

            Console.WriteLine($"Service '{serviceName}' restarted successfully.");
            return Success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Restart helper failed unexpectedly.");
            Console.Error.WriteLine(ex.ToString());
            return UnexpectedFailure;
        }
    }
}
