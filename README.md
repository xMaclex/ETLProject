# ETLProject - Data Warehouse ETL Pipeline

Un proyecto ETL (Extract, Transform, Load) completo desarrollado en .NET 9 para cargar datos en un data warehouse de ventas (DW_Ventas). El sistema extrae datos de múltiples fuentes (base de datos SQL Server, archivos CSV y API REST), transforma los datos y los carga en dimensiones y hechos del data warehouse.

## 🚀 Características

- **Múltiples fuentes de datos**: SQL Server, archivos CSV y API REST
- **Arquitectura limpia**: Separación en capas Domain, Application e Infrastructure
- **Carga incremental**: Soporte para carga de dimensiones y hechos
- **Interfaz web en tiempo real**: Monitoreo del progreso ETL con Server-Sent Events (SSE)
- **Logging estructurado**: Sistema de logging personalizado con buffer en memoria
- **Gestión de errores**: Manejo robusto de errores y recuperación
- **Configuración flexible**: Settings configurables vía appsettings.json

## 🏗️ Arquitectura

El proyecto sigue los principios de Clean Architecture:

```
ETLProject/
├── Domain/                 # Modelos de negocio y entidades
│   └── Models.cs          # StgOrder, StgProduct, StgCustomer, etc.
├── Application/           # Interfaces y contratos
│   └── Interfaces/
│       ├── DimensionLoader.cs
│       └── Extractor.cs
├── Infrastructure/        # Implementaciones concretas
│   ├── Extractors/        # Extractores de datos
│   │   ├── ApiExtractor.cs
│   │   ├── BdExtractor.cs
│   │   └── CsvExtractor.cs
│   ├── Loaders/           # Loaders de dimensiones y hechos
│   │   ├── ClienteLoader.cs
│   │   ├── FactVentaLoader.cs
│   │   ├── FechaLoader.cs
│   │   ├── PaisLoader.cs
│   │   └── ProductoLoader.cs
│   ├── EtlLogBuffer.cs    # Buffer de logs en memoria
│   └── EtlLogger.cs       # Proveedor de logging personalizado
├── Worker.cs              # Servicio background ETL
├── Program.cs             # Configuración y endpoints web
├── appsettings.json       # Configuraciones
└── wwwroot/
    └── index.html         # Interfaz web
```

## �️ Estructura de Base de Datos

El data warehouse `DW_Ventas` está organizado en tres esquemas:

- **dim**: Tablas de dimensiones (DimCliente, DimProducto, DimFecha, DimPais)
- **fact**: Tablas de hechos (FactVentas)
- **stg**: Tablas de staging para datos crudos (Stg_Customers, Stg_Orders, etc.)

### Dimensiones
- **DimCliente**: Información de clientes con claves surrogate
- **DimProducto**: Catálogo de productos con precios y categorías
- **DimFecha**: Calendario desnormalizado para análisis temporal
- **DimPais**: Países con regiones geográficas

### Hechos
- **FactVentas**: Ventas detalladas con claves foráneas a dimensiones

### Staging
- **Stg_Customers**: Datos crudos de clientes desde CSV
- **Stg_Orders**: Órdenes desde CSV o API
- **Stg_OrderDetails**: Detalles de órdenes
- **Stg_Products**: Productos desde CSV

## �📋 Requisitos

- **.NET 9.0** o superior
- **SQL Server** (local o Docker)
- **Docker** (opcional, para base de datos)
- **Visual Studio Code** o IDE compatible

## 🛠️ Instalación

1. **Clona el repositorio**:
   ```bash
   git clone <repository-url>
   cd ETLProject
   ```

2. **Restaura dependencias**:
   ```bash
   dotnet restore
   ```

3. **Configura la base de datos**:
   - Asegúrate de tener SQL Server corriendo
   - Crea la base de datos `DW_Ventas` si no existe
   - Ejecuta los scripts de creación de tablas (si los tienes)

