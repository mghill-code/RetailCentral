using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RetailCentral.ShellContracts;

var request = new ShellCommandRequest
{
    RequestId = Guid.NewGuid(),
    Action = ShellCommandActions.LaunchUtility,
    PayloadJson = JsonSerializer.Serialize(new LaunchUtilityPayload
    {
        UtilityName = "Shutdown"
    }),
    RequestedUtc = DateTime.UtcNow
};

using var pipe = new NamedPipeClientStream(
    ".",
    "RetailCentral.Shell",
    PipeDirection.InOut,
    PipeOptions.Asynchronous);

Console.WriteLine("Connecting to RetailShell pipe...");
await pipe.ConnectAsync(5000);

pipe.ReadMode = PipeTransmissionMode.Message;

var json = JsonSerializer.Serialize(request);
var requestBytes = Encoding.UTF8.GetBytes(json);

await pipe.WriteAsync(requestBytes, 0, requestBytes.Length);
await pipe.FlushAsync();

pipe.WaitForPipeDrain();

using var ms = new MemoryStream();
var buffer = new byte[4096];

int read;
do
{
    read = await pipe.ReadAsync(buffer, 0, buffer.Length);
    if (read > 0)
    {
        ms.Write(buffer, 0, read);
    }
}
while (read > 0 && !pipe.IsMessageComplete);

var responseJson = Encoding.UTF8.GetString(ms.ToArray());

Console.WriteLine("Response:");
Console.WriteLine(responseJson);