# Documentación Técnica del Proyecto ETL

## Resumen

Este proyecto es una aplicación ETL (Extract, Transform, Load) desarrollada en C# utilizando .NET 9.0. El propósito principal es extraer datos de múltiples fuentes (base de datos SQL Server, archivos CSV y APIs REST), transformarlos y cargarlos en un data warehouse para análisis de ventas.

La aplicación sigue una arquitectura limpia con separación de responsabilidades en capas: Domain, Application e Infrastructure. Utiliza inyección de dependencias, logging personalizado y se ejecuta como un servicio hospedado en ASP.NET Core.

## Arquitectura General

### Estructura del Proyecto

```
ETLProject/
├── Domain/                 # Modelos de datos y lógica de negocio
├── Application/            # Interfaces y servicios de aplicación
│   ├── Interfaces/         # Contratos para extractores y loaders
│   └── Services/           # (Vacío en la versión actual)
├── Infrastructure/         # Implementaciones concretas
│   ├── Extractors/         # Clases para extraer datos
│   │   └── Loaders/        # Clases para cargar datos
│   ├── EtlLogger.cs        # Proveedor de logging personalizado
│   └── EtlLogBuffer.cs     # Buffer para logs en memoria
├── Worker/                 # Servicio hospedado que ejecuta el ETL
├── Program.cs              # Configuración de la aplicación web
├── appsettings.json        # Configuración
└── Data/Csv/               # Archivos CSV de entrada
```

### Patrón Arquitectónico

La aplicación sigue el patrón **Clean Architecture** con las siguientes capas:

1. **Domain**: Contiene los modelos de datos (entidades de staging, dimensiones y hechos).
2. **Application**: Define interfaces para los servicios de extracción y carga.
3. **Infrastructure**: Implementa las interfaces con tecnologías específicas (Dapper, CsvHelper, HttpClient).

## Modelos de Datos

### Entidades de Staging

```csharp
// Domain/Models.cs

public class StgCustomer
{
    public int    CustomerID { get; set; }
    public string FirstName  { get; set; } = string.Empty;
    public string LastName   { get; set; } = string.Empty;
    public string Email      { get; set; } = string.Empty;
    public string Phone      { get; set; } = string.Empty;
    public string City       { get; set; } = string.Empty;
    public string Country    { get; set; } = string.Empty;
}

public class StgProduct
{
    public int    ProductID   { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Category    { get; set; } = string.Empty;
    public string Price       { get; set; } = string.Empty;
}

public class StgOrder
{
    public int    OrderID     { get; set; }
    public int    CustomerID  { get; set; }
    public string OrderDate   { get; set; } = string.Empty;
    public string StatusOrder { get; set; } = string.Empty;
}

public class StgOrderDetail
{
    public int    OrderDetailID { get; set; }
    public int    OrderID       { get; set; }
    public int    ProductID     { get; set; }
    public string Quantity      { get; set; } = string.Empty;
    public string UnitPrice     { get; set; } = string.Empty;
}
```

### Dimensiones y Hechos

```csharp
// Dimensiones
public record DimCliente(
    int    ClienteKey,
    int    ClienteID,
    string Nombre,
    string Email,
    string Pais,
    string Ciudad,
    string Segmento
);

public record DimProducto(
    int     ProductoKey,
    int     ProductoID,
    string  Nombre,
    string  Categoria,
    decimal PrecioLista,
    bool    Activo
);

public record DimFecha(
    int      FechaKey,
    DateTime FechaCompleta,
    int      Anio,
    int      Mes,
    string   NombreMes,
    int      Trimestre,
    int      Dia,
    string   DiaSemana
);

public record DimPais(
    int    PaisKey,
    string NombrePais,
    string Region
);

// Hecho
public record FactVenta(
    long    FactVentaID,
    int     FechaKey,
    int     ProductoKey,
    int     ClienteKey,
    int     PaisKey,
    int     Cantidad,
    decimal PrecioUnitario,
    decimal IngresoTotal,
    int     NumeroOrden
);
```

## Interfaces de Aplicación

### Extractor

```csharp
// Application/Interfaces/Extractor.cs

public interface IExtractor<T>
{
    Task<IEnumerable<T>> ExtractAsync();
}
```

