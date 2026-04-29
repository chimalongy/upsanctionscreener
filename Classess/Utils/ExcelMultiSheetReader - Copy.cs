//using ExcelDataReader;
//using System;
//using System.Data;
//using System.IO;
//using Microsoft.AspNetCore.Http;

//namespace Upsanctionscreener.Classess.Utils
//{
//    public class ExcelReadResult
//    {
//        public bool Success { get; set; }
//        public DataTable? Data { get; set; }
//        public string? Error { get; set; }
//    }

//    public class ExcelMultiSheetReader
//    {
//        // =========================
//        // READ FROM IFORMFILE
//        // =========================
//        public ExcelReadResult ReadExcelFile(
//            IFormFile file,
//            string idColumnName,
//            string scanColumnName,
//            bool generateId = false)
//        {
//            try
//            {
//                if (file == null || file.Length == 0)
//                    return new ExcelReadResult { Success = false, Error = "No Excel file uploaded." };

//                using var stream = file.OpenReadStream();

//                return ProcessExcel(stream, idColumnName, scanColumnName, generateId);
//            }
//            catch (Exception ex)
//            {
//                return new ExcelReadResult { Success = false, Error = ex.Message };
//            }
//        }

//        // =========================
//        // READ FROM FILE PATH
//        // =========================
//        public ExcelReadResult ReadExcelFromPath(
//            string filePath,
//            string idColumnName,
//            string scanColumnName,
//            bool generateId = false)
//        {
//            try
//            {
//                if (string.IsNullOrWhiteSpace(filePath))
//                    return new ExcelReadResult { Success = false, Error = "File path is required." };

//                if (!File.Exists(filePath))
//                    return new ExcelReadResult { Success = false, Error = "File does not exist." };

//                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read);

//                return ProcessExcel(stream, idColumnName, scanColumnName, generateId);
//            }
//            catch (Exception ex)
//            {
//                return new ExcelReadResult { Success = false, Error = ex.Message };
//            }
//        }

//        // =========================
//        // SHARED LOGIC
//        // =========================
//        private ExcelReadResult ProcessExcel(
//            Stream stream,
//            string idColumnName,
//            string scanColumnName,
//            bool generateId)
//        {
//            if (string.IsNullOrWhiteSpace(scanColumnName))
//                return new ExcelReadResult { Success = false, Error = "Scan column name is required." };

//            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

//            using var reader = ExcelReaderFactory.CreateReader(stream);

//            var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration()
//            {
//                ConfigureDataTable = (_) => new ExcelDataTableConfiguration()
//                {
//                    UseHeaderRow = true
//                }
//            });

//            if (dataSet == null || dataSet.Tables.Count == 0)
//                return new ExcelReadResult { Success = false, Error = "Excel file contains no sheets." };

//            DataTable combinedTable = CreateCombinedTable(idColumnName, scanColumnName, generateId);

//            bool foundValidSheet = false;
//            int autoId = 1;

//            foreach (DataTable sheet in dataSet.Tables)
//            {
//                if (sheet.Columns.Count == 0)
//                    continue;

//                bool hasScanColumn = sheet.Columns.Contains(scanColumnName);
//                bool hasIdColumn = sheet.Columns.Contains(idColumnName);

//                if (!hasScanColumn)
//                    continue;

//                if (!generateId && !hasIdColumn)
//                    continue;

//                foundValidSheet = true;

//                foreach (DataRow row in sheet.Rows)
//                {
//                    var scanValue = row[scanColumnName]?.ToString()?.Trim();

//                    if (string.IsNullOrWhiteSpace(scanValue))
//                        continue;

//                    DataRow newRow = combinedTable.NewRow();
//                    newRow["SheetName"] = sheet.TableName;

//                    if (generateId)
//                    {
//                        newRow["ID"] = autoId++;
//                    }
//                    else
//                    {
//                        var idValue = row[idColumnName]?.ToString()?.Trim();
//                        newRow[idColumnName] = idValue;
//                    }

//                    newRow[scanColumnName] = scanValue;

//                    combinedTable.Rows.Add(newRow);
//                }
//            }

//            if (!foundValidSheet)
//            {
//                return new ExcelReadResult
//                {
//                    Success = false,
//                    Error = generateId
//                        ? $"No sheet contains column '{scanColumnName}'."
//                        : $"No sheet contains both columns '{idColumnName}' and '{scanColumnName}'."
//                };
//            }

//            return new ExcelReadResult
//            {
//                Success = true,
//                Data = combinedTable
//            };
//        }

//        // =========================
//        // TABLE CREATION
//        // =========================
//        private DataTable CreateCombinedTable(
//            string idColumnName,
//            string scanColumnName,
//            bool generateId)
//        {
//            DataTable dt = new DataTable("ScanData");

//            dt.Columns.Add("SheetName", typeof(string));

//            if (generateId)
//            {
//                dt.Columns.Add("ID", typeof(int));
//            }
//            else
//            {
//                dt.Columns.Add(idColumnName, typeof(string));
//            }

//            dt.Columns.Add(scanColumnName, typeof(string));

//            return dt;
//        }
//    }
//}