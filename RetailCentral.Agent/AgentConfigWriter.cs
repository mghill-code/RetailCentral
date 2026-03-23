using System.Text.Json;
using System.Text.Json.Nodes;

public static class AgentConfigWriter
{
    public static void SaveProtectedSecret(string appsettingsPath, string deviceId, string protectedSecret)
    {
        JsonObject root;

        // If file does not exist, create a new JSON structure
        if (!File.Exists(appsettingsPath))
        {
            root = new JsonObject();
        }
        else
        {
            var json = File.ReadAllText(appsettingsPath);

            root = string.IsNullOrWhiteSpace(json)
                ? new JsonObject()
                : JsonNode.Parse(json)!.AsObject();
        }

        // Ensure Agent section exists
        if (root["Agent"] is not JsonObject agent)
        {
            agent = new JsonObject();
            root["Agent"] = agent;
        }

        // Update ONLY device-specific fields
        agent["DeviceId"] = deviceId;
        agent["DeviceSecret"] = ""; // always blank plaintext
        agent["DeviceSecretProtected"] = protectedSecret;

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        File.WriteAllText(appsettingsPath, root.ToJsonString(options));
    }
}