### Dimension Loader

```csharp
// Application/Interfaces/DimensionLoader.cs

public interface IDimensionLoader<T>
{
    Task LoadAsync(IEnumerable<T> data);
}

public interface IPaisLoader
{
    Task LoadAsync(IEnumerable<StgCustomer> data);
}
```

## Implementaciones de Infrastructure

### Extractores

#### CsvExtractor

Extrae datos desde archivos CSV utilizando CsvHelper.

```csharp
// Infrastructure/Extractors/CsvExtractor.cs

public class CsvExtractor : IExtractor<StgProduct>
{
    private readonly string _folderPath;
    private readonly ILogger<CsvExtractor> _logger;

    public CsvExtractor(IConfiguration config, ILogger<CsvExtractor> logger)
    {
        _folderPath = config["Extraction:CsvFolder"]!;
        _logger     = logger;
    }

    public async Task<IEnumerable<StgProduct>> ExtractAsync()
    {
        var file = Path.Combine(_folderPath, "products.csv");
        if (!File.Exists(file)) { 
            _logger.LogWarning("CSV: no existe {f}", file); 
            return []; 
        }
        var data = ReadFile<StgProduct, StgProductMap>(file);
        _logger.LogInformation("CSV productos: {n} registros", data.Count);
        return data;
    }

    // Métodos similares para ExtractCustomersAsync, ExtractOrdersAsync, etc.
}
```

#### ApiExtractor

Consume datos desde una API REST.

```csharp
// Infrastructure/Extractors/ApiExtractor.cs

public class ApiExtractor : IExtractor<StgOrder>
{
    private readonly IHttpClientFactory    _httpFactory;
    private readonly string                _baseUrl;
    private readonly ILogger<ApiExtractor> _logger;

    public ApiExtractor(
        IHttpClientFactory    httpFactory,
        IConfiguration        config,
        ILogger<ApiExtractor> logger)
    {
        _httpFactory = httpFactory;
        _baseUrl     = config["ApiSettings:BaseUrl"] ?? string.Empty;
        _logger      = logger;
    }

    public async Task<IEnumerable<StgOrder>> ExtractAsync()
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
        {
            _logger.LogWarning("API: BaseUrl no configurada");
            return [];
        }

        var client   = _httpFactory.CreateClient("ApiClient");
        var response = await client.GetStringAsync($"{_baseUrl}/posts");
        
        var orders = JsonSerializer.Deserialize<List<StgOrder>>(response,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        return orders ?? [];
    }
}
```

### Loaders

#### ClienteLoader

Carga la dimensión de clientes utilizando SQL Server con Dapper y SqlBulkCopy.

```csharp
// Infrastructure/Extractors/Loaders/ClienteLoader.cs

public class ClienteLoader : IDimensionLoader<StgCustomer>
{
    private readonly string _conn;
    private readonly ILogger<ClienteLoader> _logger;

    public ClienteLoader(IConfiguration config, ILogger<ClienteLoader> logger)
    {
        _conn   = config.GetConnectionString("DefaultConnection")!;
        _logger = logger;
    }

    public async Task LoadAsync(IEnumerable<StgCustomer> data)
    {
        var list = data.ToList();
        _logger.LogInformation("DimCliente: cargando {n} registros...", list.Count);

        if (list.Count == 0) return;

        // Crear DataTable para bulk insert
        var dt = new DataTable();
        dt.Columns.Add("ClienteID", typeof(int));
        dt.Columns.Add("Nombre", typeof(string));
        dt.Columns.Add("Email", typeof(string));
        dt.Columns.Add("Pais", typeof(string));
        dt.Columns.Add("Ciudad", typeof(string));
        dt.Columns.Add("Segmento", typeof(string));

        foreach (var c in list)
        {
            dt.Rows.Add(
                c.CustomerID,
                $"{c.FirstName} {c.LastName}".Trim(),
                c.Email,
                c.Country,
                c.City,
                "General"
            );
        }

        using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        // Crear tabla temporal
        using var cmd = new SqlCommand("CREATE TABLE #TempClientes (ClienteID INT, Nombre NVARCHAR(255), Email NVARCHAR(255), Pais NVARCHAR(100), Ciudad NVARCHAR(100), Segmento NVARCHAR(50))", conn);
        await cmd.ExecuteNonQueryAsync();

        // Bulk insert a tabla temporal
        using var bulk = new SqlBulkCopy(conn);
        bulk.DestinationTableName = "#TempClientes";
        await bulk.WriteToServerAsync(dt);

        // Merge desde tabla temporal
        const string sql = """
            MERGE dim.DimCliente AS target
            USING #TempClientes AS source
                ON target.ClienteID = source.ClienteID
            WHEN MATCHED THEN
                UPDATE SET
                    Nombre   = source.Nombre,
                    Email    = source.Email,
                    Pais     = source.Pais,
                    Ciudad   = source.Ciudad,
                    Segmento = source.Segmento
            WHEN NOT MATCHED THEN
                INSERT (ClienteID, Nombre, Email, Pais, Ciudad, Segmento)
                VALUES (source.ClienteID, source.Nombre, source.Email, source.Pais, source.Ciudad, source.Segmento);
            """;

        await conn.ExecuteAsync(sql);
        _logger.LogInformation("DimCliente: {n} registros procesados", list.Count);
    }
}
```

