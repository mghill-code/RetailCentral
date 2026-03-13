using System;

public static class ProtectSecretTool
{
    public static void Run()
    {
        Console.WriteLine("=== RetailCentral DeviceSecret Protection Tool ===");
        Console.WriteLine();
        Console.Write("Paste plaintext DeviceSecret: ");
        var plain = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(plain))
        {
            Console.WriteLine("No DeviceSecret provided.");
            return;
        }

        try
        {
            var protectedValue = DeviceSecretStore.Protect(plain.Trim());

            Console.WriteLine();
            Console.WriteLine("Protected DeviceSecret:");
            Console.WriteLine(protectedValue);
            Console.WriteLine();
            Console.WriteLine("Put this into appsettings.json as Agent:DeviceSecretProtected");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("ERROR protecting secret:");
            Console.WriteLine(ex);
        }
    }
}