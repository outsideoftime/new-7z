using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace New7z.Core;

public sealed record UpdateInfo(Version Version,string Notes,string InstallerUrl,string InstallerName,string? ChecksumsUrl);

// Reads the public GitHub release feed. Nothing is uploaded; the downloaded installer is
// checked against the SHA256SUMS.txt published in the same release before it may run.
public static class GitHubUpdateChecker
{
    public const string LatestReleaseApi="https://api.github.com/repos/outsideoftime/new-7z/releases/latest";
    private static readonly HttpClient Client=CreateClient();
    private static HttpClient CreateClient()
    {
        var client=new HttpClient{Timeout=TimeSpan.FromSeconds(30)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("New7z-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
    public static bool TryParseTag(string tag,out Version version)
    {
        var text=tag.Trim().TrimStart('v','V');
        if(Version.TryParse(text,out var parsed)&&parsed!=null){version=parsed;return true;}
        version=new Version(0,0);
        return false;
    }
    public const string ReleasesFeed="https://github.com/outsideoftime/new-7z/releases.atom";
    public static async Task<UpdateInfo?> CheckAsync(Version current,CancellationToken cancellation=default)
    {
        try{return await CheckApiAsync(current,cancellation);}
        catch(Exception ex) when(ex is HttpRequestException or IOException)
        {
            // The unauthenticated REST API is limited to 60 requests per hour per IP; the
            // public releases feed is not, so use it when the API is unavailable.
            try{return await CheckFeedAsync(current,cancellation);}
            catch(Exception fallback) when(fallback is HttpRequestException or IOException or System.Xml.XmlException)
            {
                throw new IOException(Loc.T("无法读取 GitHub 发布信息，请检查网络后重试。", "Could not read GitHub release information. Check your network and try again."),ex);
            }
        }
    }
    public static async Task<UpdateInfo?> CheckFeedAsync(Version current,CancellationToken cancellation=default)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,ReleasesFeed);
        request.Headers.Accept.ParseAdd("application/atom+xml");
        using var response=await Client.SendAsync(request,cancellation);
        response.EnsureSuccessStatusCode();
        var text=await response.Content.ReadAsStringAsync(cancellation);
        var document=System.Xml.Linq.XDocument.Parse(text);
        System.Xml.Linq.XNamespace atom="http://www.w3.org/2005/Atom";
        foreach(var entry in document.Root!.Elements(atom+"entry"))
        {
            var link=entry.Elements(atom+"link").Select(l=>l.Attribute("href")?.Value??"").FirstOrDefault(h=>h.Contains("/releases/tag/",StringComparison.OrdinalIgnoreCase));
            if(link==null)continue;
            var tag=link[(link.LastIndexOf("/releases/tag/",StringComparison.OrdinalIgnoreCase)+14)..].Trim('/');
            if(!TryParseTag(tag,out var version))continue;
            if(Normalize(version)<=Normalize(current))return null;
            var notes=HtmlToText(entry.Element(atom+"content")?.Value??"");
            var installerName=$"New7z-{version}-x64-Setup.exe";
            var baseUrl=$"https://github.com/outsideoftime/new-7z/releases/download/{tag}/";
            return new(version,notes,baseUrl+installerName,installerName,baseUrl+"SHA256SUMS.txt");
        }
        return null;
    }
    private static string HtmlToText(string html)
    {
        if(string.IsNullOrWhiteSpace(html))return "";
        var text=System.Text.RegularExpressions.Regex.Replace(html,@"<br\s*/?>","\u000A",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text=System.Text.RegularExpressions.Regex.Replace(text,@"</(p|div|li|h[1-6])>","\u000A",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text=System.Text.RegularExpressions.Regex.Replace(text,"<[^>]+>","");
        text=System.Net.WebUtility.HtmlDecode(text);
        text=System.Text.RegularExpressions.Regex.Replace(text,"[ \u0009]+"," ");
        text=System.Text.RegularExpressions.Regex.Replace(text,"\u000A{3,}","\u000A\u000A");
        return text.Trim();
    }
    private static async Task<UpdateInfo?> CheckApiAsync(Version current,CancellationToken cancellation)
    {
        using var response=await Client.GetAsync(LatestReleaseApi,cancellation);
        if(response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)throw new IOException(Loc.T("GitHub 更新接口暂时受限，请稍后再检查更新。", "The GitHub update API is temporarily rate-limited. Try checking for updates later."));
        response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(cancellation);
        using var document=await JsonDocument.ParseAsync(stream,cancellationToken:cancellation);
        var root=document.RootElement;
        if(root.TryGetProperty("draft",out var draft)&&draft.ValueKind==JsonValueKind.True)return null;
        if(root.TryGetProperty("prerelease",out var prerelease)&&prerelease.ValueKind==JsonValueKind.True)return null;
        if(!root.TryGetProperty("tag_name",out var tagNode)||tagNode.GetString() is not { } tag)return null;
        if(!TryParseTag(tag,out var version)||Normalize(version)<=Normalize(current))return null;
        var notes=root.TryGetProperty("body",out var bodyNode)?bodyNode.GetString()??"":"";
        string? installerUrl=null,installerName=null,checksumsUrl=null;
        if(root.TryGetProperty("assets",out var assets)&&assets.ValueKind==JsonValueKind.Array)
        {
            foreach(var asset in assets.EnumerateArray())
            {
                var assetName=asset.TryGetProperty("name",out var n)?n.GetString()??"":"";
                var url=asset.TryGetProperty("browser_download_url",out var u)?u.GetString()??"":"";
                if(assetName.Equals("SHA256SUMS.txt",StringComparison.OrdinalIgnoreCase)){checksumsUrl=url;continue;}
                if(installerUrl==null&&assetName.StartsWith("New7z-",StringComparison.OrdinalIgnoreCase)&&assetName.EndsWith("-x64-Setup.exe",StringComparison.OrdinalIgnoreCase)){installerName=assetName;installerUrl=url;}
            }
        }
        if(installerUrl==null||installerName==null)return null;
        return new(version,notes,installerUrl,installerName,checksumsUrl);
    }
    public static async Task DownloadAsync(UpdateInfo update,string target,IProgress<double>? progress=null,CancellationToken cancellation=default)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        using var response=await Client.GetAsync(update.InstallerUrl,HttpCompletionOption.ResponseHeadersRead,cancellation);
        response.EnsureSuccessStatusCode();
        var total=response.Content.Headers.ContentLength;
        var buffer=new byte[128*1024];long done=0;
        await using var input=await response.Content.ReadAsStreamAsync(cancellation);
        await using var output=new FileStream(target,FileMode.Create,FileAccess.Write,FileShare.None,128*1024,FileOptions.Asynchronous|FileOptions.SequentialScan);
        int read;
        while((read=await input.ReadAsync(buffer,cancellation))>0)
        {
            await output.WriteAsync(buffer.AsMemory(0,read),cancellation);
            done+=read;
            if(total>0)progress?.Report(Math.Clamp(done*100.0/total.Value,0,100));
        }
        if(total>0&&done!=total)throw new IOException(Loc.T("更新包下载不完整。", "The update download is incomplete."));
    }
    public static async Task<bool> VerifyChecksumAsync(UpdateInfo update,string file,CancellationToken cancellation=default)
    {
        if(update.ChecksumsUrl==null)return false;
        var text=await Client.GetStringAsync(update.ChecksumsUrl,cancellation);
        string? expected=null;
        foreach(var line in text.Split('\n'))
        {
            var trimmed=line.Trim();
            if(trimmed.EndsWith("  "+update.InstallerName,StringComparison.OrdinalIgnoreCase)||trimmed.EndsWith(" "+update.InstallerName,StringComparison.OrdinalIgnoreCase))
            {
                expected=trimmed.Split(' ',StringSplitOptions.RemoveEmptyEntries)[0];break;
            }
        }
        if(expected==null)return false;
        await using var stream=File.OpenRead(file);
        var actual=Convert.ToHexString(await SHA256.HashDataAsync(stream,cancellation));
        return string.Equals(expected,actual,StringComparison.OrdinalIgnoreCase);
    }
    private static Version Normalize(Version version)=>new(version.Major,version.Minor,Math.Max(version.Build,0));
}
