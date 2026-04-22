using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Upsanctionscreener.Models;
using ExcelDataReader;

namespace Upsanctionscreener.Classess.Utils
{
    public static class SanctionExcelReader
    {
        public static List<SanctionEntry> LoadFromExcel(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Excel file not found: {filePath}", filePath);

            // NOTE: System.Text.Encoding.RegisterProvider() is NOT needed on .NET Framework
            // — all encodings are available by default.

            var entries = new List<SanctionEntry>();

            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
                {
                    ConfigureDataTable = _ => new ExcelDataTableConfiguration
                    {
                        UseHeaderRow = true
                    }
                });

                var table = dataSet.Tables[0];

                foreach (DataRow row in table.Rows)
                {
                    var entry = new SanctionEntry
                    {
                        ID = GetString(row, "ID"),
                        SubjectType = GetString(row, "Subject Type"),
                        Source = GetString(row, "Source"),
                        ReferenceNumber = GetString(row, "Reference Number"),
                        DateDesignated = GetString(row, "Date Designated"),
                        SanctionImposed = GetString(row, "Sanction Imposed"),
                        Comments = GetString(row, "Comments"),
                        Names = SplitPipe(row, "All Names"),
                        Addresses = SplitPipe(row, "Addresses"),
                        PhoneNumbers = SplitPipe(row, "Phone Numbers"),
                        EmailAddresses = SplitPipe(row, "Email Addresses"),
                        Positions = SplitPipe(row, "Positions"),
                        IdList = SplitPipe(row, "ID Documents"),
                        CallSign = GetNullable(row, "Call Sign"),
                        VesselType = GetNullable(row, "Vessel Type"),
                        VesselFlag = GetNullable(row, "Vessel Flag"),
                        VesselOwner = GetNullable(row, "Vessel Owner"),
                        GrossRegisteredTonnage = GetNullable(row, "Gross Registered Tonnage"),
                    };

                    entries.Add(entry);
                }
            }

            return entries;
        }

        private static string GetString(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column)) return string.Empty;
            var val = row[column];
            return val == null || val == DBNull.Value ? string.Empty : val.ToString().Trim();
        }

        private static string GetNullable(DataRow row, string column)
        {
            var val = GetString(row, column);
            return string.IsNullOrWhiteSpace(val) ? null : val;
        }

        private static List<string> SplitPipe(DataRow row, string column)
        {
            var raw = GetString(row, column);
            if (string.IsNullOrWhiteSpace(raw))
                return new List<string>();

            return raw
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }
    }

}
