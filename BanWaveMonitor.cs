using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WoWCleaner;

public sealed class BanWaveMonitor
{
    private static readonly string[] RedditSubreddits =
    [
        "wow",
        "classicwow",
        "woweconomy",
        "wowclassic",
        "worldofwarcraft"
    ];

    private static readonly Uri BlizzardForumSearch = new(
        "https://us.forums.blizzard.com/en/wow/search.json?q=ban%20banned%20suspended%20warden%20order%3Alatest");

    private static readonly string[] SkippedSources =
    [
        "Twitter/X hashtags and @BlizzardCS: requires official X API credentials for reliable last-hour counts.",
        "YouTube creators: requires YouTube Data API/channel IDs for reliable last-hour counts.",
        "Discord communities: not public, requires server membership/API bot permissions.",
        "Telegram channels: not reliable without explicit channel feeds or Bot API access.",
        "OwnedCore WoW forum: no stable public JSON endpoint for accurate last-hour counts.",
        "Google Trends: no supported unauthenticated API for exact last-hour app counts.",
        "WoW Token price: requires a trusted data feed and does not directly prove bans.",
        "Auction House data: requires realm/API configuration and does not directly prove bans.",
        "Server population data: requires a trusted data feed and does not directly prove bans.",
        "Gold selling sites: not checked because it is an unsafe/untrusted source class."
    ];

    private static readonly Regex SignalRegex = new(
        @"ban\s*wave|banwave|mass\s*ban|wow\s*ban|blizzard\s*ban|banned|banhammer|suspension|suspended|account\s*closure|warden|bot\s*ban|exploit\s*ban",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Logger logger;

    public BanWaveMonitor(Logger logger)
    {
        this.logger = logger;
    }

    public async Task<BanWaveReport> CheckAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 WoWCleaner/1.2");

        var checkedAt = DateTimeOffset.UtcNow;
        var since = checkedAt.AddHours(-1);
        var results = new List<BanSourceResult>();

        foreach (var subreddit in RedditSubreddits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report($"Checking Reddit r/{subreddit}...");
            results.Add(await CheckRedditAsync(http, subreddit, since, cancellationToken));
            await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report("Checking Blizzard official WoW forums...");
        results.Add(await CheckBlizzardForumsAsync(http, since, cancellationToken));

        var successfulSources = results.Count(result => result.Status == BanSourceStatus.Checked);
        var signalCount = results
            .Where(result => result.Status == BanSourceStatus.Checked)
            .Sum(result => result.SignalCount);

        var risk = successfulSources == 0
            ? "UNKNOWN"
            : signalCount switch
            {
                >= 20 => "HIGH",
                >= 8 => "MEDIUM",
                >= 1 => "LOW",
                _ => "NONE"
            };

        logger.Info($"Ban wave check completed. Successful public sources: {successfulSources}. Last-hour signals: {signalCount}.");
        return new BanWaveReport(risk, signalCount, successfulSources, results, SkippedSources, checkedAt.ToLocalTime().DateTime);
    }

    private async Task<BanSourceResult> CheckRedditAsync(HttpClient http, string subreddit, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString("ban OR banned OR suspended OR warden OR banhammer");
        var uri = new Uri($"https://www.reddit.com/r/{subreddit}/search.rss?q={query}&restrict_sr=on&sort=new&t=hour");
        var sourceName = $"Reddit r/{subreddit}";

        try
        {
            var xml = await GetStringWithRetryAsync(http, uri, cancellationToken);
            var feed = XDocument.Parse(xml);
            XNamespace atom = "http://www.w3.org/2005/Atom";
            var count = 0;

            foreach (var entry in feed.Descendants(atom + "entry"))
            {
                var updatedRaw = entry.Element(atom + "updated")?.Value ?? "";
                if (!DateTimeOffset.TryParse(updatedRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var updated) ||
                    updated < since)
                {
                    continue;
                }

                var text = $"{entry.Element(atom + "title")?.Value} {entry.Element(atom + "content")?.Value}";
                count += SignalRegex.Matches(text).Count;
            }

            logger.Info($"{sourceName}: {count} last-hour keyword signals.");
            return new BanSourceResult(sourceName, count, BanSourceStatus.Checked, "Checked public Reddit RSS search results from the last hour.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warn($"{sourceName} unavailable: {ex.Message}");
            return new BanSourceResult(sourceName, 0, BanSourceStatus.Unavailable, ex.Message);
        }
    }

    private async Task<BanSourceResult> CheckBlizzardForumsAsync(HttpClient http, DateTimeOffset since, CancellationToken cancellationToken)
    {
        const string sourceName = "Blizzard Official WoW Forums";

        try
        {
            var json = await GetStringWithRetryAsync(http, BlizzardForumSearch, cancellationToken);
            using var document = JsonDocument.Parse(json);
            var count = 0;

            if (document.RootElement.TryGetProperty("posts", out var posts))
            {
                foreach (var post in posts.EnumerateArray())
                {
                    var created = TryReadDate(post, "created_at");
                    if (created is null || created < since)
                    {
                        continue;
                    }

                    var text = $"{ReadString(post, "blurb")} {ReadString(post, "topic_title")}";
                    count += SignalRegex.Matches(text).Count;
                }
            }

            if (document.RootElement.TryGetProperty("topics", out var topics))
            {
                foreach (var topic in topics.EnumerateArray())
                {
                    var created = TryReadDate(topic, "created_at");
                    if (created is null || created < since)
                    {
                        continue;
                    }

                    var text = $"{ReadString(topic, "title")} {ReadString(topic, "fancy_title")}";
                    count += SignalRegex.Matches(text).Count;
                }
            }

            logger.Info($"{sourceName}: {count} last-hour keyword signals.");
            return new BanSourceResult(sourceName, count, BanSourceStatus.Checked, "Checked public Blizzard forum search results from the last hour.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warn($"{sourceName} unavailable: {ex.Message}");
            return new BanSourceResult(sourceName, 0, BanSourceStatus.Unavailable, ex.Message);
        }
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static async Task<string> GetStringWithRetryAsync(HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var response = await http.GetAsync(uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                continue;
            }

            response.EnsureSuccessStatusCode();
        }

        return string.Empty;
    }

    private static DateTimeOffset? TryReadUnixTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var seconds) => DateTimeOffset.FromUnixTimeSeconds((long)seconds),
            _ => null
        };
    }

