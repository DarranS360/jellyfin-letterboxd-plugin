using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LetterboxdSync.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Letterboxd username whose lists/watchlist should be synced.
    /// </summary>
    public string LetterboxdUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what each Letterboxd list syncs to: "playlist" or "collection".
    /// </summary>
    public string SyncTarget { get; set; } = "playlist";

    /// <summary>
    /// Gets or sets the id of the Jellyfin user who owns any created playlists.
    /// Required when <see cref="SyncTarget"/> is "playlist". Ignored for collections,
    /// which are library-wide and have no single owner.
    /// </summary>
    public Guid PlaylistOwnerUserId { get; set; } = Guid.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether created playlists are visible to every
    /// user, not just the owner set in <see cref="PlaylistOwnerUserId"/>.
    /// </summary>
    public bool MakePlaylistsPublic { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the user's watchlist should be synced
    /// as a collection alongside their named lists.
    /// </summary>
    public bool IncludeWatchlist { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether new lists found on the user's public
    /// Letterboxd profile are automatically created as Jellyfin collections, with no
    /// need to name them individually.
    /// </summary>
    public bool AutoDiscoverLists { get; set; } = true;

    /// <summary>
    /// Gets or sets a comma-separated list of list slugs to skip when auto-discovering
    /// (e.g. "watched,diary-2025").
    /// </summary>
    public string ExcludedListSlugs { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether existing collection items are cleared
    /// before each sync, so films removed from Letterboxd are also removed from the
    /// Jellyfin collection. When false, films are only ever added, never removed.
    /// </summary>
    public bool RemoveDeletedItems { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a generated cover image is created for
    /// collections that don't already have one. Off by default since the "Collection
    /// Image Generator" plugin already covers this for collections in general.
    /// </summary>
    public bool GenerateCoverArt { get; set; } = false;
}
