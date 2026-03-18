namespace ETLProject.Domain;

// Staging

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
    // Stock no existe en stg.Stg_Products — se ignora al leer el CSV
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
    public int    OrderDetailID { get; set; }  // existe en stg, no en CSV → se autogenera
    public int    OrderID       { get; set; }
    public int    ProductID     { get; set; }
    public string Quantity      { get; set; } = string.Empty;
    public string UnitPrice     { get; set; } = string.Empty;
    // TotalPrice del CSV no existe en stg — se ignora
}

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