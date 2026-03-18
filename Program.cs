using ETLProject;
using ETLProject.Application.Interfaces;
using ETLProject.Domain;
using ETLProject.Infrastructure.Extractors;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient<ApiExtractor>();
builder.Services.AddScoped<BdExtractor>();
builder.Services.AddScoped<IExtractor<StgProduct>, CsvExtractor>();
builder.Services.AddScoped<IExtractor<StgOrder>,   ApiExtractor>();

builder.Services.AddHostedService(sp => new Worker(
    sp.GetRequiredService<ILogger<Worker>>(),
    sp.GetRequiredService<BdExtractor>(),
    sp.GetRequiredService<IExtractor<StgProduct>>(),
    sp.GetRequiredService<IExtractor<StgOrder>>()
));

var host = builder.Build();
host.Run();