    private static DateTimeOffset? TryReadDate(JsonElement element, string propertyName)
    {
        var raw = ReadString(element, propertyName);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var created)
            ? created
            : null;
    }
}

public enum BanSourceStatus
{
    Checked,
    Unavailable
}

public sealed record BanSourceResult(string SourceName, int SignalCount, BanSourceStatus Status, string Details);

public sealed record BanWaveReport(
    string RiskLevel,
    int SignalCount,
    int SuccessfulSources,
    IReadOnlyList<BanSourceResult> SourceResults,
    IReadOnlyList<string> SkippedSources,
    DateTime CheckedAt)
{
    public string ToDisplayText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Risk level: {RiskLevel}");
        builder.AppendLine($"Last-hour keyword signals: {SignalCount}");
        builder.AppendLine($"Public sources checked: {SuccessfulSources}");
        builder.AppendLine($"Checked at: {CheckedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine();
        builder.AppendLine("This is an informational public-source check only.");
        builder.AppendLine("It does not bypass Blizzard, hide the PC, spoof hardware, or change any game files.");
        builder.AppendLine();
        builder.AppendLine("Supported public source counts:");

        foreach (var result in SourceResults)
        {
            var count = result.Status == BanSourceStatus.Checked ? result.SignalCount.ToString() : "N/A";
            builder.AppendLine($"- {result.SourceName}: {count} ({result.Details})");
        }

        builder.AppendLine();
        builder.AppendLine("Skipped or unsupported sources:");

        foreach (var source in SkippedSources)
        {
            builder.AppendLine($"- {source}");
        }

        return builder.ToString();
    }
}
