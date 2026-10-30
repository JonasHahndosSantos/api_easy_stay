using ApiEasyStay.Properties.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

static class LocalConfigurationChecks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "easystay-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var environmentKey = "EASYSTAY_CONFIG_TEST_" + Guid.NewGuid().ToString("N") + "_";
        try
        {
            File.WriteAllText(Path.Combine(directory, "appsettings.json"),
                """{"ConnectionStrings":{"DefaultConnection":"base"}}""");
            File.WriteAllText(Path.Combine(directory, "appsettings.Development.json"),
                """{"ConnectionStrings":{"DefaultConnection":"development"}}""");
            File.WriteAllText(Path.Combine(directory, "appsettings.Local.json"),
                """{"ConnectionStrings":{"DefaultConnection":"local"}}""");
            using var files = new PhysicalFileProvider(directory);
            var environment = new TestEnvironment
            {
                EnvironmentName = "Development", ContentRootFileProvider = files
            };
            IConfigurationBuilder Builder() => new ConfigurationBuilder().SetFileProvider(files)
                .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json");
            void Check(IConfigurationBuilder builder, string expected, string name)
            {
                using var config = (ConfigurationRoot)builder.AddLocalDevelopmentSettings(environment).Build();
                if (config.GetConnectionString("DefaultConnection") != expected) throw new Exception(name);
            }

            Check(Builder(), "local", "Arquivo local nao sobrepoe os JSONs padrao.");
            Check(Builder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "secret"
            }), "secret", "Arquivo local sobrepoe secrets.");
            Environment.SetEnvironmentVariable(environmentKey + "ConnectionStrings__DefaultConnection", "environment");
            Check(Builder().AddEnvironmentVariables(environmentKey), "environment", "Arquivo local sobrepoe ambiente.");
            Check(Builder().AddEnvironmentVariables(environmentKey)
                .AddCommandLine(["--ConnectionStrings:DefaultConnection=command-line"]),
                "command-line", "Arquivo local sobrepoe argumentos.");

            environment.EnvironmentName = "Production";
            Check(Builder(), "development", "Producao carregou credenciais do arquivo local.");
            environment.EnvironmentName = "Development";
            File.Delete(Path.Combine(directory, "appsettings.Local.json"));
            Check(Builder(), "development", "Arquivo local opcional impediu inicializacao.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentKey + "ConnectionStrings__DefaultConnection", null);
            foreach (var file in new[] { "appsettings.json", "appsettings.Development.json", "appsettings.Local.json" })
                File.Delete(Path.Combine(directory, file));
            Directory.Delete(directory);
        }
    }
}
