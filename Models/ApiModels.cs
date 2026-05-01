// Models/ApiModels.cs
using System.Text.Json.Serialization;

namespace Upsanctionscreener.Models.Api
{
    // ── Single Scan ───────────────────────────────────────────────────────────
    public class SingleScanApiRequest
    {
        /// <summary>The name, address, email, or phone number to screen.</summary>
        [JsonPropertyName("search_term")]
        public string SearchTerm { get; set; } = string.Empty;

        /// <summary>Field to match against: name | address | email | phone</summary>
        [JsonPropertyName("search_field")]
        public string SearchField { get; set; } = "name";

        /// <summary>Match threshold 0.0 – 1.0.  Defaults to your saved setting.</summary>
        [JsonPropertyName("threshold")]
        public double? Threshold { get; set; }
    }

    // ── Bulk Scan ─────────────────────────────────────────────────────────────
    public class BulkScanApiRequest
    {
        /// <summary>List of items to screen.</summary>
        [JsonPropertyName("items")]
        public List<BulkScanItem> Items { get; set; } = new();

        /// <summary>Field to match against: name | address | email | phone</summary>
        [JsonPropertyName("search_field")]
        public string SearchField { get; set; } = "name";

        /// <summary>Match threshold 0.0 – 1.0.  Defaults to your saved setting.</summary>
        [JsonPropertyName("threshold")]
        public double? Threshold { get; set; }
    }

    public class BulkScanItem
    {
        /// <summary>Your own reference ID for this record.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>The value to screen.</summary>
        [JsonPropertyName("search_term")]
        public string SearchTerm { get; set; } = string.Empty;
    }

    // ── Shared result shapes ──────────────────────────────────────────────────
    public class SanctionHit
    {
        [JsonPropertyName("similarity")] public string Similarity { get; set; } = string.Empty;
        [JsonPropertyName("sanction_item")] public object? SanctionItem { get; set; }
    }

    public class PepHit
    {
        [JsonPropertyName("similarity")] public string Similarity { get; set; } = string.Empty;
        [JsonPropertyName("pep_item")] public object? PepItem { get; set; }
    }

    public class BulkScanResultItem
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("search_term")] public string SearchTerm { get; set; } = string.Empty;
        [JsonPropertyName("sanctions")] public List<SanctionHit> Sanctions { get; set; } = new();
        [JsonPropertyName("peps")] public List<PepHit> Peps { get; set; } = new();
        [JsonPropertyName("has_hits")] public bool HasHits { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}