### Logging Personalizado

#### EtlLogBuffer

Buffer en memoria para almacenar logs con suscripción para streaming.

```csharp
// Infrastructure/EtlLogBuffer.cs

public class EtlLogBuffer
{
    private readonly ConcurrentQueue<string> _logs = new();
    private readonly List<Action<string>>    _subscribers = new();
    private readonly object _lock = new();

    public void Add(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logs.Enqueue(entry);

        lock (_lock)
            foreach (var sub in _subscribers)
                sub(entry);
    }

    public IEnumerable<string> GetAll() => _logs.ToArray();

    public void Subscribe(Action<string> handler)
    {
        lock (_lock) _subscribers.Add(handler);
    }

    public void Unsubscribe(Action<string> handler)
    {
        lock (_lock) _subscribers.Remove(handler);
    }
}
```

#### EtlLogger

Proveedor de logging que escribe en el buffer.

```csharp
// Infrastructure/EtlLogger.cs

public class EtlLogger : ILogger
{
    private readonly EtlLogBuffer _buffer;
    private readonly string       _category;

    public EtlLogger(EtlLogBuffer buffer, string category)
    {
        _buffer   = buffer;
        _category = category;
    }

    public void Log<TState>(LogLevel level, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(level)) return;

        var prefix = level switch
        {
            LogLevel.Warning  => "⚠️ WARN",
            LogLevel.Error    => "❌ ERROR",
            LogLevel.Critical => "🔴 CRITICAL",
            _                 => "ℹ️ INFO"
        };

        var msg = $"{prefix} | {_category.Split('.').Last()} | {formatter(state, exception)}";
        if (exception != null) msg += $" → {exception.Message}";
        _buffer.Add(msg);
    }

    // Otros métodos de ILogger...
}
```

## Proceso ETL

El proceso ETL se ejecuta en el servicio hospedado `Worker`.

