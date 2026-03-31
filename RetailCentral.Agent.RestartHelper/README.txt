RetailCentral.Agent.RestartHelper

Build:
    dotnet build
or
    dotnet publish -c Release -r win-x64 --self-contained false

Recommended publish:
    dotnet publish -c Release -r win-x64 -o C:\RetailCentral\Helpers

Recommended Agent execution profile:
{
  "Name": "AgentRestart",
  "Path": "C:\\RetailCentral\\Helpers\\RetailCentral.Agent.RestartHelper.exe",
  "WorkingDirectory": "C:\\RetailCentral\\Helpers",
  "TimeoutSeconds": 60,
  "AllowedArgumentsRegex": [
    "^RetailCentral\\.Agent(\\s+[0-9]{1,2})?$"
  ]
}

Recommended CommandExecutor usage for RestartAgent:
- Call ExecuteNamedProfileAsync("RestartAgent", "AgentRestart", "RetailCentral.Agent 3", ...)
- That passes:
    service name = RetailCentral.Agent
    delay seconds = 3

Notes:
- The helper must run elevated / under an account allowed to stop/start the service.
- Keep this helper in a trusted execution root.
- This helper is intentionally small and detached from the main agent.
