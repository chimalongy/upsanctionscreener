using ClosedXML.Excel;
using Upsanctionscreener.Models;

namespace Upsanctionscreener.Classess.Search.ScanExporters
{
    public static class TargetScanResultExporter
    {
        private static readonly string[] FixedHeaders = new[]
        {
            "Scan Type", "Item ID",
            "Name", "Address", "Email", "Phone", "Gender", "Date of Birth",
            "Matched Field",
            "Name Similarity (%)", "Address Similarity (%)", "Email Similarity (%)",
            "Phone Similarity (%)", "Gender Similarity (%)", "DOB Similarity (%)",
            "Average Similarity (%)", "Hits Count",
            "Sanction ID", "Subject Type", "Source", "Reference Number", "Date Designated",
            "Sanction Imposed", "Comments", "Call Sign", "Vessel Type", "Vessel Flag",
            "Vessel Owner", "Gross Registered Tonnage", "Names", "Addresses",
            "Phone Numbers", "Email Addresses", "Positions", "ID List"
        };

        private const double DefaultColWidth = 22.0;
        private static readonly Dictionary<int, double> FixedColWidths = new()
        {
            [1] = 14.71,  // Scan Type
            [2] = 14.71,  // Item ID
            [3] = 26.71,  // Name
            [4] = 26.71,  // Address
            [5] = 24.71,  // Email
            [6] = 16.71,  // Phone
            [7] = 12.0,   // Gender
            [8] = 16.0,   // Date of Birth
            [9] = 16.71,  // Matched Field
            [16] = 18.71, // Average Similarity
            [17] = 12.0,  // Hits Count
            [30] = 40.71, // Names
            [35] = 35.71, // ID List
        };

        public static void ExportToExcel(
            List<TargetScanResult> results,
            string scanType,
            string outputPath)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("outputPath must not be empty.", nameof(outputPath));

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            // Results already come sorted by AverageSimilarity descending from
            // ParallelTargetScan, but sort again here defensively in case the
            // exporter is ever called with a differently-ordered list.
            var ordered = results.OrderByDescending(r => r.AverageSimilarity).ToList();

            // Dynamic source columns — every column from data_to_scan, taken
            // from the first result (all rows share the same schema).
            var sourceColumns = ordered.FirstOrDefault()?.RawRowData.Select(kv => kv.Key).ToList()
                                 ?? new List<string>();

            var headers = FixedHeaders
                .Concat(sourceColumns.Select(c => $"Source: {c}"))
                .ToArray();

            using var workbook = new XLWorkbook();

            var ws = workbook.Worksheets.Add("Scan Results");

            WriteHeaderRow(ws, headers);
            WriteDataRows(ws, ordered, scanType, sourceColumns);
            ApplyColumnWidths(ws, headers.Length);

            ws.SheetView.FreezeRows(1);
            ws.SheetView.FreezeColumns(2);

            WriteSummarySheet(workbook, ordered);

            workbook.SaveAs(outputPath);
        }

        // ── Header ───────────────────────────────────────────────────────────────

        private static void WriteHeaderRow(IXLWorksheet ws, string[] headers)
        {
            for (int col = 1; col <= headers.Length; col++)
            {
                var cell = ws.Cell(1, col);
                cell.Value = headers[col - 1];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Font.FontName = "Arial";
                cell.Style.Font.FontSize = 11;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E79");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Alignment.WrapText = true;
            }

            ws.Row(1).Height = 27.95;
        }

        // ── Data Rows ────────────────────────────────────────────────────────────

