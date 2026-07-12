using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.LetterboxdSync.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LetterboxdSync;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => "Letterboxd Collections";

    public override Guid Id => Guid.Parse("e09acb0e-a4b2-45b6-8081-554dbc761904");

    public override string Description =>
        "Syncs Letterboxd lists and your watchlist into native Jellyfin collections, on Jellyfin's own scheduler.";

    public static Plugin? Instance { get; private set; }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
