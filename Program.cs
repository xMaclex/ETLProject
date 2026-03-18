using ETLProject;
using ETLProject.Application.Interfaces;
using ETLProject.Domain;
using ETLProject.Infrastructure;
using ETLProject.Infrastructure.Extractors;

var builder = WebApplication.CreateBuilder(args);

// Logger personalizado que alimenta el buffer
var logBuffer = new EtlLogBuffer();
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddProvider(new EtlLoggerProvider(logBuffer));

// Servicios ETL
builder.Services.AddSingleton(logBuffer);
builder.Services.AddHttpClient("ApiClient", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<BdExtractor>();
builder.Services.AddScoped<CsvExtractor>();
builder.Services.AddScoped<IExtractor<StgProduct>, CsvExtractor>();
builder.Services.AddScoped<IExtractor<StgOrder>,   ApiExtractor>();
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

// Sirve index.html desde wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

// GET /etl/logs — devuelve todos los logs acumulados
app.MapGet("/etl/logs", (EtlLogBuffer buffer) =>
    Results.Ok(buffer.GetAll()));

// GET /etl/stream — Server-Sent Events en tiempo real
app.MapGet("/etl/stream", async (EtlLogBuffer buffer, HttpContext ctx) =>
{
    ctx.Response.Headers.Append("Content-Type", "text/event-stream");
    ctx.Response.Headers.Append("Cache-Control", "no-cache");
    ctx.Response.Headers.Append("Connection", "keep-alive");

    var ct  = ctx.RequestAborted;
    var tcs = new TaskCompletionSource();

    void Handler(string msg)
    {
        try
        {
            var data = $"data: {msg}\n\n";
            ctx.Response.WriteAsync(data, ct).GetAwaiter().GetResult();
            ctx.Response.Body.FlushAsync(ct).GetAwaiter().GetResult();
        }
        catch { tcs.TrySetResult(); }
    }

    buffer.Subscribe(Handler);
    ct.Register(() => tcs.TrySetResult());

    await tcs.Task;
    buffer.Unsubscribe(Handler);
});

app.Run();