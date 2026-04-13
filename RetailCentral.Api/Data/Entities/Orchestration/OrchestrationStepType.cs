public enum OrchestrationStepType
{
    RunCommand = 1,
    InstallPackage = 2,
    WriteFile = 3,
    ApplyConfiguration = 4,
    RestartProcess = 5,
    RestartService = 6,
    RebootMachine = 7,
    Wait = 8,
    CollectInventory = 9,
    ValidateProcess = 10,
    ValidateFile = 11,
    ValidateRegistry = 12,
    ValidateHeartbeat = 13,
    RunScript = 14,
    LaunchPos = 15,
    RestartPos = 16,
    SendNamedPipeCommand = 17
}