4. **Configura appsettings.json**:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=localhost;Database=DW_Ventas;User Id=sa;Password=tu_password;TrustServerCertificate=True;"
     },
     "ApiSettings": {
       "BaseUrl": "https://tu-api.com"
     },
     "Extraction": {
       "CsvFolder": "Data/Csv"
     }
   }
   ```

## ⚙️ Configuración

### Base de datos
- **Connection String**: Configura en `appsettings.json` bajo `ConnectionStrings:DefaultConnection`
- **Tablas requeridas** (esquemas dim, fact, stg):
  - `dim.DimPais` - Dimensión de países
  - `dim.DimCliente` - Dimensión de clientes
  - `dim.DimProducto` - Dimensión de productos
  - `dim.DimFecha` - Dimensión de fechas
  - `fact.FactVentas` - Hechos de ventas
  - `stg.Stg_Customers` - Staging de clientes (CSV)
  - `stg.Stg_Orders` - Staging de órdenes (CSV)
  - `stg.Stg_OrderDetails` - Staging de detalles de orden (CSV)
  - `stg.Stg_Products` - Staging de productos (CSV)

### Fuentes de datos
- **CSV**: Archivos en `Data/Csv/` (customers.csv, orders.csv, products.csv)
- **API**: Endpoint REST configurable en `ApiSettings:BaseUrl`
- **BD**: Consultas SQL en `BdExtractor.cs`

### Logging
- Logs se almacenan en memoria y se sirven vía SSE
- Niveles configurables en `appsettings.json`

## 🚀 Uso

### Ejecutar el ETL
```bash
dotnet run
```

El ETL se ejecutará automáticamente al iniciar la aplicación como servicio background.

### Monitoreo web
1. Abre `http://localhost:5000` en tu navegador
2. La interfaz muestra el progreso en tiempo real de las fases ETL
3. Fases incluidas:
   - Extracción BD
   - Extracción CSV
   - Extracción API
   - Carga dimensión País
   - Carga dimensión Cliente
   - Carga dimensión Producto
   - Carga dimensión Fecha
   - Carga hechos Venta

### API Endpoints

- `GET /etl/logs` - Obtiene todos los logs del ETL
- `GET /etl/stream` - Stream de logs en tiempo real (SSE)

## 📁 Estructura detallada

### Domain/Models.cs
Define los modelos de staging y dimensiones:
- `StgOrder` - Órdenes de staging
- `StgProduct` - Productos de staging
- `StgCustomer` - Clientes de staging
- `DimPais`, `DimCliente`, `DimProducto`, `DimFecha` - Dimensiones
- `FactVenta` - Hechos de ventas

### Application/Interfaces/
- `IExtractor<T>` - Interfaz para extractores
- `IDimensionLoader<T>` - Interfaz para loaders de dimensiones

### Infrastructure/Extractors/
- **BdExtractor**: Extrae datos de SQL Server usando Dapper
- **CsvExtractor**: Lee archivos CSV usando CsvHelper
- **ApiExtractor**: Consume API REST y deserializa JSON

### Infrastructure/Loaders/
- **ClienteLoader**: Carga dimensión Cliente con lookup de País
- **ProductoLoader**: Carga dimensión Producto
- **FechaLoader**: Carga dimensión Fecha desde órdenes
- **PaisLoader**: Carga dimensión País
- **FactVentaLoader**: Carga hechos de venta con lookups de dimensiones

### Worker.cs
Servicio background que orquesta el pipeline ETL:
1. Extrae datos de todas las fuentes
2. Carga dimensiones en orden correcto
3. Carga hechos con claves foráneas

### Program.cs
Configura:
- Servicios DI (Dependency Injection)
- HttpClient para API
- Endpoints web
- Logging personalizado

## 🔧 Desarrollo

### Agregar nueva fuente de datos
1. Implementa `IExtractor<T>` en `Infrastructure/Extractors/`
2. Registra en `Program.cs`
3. Agrega al pipeline en `Worker.cs`

### Agregar nueva dimensión
1. Define modelo en `Domain/Models.cs`
2. Implementa `IDimensionLoader<T>` en `Infrastructure/Loaders/`
3. Registra en `Program.cs`
4. Agrega al pipeline en `Worker.cs`

### Debugging
- Logs se muestran en consola y web
- Usa `ILogger` para logging estructurado
- El buffer de logs permite inspección histórica

## 📊 Pipeline ETL

```
Extracción ──┬─ BD ──────┬─ DimPais
            ├─ CSV ────┼─ DimCliente
            └─ API ────┴─ DimProducto
                       ─ DimFecha
                       ─ FactVenta
```

## 🐛 Troubleshooting

### Error 404 en API
- Verifica `ApiSettings:BaseUrl` en `appsettings.json`
- Asegúrate de que el endpoint `/orders` exista

### Error de conexión BD
- Verifica connection string
- Asegúrate de que SQL Server esté corriendo

### Archivos CSV no encontrados
- Verifica ruta en `Extraction:CsvFolder`
- Archivos requeridos: customers.csv, orders.csv, products.csv

### Build bloqueado
- Detén procesos `ETLProject.exe` corriendo
- Usa `dotnet clean` si es necesario

## 🤝 Contribución

1. Fork el proyecto
2. Crea una rama para tu feature (`git checkout -b feature/nueva-funcionalidad`)
3. Commit tus cambios (`git commit -am 'Agrega nueva funcionalidad'`)
4. Push a la rama (`git push origin feature/nueva-funcionalidad`)
5. Abre un Pull Request

## 📄 Licencia

Este proyecto está bajo la Licencia MIT. Ver el archivo `LICENSE` para más detalles.

## 📞 Soporte

Para soporte o preguntas, abre un issue en el repositorio o contacta al equipo de desarrollo.