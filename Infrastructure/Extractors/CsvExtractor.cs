using System.Globalization;
using CsvHelper;


public class CsvExtractor : IExtractor
{
    private readonly string _filePath;

    public CsvExtractor(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<IEnumerable<dynamic>> ExtractAsync()
    {
        var allData = new List<dynamic>();

        var files = Directory.GetFiles(_filePath, "*.csv");

        foreach (var file in files)
        {
            using var reader = new StreamReader(file);
            using var csv = new CsvHelper.CsvReader(reader, CultureInfo.InvariantCulture);

            var records = csv.GetRecords<dynamic>().ToList();
            allData.AddRange(records);
        }

       return await Task.FromResult(allData);

    }
}