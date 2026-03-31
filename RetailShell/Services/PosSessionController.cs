using System;
using System.Threading.Tasks;

namespace RetailShell.Services
{
    /// <summary>
    /// Thin wrapper around existing MainWindow POS actions so the pipe server
    /// can request interactive POS operations without duplicating UI logic.
    /// </summary>
    public sealed class PosSessionController
    {
        private readonly Func<Task> _launchPosAsync;
        private readonly Action _exitPosAction;
        private readonly Action<string>? _logAction;

        public PosSessionController(
            Func<Task> launchPosAsync,
            Action exitPosAction,
            Action<string>? logAction = null)
        {
            _launchPosAsync = launchPosAsync;
            _exitPosAction = exitPosAction;
            _logAction = logAction;
        }

        public async Task<(bool Success, int ExitCode, string StdOut, string StdErr)> RestartPosAsync()
        {
            try
            {
                _logAction?.Invoke("Shell broker received RestartPOS request.");

                _exitPosAction();

                await Task.Delay(1500);

                await _launchPosAsync();

                return (true, 0, "POS restart completed through RetailShell.", "");
            }
            catch (Exception ex)
            {
                _logAction?.Invoke("RestartPOS failed in shell broker: " + ex.Message);
                return (false, 1, "", ex.ToString());
            }
        }
    }
}