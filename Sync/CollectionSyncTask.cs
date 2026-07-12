using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LetterboxdSync.Imaging;
using Jellyfin.Plugin.LetterboxdSync.Letterboxd;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Jellyfin.Plugin.LetterboxdSync.Sync;

public class CollectionSyncTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;
    private readonly IProviderManager _providerManager;
    private readonly ILogger<CollectionSyncTask> _logger;
    private readonly LetterboxdClient _client;

    public CollectionSyncTask(
        ILibraryManager libraryManager,
        ICollectionManager collectionManager,
        IProviderManager providerManager,
        IHttpClientFactory httpClientFactory,
        ILogger<CollectionSyncTask> logger)
    {
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
        _providerManager = providerManager;
        _logger = logger;
        _client = new LetterboxdClient(httpClientFactory.CreateClient(), logger);
    }

    public string Name => "Sync Letterboxd Collections";

    public string Key => "LetterboxdSyncCollections";

    public string Description => "Fetches your Letterboxd lists and watchlist, and creates/updates matching Jellyfin collections.";

    public string Category => "Library";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(6).Ticks
            }
        ];
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;
        var username = config.LetterboxdUsername?.Trim();

        if (string.IsNullOrEmpty(username))
        {
            _logger.LogWarning("Letterboxd Sync: no username configured, skipping.");
            return;
        }

        var excluded = new HashSet<string>(
            (config.ExcludedListSlugs ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

        var jobs = new List<(string Key, string? OverrideName, Func<CancellationToken, Task<LetterboxdListContent?>> Fetch)>();

        if (config.IncludeWatchlist)
        {
            jobs.Add(("watchlist", $"{username} Watchlist", ct => _client.GetWatchlistAsync(username, ct)));
        }

        if (config.AutoDiscoverLists)
        {
            var lists = await _client.DiscoverListsAsync(username, cancellationToken).ConfigureAwait(false);
            foreach (var list in lists)
            {
                if (excluded.Contains(list.Slug))
                {
                    continue;
                }

                jobs.Add((list.Slug, null, ct => _client.GetListAsync(username, list.Slug, ct)));
            }
        }

        var total = Math.Max(jobs.Count, 1);
        for (var i = 0; i < jobs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (key, overrideName, fetch) = jobs[i];

            try
            {
                var content = await fetch(cancellationToken).ConfigureAwait(false);
                if (content is not null)
                {
                    await SyncCollectionAsync(overrideName ?? content.Title, content, config, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogWarning("Letterboxd Sync: could not fetch '{Key}' for user {Username}", key, username);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Letterboxd Sync: failed processing '{Key}'", key);
            }

            progress.Report((i + 1) * 100.0 / total);
        }

        progress.Report(100);
    }

    private async Task SyncCollectionAsync(string collectionName, LetterboxdListContent content, Configuration.PluginConfiguration config, CancellationToken cancellationToken)
    {
        var matchedIds = new List<Guid>();
        foreach (var film in content.Films)
        {
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie],
                Recursive = true,
                Name = film.Title,
                Years = film.Year.HasValue ? [film.Year.Value] : Array.Empty<int>()
            };

            var match = _libraryManager.GetItemList(query).FirstOrDefault();
            if (match is not null)
            {
                matchedIds.Add(match.Id);
            }
        }

        _logger.LogInformation(
            "Letterboxd Sync: '{Name}' - {Matched}/{Total} films matched in library",
            collectionName,
            matchedIds.Count,
            content.Films.Count);

        var existing = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            Recursive = true,
            Name = collectionName
        }).OfType<BoxSet>().FirstOrDefault();

        BoxSet boxSet;
        if (existing is null)
        {
            boxSet = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = collectionName,
                ItemIdList = matchedIds.Select(id => id.ToString("N")).ToArray()
            }).ConfigureAwait(false);
        }
        else
        {
            boxSet = existing;
            var currentIds = boxSet.GetLinkedChildren().Select(i => i.Id).ToHashSet();

            if (config.RemoveDeletedItems)
            {
                var toRemove = currentIds.Except(matchedIds).ToArray();
                if (toRemove.Length > 0)
                {
                    await _collectionManager.RemoveFromCollectionAsync(boxSet.Id, toRemove).ConfigureAwait(false);
                }
            }

            var toAdd = matchedIds.Where(id => !currentIds.Contains(id)).ToArray();
            if (toAdd.Length > 0)
            {
                await _collectionManager.AddToCollectionAsync(boxSet.Id, toAdd).ConfigureAwait(false);
            }
        }

        if (!string.IsNullOrEmpty(content.Description) && string.IsNullOrEmpty(boxSet.Overview))
        {
            boxSet.Overview = content.Description;
            await _libraryManager.UpdateItemAsync(boxSet, boxSet.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        }

        if (config.GenerateCoverArt && !boxSet.HasImage(ImageType.Primary, 0))
        {
            await GenerateCoverArtAsync(boxSet, matchedIds, collectionName, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task GenerateCoverArtAsync(BoxSet boxSet, IReadOnlyList<Guid> itemIds, string collectionName, CancellationToken cancellationToken)
    {
        var posterImages = new List<Image<Rgba32>>();
        try
        {
            foreach (var id in itemIds)
            {
                if (posterImages.Count >= 3)
                {
                    break;
                }

                var item = _libraryManager.GetItemById(id);
                if (item is null || !item.HasImage(ImageType.Primary, 0))
                {
                    continue;
                }

                var path = item.GetImagePath(ImageType.Primary, 0);
                if (!File.Exists(path))
                {
                    continue;
                }

                posterImages.Add(await Image.LoadAsync<Rgba32>(path, cancellationToken).ConfigureAwait(false));
            }

            if (posterImages.Count == 0)
            {
                return;
            }

            using var cover = PosterGenerator.Create(posterImages, collectionName, "Letterboxd Collection");
            using var stream = new MemoryStream();
            await cover.SaveAsJpegAsync(stream, cancellationToken).ConfigureAwait(false);
            stream.Position = 0;

            await _providerManager.SaveImage(boxSet, stream, "image/jpeg", ImageType.Primary, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var image in posterImages)
            {
                image.Dispose();
            }
        }
    }
}
