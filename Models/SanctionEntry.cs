using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Upsanctionscreener.Models
{
    public class SanctionEntry
    {
        public string ID { get; set; } = string.Empty;
        public string SubjectType { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string ReferenceNumber { get; set; } = string.Empty;
        public string DateDesignated { get; set; } = string.Empty;
        public string SanctionImposed { get; set; } = string.Empty;
        public string Comments { get; set; } = string.Empty;
        public List<string> Names { get; set; } = new List<string>();
        public List<string> Addresses { get; set; } = new List<string>();
        public List<string> PhoneNumbers { get; set; } = new List<string>();
        public List<string> EmailAddresses { get; set; } = new List<string>();
        public List<string> Positions { get; set; } = new List<string>();
        public List<string> IdList { get; set; } = new List<string>();
        public string? CallSign { get; set; }
        public string? VesselType { get; set; }
        public string? VesselFlag { get; set; }
        public string? VesselOwner { get; set; }
        public string? GrossRegisteredTonnage { get; set; }

        // Derived helper — always reflects Names[0] without needing a separate field
        public string PrimaryName => Names.FirstOrDefault() ?? string.Empty;
    }

}
