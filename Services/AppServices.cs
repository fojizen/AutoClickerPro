using Microsoft.Extensions.DependencyInjection;
using AutoClickerPro.ViewModels;

namespace AutoClickerPro.Services;

/// <summary>
/// Configures the application's dependency injection container.
/// Services are resolved via AppServices.Provider throughout the app.
/// </summary>
public static class AppServices
{
    private static IServiceProvider? _provider;

    public static IServiceProvider Provider => _provider ?? throw new InvalidOperationException("Services not initialized.");

    public static void Initialize()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ClickEngine>();
        services.AddSingleton<HotkeyService>();
        services.AddSingleton<ProfileService>();

        _provider = services.BuildServiceProvider();
    }

    public static T GetService<T>() where T : notnull
        => Provider.GetRequiredService<T>();
}
