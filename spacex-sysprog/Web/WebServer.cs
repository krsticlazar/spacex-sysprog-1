using System.Net;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Threading; // ThreadPool
using spacex_sysprog.Core;
using spacex_sysprog.Core.Interfaces;
using spacex_sysprog.Infrastructure.Cache;
using spacex_sysprog.Infrastructure.Logging;

namespace spacex_sysprog.Web;

public class WebServer
{
    private readonly HttpListener _listener = new();
    private readonly ILaunchService _service;
    private readonly Logger _logger;
    private readonly CacheManager _cache;

    public WebServer(string prefix, ILaunchService service, CacheManager cache, Logger logger)
    {
        _service = service;
        _cache = cache;
        _logger = logger;
        _listener.Prefixes.Add(prefix);
    }

    public void Start()
    {
        _listener.Start();
        _logger.Info("Server pokrenut. CTRL+C za prekid rada.");
        while (true)
        {
            // BLOKIRA glavnu nit dok ne stigne zahtev
            var ctx = _listener.GetContext();
            // Obradi NA THREADPOOL niti (System.Threading zahtev)
            ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        var sw = Stopwatch.StartNew();
        var req = ctx.Request;
        var res = ctx.Response;
        string path = req.Url?.AbsolutePath ?? "(null)";
        string query = req.Url?.Query ?? "";
        _logger.Info($"REQ {req.HttpMethod} {path}{query} from {req.RemoteEndPoint}");

        try
        {
            if (req.Url == null)
            {
                WriteJson(res, new { error = "Invalid URL" }, HttpStatusCode.BadRequest);
                _logger.Warn($"RES 400 {path}{query} ({sw.ElapsedMilliseconds} ms)");
                return;
            }

            switch (path.ToLowerInvariant())
            {
                case "/health":
                    WriteJson(res, new { status = "ok" });
                    _logger.Info($"RES 200 /health ({sw.ElapsedMilliseconds} ms)");
                    break;

                case "/launches":
                    HandleLaunches(req, res);
                    _logger.Info($"RES {res.StatusCode} /launches ({sw.ElapsedMilliseconds} ms)");
                    break;

                default:
                    WriteJson(res, new { error = "Not found" }, HttpStatusCode.NotFound);
                    _logger.Warn($"RES 404 {path} ({sw.ElapsedMilliseconds} ms)");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex.ToString());
            try
            {
                WriteJson(res, new { error = "Internal server error" }, HttpStatusCode.InternalServerError);
                _logger.Error($"RES 500 {path}{query} ({sw.ElapsedMilliseconds} ms)");
            }
            catch { }
        }
    }

    private void HandleLaunches(HttpListenerRequest req, HttpListenerResponse res)
    {
        var q = req.QueryString;
        var p = new LaunchQueryParameters();

        if (bool.TryParse(q.Get("success"), out var success)) p.Success = success;
        if (bool.TryParse(q.Get("upcoming"), out var upcoming)) p.Upcoming = upcoming;
        if (DateTime.TryParse(q.Get("from"), out var from)) p.From = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        if (DateTime.TryParse(q.Get("to"), out var to)) p.To = DateTime.SpecifyKind(to, DateTimeKind.Utc);
        var name = q.Get("name");
        if (!string.IsNullOrWhiteSpace(name)) p.NameContains = name.Trim();
        if (int.TryParse(q.Get("limit"), out var limit)) p.Limit = Math.Clamp(limit, 1, 50);
        var sort = q.Get("sort");
        if (!string.IsNullOrWhiteSpace(sort)) p.Sort = sort.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";

        // keš finalnog JSON odgovora
        string responseKey = "RESP:" + p.ToCacheKey();
        if (_cache.TryGet(responseKey, out var cachedJson))
        {
            _logger.Info($"Cache HIT (response) {responseKey}");
            WriteRawJson(res, cachedJson);
            return;
        }

        var result = _service.QueryLaunches(p); // SINHRONO
        var finalJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        _cache.Set(responseKey, finalJson);
        WriteRawJson(res, finalJson);
    }

    private static void WriteRawJson(HttpListenerResponse res, string json, HttpStatusCode code = HttpStatusCode.OK)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        res.StatusCode = (int)code;
        res.ContentType = "application/json";
        res.ContentEncoding = Encoding.UTF8;
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.Close();
    }

    private static void WriteJson(HttpListenerResponse res, object obj, HttpStatusCode code = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        WriteRawJson(res, json, code);
    }
}
