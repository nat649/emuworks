using System.Net.Http;
using System.Text.Json;

namespace EmuWorks;

internal static class UpdateService
{
    internal record Release(Version Version, string Page, string Notes);
    internal static Version Current => typeof(MainForm).Assembly.GetName().Version ?? new(0, 0, 0);
    internal static async Task<Release> Latest(CancellationToken cancellation = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("EmuWorks/" + Current.ToString(3));
        using var response = await http.GetAsync("https://api.github.com/repos/nat649/emuworks/releases/latest", cancellation);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        string tag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v'), out var version)) throw new IOException("Unrecognized release version.");
        string page = json.RootElement.GetProperty("html_url").GetString() ?? "";
        if (!Uri.TryCreate(page, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com"
            || !uri.AbsolutePath.StartsWith("/nat649/emuworks/releases/", StringComparison.Ordinal)) throw new IOException("Invalid release link.");
        return new(version, page, json.RootElement.GetProperty("body").GetString() ?? "");
    }
}
