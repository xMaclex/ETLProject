using ETLProject;
using ETLProject.Application.Interfaces;
using ETLProject.Domain;
using ETLProject.Infrastructure.Extractors;
using ETLProject.Infrastructure.Loaders;

var builder = WebApplication.CreateBuilder(args);

// Logging
var logBuffer = new ETLProject.Infrastructure.EtlLogBuffer();
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddProvider(new ETLProject.Infrastructure.EtlLoggerProvider(logBuffer));
builder.Services.AddSingleton(logBuffer);

// HTTP
builder.Services.AddHttpClient("ApiClient", c => c.Timeout = TimeSpan.FromSeconds(30));

// Extractores
builder.Services.AddScoped<BdExtractor>();
builder.Services.AddScoped<CsvExtractor>();
builder.Services.AddScoped<IExtractor<StgProduct>, CsvExtractor>();
builder.Services.AddScoped<IExtractor<StgOrder>,   ApiExtractor>();

// Loaders de dimensiones
builder.Services.AddScoped<IDimensionLoader<StgCustomer>, ClienteLoader>();
builder.Services.AddScoped<IDimensionLoader<StgProduct>,  ProductoLoader>();
builder.Services.AddScoped<IDimensionLoader<StgOrder>,    FechaLoader>();

// Loaders adicionales
builder.Services.AddScoped<FactVentaLoader>();

// PaisLoader usa interfaz específica
builder.Services.AddScoped<IPaisLoader, PaisLoader>();

builder.Services.AddHostedService<Worker>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/etl/logs",   (ETLProject.Infrastructure.EtlLogBuffer b) => Results.Ok(b.GetAll()));
app.MapGet("/etl/stream", async (ETLProject.Infrastructure.EtlLogBuffer b, HttpContext ctx) =>
{
    ctx.Response.Headers.Append("Content-Type", "text/event-stream");
    ctx.Response.Headers.Append("Cache-Control", "no-cache");
    var ct  = ctx.RequestAborted;
    var tcs = new TaskCompletionSource();
    void Handler(string msg) {
        try { ctx.Response.WriteAsync($"data: {msg}\n\n", ct).GetAwaiter().GetResult();
              ctx.Response.Body.FlushAsync(ct).GetAwaiter().GetResult(); }
        catch { tcs.TrySetResult(); }
    }
    b.Subscribe(Handler);
    ct.Register(() => tcs.TrySetResult());
    await tcs.Task;
    b.Unsubscribe(Handler);
});

app.Run();