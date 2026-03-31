using RetailCentral.ShellContracts;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace RetailShell.Services
{
    /// <summary>
    /// Named pipe server hosted inside RetailShell so the agent service can request
    /// interactive desktop actions such as RestartPOS.
    /// </summary>
    public sealed class ShellCommandServer
    {
        private readonly PosSessionController _posController;
        private readonly UtilitySessionController _utilityController;
        private readonly Dispatcher _dispatcher;
        private readonly Action<string>? _logAction;
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;

        public ShellCommandServer(
             PosSessionController posController,
             UtilitySessionController utilityController,
             Dispatcher dispatcher,
             Action<string>? logAction = null)
        {
            _posController = posController;
            _utilityController = utilityController;
            _dispatcher = dispatcher;
            _logAction = logAction;
        }

        public void Start()
        {
            if (_listenerTask != null)
                return;

            _cts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        }

        public async Task StopAsync()
        {
            try
            {
                _cts?.Cancel();

                if (_listenerTask != null)
                {
                    await _listenerTask;
                }
            }
            catch
            {
                // Best effort for shutdown cleanup.
            }
        }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        "RetailCentral.Shell",
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous);

                    _logAction?.Invoke("Shell broker waiting for pipe connection...");
                    await pipe.WaitForConnectionAsync(ct);
                    _logAction?.Invoke("Shell broker pipe connected.");

                    using var ms = new MemoryStream();
                    var buffer = new byte[4096];

                    int read;
                    do
                    {
                        read = await pipe.ReadAsync(buffer, 0, buffer.Length, ct);
                        if (read > 0)
                        {
                            ms.Write(buffer, 0, read);
                        }
                    }
                    while (read > 0 && !pipe.IsMessageComplete);

                    var requestJson = Encoding.UTF8.GetString(ms.ToArray());
                    _logAction?.Invoke("Shell broker received request: " + requestJson);

                    var request = JsonSerializer.Deserialize<ShellCommandRequest>(requestJson);
                    var response = await HandleAsync(request);

                    var responseJson = JsonSerializer.Serialize(response);
                    var responseBytes = Encoding.UTF8.GetBytes(responseJson);

                    await pipe.WriteAsync(responseBytes, 0, responseBytes.Length, ct);
                    await pipe.FlushAsync(ct);

                    _logAction?.Invoke("Shell broker sent response: " + responseJson);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logAction?.Invoke("Shell broker listener error: " + ex.Message);

                    try
                    {
                        await Task.Delay(1000, ct);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }

        private async Task<ShellCommandResponse> HandleAsync(ShellCommandRequest? request)
        {
            if (request == null)
            {
                return new ShellCommandResponse
                {
                    RequestId = Guid.Empty,
                    Success = false,
                    ExitCode = 900,
                    StdErr = "Shell command request was null."
                };
            }

            _logAction?.Invoke(
                $"Shell broker handling requestId={request.RequestId} " +
                $"action={request.Action} " +
                $"requestedUtc={request.RequestedUtc:O}");

            try
            {
                switch (request.Action)
                {
                    case ShellCommandActions.LaunchUtility:
                        {
                            if (string.IsNullOrWhiteSpace(request.PayloadJson))
                            {
                                _logAction?.Invoke(
                                    $"Shell broker rejected requestId={request.RequestId} " +
                                    $"action={request.Action} reason=EmptyPayload");

                                return new ShellCommandResponse
                                {
                                    RequestId = request.RequestId,
                                    Success = false,
                                    ExitCode = 902,
                                    StdErr = "LaunchUtility payload was empty."
                                };
                            }

                            var payload = JsonSerializer.Deserialize<LaunchUtilityPayload>(request.PayloadJson);

                            if (payload == null || string.IsNullOrWhiteSpace(payload.UtilityName))
                            {
                                _logAction?.Invoke(
                                    $"Shell broker rejected requestId={request.RequestId} " +
                                    $"action={request.Action} reason=InvalidPayload");

                                return new ShellCommandResponse
                                {
                                    RequestId = request.RequestId,
                                    Success = false,
                                    ExitCode = 903,
                                    StdErr = "LaunchUtility payload was invalid."
                                };
                            }

                            var brokerAllowedUtilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Configure PinPad",
                    "Scanner Program",
                    "Scanner Programming"
                };

                            if (!brokerAllowedUtilities.Contains(payload.UtilityName))
                            {
                                _logAction?.Invoke(
                                    $"Shell broker rejected requestId={request.RequestId} " +
                                    $"action={request.Action} utility='{payload.UtilityName}' reason=NotAllowed");

                                return new ShellCommandResponse
                                {
                                    RequestId = request.RequestId,
                                    Success = false,
                                    ExitCode = 904,
                                    StdErr = $"Utility '{payload.UtilityName}' is not allowed through the shell broker."
                                };
                            }

                            var result = await _dispatcher.InvokeAsync(() => _utilityController.LaunchUtility(payload.UtilityName));
                            var final = result;

                            _logAction?.Invoke(
                                $"Shell broker completed requestId={request.RequestId} " +
                                $"action={request.Action} utility='{payload.UtilityName}' " +
                                $"success={final.Success} exitCode={final.ExitCode}");

                            if (!string.IsNullOrWhiteSpace(final.StdOut))
                            {
                                _logAction?.Invoke($"Shell broker stdout requestId={request.RequestId}: {final.StdOut}");
                            }

                            if (!string.IsNullOrWhiteSpace(final.StdErr))
                            {
                                _logAction?.Invoke($"Shell broker stderr requestId={request.RequestId}: {final.StdErr}");
                            }

                            return new ShellCommandResponse
                            {
                                RequestId = request.RequestId,
                                Success = final.Success,
                                ExitCode = final.ExitCode,
                                StdOut = final.StdOut,
                                StdErr = final.StdErr
                            };
                        }

                    case ShellCommandActions.RestartPOS:
                        {
                            var result = await _dispatcher.InvokeAsync(() => _posController.RestartPosAsync());
                            var final = await result;

                            _logAction?.Invoke(
                                $"Shell broker completed requestId={request.RequestId} " +
                                $"action={request.Action} " +
                                $"success={final.Success} " +
                                $"exitCode={final.ExitCode}");

                            if (!string.IsNullOrWhiteSpace(final.StdOut))
                            {
                                _logAction?.Invoke($"Shell broker stdout requestId={request.RequestId}: {final.StdOut}");
                            }

                            if (!string.IsNullOrWhiteSpace(final.StdErr))
                            {
                                _logAction?.Invoke($"Shell broker stderr requestId={request.RequestId}: {final.StdErr}");
                            }

                            return new ShellCommandResponse
                            {
                                RequestId = request.RequestId,
                                Success = final.Success,
                                ExitCode = final.ExitCode,
                                StdOut = final.StdOut,
                                StdErr = final.StdErr
                            };
                        }

                    default:
                        {
                            _logAction?.Invoke(
                                $"Shell broker rejected requestId={request.RequestId} " +
                                $"action={request.Action} reason=UnknownAction");

                            return new ShellCommandResponse
                            {
                                RequestId = request.RequestId,
                                Success = false,
                                ExitCode = 901,
                                StdErr = $"Unknown shell action '{request.Action}'."
                            };
                        }
                }
            }
            catch (Exception ex)
            {
                _logAction?.Invoke(
                    $"Shell broker failed requestId={request.RequestId} " +
                    $"action={request.Action} error={ex.Message}");

                return new ShellCommandResponse
                {
                    RequestId = request.RequestId,
                    Success = false,
                    ExitCode = 999,
                    StdErr = ex.ToString()
                };
            }
        }
    }
}