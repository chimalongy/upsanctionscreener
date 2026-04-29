using ExcelDataReader;
using System;
using System.Data;
using System.IO;
using Microsoft.AspNetCore.Http;

namespace Upsanctionscreener.Classess.Utils
{
    public class ExcelReadResult
    {
        public bool Success { get; set; }
        public DataTable? Data { get; set; }
        public string? Error { get; set; }
    }

    public class ExcelMultiSheetReader
    {
        // =========================
        // READ FROM IFORMFILE
        // =========================
        public ExcelReadResult ReadExcelFile(
            IFormFile file,
            string idColumnName,
            string scanColumnName,
            bool generateId = false)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return new ExcelReadResult { Success = false, Error = "No Excel file uploaded." };

                using var stream = file.OpenReadStream();

                return ProcessExcel(stream, idColumnName, scanColumnName, generateId);
            }
            catch (Exception ex)
            {
                return new ExcelReadResult { Success = false, Error = ex.Message };
            }
        }

        // =========================
        // READ FROM FILE PATH
        // =========================
        public ExcelReadResult ReadExcelFromPath(
            string filePath,
            string idColumnName,
            string scanColumnName,
            bool generateId = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return new ExcelReadResult { Success = false, Error = "File path is required." };

                if (!File.Exists(filePath))
                    return new ExcelReadResult { Success = false, Error = "File does not exist." };

                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read);

                return ProcessExcel(stream, idColumnName, scanColumnName, generateId);
            }
            catch (Exception ex)
            {
                return new ExcelReadResult { Success = false, Error = ex.Message };
            }
        }

        // =========================
        // SHARED LOGIC
        // =========================
        private ExcelReadResult ProcessExcel(
            Stream stream,
            string idColumnName,
            string scanColumnName,
            bool generateId)
        {
            bool fetchAllColumns = string.IsNullOrWhiteSpace(scanColumnName);

            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            using var reader = ExcelReaderFactory.CreateReader(stream);

            var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration()
            {
                ConfigureDataTable = (_) => new ExcelDataTableConfiguration()
                {
                    UseHeaderRow = true
                }
            });

            if (dataSet == null || dataSet.Tables.Count == 0)
                return new ExcelReadResult { Success = false, Error = "Excel file contains no sheets." };

            // We'll build the combined table after we inspect the first valid sheet
            DataTable? combinedTable = null;
            bool foundValidSheet = false;
            int autoId = 1;

            foreach (DataTable sheet in dataSet.Tables)
            {
                if (sheet.Columns.Count == 0)
                    continue;

                bool hasIdColumn = !generateId && sheet.Columns.Contains(idColumnName);

                // Always enforce ID column presence when not auto-generating
                if (!generateId && !hasIdColumn)
                    continue;

                // When a specific scan column is requested, enforce its existence too
                if (!fetchAllColumns && !sheet.Columns.Contains(scanColumnName))
                    continue;

                // First valid sheet — build the combined table schema
                if (combinedTable == null)
                {
                    combinedTable = fetchAllColumns
                        ? CreateCombinedTableAllColumns(idColumnName, sheet, generateId)
                        : CreateCombinedTable(idColumnName, scanColumnName, generateId);
                }

                foundValidSheet = true;

                foreach (DataRow row in sheet.Rows)
                {
                    // When fetching a specific column, skip rows where that column is empty
                    if (!fetchAllColumns)
                    {
                        var scanValue = row[scanColumnName]?.ToString()?.Trim();
                        if (string.IsNullOrWhiteSpace(scanValue))
                            continue;
                    }
                    else
                    {
                        // For all-columns mode, skip rows that are entirely empty
                        bool rowIsEmpty = true;
                        foreach (DataColumn col in sheet.Columns)
                        {
                            if (!string.IsNullOrWhiteSpace(row[col]?.ToString()))
                            {
                                rowIsEmpty = false;
                                break;
                            }
                        }
                        if (rowIsEmpty) continue;
                    }

                    DataRow newRow = combinedTable.NewRow();
                    newRow["SheetName"] = sheet.TableName;

                    if (generateId)
                    {
                        newRow["ID"] = autoId++;
                    }
                    else
                    {
                        newRow[idColumnName] = row[idColumnName]?.ToString()?.Trim();
                    }

                    if (fetchAllColumns)
                    {
                        // Copy every column except the ID (already handled above)
                        foreach (DataColumn col in sheet.Columns)
                        {
                            if (!generateId && col.ColumnName == idColumnName)
                                continue;

                            if (combinedTable.Columns.Contains(col.ColumnName))
                                newRow[col.ColumnName] = row[col]?.ToString()?.Trim();
                        }
                    }
                    else
                    {
                        newRow[scanColumnName] = row[scanColumnName]?.ToString()?.Trim();
                    }

                    combinedTable.Rows.Add(newRow);
                }
            }

            if (!foundValidSheet || combinedTable == null)
            {
                return new ExcelReadResult
                {
                    Success = false,
                    Error = generateId
                        ? fetchAllColumns
                            ? "No valid sheet found with data."
                            : $"No sheet contains column '{scanColumnName}'."
                        : fetchAllColumns
                            ? $"No sheet contains the required ID column '{idColumnName}'."
                            : $"No sheet contains both columns '{idColumnName}' and '{scanColumnName}'."
                };
            }

            return new ExcelReadResult { Success = true, Data = combinedTable };
        }

        // =========================
        // TABLE CREATION — specific scan column
        // =========================
        private DataTable CreateCombinedTable(
            string idColumnName,
            string scanColumnName,
            bool generateId)
        {
            DataTable dt = new DataTable("ScanData");

            dt.Columns.Add("SheetName", typeof(string));
            dt.Columns.Add(generateId ? "ID" : idColumnName, generateId ? typeof(int) : typeof(string));
            dt.Columns.Add(scanColumnName, typeof(string));

            return dt;
        }

        // =========================
        // TABLE CREATION — all columns mode
        // =========================
        private DataTable CreateCombinedTableAllColumns(
            string idColumnName,
            DataTable sourceSheet,
            bool generateId)
        {
            DataTable dt = new DataTable("ScanData");

            dt.Columns.Add("SheetName", typeof(string));
            dt.Columns.Add(generateId ? "ID" : idColumnName, generateId ? typeof(int) : typeof(string));

            foreach (DataColumn col in sourceSheet.Columns)
            {
                // ID column is already added above — skip to avoid duplicates
                if (!generateId && col.ColumnName == idColumnName)
                    continue;

                dt.Columns.Add(col.ColumnName, typeof(string));
            }

            return dt;
        }
    }
}