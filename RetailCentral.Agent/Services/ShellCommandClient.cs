using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RetailCentral.ShellContracts;

namespace RetailCentral.Agent.Services
{
    /// <summary>
    /// Sends interactive shell requests from the agent service to RetailShell
    /// over a local named pipe.
    /// </summary>
    public sealed class ShellCommandClient
    {
        private const string PipeName = "RetailCentral.Shell";

        public async Task<ShellCommandResponse> SendAsync(ShellCommandRequest request, CancellationToken ct)
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(5000, ct);
            pipe.ReadMode = PipeTransmissionMode.Message;

            var requestJson = JsonSerializer.Serialize(request);
            var requestBytes = Encoding.UTF8.GetBytes(requestJson);

            await pipe.WriteAsync(requestBytes, 0, requestBytes.Length, ct);
            await pipe.FlushAsync(ct);
            pipe.WaitForPipeDrain();

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

            var responseJson = Encoding.UTF8.GetString(ms.ToArray());

            return JsonSerializer.Deserialize<ShellCommandResponse>(responseJson)
                   ?? throw new InvalidOperationException("Shell command response could not be parsed.");
        }
    }
}