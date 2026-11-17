using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlareLabels;

public sealed record UpdateInfo(string Version,string InstallerUrl,long InstallerSize,string Sha256);

public static class UpdateService
{
    public const string Repository="kbAppDev/flare-labels-updates";
    public const string ManifestName="flare-labels-update.json";
    public static string CurrentVersion=>Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
    static readonly HttpClient Client=CreateClient();
    static HttpClient CreateClient()
    {
        var client=new HttpClient{Timeout=TimeSpan.FromMinutes(10)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FlareLabels/"+CurrentVersion);
        return client;
    }
    public static string PublicKey()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("FlareLabels.Assets.Update-Public-Key.pem")??throw new InvalidDataException("The update verification key is missing.");
        using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }
    static async Task<byte[]> GetLimitedAsync(string url,int maxBytes,CancellationToken cancellation)
    {
        using var response=await Client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,cancellation);
        if(response.StatusCode==HttpStatusCode.NotFound)throw new InvalidDataException("The update channel does not have a published release yet. Please try again later.");
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength>maxBytes)throw new InvalidDataException("Update information exceeded its size limit.");
        using var input=await response.Content.ReadAsStreamAsync(cancellation);using var output=new MemoryStream();var buffer=new byte[16384];
        int read;while((read=await input.ReadAsync(buffer,cancellation))>0){if(output.Length+read>maxBytes)throw new InvalidDataException("Update information exceeded its size limit.");await output.WriteAsync(buffer.AsMemory(0,read),cancellation);}
        return output.ToArray();
    }
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancellation=default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(25));
        var raw=await GetLimitedAsync($"https://api.github.com/repos/{Repository}/releases/latest",1024*1024,timeout.Token);
        using var release=JsonDocument.Parse(raw);var root=release.RootElement;
        if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())throw new InvalidDataException("Only published stable releases are supported.");
        string tag=root.GetProperty("tag_name").GetString()??"";
        if(!Regex.IsMatch(tag,@"^v\d+\.\d+\.\d+$"))throw new InvalidDataException("The release version could not be verified.");
        string version=tag[1..];
        var asset=root.GetProperty("assets").EnumerateArray().FirstOrDefault(a=>a.GetProperty("name").GetString()==ManifestName);
        if(asset.ValueKind==JsonValueKind.Undefined)throw new InvalidDataException("This release does not include verified update information.");
        string url=asset.GetProperty("browser_download_url").GetString()??"";
        if(url!=$"https://github.com/{Repository}/releases/download/{tag}/{ManifestName}")throw new InvalidDataException("The update manifest is outside the expected release channel.");
        var manifest=await GetLimitedAsync(url,65536,timeout.Token);
        var info=VerifyManifest(Encoding.UTF8.GetString(manifest),version,PublicKey());
        var installer=root.GetProperty("assets").EnumerateArray().FirstOrDefault(a=>a.GetProperty("name").GetString()==$"Flare-Labels-Setup-{version}.exe");
        if(installer.ValueKind==JsonValueKind.Undefined||installer.GetProperty("size").GetInt64()!=info.InstallerSize||installer.GetProperty("browser_download_url").GetString()!=info.InstallerUrl)throw new InvalidDataException("The installer does not match the signed release information.");
        return Version.Parse(info.Version)>Version.Parse(CurrentVersion)?info:null;
    }
    public static UpdateInfo VerifyManifest(string envelope,string expectedVersion,string publicKey)
    {
        if(envelope.Length>65536)throw new InvalidDataException("Update manifest is too large.");
        using var document=JsonDocument.Parse(envelope);var root=document.RootElement;
        if(root.GetProperty("algorithm").GetString()!="RS256")throw new InvalidDataException("The update signature format is not supported.");
        byte[] payload=Convert.FromBase64String(root.GetProperty("payload").GetString()??"");
        byte[] signature=Convert.FromBase64String(root.GetProperty("signature").GetString()??"");
        using var rsa=RSA.Create();rsa.ImportFromPem(publicKey);
        if(!rsa.VerifyData(payload,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))throw new InvalidDataException("The update signature could not be verified. No installer was opened.");
        using var content=JsonDocument.Parse(payload);var p=content.RootElement;
        if(p.GetProperty("schema").GetInt32()!=1||p.GetProperty("application").GetString()!="FlareLabels")throw new InvalidDataException("This update belongs to a different application.");
        string version=p.GetProperty("version").GetString()??"";
        string url=p.GetProperty("installerUrl").GetString()??"";
        string hash=p.GetProperty("sha256").GetString()??"";
        long bytes=p.GetProperty("installerSize").GetInt64();
        if(version!=expectedVersion||!Regex.IsMatch(version,@"^\d+\.\d+\.\d+$")||url!=$"https://github.com/{Repository}/releases/download/v{version}/Flare-Labels-Setup-{version}.exe"||bytes<=0||bytes>512L*1024*1024||!Regex.IsMatch(hash,@"^[a-fA-F0-9]{64}$"))throw new InvalidDataException("The signed update details did not match the release.");
        return new(version,url,bytes,hash);
    }
    public static async Task<string> DownloadAsync(UpdateInfo info,IProgress<int>? progress=null,CancellationToken cancellation=default)
    {
        if(!Regex.IsMatch(info.Version,@"^\d+\.\d+\.\d+$")||info.InstallerUrl!=$"https://github.com/{Repository}/releases/download/v{info.Version}/Flare-Labels-Setup-{info.Version}.exe")throw new InvalidDataException("Unexpected installer URL.");
        string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Flare Labels","Updates",info.Version);
        Directory.CreateDirectory(directory);string target=Path.Combine(directory,$"Flare-Labels-Setup-{info.Version}.exe");string temp=target+"."+Guid.NewGuid().ToString("N")+".download";
        try
        {
            using var response=await Client.GetAsync(info.InstallerUrl,HttpCompletionOption.ResponseHeadersRead,cancellation);response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength is long size&&size!=info.InstallerSize)throw new InvalidDataException("The download size did not match the signed update.");
            long total=0;using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using(var input=await response.Content.ReadAsStreamAsync(cancellation))
            using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
            {
                var buffer=new byte[65536];int read;
                while((read=await input.ReadAsync(buffer,cancellation))>0)
                {
                    total+=read;if(total>info.InstallerSize)throw new InvalidDataException("The download exceeded its signed size.");
                    digest.AppendData(buffer,0,read);await output.WriteAsync(buffer.AsMemory(0,read),cancellation);progress?.Report((int)(total*100/info.InstallerSize));
                }
            }
            string actual=Convert.ToHexString(digest.GetHashAndReset());
            VerifyDownloadedContent(total,actual,info);
            File.Move(temp,target,true);return target;
        }
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
    public static void VerifyDownloadedContent(long bytes,string hash,UpdateInfo info)
    {
        if(bytes!=info.InstallerSize||!hash.Equals(info.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The installer checksum did not match. No installer was opened.");
    }
    public static void LaunchInstaller(string path)=>Process.Start(new ProcessStartInfo(path,"--update"){UseShellExecute=true});
}
