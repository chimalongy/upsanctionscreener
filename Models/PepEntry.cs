using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Upsanctionscreener.Models
{
    public class PepEntry
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("fullname")]
        public string FullName { get; set; } = string.Empty;

        [JsonPropertyName("merchant_name")]
        public string MerchantName { get; set; } = string.Empty;

        [JsonPropertyName("DOO")]
        public string? DOO { get; set; }

        [JsonPropertyName("transaction_monitoring_frequency")]
        public string TransactionMonitoringFrequency { get; set; } = "DAILY";

        [JsonPropertyName("monitoring_category")]
        public string MonitoringCategory { get; set; } = "HIGH RISK MERCHANT";

        [JsonPropertyName("merchant_ids")]
        public List<string> MerchantIds { get; set; } = new();

        [JsonPropertyName("merchant_location")]
        public string? MerchantLocation { get; set; }
    }

    public class PepUpsertRequest
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("fullname")]
        public string? Fullname { get; set; }

        [JsonPropertyName("merchant_name")]
        public string? MerchantName { get; set; }

        [JsonPropertyName("DOO")]
        public string? DOO { get; set; }

        [JsonPropertyName("transaction_monitoring_frequency")]
        public string? TransactionMonitoringFrequency { get; set; }

        [JsonPropertyName("monitoring_category")]
        public string? MonitoringCategory { get; set; }

        [JsonPropertyName("merchant_ids")]
        public List<string>? MerchantIds { get; set; }

        [JsonPropertyName("merchant_location")]
        public string? MerchantLocation { get; set; }
    }


}
