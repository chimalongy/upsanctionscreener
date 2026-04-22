using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Upsanctionscreener.Models
{


    // UBO Model Classes
    public class UboEntry
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("full_name")]
        public string FullName { get; set; } = string.Empty;

        [JsonPropertyName("dob")]
        public string? Dob { get; set; }

        [JsonPropertyName("nationality")]
        public string? Nationality { get; set; }

        [JsonPropertyName("gender")]
        public string? Gender { get; set; }

        [JsonPropertyName("address")]
        public string? Address { get; set; }

        [JsonPropertyName("percentage_of_ownership")]
        public object? PercentageOfOwnership { get; set; }

        [JsonPropertyName("nature_of_control")]
        public string? NatureOfControl { get; set; }

        [JsonPropertyName("id_number")]
        public string? IdNumber { get; set; }

        [JsonPropertyName("remark")]
        public string? Remark { get; set; }

        [JsonPropertyName("company")]
        public string? Company { get; set; }

        [JsonPropertyName("merchant_ids")]
        public List<string> MerchantIds { get; set; } = new();
    }

    public class UboUpsertRequest
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("full_name")]
        public string? FullName { get; set; }

        [JsonPropertyName("dob")]
        public string? Dob { get; set; }

        [JsonPropertyName("nationality")]
        public string? Nationality { get; set; }

        [JsonPropertyName("gender")]
        public string? Gender { get; set; }

        [JsonPropertyName("address")]
        public string? Address { get; set; }

        [JsonPropertyName("percentage_of_ownership")]
        public object? PercentageOfOwnership { get; set; }

        [JsonPropertyName("nature_of_control")]
        public string? NatureOfControl { get; set; }

        [JsonPropertyName("id_number")]
        public string? IdNumber { get; set; }

        [JsonPropertyName("remark")]
        public string? Remark { get; set; }

        [JsonPropertyName("company")]
        public string? Company { get; set; }

        [JsonPropertyName("merchant_ids")]
        public List<string>? MerchantIds { get; set; }
    }













}
