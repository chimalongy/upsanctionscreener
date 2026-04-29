using ClosedXML.Excel;
using System.Data;

namespace Upsanctionscreener.Classess.Search.ScanExporters
{
    public static class TargetScanResultExporter
    {
        private static readonly string[] Headers = new[]
        {
            "Scan Type", "Item ID", "Name", "Address", "Email", "Phone",
            "Matched Field", "Similarity (%)", "Candidates Count",
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
            30.71,   // 3  Name
            30.71,   // 4  Address
            25.71,   // 5  Email
            18.71,   // 6  Phone
            18.71,   // 7  Matched Field
            16.71,   // 8  Similarity (%)
            18.71,   // 9  Candidates Count
            14.71,   // 10 Sanction ID
            16.71,   // 11 Subject Type
            20.71,   // 12 Source
            14.0,    // 13 Reference Number
            18.71,   // 14 Date Designated
            22.71,   // 15 Sanction Imposed
            35.71,   // 16 Comments
            14.71,   // 17 Call Sign
            16.71,   // 18 Vessel Type
            14.0,    // 19 Vessel Flag
            22.71,   // 20 Vessel Owner
            24.71,   // 21 Gross Registered Tonnage
            40.71,   // 22 Names
            14.0,    // 23 Addresses
            25.71,   // 24 Phone Numbers
            30.71,   // 25 Email Addresses
            14.0,    // 26 Positions
            35.71,   // 27 ID List
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

            using var workbook = new XLWorkbook();

            // ── Sheet 1: Scan Results ────────────────────────────────────────────
            var ws = workbook.Worksheets.Add("Scan Results");

            WriteHeaderRow(ws);
            int dataRowCount = WriteDataRows(ws, results, scanType);
            ApplyColumnWidths(ws);

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
            List<TargetScanResult> results,
            string scanType)
        {
            bool IsMatch(TargetScanResult r) => r.Hits.Count > 0;

            var matchedRows = results
                .Where(r => IsMatch(r))
                .Select(r => new { Result = r, TopHit = r.Hits.OrderByDescending(h => h.Similarity).First() })
                .OrderByDescending(x => x.TopHit.Similarity)
                .ToList();

            var noMatchRows = results
                .Where(r => !IsMatch(r))
                .OrderBy(r => r.RowId)
                .ToList();

            int row = 2;

            // ── Matched rows ─────────────────────────────────────────────────────
            foreach (var item in matchedRows)
            {
                double similarityPct = Math.Round(item.TopHit.Similarity * 100, 2);
                int candidatesCount = item.Result.Hits.Count;

                // Get the resolved sanction DataRow at the same index as the TopHit
                int topHitIndex = item.Result.Hits.IndexOf(item.TopHit);
                DataRow? sanctionRow = (topHitIndex >= 0 && topHitIndex < item.Result.ResolvedSanctionEntries.Count)
                    ? item.Result.ResolvedSanctionEntries[topHitIndex]
                    : null;

                ws.Cell(row, 1).Value = scanType;
                ws.Cell(row, 2).Value = item.Result.RowId;
                ws.Cell(row, 3).Value = item.Result.Name ?? string.Empty;
                ws.Cell(row, 4).Value = item.Result.Address ?? string.Empty;
                ws.Cell(row, 5).Value = item.Result.Email ?? string.Empty;
                ws.Cell(row, 6).Value = item.Result.Phone ?? string.Empty;
                ws.Cell(row, 7).Value = item.TopHit.MatchedName;
                ws.Cell(row, 8).Value = similarityPct;
                ws.Cell(row, 8).Style.NumberFormat.Format = "0.00";
                ws.Cell(row, 9).Value = candidatesCount;

                if (sanctionRow != null)
                {
                    ws.Cell(row, 10).Value = sanctionRow["ID"]?.ToString();
                    ws.Cell(row, 11).Value = sanctionRow["SubjectType"]?.ToString();
                    ws.Cell(row, 12).Value = sanctionRow["Source"]?.ToString();
                    ws.Cell(row, 13).Value = sanctionRow["ReferenceNumber"]?.ToString();
                    ws.Cell(row, 14).Value = sanctionRow["DateDesignated"]?.ToString();
                    ws.Cell(row, 15).Value = sanctionRow["SanctionImposed"]?.ToString();
                    ws.Cell(row, 16).Value = sanctionRow["Comments"]?.ToString();
                    ws.Cell(row, 17).Value = sanctionRow["CallSign"]?.ToString();
                    ws.Cell(row, 18).Value = sanctionRow["VesselType"]?.ToString();
                    ws.Cell(row, 19).Value = sanctionRow["VesselFlag"]?.ToString();
                    ws.Cell(row, 20).Value = sanctionRow["VesselOwner"]?.ToString();
                    ws.Cell(row, 21).Value = sanctionRow["GrossRegisteredTonnage"]?.ToString();
                    ws.Cell(row, 22).Value = sanctionRow["Names"]?.ToString();
                    ws.Cell(row, 23).Value = sanctionRow["Addresses"]?.ToString();
                    ws.Cell(row, 24).Value = sanctionRow["PhoneNumbers"]?.ToString();
                    ws.Cell(row, 25).Value = sanctionRow["EmailAddresses"]?.ToString();
                    ws.Cell(row, 26).Value = sanctionRow["Positions"]?.ToString();
                    ws.Cell(row, 27).Value = sanctionRow["IdList"]?.ToString();
                }

                ws.Cell(row, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#C6EFCE");
                ApplyDataRowStyle(ws, row);
                row++;
            }

            // ── No-match rows ────────────────────────────────────────────────────
            foreach (var result in noMatchRows)
            {
                ws.Cell(row, 1).Value = scanType;
                ws.Cell(row, 2).Value = result.RowId;
                ws.Cell(row, 3).Value = result.Name ?? string.Empty;
                ws.Cell(row, 4).Value = result.Address ?? string.Empty;
                ws.Cell(row, 5).Value = result.Email ?? string.Empty;
                ws.Cell(row, 6).Value = result.Phone ?? string.Empty;
                ws.Cell(row, 7).Value = string.Empty;
                ws.Cell(row, 8).Value = 0;
                ws.Cell(row, 8).Style.NumberFormat.Format = "0.00";
                ws.Cell(row, 9).Value = 0;

                ws.Cell(row, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFC7CE");
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

        private static void WriteSummarySheet(XLWorkbook workbook, List<TargetScanResult> results, int totalRecords)
        {
            var ws = workbook.Worksheets.Add("Summary");

            bool IsMatch(TargetScanResult r) => r.Hits.Count > 0;

            int matched = results.Count(r => IsMatch(r));
            int highConf = results.Count(r => IsMatch(r) && r.Hits.Any(h => h.Similarity * 100 >= 95));
            int medConf = results.Count(r => IsMatch(r) && r.Hits.Any(h => h.Similarity * 100 >= 75 && h.Similarity * 100 < 95));
            int lowConf = results.Count(r => IsMatch(r) && r.Hits.Any(h => h.Similarity * 100 < 75));
            int noMatch = results.Count(r => !IsMatch(r));

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