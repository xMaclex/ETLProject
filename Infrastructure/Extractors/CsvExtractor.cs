using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using ETLProject.Application.Interfaces;
using ETLProject.Domain;
// using Microsoft.Extensions.Configuration;
// using Microsoft.Extensions.Logging;

public class CsvExtractor : IExtractor<StgProduct>
{
    private readonly string _FolderPath;
    private readonly ILogger<CsvExtractor> _logger;

    public CsvExtractor(IConfiguration config, ILogger<CsvExtractor> logger)
    {
        _FolderPath = config["Extraction:CsvFolder"]!;
        _logger = logger;
    }

    public async Task<IEnumerable<StgProduct>> ExtractAsync()
    {
        var allData = new List<StgProduct>();

        var files = Directory.GetFiles(_FolderPath, "*.csv");

        _logger.LogInformation("Encontrados {count} archivos CSV en la carpeta {folder}.", files.Length, _FolderPath);

        foreach (var file in files)
        {
            using var reader = new StreamReader(file);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null
            });

            var records = csv.GetRecords<StgProduct>().ToList();
            allData.AddRange(records);
            _logger.LogInformation("CSV: {n} procesado con {File} registros.", records.Count, Path.GetFileName(file));
        }
        
       return await Task.FromResult(allData);
    }
}