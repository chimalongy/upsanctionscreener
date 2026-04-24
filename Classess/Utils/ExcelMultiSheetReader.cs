using ExcelDataReader;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public ExcelReadResult ReadExcelFile(IFormFile file, string idColumnName, string scanColumnName)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return new ExcelReadResult { Success = false, Error = "No Excel file uploaded." };

                if (string.IsNullOrWhiteSpace(idColumnName))
                    return new ExcelReadResult { Success = false, Error = "Id column name is required." };

                if (string.IsNullOrWhiteSpace(scanColumnName))
                    return new ExcelReadResult { Success = false, Error = "Scan column name is required." };

                // Required for ExcelDataReader (especially for .xls)
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                using var stream = file.OpenReadStream();
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

                DataTable combinedTable = CreateCombinedTable(idColumnName, scanColumnName);

                bool foundValidSheet = false;

                foreach (DataTable sheet in dataSet.Tables)
                {
                    if (sheet.Columns.Count == 0)
                        continue;

                    bool hasIdColumn = sheet.Columns.Contains(idColumnName);
                    bool hasScanColumn = sheet.Columns.Contains(scanColumnName);

                    if (!hasIdColumn || !hasScanColumn)
                        continue;

                    foundValidSheet = true;

                    foreach (DataRow row in sheet.Rows)
                    {
                        var idValue = row[idColumnName]?.ToString()?.Trim();
                        var scanValue = row[scanColumnName]?.ToString()?.Trim();

                        // skip empty rows
                        if (string.IsNullOrWhiteSpace(idValue) && string.IsNullOrWhiteSpace(scanValue))
                            continue;

                        DataRow newRow = combinedTable.NewRow();
                        newRow["SheetName"] = sheet.TableName;
                        newRow[idColumnName] = idValue;
                        newRow[scanColumnName] = scanValue;

                        combinedTable.Rows.Add(newRow);
                    }
                }

                if (!foundValidSheet)
                {
                    return new ExcelReadResult
                    {
                        Success = false,
                        Error = $"No sheet contains both columns '{idColumnName}' and '{scanColumnName}'."
                    };
                }

                return new ExcelReadResult
                {
                    Success = true,
                    Data = combinedTable
                };
            }
            catch (Exception ex)
            {
                return new ExcelReadResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        private DataTable CreateCombinedTable(string idColumnName, string scanColumnName)
        {
            DataTable dt = new DataTable("ScanData");
            dt.Columns.Add("SheetName", typeof(string));
            dt.Columns.Add(idColumnName, typeof(string));
            dt.Columns.Add(scanColumnName, typeof(string));
            return dt;
        }
    }
}
