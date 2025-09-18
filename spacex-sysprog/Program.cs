using System.Text.Json;
using spacex_sysprog.Configuration;
using spacex_sysprog.Infrastructure;
using spacex_sysprog.Infrastructure.Cache;
using spacex_sysprog.Infrastructure.Logging;
using spacex_sysprog.Web;

namespace spacex_sysprog;

public class Program
{
    public static void Main(string[] args)
    {
        var settings = LoadSettings();
        var logger = new Logger();
        var cache  = new CacheManager(settings.Cache.TtlSeconds, logger);
        var spacex = new SpacexServiceImpl(settings.Spacex.BaseUrl, cache, logger); // sync implementacija ispod
        var server = new WebServer(settings.Server.Prefix, spacex, cache, logger);

        logger.Info($"Starting server on {settings.Server.Prefix}");
        server.Start(); // sinhrono, blokira glavnu nit
    }

    private static AppSettings LoadSettings()
    {
        var json = File.ReadAllText("AppSettings.json");
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<AppSettings>(json, opts) ?? throw new Exception("AppSettings.json invalid");
    }
}
