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

#### FactVentaLoader

Carga los hechos (fact.FactVentas) después de que todas las dimensiones estén cargadas. Este es un loader especializado que realiza transformaciones complejas: resuelve claves surrogate, valida la integridad referencial y optimiza la carga con bulk inserts.

```csharp
// Infrastructure/Extractors/Loaders/FactVentaLoader.cs

public class FactVentaLoader
{
    private readonly string _conn;
    private readonly ILogger<FactVentaLoader> _logger;

    public FactVentaLoader(IConfiguration config, ILogger<FactVentaLoader> logger)
    {
        _conn   = config.GetConnectionString("DefaultConnection")!;
        _logger = logger;
    }

    public async Task LoadAsync(
        IEnumerable<StgOrder>       orders,
        IEnumerable<StgOrderDetail> orderDetails,
        IEnumerable<StgCustomer>    customers,
        IEnumerable<StgProduct>     products)
    {
        _logger.LogInformation("FactVenta: comenzando carga de hechos...");

        // ── 1. MATERIALIZARE LISTAS EN MEMORIA ────────────────────────────────
        var orderList       = orders.ToList();
        var orderDetailList = orderDetails.ToList();
        var customerList    = customers.ToList();
        var productList     = products.ToList();

        // ── 2. DICCIONARIOS DE LOOKUP ─────────────────────────────────────────
        // Performance O(1) para búsquedas en vez de O(n)
        var orderLookup    = orderList.ToDictionary(o => o.OrderID, o => o);
        var customerLookup = customerList.ToDictionary(c => c.CustomerID, c => c);
        var productLookup  = productList.ToDictionary(p => p.ProductID, p => p);

        // ── 3. CONSTRUIR REGISTROS DE HECHOS EN MEMORIA ────────────────────────
        // Se cruzan OrderDetails con Orders, Customers y Products
        var factRows = new List<FactRowStaging>();

        foreach (var od in orderDetailList)
        {
            // Validación referencial
            if (!orderLookup.TryGetValue(od.OrderID, out var order)) continue;
            if (!customerLookup.ContainsKey(order.CustomerID)) continue;
            if (!productLookup.ContainsKey(od.ProductID)) continue;

            var customer = customerLookup[order.CustomerID];

            // Parseo de string a tipos numéricos
            int.TryParse(od.Quantity, out var cantidad);
            decimal.TryParse(od.UnitPrice,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var precioUnitario);

            // Cálculo de FechaKey (formato YYYYMMDD como entero)
            int fechaKey = 0;
            if (DateTime.TryParse(order.OrderDate, out var fecha))
                fechaKey = int.Parse(fecha.ToString("yyyyMMdd"));

            factRows.Add(new FactRowStaging
            {
                FechaKey       = fechaKey,
                ClienteID      = order.CustomerID,
                ProductoID     = od.ProductID,
                PaisNombre     = customer.Country.Trim(),
                Cantidad       = cantidad,
                PrecioUnitario = precioUnitario,
                IngresoTotal   = cantidad * precioUnitario,
                NumeroOrden    = order.OrderID
            });
        }

        _logger.LogInformation("FactVenta: {n} filas de hechos construidas en memoria", factRows.Count);

        if (factRows.Count == 0)
        {
            _logger.LogInformation("FactVenta: no hay hechos para cargar");
            return;
        }

        // ── 4. RESOLVER CLAVES SURROGATE DESDE BASE DE DATOS ──────────────────
        // Consultamos las dimensiones ya cargadas para obtener las claves surrogate
        using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        _logger.LogInformation("FactVenta: leyendo claves surrogate de dimensiones...");

        var clienteKeys = (await conn.QueryAsync<(int Key, int ID)>(
            "SELECT ClienteKey, ClienteID FROM dim.DimCliente"))
            .ToDictionary(x => x.ID, x => x.Key);

        var productoKeys = (await conn.QueryAsync<(int Key, int ID)>(
            "SELECT ProductoKey, ProductoID FROM dim.DimProducto"))
            .ToDictionary(x => x.ID, x => x.Key);

        var paisKeys = (await conn.QueryAsync<(int Key, string Nombre)>(
            "SELECT PaisKey, NombrePais FROM dim.DimPais"))
            .ToDictionary(x => x.Nombre, x => x.Key, StringComparer.OrdinalIgnoreCase);

        // ── 5. CREAR DATATABLE CON CLAVES SURROGATE ──────────────────────────
        var dt = new DataTable();
        dt.Columns.Add("FechaKey",       typeof(int));
        dt.Columns.Add("ClienteKey",     typeof(int));
        dt.Columns.Add("ProductoKey",    typeof(int));
        dt.Columns.Add("PaisKey",        typeof(int));
        dt.Columns.Add("Cantidad",       typeof(int));
        dt.Columns.Add("PrecioUnitario", typeof(decimal));
        dt.Columns.Add("IngresoTotal",   typeof(decimal));
        dt.Columns.Add("NumeroOrden",    typeof(int));

        int sinCliente = 0, sinProducto = 0, sinPais = 0;

        foreach (var row in factRows)
        {
            var ck = clienteKeys.TryGetValue(row.ClienteID, out var ck_val) ? ck_val : 0;
            var pk = productoKeys.TryGetValue(row.ProductoID, out var pk_val) ? pk_val : 0;
            var psk = paisKeys.TryGetValue(row.PaisNombre, out var psk_val) ? psk_val : 0;

            if (ck == 0)  sinCliente++;
            if (pk == 0)  sinProducto++;
            if (psk == 0) sinPais++;

            dt.Rows.Add(row.FechaKey, ck, pk, psk,
                        row.Cantidad, row.PrecioUnitario,
                        row.IngresoTotal, row.NumeroOrden);
        }

        if (sinCliente  > 0) _logger.LogWarning("FactVenta: {n} filas sin ClienteKey", sinCliente);
        if (sinProducto > 0) _logger.LogWarning("FactVenta: {n} filas sin ProductoKey", sinProducto);
        if (sinPais     > 0) _logger.LogWarning("FactVenta: {n} filas sin PaisKey", sinPais);

        // ── 6. TRUNCATE (más rápido que DELETE) ──────────────────────────────
        _logger.LogInformation("FactVenta: limpiando tabla fact.FactVentas (TRUNCATE)...");
        await conn.ExecuteAsync("TRUNCATE TABLE fact.FactVentas");

        // ── 7. TABLA TEMPORAL + BULK INSERT ──────────────────────────────────
        await conn.ExecuteAsync("""
            CREATE TABLE #TempFactVentas (
                FechaKey       INT,
                ClienteKey     INT,
                ProductoKey    INT,
                PaisKey        INT,
                Cantidad       INT,
                PrecioUnitario DECIMAL(18,2),
                IngresoTotal   DECIMAL(18,2),
                NumeroOrden    INT
            )
            """);

        using (var bulk = new SqlBulkCopy(conn))
        {
            bulk.DestinationTableName = "#TempFactVentas";
            bulk.BulkCopyTimeout      = 120;
            await bulk.WriteToServerAsync(dt);
        }

        _logger.LogInformation("FactVenta: {n} filas en temporal — insertando...", dt.Rows.Count);

        // ── 8. INSERT FINAL CON VALIDACIÓN ──────────────────────────────────
        // Solo inserta filas con claves válidas (> 0)
        const string insertSql = """
            INSERT INTO fact.FactVentas (
                FechaKey, ClienteKey, ProductoKey, PaisKey,
                Cantidad, PrecioUnitario, IngresoTotal, NumeroOrden
            )
            SELECT
                FechaKey, ClienteKey, ProductoKey, PaisKey,
                Cantidad, PrecioUnitario, IngresoTotal, NumeroOrden
            FROM #TempFactVentas
            WHERE ClienteKey  > 0
              AND ProductoKey > 0
              AND PaisKey     > 0
              AND FechaKey    > 0;
            """;

        var inserted = await conn.ExecuteAsync(insertSql);
        _logger.LogInformation("FactVenta: {n} registros insertados exitosamente", inserted);

        int omitidas = dt.Rows.Count - inserted;
        if (omitidas > 0)
            _logger.LogWarning("FactVenta: {n} filas omitidas por claves inválidas", omitidas);
    }

    private sealed class FactRowStaging
    {
        public int     FechaKey       { get; init; }
        public int     ClienteID      { get; init; }
        public int     ProductoID     { get; init; }
        public string  PaisNombre     { get; init; } = string.Empty;
        public int     Cantidad       { get; init; }
        public decimal PrecioUnitario { get; init; }
        public decimal IngresoTotal   { get; init; }
        public int     NumeroOrden    { get; init; }
    }
}
```

