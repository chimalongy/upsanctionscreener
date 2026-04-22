using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Upsanctionscreener.Models
{
    public class SanctionMatchRow
    {
        // BKSearchResult fields
        public string EntryId { get; set; } = "";
        public string MatchedName { get; set; } = "";
        public double Similarity { get; set; }
        public int EditDistance { get; set; }

        // SanctionEntry fields
        public string ID { get; set; } = "";
        public string SubjectType { get; set; } = "";
        public string Source { get; set; } = "";
        public string ReferenceNumber { get; set; } = "";
        public string DateDesignated { get; set; } = "";
        public string SanctionImposed { get; set; } = "";
        public string Comments { get; set; } = "";

        public string Names { get; set; } = "";
        public string Addresses { get; set; } = "";
        public string PhoneNumbers { get; set; } = "";
        public string EmailAddresses { get; set; } = "";
        public string Positions { get; set; } = "";
        public string IdList { get; set; } = "";

        public string? CallSign { get; set; }
        public string? VesselType { get; set; }
        public string? VesselFlag { get; set; }
        public string? VesselOwner { get; set; }
        public string? GrossRegisteredTonnage { get; set; }
    }
}
