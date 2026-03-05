using Microsoft.AspNetCore.DataProtection;

namespace RetailCentral.Api.Security
{
    public class DeviceSecretProtection
    {
        private readonly IDataProtector _protector;

        public DeviceSecretProtection(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector("RetailCentral.DeviceSecret.v1");
        }

        public string Protect(string plaintext) => _protector.Protect(plaintext);

        public bool TryUnprotect(string protectedOrPlaintext, out string plaintext)
        {
            plaintext = "";
            if (string.IsNullOrWhiteSpace(protectedOrPlaintext))
                return false;

            // Migration-friendly: try unprotect; if it fails, assume it's already plaintext base64
            try
            {
                plaintext = _protector.Unprotect(protectedOrPlaintext);
                return true;
            }
            catch
            {
                plaintext = protectedOrPlaintext;
                return true;
            }
        }
    }
}