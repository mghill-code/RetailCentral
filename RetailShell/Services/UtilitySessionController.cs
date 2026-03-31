using System;

namespace RetailShell.Services
{
    /// <summary>
    /// Thin wrapper around MainWindow utility launching so the named pipe broker
    /// can invoke approved utilities in the interactive session.
    /// </summary>
    public sealed class UtilitySessionController
    {
        private readonly Func<string, (bool Success, string Message)> _launchUtilityFunc;
        private readonly Action<string>? _logAction;

        public UtilitySessionController(
            Func<string, (bool Success, string Message)> launchUtilityFunc,
            Action<string>? logAction = null)
        {
            _launchUtilityFunc = launchUtilityFunc;
            _logAction = logAction;
        }

        public (bool Success, int ExitCode, string StdOut, string StdErr) LaunchUtility(string utilityName)
        {
            try
            {
                _logAction?.Invoke($"Shell broker received LaunchUtility request for '{utilityName}'.");

                var result = _launchUtilityFunc(utilityName);

                return result.Success
                    ? (true, 0, result.Message, "")
                    : (false, 1, "", result.Message);
            }
            catch (Exception ex)
            {
                return (false, 1, "", ex.ToString());
            }
        }
    }
}