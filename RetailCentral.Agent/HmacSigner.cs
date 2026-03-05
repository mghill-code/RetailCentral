using System.Security.Cryptography;
using System.Text;

public sealed class HmacSigner
{
    public (string Timestamp, string Signature) Sign(string deviceSecretBase64, string method, string pathAndQuery, byte[] bodyBytes)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        var bodyShaHex = ToLowerHex(SHA256.HashData(bodyBytes ?? Array.Empty<byte>()));

        var canonical = $"{ts}\n{method.ToUpperInvariant()}\n{pathAndQuery}\n{bodyShaHex}";

        var key = Convert.FromBase64String(deviceSecretBase64);
        using var hmac = new HMACSHA256(key);

        var sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        var sig = Convert.ToBase64String(sigBytes);

        return (ts, sig);
    }

    private static string ToLowerHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}