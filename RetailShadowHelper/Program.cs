using System.Diagnostics;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                Console.WriteLine("No protocol URL was provided.");
                return 1;
            }

            var rawUrl = args[0];
            Console.WriteLine("Received URL: " + rawUrl);

            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            {
                Console.WriteLine("Invalid URL.");
                return 1;
            }

            if (!string.Equals(uri.Scheme, "retailshadow", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Unsupported protocol scheme.");
                return 1;
            }

            var query = ParseQueryString(uri.Query);
            if (!query.TryGetValue("target", out var target) || string.IsNullOrWhiteSpace(target))
            {
                Console.WriteLine("Missing target parameter.");
                return 1;
            }

            target = target.Trim();

            var arguments = $"/shadow:1 /v:{target} /noConsentPrompt /control";

            Console.WriteLine("Launching: mstsc.exe " + arguments);

            Process.Start(new ProcessStartInfo
            {
                FileName = "mstsc.exe",
                Arguments = arguments,
                UseShellExecute = true
            });

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    private static Dictionary<string, string> ParseQueryString(string queryString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(queryString))
            return result;

        var trimmed = queryString.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
            return result;

        var parts = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var pair = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0]);

            var value = pair.Length > 1
                ? Uri.UnescapeDataString(pair[1])
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }
}