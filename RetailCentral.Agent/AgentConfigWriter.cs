using System.Text.Json;
using System.Text.Json.Nodes;

public static class AgentConfigWriter
{
    public static void SaveProtectedSecret(string appsettingsPath, string deviceId, string protectedSecret)
    {
        var json = File.ReadAllText(appsettingsPath);
        var root = JsonNode.Parse(json)!.AsObject();

        var agent = root["Agent"]!.AsObject();
        agent["DeviceId"] = deviceId;
        agent["DeviceSecret"] = "";
        agent["DeviceSecretProtected"] = protectedSecret;

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        File.WriteAllText(appsettingsPath, root.ToJsonString(options));
    }
}