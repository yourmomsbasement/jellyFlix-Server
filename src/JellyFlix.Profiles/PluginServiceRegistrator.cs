using System;
using Jellyfin.Data.Events.Users;
using JellyFlix.Profiles.Configuration;
using JellyFlix.Profiles.Data;
using JellyFlix.Profiles.Events;
using JellyFlix.Profiles.Security;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace JellyFlix.Profiles;

/// <summary>
/// Registers the plugin's services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IHouseholdStore, JsonHouseholdStore>();
        serviceCollection.AddSingleton(_ => new PinAttemptLimiter(TimeProvider.System));
        serviceCollection.AddSingleton<Func<PluginConfiguration>>(_ => () => Plugin.Instance?.Configuration ?? new PluginConfiguration());
        serviceCollection.AddSingleton<ProfileService>();
        serviceCollection.AddScoped<IEventConsumer<UserDeletedEventArgs>, UserDeletedConsumer>();
    }
}
