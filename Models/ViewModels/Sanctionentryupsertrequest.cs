using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Upsanctionscreener.Models.ViewModels
{
    /// <summary>
    /// Payload sent from the Nigerian Sanction List UI when adding or editing an entry.
    /// </summary>
    public class SanctionEntryUpsertRequest
    {
        // ── Core ──────────────────────────────────────────────────────────
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("subjectType")]
        public string? SubjectType { get; set; }

        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("referenceNumber")]
        public string? ReferenceNumber { get; set; }

        [JsonPropertyName("dateDesignated")]
        public string? DateDesignated { get; set; }

        [JsonPropertyName("sanctionImposed")]
        public string? SanctionImposed { get; set; }

        [JsonPropertyName("comments")]
        public string? Comments { get; set; }

        // ── Lists ─────────────────────────────────────────────────────────
        [JsonPropertyName("names")]
        public List<string>? Names { get; set; }

        [JsonPropertyName("addresses")]
        public List<string>? Addresses { get; set; }

        [JsonPropertyName("phoneNumbers")]
        public List<string>? PhoneNumbers { get; set; }

        [JsonPropertyName("emailAddresses")]
        public List<string>? EmailAddresses { get; set; }

        [JsonPropertyName("positions")]
        public List<string>? Positions { get; set; }

        [JsonPropertyName("idList")]
        public List<string>? IdList { get; set; }

        // ── Vessel ────────────────────────────────────────────────────────
        [JsonPropertyName("callSign")]
        public string? CallSign { get; set; }

        [JsonPropertyName("vesselType")]
        public string? VesselType { get; set; }

        [JsonPropertyName("vesselFlag")]
        public string? VesselFlag { get; set; }

        [JsonPropertyName("vesselOwner")]
        public string? VesselOwner { get; set; }

        [JsonPropertyName("grossRegisteredTonnage")]
        public string? GrossRegisteredTonnage { get; set; }

        // ── Edit metadata ─────────────────────────────────────────────────
        /// <summary>True when updating an existing entry; false for new entries.</summary>
        [JsonPropertyName("isEdit")]
        public bool IsEdit { get; set; }

        /// <summary>The original ID of the entry being edited (may differ from Id if user changed it).</summary>
        [JsonPropertyName("originalId")]
        public string? OriginalId { get; set; }
    }
}
