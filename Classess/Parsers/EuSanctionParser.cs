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

    public class EuSanctionParser : ISanctionParser
    {
        public string Source => "EU";

        public List<SanctionEntry> Parse(string xmlContent)
        {
            var entries = new List<SanctionEntry>();
            var doc = XDocument.Parse(xmlContent);
            XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            foreach (var subject in doc.Descendants(ns + "sanctionEntity"))
            {
                var entry = new SanctionEntry
                {
                    Source = Source,
                    SubjectType = subject.Attribute("subjectType")?.Value,
                    ID = subject.Attribute("euReferenceNumber")?.Value,
                    ReferenceNumber = subject.Attribute("euReferenceNumber")?.Value,
                    DateDesignated = subject.Attribute("designationDate")?.Value,
                    SanctionImposed = subject.Attribute("unitedNationId")?.Value,
                    Comments = subject.Element(ns + "remark")?.Value,
                };

                // Names
                foreach (var nameAlias in subject.Descendants(ns + "nameAlias"))
                {
                    var fullName = nameAlias.Attribute("wholeName")?.Value
                        ?? string.Join(" ", new[]
                        {
                            nameAlias.Attribute("firstName")?.Value,
                            nameAlias.Attribute("middleName")?.Value,
                            nameAlias.Attribute("lastName")?.Value,
                        }.Where(s => !string.IsNullOrWhiteSpace(s)));

                    if (!string.IsNullOrWhiteSpace(fullName))
                        entry.Names.Add(fullName);
                }

                // Addresses
                foreach (var addr in subject.Descendants(ns + "address"))
                {
                    var parts = new[]
                    {
                        addr.Attribute("street")?.Value,
                        addr.Attribute("city")?.Value,
                        addr.Attribute("zipCode")?.Value,
                        addr.Attribute("countryDescription")?.Value
                    }.Where(s => !string.IsNullOrWhiteSpace(s));
                    var addrStr = string.Join(", ", parts);
                    if (!string.IsNullOrWhiteSpace(addrStr))
                        entry.Addresses.Add(addrStr);
                }

                // IDs
                foreach (var id in subject.Descendants(ns + "identification"))
                {
                    var idVal = $"{id.Attribute("identificationTypeDescription")?.Value}: {id.Attribute("number")?.Value}";
                    if (!string.IsNullOrWhiteSpace(idVal.Trim(':', ' ')))
                        entry.IdList.Add(idVal);
                }

                entries.Add(entry);
            }

            return entries;
        }
    }

}
