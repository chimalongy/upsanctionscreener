namespace Upsanctionscreener.Classess.Utils
{
    using Microsoft.AspNetCore.Components;
    using PuppeteerSharp;
    using PuppeteerSharp.Media;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Upsanctionscreener.Models;
    using Upsanctionscreener.Services;

    public static class SingleScreenReportGenerator
    {
        public static async Task EnsureBrowserAsync()
        {
            Console.WriteLine("Creating BrowserFetcher");

            var fetcher = new BrowserFetcher();

            Console.WriteLine("Starting DownloadAsync");

            await fetcher.DownloadAsync();

            Console.WriteLine("Download finished");
        }

        public static async Task<string> GenerateAsync(
            string searchTerm,
            string searchField,
            double threshold,
            IEnumerable<SingleSearchSanctionMatchRow> sanctions,
            IEnumerable<RssNewsItem> adverseMedia)
        {
            var outputDirectory = Path.Combine(GlobalVariables.root_folder, "reports", "scan-reports");
            Directory.CreateDirectory(outputDirectory);

            var timestamp = DateTime.UtcNow;
            var fileName = $"scan-report_{SanitiseFilename(searchTerm)}_{timestamp:yyyyMMdd_HHmmss}.pdf";
            var outputPath = Path.Combine(outputDirectory, fileName);

            var html = BuildHtml(searchTerm, searchField, threshold, timestamp,
                                 sanctions.ToList(), adverseMedia.ToList());

            await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-gpu"
                }
            });

            await using var page = await browser.NewPageAsync();

            await page.SetContentAsync(html, new PuppeteerSharp.NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.Networkidle0 }
            });

            var pdfBytes = await page.PdfDataAsync(new PdfOptions
            {
                Format = PaperFormat.A4,
                PrintBackground = true,
                MarginOptions = new MarginOptions
                {
                    Top = "10mm",
                    Bottom = "10mm",
                    Left = "12mm",
                    Right = "12mm"
                },
                DisplayHeaderFooter = true,
                HeaderTemplate = "<div style='font-size:0px;'></div>",
                FooterTemplate = $"<div style='font-size:7px;font-family:Arial;width:100%;" +
                                 $"text-align:center;color:#9ca3af;padding:0 20px;'>" +
                                 $"Generated {timestamp:dd MMM yyyy HH:mm} UTC · Unified Payments Sanction Screening" +
                                 $"</div>",
            });

            await File.WriteAllBytesAsync(outputPath, pdfBytes);

            return fileName;
        }

        private static string BuildHtml(
            string searchTerm, string searchField, double threshold, DateTime timestamp,
            List<SingleSearchSanctionMatchRow> sanctions,
            List<RssNewsItem> adverseMedia)
        {
            string logoBase64 = "";
            try
            {
                var logoPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "images", "logo.png");
                if (File.Exists(logoPath))
                    logoBase64 = Convert.ToBase64String(File.ReadAllBytes(logoPath));
            }
            catch { /* fall back silently */ }

            string logoHtml = !string.IsNullOrEmpty(logoBase64)
                ? $"<img src='data:image/png;base64,{logoBase64}' style='width:32px;height:32px;object-fit:contain;border-radius:6px;' />"
                : "<div style='width:32px;height:32px;background:#fff;border-radius:8px;display:flex;align-items:center;justify-content:center;'><span style='font-size:14px;font-weight:900;color:#1e1b4b;'>UP</span></div>";

            var top5Sanctions = sanctions.Take(5).ToList();
            var top5Media = adverseMedia.Take(5).ToList();

            bool hasHits = sanctions.Count > 0;

            var sb = new StringBuilder();

            sb.Append(@"<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='utf-8'/>
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: Arial, Helvetica, sans-serif; font-size: 10px; color: #111827; background: #fff; }

  /* ── Header ── */
  .report-header { background: #1e1b4b; color: #fff; padding: 14px 18px 12px; border-radius: 0 0 8px 8px; margin-bottom: 12px; }
  .logo-row { display: flex; align-items: center; gap: 10px; margin-bottom: 8px; }
  .logo-row h1 { font-size: 15px; font-weight: 700; }
  .logo-row .sub { font-size: 9px; color: #c4b5fd; margin-top: 1px; }
  .meta-grid { display: grid; grid-template-columns: repeat(4,1fr); gap: 6px; }
  .meta-cell { background: rgba(255,255,255,0.08); border-radius: 6px; padding: 6px 8px; }
  .mc-label { font-size: 7px; color: #a5b4fc; text-transform: uppercase; letter-spacing: 0.06em; margin-bottom: 2px; }
  .mc-val   { font-size: 10px; font-weight: 600; color: #fff; }

  /* ── Summary bar ── */
  .summary-bar { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin-bottom: 10px; }
  .sum-card { border-radius: 8px; padding: 8px 12px; }
  .sc-count { font-size: 18px; font-weight: 700; margin-bottom: 1px; }
  .sc-label { font-size: 8px; text-transform: uppercase; letter-spacing: 0.05em; }
  .sum-sanction { background: #fef2f2; border: 1px solid #fecaca; }
  .sum-sanction .sc-count { color: #dc2626; }
  .sum-sanction .sc-label { color: #ef4444; }
  .sum-media    { background: #eff6ff; border: 1px solid #bfdbfe; }
  .sum-media    .sc-count { color: #2563eb; }
  .sum-media    .sc-label { color: #3b82f6; }

  /* ── Verdict ── */
  .verdict { border-radius: 6px; padding: 8px 12px; margin-bottom: 10px; display: flex; align-items: center; gap: 8px; }
  .verdict-clear { background: #f0fdf4; border: 1px solid #bbf7d0; }
  .verdict-hit   { background: #fef2f2; border: 1px solid #fecaca; }
  .verdict-icon  { font-size: 16px; }
  .verdict-label { font-size: 11px; font-weight: 700; }
  .verdict-sub   { font-size: 9px; color: #6b7280; margin-top: 1px; }
  .verdict-clear .verdict-label { color: #15803d; }
  .verdict-hit   .verdict-label { color: #dc2626; }

  /* ── 2-column match grid ── */
  .matches-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 10px; }
  .match-col { display: flex; flex-direction: column; gap: 0; }

  /* ── Section heading inside each column ── */
  .col-heading {
    font-size: 9px; font-weight: 700; color: #1e1b4b;
    border-bottom: 2px solid #ede9fe; padding-bottom: 4px; margin-bottom: 6px;
    display: flex; align-items: center; gap: 4px; text-transform: uppercase; letter-spacing: 0.05em;
  }
  .sh-badge { font-size: 8px; font-weight: 700; padding: 1px 6px; border-radius: 999px; }
  .sh-badge.red   { background: #fee2e2; color: #b91c1c; }
  .sh-badge.blue  { background: #dbeafe; color: #1e40af; }

  /* ── Match cards ── */
  .result-card { border: 1px solid #e5e7eb; border-radius: 6px; padding: 7px 9px; margin-bottom: 5px; }
  .card-top    { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 4px; }
  .card-name   { font-size: 9px; font-weight: 700; color: #111827; line-height: 1.3; max-width: 75%; }
  .sim-pill    { font-size: 8px; font-weight: 700; padding: 2px 5px; border-radius: 999px; white-space: nowrap; }
  .sim-high    { background: #fef2f2; color: #dc2626; border: 1px solid #fecaca; }
  .sim-medium  { background: #fffbeb; color: #d97706; border: 1px solid #fde68a; }
  .sim-low     { background: #f0fdf4; color: #16a34a; border: 1px solid #bbf7d0; }
  .card-badges { display: flex; flex-wrap: wrap; gap: 3px; margin-bottom: 4px; }
  .badge       { font-size: 7.5px; padding: 1px 5px; border-radius: 999px; border: 1px solid #e5e7eb; color: #374151; background: #f9fafb; }
  .badge.red   { background: #fef2f2; border-color: #fecaca; color: #b91c1c; }
  .kv-grid     { display: grid; grid-template-columns: 60px 1fr; row-gap: 2px; font-size: 8px; }
  .kv-label    { color: #6b7280; font-weight: 500; }
  .kv-val      { color: #111827; word-break: break-word; }

  /* ── Adverse media card (inside column) ── */
  .media-card  { border: 1px solid #dbeafe; border-radius: 6px; padding: 7px 9px; margin-bottom: 5px; background: #f0f9ff; }
  .media-title { font-size: 8.5px; font-weight: 600; color: #1d4ed8; margin-bottom: 3px; line-height: 1.3; }
  .media-meta  { font-size: 7.5px; color: #4b5563; }

  .empty-state { text-align: center; padding: 14px 8px; color: #9ca3af; font-size: 8.5px;
                 border: 1px dashed #e5e7eb; border-radius: 6px; }

  /* ── More-results note ── */
  .more-note { font-size: 7.5px; color: #6b7280; text-align: center; margin-top: 2px; font-style: italic; }

  /* ── Disclaimer ── */
  .disclaimer { margin-top: 8px; padding: 8px 12px; background: #f9fafb; border: 1px solid #e5e7eb;
                border-radius: 6px; font-size: 7.5px; color: #6b7280; line-height: 1.5; }
</style>
</head>
<body>
");

            // ── Header ──────────────────────────────────────────────────────
            sb.Append($@"
<div class='report-header'>
  <div class='logo-row'>
    {logoHtml}
    <div>
      <h1>Sanction Screening Report</h1>
      <div class='sub'>Unified Payments · Compliance &amp; AML</div>
    </div>
  </div>
  <div class='meta-grid'>
    <div class='meta-cell'><div class='mc-label'>Search Term</div><div class='mc-val'>{H(searchTerm)}</div></div>
    <div class='meta-cell'><div class='mc-label'>Field</div><div class='mc-val'>{H(searchField?.ToUpperInvariant())}</div></div>
    <div class='meta-cell'><div class='mc-label'>Threshold</div><div class='mc-val'>{(threshold * 100):F0}%</div></div>
    <div class='meta-cell'><div class='mc-label'>Screened At</div><div class='mc-val'>{timestamp:dd MMM yyyy HH:mm} UTC</div></div>
  </div>
</div>
");

            // ── Summary bar ─────────────────────────────────────────────────
            sb.Append($@"
<div class='summary-bar'>
  <div class='sum-card sum-sanction'>
    <div class='sc-count'>{sanctions.Count}</div>
    <div class='sc-label'>Sanction Matches</div>
  </div>
  <div class='sum-card sum-media'>
    <div class='sc-count'>{adverseMedia.Count}</div>
    <div class='sc-label'>Adverse Media Items</div>
  </div>
</div>
");

            // ── Verdict ─────────────────────────────────────────────────────
            sb.Append(hasHits
                ? @"<div class='verdict verdict-hit'>
  <div class='verdict-icon'>⚠</div>
  <div>
    <div class='verdict-label'>Potential Match(es) Detected</div>
    <div class='verdict-sub'>Manual review and escalation required before proceeding.</div>
  </div>
</div>"
                : @"<div class='verdict verdict-clear'>
  <div class='verdict-icon'>✓</div>
  <div>
    <div class='verdict-label'>No Direct Sanction Matches</div>
    <div class='verdict-sub'>Review adverse media below if applicable. Continue with standard due diligence.</div>
  </div>
</div>");

            // ── 2-column matches grid ────────────────────────────────────────
            sb.Append("<div class='matches-grid'>");

            // ── Column 1: Sanction Matches ───────────────────────────────────
            sb.Append($@"
<div class='match-col'>
  <div class='col-heading'>
    Sanction Matches
    <span class='sh-badge red'>{sanctions.Count}</span>
  </div>");

            if (top5Sanctions.Count == 0)
            {
                sb.Append("<div class='empty-state'>No sanction matches found.</div>");
            }
            else
            {
                foreach (var row in top5Sanctions)
                {
                    var e = row.sanction_item;
                    if (e == null) continue;

                    string primaryName = e.Names?.FirstOrDefault() ?? e.PrimaryName ?? "—";
                    string addresses = string.Join("; ", e.Addresses ?? Enumerable.Empty<string>());
                    string simStr = row.similarity ?? "—";

                    sb.Append($@"
<div class='result-card'>
  <div class='card-top'>
    <div class='card-name'>{H(primaryName)}</div>
    <span class='sim-pill {SimClass(simStr)}'>{H(simStr)}</span>
  </div>
  <div class='card-badges'>
    <span class='badge red'>{H(e.Source)}</span>
    <span class='badge'>{H(e.SubjectType)}</span>
    {(!string.IsNullOrWhiteSpace(e.ReferenceNumber) ? $"<span class='badge'>Ref: {H(e.ReferenceNumber)}</span>" : "")}
  </div>
  <div class='kv-grid'>
    {KvRow("Address", addresses)}
    {KvRow("Designated", e.DateDesignated)}
    {KvRow("Sanction", e.SanctionImposed)}
    {KvRow("Comments", e.Comments)}
  </div>
</div>");
                }

                if (sanctions.Count > 5)
                    sb.Append($"<div class='more-note'>+ {sanctions.Count - 5} more match(es) — see full report</div>");
            }

            sb.Append("</div>"); // end sanctions column

            // ── Column 2: Adverse Media ──────────────────────────────────────
            sb.Append($@"
<div class='match-col'>
  <div class='col-heading'>
    Adverse Media
    <span class='sh-badge blue'>{adverseMedia.Count}</span>
  </div>");

            if (top5Media.Count == 0)
            {
                sb.Append("<div class='empty-state'>No adverse media found.</div>");
            }
            else
            {
                foreach (var item in top5Media)
                {
                    string pubDate = item.PublishedDate != default
                        ? item.PublishedDate.ToString("dd MMM yyyy")
                        : "";

                    sb.Append($@"
<div class='media-card'>
  <div class='media-title'>{H(item.Title)}</div>
  <div class='media-meta'>
    <strong>{H(item.Source)}</strong>
    {(!string.IsNullOrWhiteSpace(pubDate) ? $" · {H(pubDate)}" : "")}
  </div>
</div>");
                }

                if (adverseMedia.Count > 5)
                    sb.Append($"<div class='more-note'>+ {adverseMedia.Count - 5} more item(s) — see full report</div>");
            }

            sb.Append("</div>"); // end media column

            sb.Append("</div>"); // end matches-grid

            // ── Disclaimer ───────────────────────────────────────────────────
            sb.Append(@"
<div class='disclaimer'>
  <strong>Disclaimer:</strong> This report is generated automatically by the Unified Payments Sanction Screening System
  and is intended for internal compliance use only. Results are based on fuzzy-matching algorithms and may include
  false positives. All matches must be independently verified by a qualified compliance officer before any action
  is taken. Unified Payments accepts no liability for decisions made solely on the basis of this report.
</div>
</body></html>");

            return sb.ToString();
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private static string H(string? s) =>
            System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

        private static string KvRow(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return $"<div class='kv-label'>{H(label)}</div><div class='kv-val'>{H(value)}</div>";
        }

        private static string SimClass(string simStr)
        {
            if (double.TryParse(simStr.TrimEnd('%'), out var val))
            {
                if (val >= 80) return "sim-high";
                if (val >= 50) return "sim-medium";
                return "sim-low";
            }
            return "sim-low";
        }

        private static string SanitiseFilename(string s)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return cleaned.Length > 40 ? cleaned[..40] : cleaned;
        }
    }
}