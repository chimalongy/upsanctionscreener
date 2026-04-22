using Upsanctionscreener.Classess.Interfaces;
using Upsanctionscreener.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
namespace Upsanctionscreener.Classess.Parsers
{

    public class OfacSanctionParser : ISanctionParser
    {
        public string Source => "OFAC";

        public List<SanctionEntry> Parse(string xmlContent)
        {
            var entries = new List<SanctionEntry>();
            var doc = XDocument.Parse(xmlContent);
            XNamespace ns = "https://sanctionslistservice.ofac.treas.gov/api/PublicationPreview/exports/XML";

            foreach (var sdn in doc.Descendants(ns + "sdnEntry"))
            {
                var entry = new SanctionEntry
                {
                    Source = Source,
                    SubjectType = sdn.Element(ns + "sdnType")?.Value,
                    ID = sdn.Element(ns + "uid")?.Value,
                    ReferenceNumber = sdn.Element(ns + "uid")?.Value,
                    Comments = sdn.Element(ns + "remarks")?.Value,
                    CallSign = sdn.Element(ns + "callSign")?.Value,
                    VesselType = sdn.Element(ns + "vesselType")?.Value,
                    VesselFlag = sdn.Element(ns + "vesselFlag")?.Value,
                    VesselOwner = sdn.Element(ns + "vesselOwner")?.Value,
                    GrossRegisteredTonnage = sdn.Element(ns + "tonnage")?.Value,
                };

                // Primary name
                var lastName = sdn.Element(ns + "lastName")?.Value;
                var firstName = sdn.Element(ns + "firstName")?.Value;
                var primaryName = string.Join(", ", new[] { lastName, firstName }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(primaryName))
                    entry.Names.Add(primaryName);

                // AKAs
                foreach (var aka in sdn.Descendants(ns + "aka"))
                {
                    var akaName = string.Join(", ", new[]
                    {
                        aka.Element(ns + "lastName")?.Value,
                        aka.Element(ns + "firstName")?.Value
                    }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (!string.IsNullOrWhiteSpace(akaName))
                        entry.Names.Add(akaName);
                }

                // Addresses
                foreach (var addr in sdn.Descendants(ns + "address"))
                {
                    var parts = new[]
                    {
                        addr.Element(ns + "address1")?.Value,
                        addr.Element(ns + "address2")?.Value,
                        addr.Element(ns + "address3")?.Value,
                        addr.Element(ns + "city")?.Value,
                        addr.Element(ns + "stateOrProvince")?.Value,
                        addr.Element(ns + "postalCode")?.Value,
                        addr.Element(ns + "country")?.Value,
                    }.Where(s => !string.IsNullOrWhiteSpace(s));
                    var addrStr = string.Join(", ", parts);
                    if (!string.IsNullOrWhiteSpace(addrStr))
                        entry.Addresses.Add(addrStr);
                }

                // IDs
                foreach (var id in sdn.Descendants(ns + "id"))
                {
                    var idVal = $"{id.Element(ns + "idType")?.Value}: {id.Element(ns + "idNumber")?.Value}";
                    if (!string.IsNullOrWhiteSpace(idVal.Trim(':', ' ')))
                        entry.IdList.Add(idVal);
                }

                // Programs as SanctionImposed
                var programs = sdn.Descendants(ns + "program").Select(p => p.Value);
                entry.SanctionImposed = string.Join("; ", programs);

                entries.Add(entry);
            }

            return entries;
        }
    }

}
