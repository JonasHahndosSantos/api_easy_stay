using Microsoft.Extensions.Configuration.Json;

namespace ApiEasyStay.Properties.Configuration;

public static class LocalDevelopmentSettings
{
    public static IConfigurationBuilder AddLocalDevelopmentSettings(
        this IConfigurationBuilder configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) return configuration;

        // O arquivo local complementa os JSONs; secrets, ambiente e argumentos
        // continuam com a prioridade definida pelo ASP.NET Core.
        var insertionIndex = 0;
        for (var index = 0; index < configuration.Sources.Count; index++)
        {
            if (configuration.Sources[index] is JsonConfigurationSource source &&
                (source.Path == "appsettings.json" ||
                 source.Path == $"appsettings.{environment.EnvironmentName}.json"))
                insertionIndex = index + 1;
        }

        configuration.Sources.Insert(insertionIndex, new JsonConfigurationSource
        {
            FileProvider = environment.ContentRootFileProvider,
            Path = "appsettings.Local.json",
            Optional = true,
            ReloadOnChange = false
        });
        return configuration;
    }
}
