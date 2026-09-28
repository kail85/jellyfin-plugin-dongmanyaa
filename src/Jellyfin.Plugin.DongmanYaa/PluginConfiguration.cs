using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DongmanYaa;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public string SourcesJson { get; set; } = "{\"sources\":[]}";
}