**Puntos clave de FactVentaLoader:**

- **Diccionarios de lookup**: Utilizados para resoluciones O(1) en lugar de O(n). Crítico para rendimiento con miles de registros.
- **Validación referencial**: Verifica que existan los padres (Orden, Cliente, Producto) antes de crear el hecho.
- **Parseo de datos**: Convierte strings a int/decimal con manejo de errores.
- **Resolución de claves surrogate**: Consulta la BD para obtener las claves de las dimensiones ya cargadas.
- **DataTable + SqlBulkCopy**: Inserta los datos en una tabla temporal para luego hacer INSERT masivo, optimizando velocidad.
- **TRUNCATE**: Más rápido que DELETE porque no registra fila por fila en el log de transacciones.
- **Validación en INSERT**: Solo inserta filas con todas las claves válidas (> 0), omitiendo registros huérfanos.

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

El proceso ETL se ejecuta en el servicio hospedado `Worker` y se divide en **3 fases claramente definidas**: Extracción, Carga de Dimensiones y Carga de Hechos. El orden es crítico porque las claves surrogate de las dimensiones deben existir antes de insertar los hechos.

### Orden de Ejecución

```
Extracción (3 fuentes) → Combinar/Deduplicar → Cargar Dimensiones (orden específico) → Cargar Hechos
```

