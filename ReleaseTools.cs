using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FlareLabels;
public static class ReleaseTools
{
    public static void Run(string[] args)
    {
        if(args[0]=="--sign-update")
        {
            byte[] payload=File.ReadAllBytes(args[1]);
            string key=Environment.GetEnvironmentVariable("FLARE_LABELS_SIGNING_PRIVATE_KEY")??throw new InvalidOperationException("Release signing key is not available.");
            using var rsa=RSA.Create();rsa.ImportFromPem(key);
            byte[] signature=rsa.SignData(payload,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            var envelope=JsonSerializer.Serialize(new{algorithm="RS256",payload=Convert.ToBase64String(payload),signature=Convert.ToBase64String(signature)});
            UpdateService.VerifyManifest(envelope,UpdateService.CurrentVersion,UpdateService.PublicKey());
            File.WriteAllText(args[2],envelope);
        }
        else
        {
            var info=UpdateService.VerifyManifest(File.ReadAllText(args[1]),UpdateService.CurrentVersion,UpdateService.PublicKey());
            using var stream=File.OpenRead(args[2]);string hash=Convert.ToHexString(SHA256.HashData(stream));
            UpdateService.VerifyDownloadedContent(stream.Length,hash,info);
        }
    }
}
