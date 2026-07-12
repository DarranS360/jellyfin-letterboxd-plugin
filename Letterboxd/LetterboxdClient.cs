using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LetterboxdSync.Letterboxd;

/// <summary>
/// Scrapes public Letterboxd pages. There is no official public API, so this reads the
/// same server-rendered HTML a browser would - the poster grid items carry
/// data-item-name/data-item-slug attributes that are scraped directly.
/// </summary>
public class LetterboxdClient
{
    private const string BaseUrl = "https://letterboxd.com";
    private static readonly Regex YearRegex = new(@"\((\d{4})\)\s*$", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly HtmlParser _parser = new();

    public LetterboxdClient(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LetterboxdListSummary>> DiscoverListsAsync(string username, CancellationToken cancellationToken)
    {
        var results = new List<LetterboxdListSummary>();
        string? url = $"{BaseUrl}/{username}/lists/";

        while (url is not null)
        {
            var document = await LoadDocumentAsync(url, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                break;
            }

            foreach (var article in document.QuerySelectorAll("article.list-summary"))
            {
                var link = article.QuerySelector("h2 a[href*='/list/']") ?? article.QuerySelector("a[href*='/list/']");
                var href = link?.GetAttribute("href");
                if (string.IsNullOrEmpty(href))
                {
                    continue;
                }

                var match = Regex.Match(href, @"/list/([^/]+)/?");
                if (!match.Success)
                {
                    continue;
                }

                var title = link!.TextContent.Trim();
                results.Add(new LetterboxdListSummary(match.Groups[1].Value, title));
            }

            url = GetNextPageUrl(document);
        }

        return results;
    }

    public Task<LetterboxdListContent?> GetListAsync(string username, string slug, CancellationToken cancellationToken)
        => GetFilmsAsync($"{BaseUrl}/{username}/list/{slug}/", "ul.poster-list li.posteritem, ul.poster-list li.griditem", cancellationToken);

    public Task<LetterboxdListContent?> GetWatchlistAsync(string username, CancellationToken cancellationToken)
        => GetFilmsAsync($"{BaseUrl}/{username}/watchlist/", "ul.grid li.griditem, ul.grid li.posteritem", cancellationToken, defaultTitle: "Watchlist");

    private async Task<LetterboxdListContent?> GetFilmsAsync(string startUrl, string itemSelector, CancellationToken cancellationToken, string? defaultTitle = null)
    {
        var films = new List<LetterboxdFilm>();
        string? title = null;
        string? description = null;
        string? url = startUrl;
        var seen = new HashSet<string>();

        while (url is not null)
        {
            var document = await LoadDocumentAsync(url, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                break;
            }

            title ??= document.QuerySelector("h1.title-1")?.TextContent.Trim();
            description ??= document.QuerySelector("div.notes, div.body-text")?.TextContent.Trim();

            foreach (var item in document.QuerySelectorAll(itemSelector))
            {
                var poster = item.QuerySelector("[data-item-name], [data-film-name]") ?? item;
                var name = poster.GetAttribute("data-item-name") ?? poster.GetAttribute("data-film-name");
                var slug = poster.GetAttribute("data-item-slug") ?? poster.GetAttribute("data-film-slug");

                if (string.IsNullOrEmpty(name) || (slug is not null && !seen.Add(slug)))
                {
                    continue;
                }

                var yearMatch = YearRegex.Match(name);
                int? year = yearMatch.Success ? int.Parse(yearMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
                var cleanTitle = yearMatch.Success ? name[..yearMatch.Index].Trim() : name.Trim();

                films.Add(new LetterboxdFilm(cleanTitle, year));
            }

            url = GetNextPageUrl(document);
        }

        if (films.Count == 0 && title is null)
        {
            return null;
        }

        return new LetterboxdListContent(title ?? defaultTitle ?? "Untitled", description, films);
    }

    private static string? GetNextPageUrl(IDocument document)
    {
        var next = document.QuerySelector("a.next[href]");
        var href = next?.GetAttribute("href");
        if (string.IsNullOrEmpty(href))
        {
            return null;
        }

        return href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : $"{BaseUrl}{href}";
    }

    private async Task<IDocument?> LoadDocumentAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Jellyfin-LetterboxdSync/1.0)");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Letterboxd request to {Url} failed with status {Status}", url, response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var context = BrowsingContext.New(AngleSharp.Configuration.Default);
            return await context.OpenAsync(req => req.Content(html), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch Letterboxd page {Url}", url);
            return null;
        }
    }
}