```csharp
// Worker/Worker.cs

public class Worker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=== ETL POS iniciando: {time} ===", DateTimeOffset.Now);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Extractores
        var bdExtractor  = sp.GetRequiredService<BdExtractor>();
        var csvExtractor = sp.GetRequiredService<CsvExtractor>();
        var apiExtractor = sp.GetRequiredService<IExtractor<StgOrder>>();

        // Loaders
        var clienteLoader  = sp.GetRequiredService<IDimensionLoader<StgCustomer>>();
        var productoLoader = sp.GetRequiredService<IDimensionLoader<StgProduct>>();
        var fechaLoader    = sp.GetRequiredService<IDimensionLoader<StgOrder>>();
        var paisLoader     = sp.GetRequiredService<IPaisLoader>();

        try
        {
            // EXTRACCIÓN
            var customersDb    = (await bdExtractor.ExtractCustomersAsync()).ToList();
            var ordersDb       = (await bdExtractor.ExtractOrdersAsync()).ToList();
            var orderDetailsDb = (await bdExtractor.ExtractAsync()).ToList();
            var productsDb     = (await bdExtractor.ExtractProductsAsync()).ToList();

            var customersCsv    = (await csvExtractor.ExtractCustomersAsync()).ToList();
            var ordersCsv       = (await csvExtractor.ExtractOrdersAsync()).ToList();
            var orderDetailsCsv = (await csvExtractor.ExtractOrderDetailsAsync()).ToList();
            var productsCsv     = (await csvExtractor.ExtractAsync()).ToList();

            var ordersApi = (await apiExtractor.ExtractAsync()).ToList();

            // Combinar fuentes
            var allCustomers = customersDb.Concat(customersCsv).DistinctBy(c => c.CustomerID).ToList();
            var allProducts  = productsDb.Concat(productsCsv).DistinctBy(p => p.ProductID).ToList();
            var allOrders    = ordersDb.Concat(ordersCsv).Concat(ordersApi).DistinctBy(o => o.OrderID).ToList();

            // CARGA DE DIMENSIONES
            await paisLoader.LoadAsync(allCustomers);
            await clienteLoader.LoadAsync(allCustomers);
            await productoLoader.LoadAsync(allProducts);
            await fechaLoader.LoadAsync(allOrders);

            _logger.LogInformation("=== Extracción y carga de dimensiones completadas ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en el pipeline ETL");
        }
    }
}
```

## Configuración

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=127.0.0.1,1433;Database=DW_Ventas;User Id=sa;Password=Elmejorde1!;TrustServerCertificate=True;Encrypt=False;Connection Timeout=30;"
  },
  "ApiSettings": {
    "BaseUrl": "https://jsonplaceholder.typicode.com"
  },
  "Extraction": {
    "CsvFolder": "Data/Csv"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

### Configuración en Program.cs

```csharp
// Program.cs

var builder = WebApplication.CreateBuilder(args);

// Logging personalizado
var logBuffer = new EtlLogBuffer();
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddProvider(new EtlLoggerProvider(logBuffer));
builder.Services.AddSingleton(logBuffer);

// HTTP Client
builder.Services.AddHttpClient("ApiClient", c => c.Timeout = TimeSpan.FromSeconds(30));

// Servicios
builder.Services.AddScoped<BdExtractor>();
builder.Services.AddScoped<CsvExtractor>();
builder.Services.AddScoped<IExtractor<StgProduct>, CsvExtractor>();
builder.Services.AddScoped<IExtractor<StgOrder>, ApiExtractor>();

builder.Services.AddScoped<IDimensionLoader<StgCustomer>, ClienteLoader>();
builder.Services.AddScoped<IDimensionLoader<StgProduct>, ProductoLoader>();
builder.Services.AddScoped<IDimensionLoader<StgOrder>, FechaLoader>();
builder.Services.AddScoped<FactVentaLoader>();
builder.Services.AddScoped<IPaisLoader, PaisLoader>();

builder.Services.AddHostedService<Worker>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

// Endpoints para logs
app.MapGet("/etl/logs", (EtlLogBuffer b) => Results.Ok(b.GetAll()));
app.MapGet("/etl/stream", async (EtlLogBuffer b, HttpContext ctx) => { ... });

app.Run();
```

## Tecnologías Utilizadas

- **.NET 9.0**: Framework principal
- **ASP.NET Core**: Para la aplicación web y servicios hospedados
- **Dapper**: ORM ligero para SQL Server
- **CsvHelper**: Para lectura de archivos CSV
- **System.Data.SqlClient**: Para conexiones a SQL Server
- **System.Text.Json**: Para deserialización de APIs
- **Microsoft.Extensions.Logging**: Para logging
- **IHttpClientFactory**: Para gestión de HttpClient

## Endpoints de la API

- `GET /etl/logs`: Retorna todos los logs acumulados
- `GET /etl/stream`: Streaming de logs en tiempo real (Server-Sent Events)

## Ejecución

La aplicación se ejecuta como un servicio web. El proceso ETL se inicia automáticamente al arrancar la aplicación a través del `HostedService` `Worker`.

Para ejecutar:
```bash
dotnet run
```

La aplicación estará disponible en `http://localhost:5000` (o el puerto configurado).</content>
<parameter name="filePath">c:\Users\Michael\Desktop\ETLProject\Technical_Documentation.md