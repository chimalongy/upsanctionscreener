using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Upsanctionscreener.Models;
using ClosedXML.Excel;

namespace Upsanctionscreener.Classess.Utils
{
    public class SanctionExcelExporter
    {
        public void Export(List<SanctionEntry> entries, string outputPath)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Sanctions");

            // Header row
            var headers = new[]
            {
                "ID", "Source", "Subject Type", "Reference Number",
                "Primary Name", "All Names", "Date Designated",
                "Sanction Imposed", "Addresses", "Phone Numbers",
                "Email Addresses", "Positions", "ID Documents",
                "Call Sign", "Vessel Type", "Vessel Flag",
                "Vessel Owner", "Gross Registered Tonnage", "Comments"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontName = "Arial";
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // Data rows
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var row = i + 2;

                ws.Cell(row, 1).Value = e.ID;
                ws.Cell(row, 2).Value = e.Source;
                ws.Cell(row, 3).Value = e.SubjectType;
                ws.Cell(row, 4).Value = e.ReferenceNumber;
                ws.Cell(row, 5).Value = e.Names.FirstOrDefault() ?? "";
                ws.Cell(row, 6).Value = string.Join(" | ", e.Names);
                ws.Cell(row, 7).Value = e.DateDesignated;
                ws.Cell(row, 8).Value = e.SanctionImposed;
                ws.Cell(row, 9).Value = string.Join(" | ", e.Addresses);
                ws.Cell(row, 10).Value = string.Join(" | ", e.PhoneNumbers);
                ws.Cell(row, 11).Value = string.Join(" | ", e.EmailAddresses);
                ws.Cell(row, 12).Value = string.Join(" | ", e.Positions);
                ws.Cell(row, 13).Value = string.Join(" | ", e.IdList);
                ws.Cell(row, 14).Value = e.CallSign;
                ws.Cell(row, 15).Value = e.VesselType;
                ws.Cell(row, 16).Value = e.VesselFlag;
                ws.Cell(row, 17).Value = e.VesselOwner;
                ws.Cell(row, 18).Value = e.GrossRegisteredTonnage;
                ws.Cell(row, 19).Value = e.Comments;

                // Alternate row shading
                if (i % 2 == 1)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");

                ws.Row(row).Style.Font.FontName = "Arial";
            }

            // Source summary sheet
            var summary = wb.Worksheets.Add("Summary");
            summary.Cell(1, 1).Value = "Source";
            summary.Cell(1, 2).Value = "Count";
            summary.Cell(1, 1).Style.Font.Bold = true;
            summary.Cell(1, 2).Style.Font.Bold = true;

            var grouped = entries.GroupBy(e => e.Source).OrderBy(g => g.Key).ToList();
            for (int i = 0; i < grouped.Count; i++)
            {
                summary.Cell(i + 2, 1).Value = grouped[i].Key;
                summary.Cell(i + 2, 2).Value = grouped[i].Count();
            }
            summary.Cell(grouped.Count + 2, 1).Value = "TOTAL";
            summary.Cell(grouped.Count + 2, 1).Style.Font.Bold = true;
            summary.Cell(grouped.Count + 2, 2).Value = entries.Count;
            summary.Cell(grouped.Count + 2, 2).Style.Font.Bold = true;
            summary.Columns().AdjustToContents();

            // Auto-fit and freeze header
            ws.SheetView.FreezeRows(1);
            ws.RangeUsed()?.SetAutoFilter();
            ws.Columns().AdjustToContents();

            // Cap column widths so comments/names don't stretch too wide
            foreach (var col in ws.ColumnsUsed())
                if (col.Width > 60) col.Width = 60;

            wb.SaveAs(outputPath);
        }
    }


}
