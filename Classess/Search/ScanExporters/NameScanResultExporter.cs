using ClosedXML.Excel;
using Upsanctionscreener.Models;

namespace Upsanctionscreener.Classess.Search.ScanExporters
{
    public static class NameScanResultExporter
    {
        private static readonly string[] Headers = new[]
        {
            "Scan Type", "Item ID", "Item Matched", "Matched Field", "Similarity (%)", "Candidates Count",
            "Sanction ID", "Subject Type", "Source", "Reference Number", "Date Designated",
            "Sanction Imposed", "Comments", "Call Sign", "Vessel Type", "Vessel Flag",
            "Vessel Owner", "Gross Registered Tonnage", "Names", "Addresses",
            "Phone Numbers", "Email Addresses", "Positions", "ID List"
        };

        private static readonly double[] ColWidths = new double[]
        {
            0,       // placeholder (1-based)
            14.71,   // 1  Scan Type
            14.71,   // 2  Item ID
            30.71,   // 3  Item Matched
            18.71,   // 4  Matched Field
            16.71,   // 5  Similarity (%)
            18.71,   // 6  Candidates Count
            14.71,   // 7  Sanction ID
            16.71,   // 8  Subject Type
            20.71,   // 9  Source
            14.0,    // 10 Reference Number
            18.71,   // 11 Date Designated
            22.71,   // 12 Sanction Imposed
            35.71,   // 13 Comments
            14.71,   // 14 Call Sign
            16.71,   // 15 Vessel Type
            14.0,    // 16 Vessel Flag
            22.71,   // 17 Vessel Owner
            24.71,   // 18 Gross Registered Tonnage
            40.71,   // 19 Names
            14.0,    // 20 Addresses
            25.71,   // 21 Phone Numbers
            30.71,   // 22 Email Addresses
            14.0,    // 23 Positions
            35.71,   // 24 ID List
        };

        public static void ExportToExcel(
            List<NameScanResult> results,
            Dictionary<string, SanctionEntry> sanctionLookup,
            string scannedColumnName,
            string scanType,
            string outputPath)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("outputPath must not be empty.", nameof(outputPath));

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            using var workbook = new XLWorkbook();

            // ── Sheet 1: Scan Results ────────────────────────────────────────────
            var ws = workbook.Worksheets.Add("Scan Results");

            WriteHeaderRow(ws);
            int dataRowCount = WriteDataRows(ws, results, sanctionLookup, scannedColumnName, scanType);
            ApplyColumnWidths(ws);

            // Freeze header row
            ws.SheetView.FreezeRows(1);

            // ── Sheet 2: Summary ─────────────────────────────────────────────────
            WriteSummarySheet(workbook, results, dataRowCount);

            workbook.SaveAs(outputPath);
        }

        // ── Header ───────────────────────────────────────────────────────────────