**Orden de carga de dimensiones importante:**
1. **DimPais** (primero, porque DimCliente la referencia)
2. **DimCliente** (referencia DimPais)
3. **DimProducto** (independiente)
4. **DimFecha** (independiente)
5. **FactVentas** (último, requiere todas las claves surrogate)

### Código del Worker

```csharp
// Worker/Worker.cs

public class Worker : BackgroundService
{
    private readonly ILogger<Worker>      _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=== ETL POS iniciando: {time} ===", DateTimeOffset.Now);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // ── Extractores ──────────────────────────────────────────────────────
        var bdExtractor  = sp.GetRequiredService<BdExtractor>();
        var csvExtractor = sp.GetRequiredService<CsvExtractor>();
        var apiExtractor = sp.GetRequiredService<IExtractor<StgOrder>>();

        // ── Loaders de dimensiones (se deben cargar ANTES de los hechos) ─────
        var clienteLoader  = sp.GetRequiredService<IDimensionLoader<StgCustomer>>();
        var productoLoader = sp.GetRequiredService<IDimensionLoader<StgProduct>>();
        var fechaLoader    = sp.GetRequiredService<IDimensionLoader<StgOrder>>();
        var paisLoader     = sp.GetRequiredService<IPaisLoader>();

        // ── Loader de hechos (siempre al final) ──────────────────────────────
        var factVentaLoader = sp.GetRequiredService<FactVentaLoader>();

        try
        {
            // ════════════════════════════════════════════════════════════════
            // FASE 1 — EXTRACCIÓN
            // ════════════════════════════════════════════════════════════════
            _logger.LogInformation("Extrayendo desde SQL Server...");
            var customersDb    = (await bdExtractor.ExtractCustomersAsync()).ToList();
            var ordersDb       = (await bdExtractor.ExtractOrdersAsync()).ToList();
            var orderDetailsDb = (await bdExtractor.ExtractAsync()).ToList();
            var productsDb     = (await bdExtractor.ExtractProductsAsync()).ToList();
            _logger.LogInformation("BD | Customers: {c} | Orders: {o} | Details: {d} | Products: {p}",
                customersDb.Count, ordersDb.Count, orderDetailsDb.Count, productsDb.Count);

            _logger.LogInformation("Extrayendo desde CSV...");
            var customersCsv    = (await csvExtractor.ExtractCustomersAsync()).ToList();
            var ordersCsv       = (await csvExtractor.ExtractOrdersAsync()).ToList();
            var orderDetailsCsv = (await csvExtractor.ExtractOrderDetailsAsync()).ToList();
            var productsCsv     = (await csvExtractor.ExtractAsync()).ToList();
            _logger.LogInformation("CSV | Customers: {c} | Orders: {o} | Details: {d} | Products: {p}",
                customersCsv.Count, ordersCsv.Count, orderDetailsCsv.Count, productsCsv.Count);

            var ordersApi = (await apiExtractor.ExtractAsync()).ToList();
            _logger.LogInformation("API | Órdenes: {n}", ordersApi.Count);

            // ── Combinar y deduplicar por clave natural ───────────────────
            var allCustomers = customersDb
                .Concat(customersCsv)
                .DistinctBy(c => c.CustomerID)
                .ToList();

            var allProducts = productsDb
                .Concat(productsCsv)
                .DistinctBy(p => p.ProductID)
                .ToList();

            var allOrders = ordersDb
                .Concat(ordersCsv)
                .Concat(ordersApi)
                .DistinctBy(o => o.OrderID)
                .ToList();

            var allOrderDetails = orderDetailsDb
                .Concat(orderDetailsCsv)
                .DistinctBy(od => new { od.OrderID, od.ProductID, od.Quantity, od.UnitPrice })
                .ToList();

            // ════════════════════════════════════════════════════════════════
            // FASE 2 — CARGA DE DIMENSIONES
            // ════════════════════════════════════════════════════════════════
            _logger.LogInformation("Cargando dimensiones...");

            await paisLoader.LoadAsync(allCustomers);       // 1°: DimPais
            await clienteLoader.LoadAsync(allCustomers);    // 2°: DimCliente
            await productoLoader.LoadAsync(allProducts);    // 3°: DimProducto
            await fechaLoader.LoadAsync(allOrders);         // 4°: DimFecha

            _logger.LogInformation("Dimensiones cargadas. Iniciando carga de hechos...");

            // ════════════════════════════════════════════════════════════════
            // FASE 3 — CARGA DE HECHOS
            // ════════════════════════════════════════════════════════════════
            await factVentaLoader.LoadAsync(
                allOrders,
                allOrderDetails,
                allCustomers,
                allProducts);

            _logger.LogInformation(
                "=== Extracción, carga de dimensiones y hechos completadas: {time} ===",
                DateTimeOffset.Now);
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

## Flujo Completo del ETL (3 Fases)

### Fase 1: Extracción
**Responsables:** `BdExtractor`, `CsvExtractor`, `ApiExtractor`

1. **BdExtractor**: Extrae datos de SQL Server
   - Clientes, Órdenes, Detalles de Órdenes, Productos

2. **CsvExtractor**: Extrae datos de archivos CSV
   - Clientes, Órdenes, Detalles de Órdenes, Productos

3. **ApiExtractor**: Consume datos de REST API
   - Órdenes desde endpoint externo

**Proceso de combinación:**
- Se concatenan datos de múltiples fuentes
- Se dедупликан por clave natural (`DistinctBy`)
- Para detalles se usa clave compuesta: `{OrderID, ProductID, Quantity, UnitPrice}`

**Resultado:** Listas consolidadas de clientes, productos, órdenes y detalles listos para cargar.

### Fase 2: Carga de Dimensiones
**Responsables:** `PaisLoader`, `ClienteLoader`, `ProductoLoader`, `FechaLoader`

**Patrón común para todos los dimension loaders:**
1. Crear `DataTable` en memoria con los datos
2. Abrir conexión a BD
3. Crear tabla temporal `#TempXxx`
4. Bulk insert a tabla temporal
5. Ejecutar `MERGE` desde temporal a tabla final (INSERT + UPDATE con deduplicación)

