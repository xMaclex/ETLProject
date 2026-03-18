namespace ETLProject.Domain;

// Starging

public record StgCustomer(
    int    CustomerID,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    string City,
    string Country
);

public record StgOrder(
    int    OrderID,
    int    CustomerID,
    string OrderDate,       
    string StatusOrder
);

public record StgOrderDetail(
    int    OrderDetailID,
    int    OrderID,
    int    ProductID,
    string Quantity,        
    string UnitPrice        
);

public record StgProduct(
    int    ProductID,
    string ProductName,
    string Category,
    string Price          
);

// Dimenciones

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

// Tabla Fact

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