using Jellyfin.Plugin.DongmanYaa.Providers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.DongmanYaa;

public sealed class PluginServiceRegistrar : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient<TvBoxJsonProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-DongmanYaa/0.1.0");
        });
        serviceCollection.AddSingleton<ITvBoxProvider>(services => services.GetRequiredService<TvBoxJsonProvider>());
        serviceCollection.AddSingleton<DongmanYaaChannel>();
        serviceCollection.AddSingleton<MediaBrowser.Controller.Channels.IChannel>(services => services.GetRequiredService<DongmanYaaChannel>());
        serviceCollection.AddSingleton<MediaBrowser.Controller.Channels.IRequiresMediaInfoCallback>(services => services.GetRequiredService<DongmanYaaChannel>());
    }
}
