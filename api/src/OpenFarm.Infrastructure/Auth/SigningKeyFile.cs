using System.Security.Cryptography;
using System.Text;

namespace OpenFarm.Infrastructure.Auth;

// Signing key for installs that leave Jwt:SigningKey unset: generated once, then kept next to the DataProtection keys.
public static class SigningKeyFile
{
    public static string LoadOrCreate(string directory)
    {
        var path = Path.Combine(directory, "jwt-signing-key");
        Directory.CreateDirectory(directory);
        try
        {
            // CreateNew fails when another process created the key first; that key is the one everybody reads.
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            stream.Write(Encoding.ASCII.GetBytes(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))));
        }
        catch (IOException)
        {
        }

        // The creator may still be writing.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var key = File.ReadAllText(path).Trim();
            if (key.Length > 0)
            {
                return key;
            }

            Thread.Sleep(20);
        }

        throw new InvalidOperationException($"{path} is empty; delete it to generate a new signing key.");
    }
}