**Orden obligatorio:**
```
DimPais (base) 
  ↓
DimCliente (referencia Pais)
  ↓
DimProducto (independiente)
  ↓
DimFecha (independiente)
```

**Importante:** Las claves naturales (`ClienteID`, `ProductoID`, etc.) se transforman en claves surrogate (`ClienteKey`, `ProductoKey`, etc.) **dentro de la BD**. El Worker solo usa claves naturales; los loaders las resuelven al guardar.

### Fase 3: Carga de Hechos
**Responsable:** `FactVentaLoader`

Proceso más complejo porque requiere:

1. **Construcción en memoria:**
   - Cruza `OrderDetails` con `Orders`, `Customers`, `Products`
   - Usa diccionarios lookup para O(1)
   - Valida que existan los padres
   - Parsea tipos de string a números
   - Calcula `FechaKey` en formato YYYYMMDD

2. **Resolución de claves surrogate:**
   - Consulta `DIM.DimCliente`, `DIM.DimProducto`, `DIM.DimPais`
   - Obtiene sus claves usando diccionarios (también O(1))

3. **Carga optimizada:**
   - Crea tabla temporal
   - Usa `SqlBulkCopy` (muy rápido)
   - Ejecuta `INSERT` validando que todas las claves sean > 0
   - Omite filas huérfanas (sin referencias válidas en dimensiones)

**Validaciones:**
- Si un `ClienteKey = 0` → fila omitida
- Si un `ProductoKey = 0` → fila omitida
- Si un `PaisKey = 0` → fila omitida
- Si un `FechaKey = 0` → fila omitida

Se registran logs detallados de cuántas filas fallaron en cada validación.

## Ejecución

La aplicación se ejecuta como un servicio web. El proceso ETL se inicia automáticamente al arrancar la aplicación a través del `HostedService` `Worker`.

Para ejecutar:
```bash
dotnet run
```

La aplicación estará disponible en `http://localhost:5000` (o el puerto configurado).</content>
<parameter name="filePath">c:\Users\Michael\Desktop\ETLProject\Technical_Documentation.md