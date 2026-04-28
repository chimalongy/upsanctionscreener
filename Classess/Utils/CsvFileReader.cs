using System;
using System.Data;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Http;

namespace Upsanctionscreener.Classess.Utils
{
    // RESULT MODEL
    public class CsvScanResult
    {
        public bool Success { get; set; }
        public DataTable? Data { get; set; }
        public string? Error { get; set; }
    }

    // CSV READER CLASS
    public class CsvFileReader
    {
        // READ FROM IFORMFILE
        public CsvScanResult ReadCsvFile(
            IFormFile file,
            string idColumnName,
            string scanColumnName,
            bool generateId = false)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return new CsvScanResult { Success = false, Error = "No CSV file uploaded." };

                using var stream = file.OpenReadStream();
                using var reader = new StreamReader(stream);

                return ProcessCsv(reader, idColumnName, scanColumnName, generateId);
            }
            catch (Exception ex)
            {
                return new CsvScanResult { Success = false, Error = ex.Message };
            }
        }

        // READ FROM FILE PATH
        public CsvScanResult ReadCsvFileFromPath(
            string filePath,
            string idColumnName,
            string scanColumnName,
            bool generateId = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return new CsvScanResult { Success = false, Error = "File path is required." };

                if (!File.Exists(filePath))
                    return new CsvScanResult { Success = false, Error = "File does not exist." };

                using var reader = new StreamReader(filePath);

                return ProcessCsv(reader, idColumnName, scanColumnName, generateId);
            }
            catch (Exception ex)
            {
                return new CsvScanResult { Success = false, Error = ex.Message };
            }
        }

        // SHARED LOGIC
        private CsvScanResult ProcessCsv(
            StreamReader reader,
            string idColumnName,
            string scanColumnName,
            bool generateId)
        {
            if (string.IsNullOrWhiteSpace(scanColumnName))
                return new CsvScanResult { Success = false, Error = "scanColumn is required." };

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

            csv.Read();
            csv.ReadHeader();

            var headers = csv.HeaderRecord;

            if (headers == null || headers.Length == 0)
                return new CsvScanResult { Success = false, Error = "CSV file has no headers." };

            bool hasIdColumn = Array.Exists(headers, h =>
                h.Equals(idColumnName, StringComparison.OrdinalIgnoreCase));

            bool hasScanColumn = Array.Exists(headers, h =>
                h.Equals(scanColumnName, StringComparison.OrdinalIgnoreCase));

            if (!hasScanColumn)
                return new CsvScanResult
                {
                    Success = false,
                    Error = $"CSV file does not contain column '{scanColumnName}'."
                };

            if (!generateId && !hasIdColumn)
                return new CsvScanResult
                {
                    Success = false,
                    Error = $"CSV file does not contain column '{idColumnName}'."
                };

            DataTable dt = new DataTable("ScanData");

            if (generateId)
            {
                dt.Columns.Add("ID", typeof(int));
            }
            else
            {
                dt.Columns.Add(idColumnName, typeof(string));
            }

            dt.Columns.Add(scanColumnName, typeof(string));

            int autoId = 1;

            while (csv.Read())
            {
                var scanValue = csv.GetField(scanColumnName)?.Trim();

                if (string.IsNullOrWhiteSpace(scanValue))
                    continue;

                DataRow row = dt.NewRow();

                if (generateId)
                {
                    row["ID"] = autoId++;
                }
                else
                {
                    var idValue = csv.GetField(idColumnName)?.Trim();
                    row[idColumnName] = idValue;
                }

                row[scanColumnName] = scanValue;

                dt.Rows.Add(row);
            }

            return new CsvScanResult
            {
                Success = true,
                Data = dt
            };
        }
    }
}