        private static void WriteHeaderRow(IXLWorksheet ws)
        {
            for (int col = 1; col <= Headers.Length; col++)
            {
                var cell = ws.Cell(1, col);
                cell.Value = Headers[col - 1];
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

        private static int WriteDataRows(
            IXLWorksheet ws,
            List<NameScanResult> results,
            Dictionary<string, SanctionEntry> sanctionLookup,
            string scannedColumnName,
            string scanType)
        {
            var matchedRows = results
                .Where(r => r.IsMatch)
                .Select(r => new { Result = r, TopHit = r.Hits.OrderByDescending(h => h.Similarity).First() })
                .OrderByDescending(x => x.TopHit.Similarity)
                .ToList();

            var noMatchRows = results
                .Where(r => !r.IsMatch)
                .OrderBy(r => r.RowId)
                .ToList();

            int row = 2;

            // ── Matched rows ─────────────────────────────────────────────────────
            foreach (var item in matchedRows)
            {
                sanctionLookup.TryGetValue(item.TopHit.EntryId, out var entry);

                double similarityPct = Math.Round(item.TopHit.Similarity * 100, 2);
                int candidatesCount = item.Result.Hits.Count;

                ws.Cell(row, 1).Value = scanType;
                ws.Cell(row, 2).Value = item.Result.RowId;
                ws.Cell(row, 3).Value = item.Result.ScannedValue;
                ws.Cell(row, 4).Value = scannedColumnName;
                ws.Cell(row, 5).Value = similarityPct;
                ws.Cell(row, 5).Style.NumberFormat.Format = "0.00";
                ws.Cell(row, 6).Value = candidatesCount;

                if (entry != null)
                {
                    ws.Cell(row, 7).Value = entry.ID;
                    ws.Cell(row, 8).Value = entry.SubjectType;
                    ws.Cell(row, 9).Value = entry.Source;
                    ws.Cell(row, 10).Value = entry.ReferenceNumber;
                    ws.Cell(row, 11).Value = entry.DateDesignated;
                    ws.Cell(row, 12).Value = entry.SanctionImposed;
                    ws.Cell(row, 13).Value = entry.Comments;
                    ws.Cell(row, 14).Value = entry.CallSign;
                    ws.Cell(row, 15).Value = entry.VesselType;
                    ws.Cell(row, 16).Value = entry.VesselFlag;
                    ws.Cell(row, 17).Value = entry.VesselOwner;
                    ws.Cell(row, 18).Value = entry.GrossRegisteredTonnage;
                    ws.Cell(row, 19).Value = entry.Names.Count > 0
                        ? string.Join(" | ", entry.Names) : string.Empty;
                    ws.Cell(row, 20).Value = entry.Addresses.Count > 0
                        ? string.Join(" | ", entry.Addresses) : string.Empty;
                    ws.Cell(row, 21).Value = entry.PhoneNumbers.Count > 0
                        ? string.Join(" | ", entry.PhoneNumbers) : string.Empty;
                    ws.Cell(row, 22).Value = entry.EmailAddresses.Count > 0
                        ? string.Join(" | ", entry.EmailAddresses) : string.Empty;
                    ws.Cell(row, 23).Value = entry.Positions.Count > 0
                        ? string.Join(" | ", entry.Positions) : string.Empty;
                    ws.Cell(row, 24).Value = entry.IdList.Count > 0
                        ? string.Join(" | ", entry.IdList) : string.Empty;
                }

                ws.Cell(row, 5).Style.Fill.BackgroundColor = XLColor.FromHtml("#C6EFCE");
                ApplyDataRowStyle(ws, row);
                row++;
            }

            // ── No-match rows ────────────────────────────────────────────────────
            foreach (var result in noMatchRows)
            {
                ws.Cell(row, 1).Value = scanType;
                ws.Cell(row, 2).Value = result.RowId;
                ws.Cell(row, 3).Value = result.ScannedValue;
                ws.Cell(row, 4).Value = scannedColumnName;
                ws.Cell(row, 5).Value = 0;
                ws.Cell(row, 5).Style.NumberFormat.Format = "0.00";
                ws.Cell(row, 6).Value = 0;

                ws.Cell(row, 5).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFC7CE");
                ApplyDataRowStyle(ws, row);
                row++;
            }

            return row - 2;
        }

        private static void ApplyDataRowStyle(IXLWorksheet ws, int row)
        {
            var rowRange = ws.Range(row, 1, row, Headers.Length);
            rowRange.Style.Font.FontName = "Arial";
            rowRange.Style.Font.FontSize = 11;
            rowRange.Style.Alignment.WrapText = true;
            rowRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(row).Height = 39.95;
        }

        // ── Column Widths ─────────────────────────────────────────────────────────

        private static void ApplyColumnWidths(IXLWorksheet ws)
        {
            for (int col = 1; col < ColWidths.Length; col++)
            {
                ws.Column(col).Width = ColWidths[col];
            }
        }

        // ── Summary Sheet ─────────────────────────────────────────────────────────

        private static void WriteSummarySheet(XLWorkbook workbook, List<NameScanResult> results, int totalRecords)
        {
            var ws = workbook.Worksheets.Add("Summary");

            int matched = results.Count(r => r.IsMatch);
            int highConf = results.Count(r => r.IsMatch && r.Hits.Any(h => h.Similarity * 100 >= 95));
            int medConf = results.Count(r => r.IsMatch && r.Hits.Any(h => h.Similarity * 100 >= 75 && h.Similarity * 100 < 95));
            int lowConf = results.Count(r => r.IsMatch && r.Hits.Any(h => h.Similarity * 100 < 75));
            int noMatch = results.Count(r => !r.IsMatch);

            var summaryData = new (string Label, object Value)[]
            {
                ("Total Records",              totalRecords),
                ("Matched (Similarity > 0)",   matched),
                ("High Confidence (≥ 95%)",    highConf),
                ("Medium Confidence (75–94%)", medConf),
                ("Low Confidence (< 75%)",     lowConf),
                ("No Match Found",             noMatch),
                ("Report Generated",           DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
            };

            ws.Column(1).Width = 30;
            ws.Column(2).Width = 20;

            for (int i = 0; i < summaryData.Length; i++)
            {
                int row = i + 1;

                var labelCell = ws.Cell(row, 1);
                labelCell.Value = summaryData[i].Label;
                labelCell.Style.Font.Bold = true;
                labelCell.Style.Font.FontName = "Arial";

                var valueCell = ws.Cell(row, 2);
                valueCell.Value = summaryData[i].Value.ToString();
                valueCell.Style.Font.FontName = "Arial";
            }
        }
    }
}