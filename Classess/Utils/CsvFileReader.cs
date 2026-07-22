using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Generic;
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
        // =========================
        // READ FROM IFORMFILE — single scan column (legacy / unused by MultiScan doc uploads now,
        // kept for backward compatibility with any other callers)
        // =========================
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

        // =========================
        // READ FROM FILE PATH — single scan column (legacy)
        // =========================
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

        // =========================
        // SHARED LOGIC — single scan column (legacy)
        // =========================
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

        // =========================
        // READ FROM FILE PATH — target-style, multiple mapped columns + JSON support
        // Mirrors ExcelMultiSheetReader.ReadTargetExcelFile so MultiScan document
        // uploads use the exact same Field Mapping shape as Target Scans.
        // =========================
        public CsvScanResult ReadTargetCsvFile(
            string filePath,
            string idColumnName,
            List<FieldMapping> otherFields,
            bool generateId = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return new CsvScanResult { Success = false, Error = "File path is required." };

                if (!File.Exists(filePath))
                    return new CsvScanResult { Success = false, Error = "File does not exist." };

                using var reader = new StreamReader(filePath);

                return ProcessTargetCsv(reader, idColumnName, otherFields, generateId);
            }
            catch (Exception ex)
            {
                return new CsvScanResult { Success = false, Error = ex.Message };
            }
        }

        // =========================
        // READ FROM IFORMFILE — target-style, multiple mapped columns + JSON support
        // =========================
        public CsvScanResult ReadTargetCsvFile(
            IFormFile file,
            string idColumnName,
            List<FieldMapping> otherFields,
            bool generateId = false)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return new CsvScanResult { Success = false, Error = "No CSV file uploaded." };

                using var stream = file.OpenReadStream();
                using var reader = new StreamReader(stream);

                return ProcessTargetCsv(reader, idColumnName, otherFields, generateId);
            }
            catch (Exception ex)
            {
                return new CsvScanResult { Success = false, Error = ex.Message };
            }
        }

        // =========================
        // SHARED LOGIC — target-style, multiple mapped columns + JSON support
        // =========================
        private CsvScanResult ProcessTargetCsv(
            StreamReader reader,
            string idColumnName,
            List<FieldMapping> otherFields,
            bool generateId)
        {
            if (otherFields is null || otherFields.Count == 0)
                return new CsvScanResult { Success = false, Error = "At least one field mapping is required." };

            if (!generateId && string.IsNullOrWhiteSpace(idColumnName))
                return new CsvScanResult { Success = false, Error = "idColumn is required when autoGenerateId is false." };

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

            bool hasIdColumn = !generateId && Array.Exists(headers, h =>
                h.Equals(idColumnName, StringComparison.OrdinalIgnoreCase));

            if (!generateId && !hasIdColumn)
                return new CsvScanResult
                {
                    Success = false,
                    Error = $"CSV file does not contain column '{idColumnName}'."
                };

            // Resolve each mapped column against the actual header casing on disk,
            // so we don't depend on the user having typed the exact case.
            var resolvedColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var missingFields = new List<string>();

            foreach (var field in otherFields)
            {
                var match = Array.Find(headers, h => h.Equals(field.ColumnName, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    missingFields.Add(field.ColumnName);
                else
                    resolvedColumns[field.ColumnName] = match;
            }

            if (missingFields.Count > 0)
                return new CsvScanResult
                {
                    Success = false,
                    Error = $"CSV file is missing required column(s): {string.Join(", ", missingFields)}."
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

            foreach (var field in otherFields)
            {
                if (!dt.Columns.Contains(field.ColumnName))
                    dt.Columns.Add(field.ColumnName, typeof(string));
            }

            int autoId = 1;

            while (csv.Read())
            {
                // Skip rows where ID column is empty (when not auto-generating)
                if (!generateId)
                {
                    var idValue = csv.GetField(idColumnName)?.Trim();
                    if (string.IsNullOrWhiteSpace(idValue))
                        continue;
                }

                // Skip rows where ALL mapped fields are empty
                bool allOtherFieldsEmpty = otherFields.All(f =>
                    string.IsNullOrWhiteSpace(csv.GetField(resolvedColumns[f.ColumnName])?.Trim()));
                if (allOtherFieldsEmpty)
                    continue;

                DataRow row = dt.NewRow();

                if (generateId)
                {
                    row["ID"] = autoId++;
                }
                else
                {
                    row[idColumnName] = csv.GetField(idColumnName)?.Trim();
                }

                foreach (var field in otherFields)
                {
                    row[field.ColumnName] = csv.GetField(resolvedColumns[field.ColumnName])?.Trim();
                }

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