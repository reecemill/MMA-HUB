using System.Net;

// Downloads from UFC.com and saves every response under data/ufc-com/, so a run that
// stops halfway carries on where it left off, and the database can be rebuilt from the
// saved files (dotnet run -- --offline) without downloading anything again.
//
// Two hosts, two speeds. The stats API is the CDN that UFC.com's own event pages poll
// during live events; it has no robots.txt, so it gets a short pause between requests.
// www.ufc.com's robots.txt asks for 15 seconds between requests.
class UfcComClient
{
    public const string ApiBase = "https://d29dxerjsp82wz.cloudfront.net/api/v3/";
    public const string SiteBase = "https://www.ufc.com";

    static readonly TimeSpan ApiDelay = TimeSpan.FromMilliseconds(500);
    static readonly TimeSpan SiteDelay = TimeSpan.FromSeconds(15);

    readonly HttpClient _http;
    DateTime _lastApiRequest = DateTime.MinValue;
    DateTime _lastSiteRequest = DateTime.MinValue;

    public string CacheDir { get; }

    public UfcComClient(string cacheDir)
    {
        CacheDir = cacheDir;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MMA-HUB-scraper/1.0 (+https://github.com/reecemill/MMA-HUB)");
    }

    public string CachePath(string cacheFile) => Path.Combine(CacheDir, cacheFile);

    public string? ReadCached(string cacheFile)
    {
        string path = CachePath(cacheFile);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public Task<string?> DownloadApi(string apiPath, string cacheFile) =>
        Download(ApiBase + apiPath, cacheFile, site: false);

    public Task<string?> DownloadPage(string sitePath, string cacheFile) =>
        Download(SiteBase + sitePath, cacheFile, site: true);

    // Returns the body and saves it. A 404 saves and returns an empty string, so a page
    // that doesn't exist isn't asked for again on every run. Returns null and saves
    // nothing if the request keeps failing, so the next run tries again.
    async Task<string?> Download(string url, string cacheFile, bool site)
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            await WaitTurn(site);
            try
            {
                using HttpResponseMessage response = await _http.GetAsync(url);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    await Save(cacheFile, "");
                    return "";
                }
                if (response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync();
                    await Save(cacheFile, body);
                    return body;
                }
                Console.WriteLine($"HTTP {(int)response.StatusCode} from {url}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Console.WriteLine($"{ex.GetType().Name} from {url}: {ex.Message}");
            }
            if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(30 * attempt));
        }
        Console.WriteLine($"Giving up on {url} for this run.");
        return null;
    }

    // Written to a temporary file first: a run killed mid-write would otherwise leave a
    // cut-off file that the next run takes for a finished download.
    async Task Save(string cacheFile, string body)
    {
        string path = CachePath(cacheFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path + ".tmp", body);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    async Task WaitTurn(bool site)
    {
        DateTime last = site ? _lastSiteRequest : _lastApiRequest;
        TimeSpan wait = last + (site ? SiteDelay : ApiDelay) - DateTime.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait);
        if (site) _lastSiteRequest = DateTime.UtcNow;
        else _lastApiRequest = DateTime.UtcNow;
    }
}