        private static void WriteDataRows(
            IXLWorksheet ws,
            List<TargetScanResult> results,
            string scanType,
            List<string> sourceColumns)
        {
            int row = 2;

            foreach (var r in results)
            {
                var entry = r.MatchedSanctionEntry;

                ws.Cell(row, 1).Value = scanType;
                ws.Cell(row, 2).Value = r.RowId ?? string.Empty;
                ws.Cell(row, 3).Value = r.Name ?? string.Empty;
                ws.Cell(row, 4).Value = r.Address ?? string.Empty;
                ws.Cell(row, 5).Value = r.Email ?? string.Empty;
                ws.Cell(row, 6).Value = r.Phone ?? string.Empty;
                ws.Cell(row, 7).Value = r.Gender ?? string.Empty;
                ws.Cell(row, 8).Value = r.DateOfBirth ?? string.Empty;
                ws.Cell(row, 9).Value = r.MatchedColumn ?? string.Empty;

                ws.Cell(row, 10).Value = Math.Round(r.NameSimilarity * 100, 2);
                ws.Cell(row, 11).Value = Math.Round(r.AddressSimilarity * 100, 2);
                ws.Cell(row, 12).Value = Math.Round(r.EmailSimilarity * 100, 2);
                ws.Cell(row, 13).Value = Math.Round(r.PhoneSimilarity * 100, 2);
                ws.Cell(row, 14).Value = Math.Round(r.GenderSimilarity * 100, 2);
                ws.Cell(row, 15).Value = Math.Round(r.DobSimilarity * 100, 2);
                for (int c = 10; c <= 15; c++)
                    ws.Cell(row, c).Style.NumberFormat.Format = "0.00";

                double avgPct = Math.Round(r.AverageSimilarity * 100, 2);
                ws.Cell(row, 16).Value = avgPct;
                ws.Cell(row, 16).Style.NumberFormat.Format = "0.00";
                ws.Cell(row, 16).Style.Fill.BackgroundColor = ConfidenceColor(avgPct);

                ws.Cell(row, 17).Value = r.HitsCount;

                if (entry != null)
                {
                    ws.Cell(row, 18).Value = entry.ID ?? string.Empty;
                    ws.Cell(row, 19).Value = entry.SubjectType ?? string.Empty;
                    ws.Cell(row, 20).Value = entry.Source ?? string.Empty;
                    ws.Cell(row, 21).Value = entry.ReferenceNumber ?? string.Empty;
                    ws.Cell(row, 22).Value = entry.DateDesignated ?? string.Empty;
                    ws.Cell(row, 23).Value = entry.SanctionImposed ?? string.Empty;
                    ws.Cell(row, 24).Value = entry.Comments ?? string.Empty;
                    ws.Cell(row, 25).Value = entry.CallSign ?? string.Empty;
                    ws.Cell(row, 26).Value = entry.VesselType ?? string.Empty;
                    ws.Cell(row, 27).Value = entry.VesselFlag ?? string.Empty;
                    ws.Cell(row, 28).Value = entry.VesselOwner ?? string.Empty;
                    ws.Cell(row, 29).Value = entry.GrossRegisteredTonnage ?? string.Empty;

                    ws.Cell(row, 30).Value = entry.Names != null ? string.Join(" | ", entry.Names) : string.Empty;
                    ws.Cell(row, 31).Value = entry.Addresses != null ? string.Join(" | ", entry.Addresses) : string.Empty;
                    ws.Cell(row, 32).Value = entry.PhoneNumbers != null ? string.Join(" | ", entry.PhoneNumbers) : string.Empty;
                    ws.Cell(row, 33).Value = entry.EmailAddresses != null ? string.Join(" | ", entry.EmailAddresses) : string.Empty;
                    ws.Cell(row, 34).Value = entry.Positions != null ? string.Join(" | ", entry.Positions) : string.Empty;
                    ws.Cell(row, 35).Value = entry.IdList != null ? string.Join(" | ", entry.IdList) : string.Empty;
                }

                // Dynamic source columns — dump every field of data_to_scan
                int col = FixedHeaders.Length + 1;
                var rawLookup = r.RawRowData.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                foreach (var sourceCol in sourceColumns)
                {
                    ws.Cell(row, col).Value = rawLookup.TryGetValue(sourceCol, out var val) ? val : string.Empty;
                    col++;
                }

                ApplyDataRowStyle(ws, row, FixedHeaders.Length + sourceColumns.Count);
                row++;
            }
        }

        private static XLColor ConfidenceColor(double avgPct)
        {
            if (avgPct >= 95) return XLColor.FromHtml("#C6EFCE"); // high — green
            if (avgPct >= 75) return XLColor.FromHtml("#FFEB9C"); // medium — amber
            return XLColor.FromHtml("#FFC7CE");                   // low — red
        }

        private static void ApplyDataRowStyle(IXLWorksheet ws, int row, int totalCols)
        {
            var rowRange = ws.Range(row, 1, row, totalCols);
            rowRange.Style.Font.FontName = "Arial";
            rowRange.Style.Font.FontSize = 11;
            rowRange.Style.Alignment.WrapText = true;
            rowRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(row).Height = 39.95;
        }

        // ── Column Widths ─────────────────────────────────────────────────────────

        private static void ApplyColumnWidths(IXLWorksheet ws, int totalCols)
        {
            for (int col = 1; col <= totalCols; col++)
            {
                ws.Column(col).Width = FixedColWidths.TryGetValue(col, out var w) ? w : DefaultColWidth;
            }
        }

        // ── Summary Sheet ─────────────────────────────────────────────────────────

        private static void WriteSummarySheet(XLWorkbook workbook, List<TargetScanResult> results)
        {
            var ws = workbook.Worksheets.Add("Summary");

            int total = results.Count;
            int highConf = results.Count(r => r.AverageSimilarity * 100 >= 95);
            int medConf = results.Count(r => r.AverageSimilarity * 100 >= 75 && r.AverageSimilarity * 100 < 95);
            int lowConf = results.Count(r => r.AverageSimilarity * 100 < 75);
            double avgOfAll = total > 0 ? Math.Round(results.Average(r => r.AverageSimilarity) * 100, 2) : 0;

            var summaryData = new (string Label, object Value)[]
            {
                ("Total Matched Records",      total),
                ("High Confidence (≥ 95%)",    highConf),
                ("Medium Confidence (75–94%)", medConf),
                ("Low Confidence (< 75%)",     lowConf),
                ("Mean Average Similarity (%)", avgOfAll),
                ("Report Generated",           DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
            };

            ws.Column(1).Width = 32;
            ws.Column(2).Width = 20;

            for (int i = 0; i < summaryData.Length; i++)
            {
                int r = i + 1;

                var labelCell = ws.Cell(r, 1);
                labelCell.Value = summaryData[i].Label;
                labelCell.Style.Font.Bold = true;
                labelCell.Style.Font.FontName = "Arial";

                var valueCell = ws.Cell(r, 2);
                valueCell.Value = summaryData[i].Value.ToString();
                valueCell.Style.Font.FontName = "Arial";
            }
        }
    }
}