using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ApiEasyStay.Properties.Configuration;
using ApiEasyStay.Properties.Data;
using ApiEasyStay.Properties.Data.Repositorys;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddLocalDevelopmentSettings(builder.Environment);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    var instructions = builder.Environment.IsDevelopment()
        ? "Preencha ConnectionStrings:DefaultConnection em appsettings.Local.json ou nos User Secrets. Veja appsettings.Local.example.json."
        : "Configure ConnectionStrings__DefaultConnection no ambiente. Para testar localmente, use dotnet run --launch-profile local-network.";
    throw new InvalidOperationException($"Conexao com PostgreSQL nao configurada. Ambiente: {builder.Environment.EnvironmentName}. {instructions}");
}
if (!builder.Environment.IsDevelopment() &&
    new NpgsqlConnectionStringBuilder(connectionString).SslMode != SslMode.VerifyFull)
    throw new InvalidOperationException("Em producao, o PostgreSQL deve usar SSL Mode=VerifyFull e certificado valido.");
if (!builder.Environment.IsDevelopment() &&
    (string.IsNullOrWhiteSpace(builder.Configuration["AllowedHosts"]) ||
     builder.Configuration["AllowedHosts"]!.Split(';').Any(host => host.Trim() == "*")))
    throw new InvalidOperationException("Configure AllowedHosts com os dominios publicados da API.");

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 8_000_000;
});
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddRouting(options => options.LowercaseUrls = true);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var value in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(value));
    foreach (var value in builder.Configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(value));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellation) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsJsonAsync(new { message = "Muitas solicitacoes. Aguarde e tente novamente." }, cancellation);
    };
    options.AddPolicy("sync", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 180, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddSingleton<SyncSecurity>();
builder.Services.AddSingleton<MicrosoftGraphMailSender>(services => new MicrosoftGraphMailSender(services.GetRequiredService<IConfiguration>(), new HttpClient()));
builder.Services.AddScoped<AuthCodeSender>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
builder.Services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));
builder.Services.AddScoped(typeof(IBaseService<,>), typeof(BaseService<,>));

var app = builder.Build();
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    return;
}
_ = app.Services.GetRequiredService<SyncSecurity>();
app.UseForwardedHeaders();
app.UseExceptionHandler(error => error.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new
    {
        message = "Nao foi possivel concluir a solicitacao. Tente novamente.",
        traceId = context.TraceIdentifier
    });
}));
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    if (!app.Environment.IsDevelopment() && !context.Request.IsHttps &&
        !context.Request.Path.StartsWithSegments("/health"))
    {
        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        await context.Response.WriteAsJsonAsync(new { message = "Use HTTPS para acessar a API." });
        return;
    }
    await next();
});
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    app.MapOpenApi();
}
else app.UseHsts();
app.UseRateLimiter();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken cancellation) =>
{
    try
    {
        var ready = await db.Database.CanConnectAsync(cancellation) &&
            !(await db.Database.GetPendingMigrationsAsync(cancellation)).Any();
        return ready ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503);
    }
    catch { return Results.StatusCode(503); }
});
app.MapControllers();
app.Run();
