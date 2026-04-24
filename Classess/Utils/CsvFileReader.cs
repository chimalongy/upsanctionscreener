using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace Upsanctionscreener.Classess.Utils
{
    public class CsvScanResult
    {
        public bool Success { get; set; }
        public DataTable? Data { get; set; }
        public string? Error { get; set; }
    }

    public class CsvFileReader
    {
        public CsvScanResult ReadCsvFile(IFormFile file, string idColumnName, string scanColumnName)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return new CsvScanResult { Success = false, Error = "No CSV file uploaded." };

                if (string.IsNullOrWhiteSpace(scanColumnName))
                    return new CsvScanResult { Success = false, Error = "scanColumn is required." };

                if (string.IsNullOrWhiteSpace(idColumnName))
                    return new CsvScanResult { Success = false, Error = "idColumn is required." };

                using var stream = file.OpenReadStream();
                using var reader = new StreamReader(stream);

                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    IgnoreBlankLines = true,
                    BadDataFound = null,
                    MissingFieldFound = null,
                    HeaderValidated = null,
                    TrimOptions = TrimOptions.Trim
                };

                using var csv = new CsvReader(reader, config);

                // Read header
                csv.Read();
                csv.ReadHeader();

                var headers = csv.HeaderRecord;

                if (headers == null || headers.Length == 0)
                    return new CsvScanResult { Success = false, Error = "CSV file has no headers." };

                bool hasIdColumn = Array.Exists(headers, h => h.Equals(idColumnName, StringComparison.OrdinalIgnoreCase));
                bool hasScanColumn = Array.Exists(headers, h => h.Equals(scanColumnName, StringComparison.OrdinalIgnoreCase));

                if (!hasIdColumn)
                    return new CsvScanResult { Success = false, Error = $"CSV file does not contain column '{idColumnName}'." };

                if (!hasScanColumn)
                    return new CsvScanResult { Success = false, Error = $"CSV file does not contain column '{scanColumnName}'." };

                // Create output datatable
                DataTable dt = new DataTable("ScanData");
                dt.Columns.Add(idColumnName, typeof(string));
                dt.Columns.Add(scanColumnName, typeof(string));

                while (csv.Read())
                {
                    var idValue = csv.GetField(idColumnName)?.Trim();
                    var scanValue = csv.GetField(scanColumnName)?.Trim();

                    if (string.IsNullOrWhiteSpace(idValue) && string.IsNullOrWhiteSpace(scanValue))
                        continue;

                    var row = dt.NewRow();
                    row[idColumnName] = idValue;
                    row[scanColumnName] = scanValue;

                    dt.Rows.Add(row);
                }

                return new CsvScanResult
                {
                    Success = true,
                    Data = dt
                };
            }
            catch (Exception ex)
            {
                return new CsvScanResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }
    }
}
