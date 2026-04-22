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
    public class UkSanctionParser : ISanctionParser
    {
        public string Source => "UK";

        public List<SanctionEntry> Parse(string xmlContent)
        {
            var entries = new List<SanctionEntry>();
            var doc = XDocument.Parse(xmlContent);
            XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            foreach (var des in doc.Descendants(ns + "Designation"))
            {
                var entry = new SanctionEntry
                {
                    Source = Source,
                    SubjectType = des.Element(ns + "GroupType")?.Value,
                    ID = des.Element(ns + "UniqueID")?.Value,
                    ReferenceNumber = des.Element(ns + "UKSanctionsListRef")?.Value,
                    DateDesignated = des.Element(ns + "DateDesignated")?.Value,
                    SanctionImposed = des.Element(ns + "RegimeName")?.Value,
                    Comments = des.Element(ns + "OtherInformation")?.Value,
                };

                // Names
                foreach (var nameNode in des.Descendants(ns + "Name"))
                {
                    var fullName = nameNode.Element(ns + "FullName")?.Value
                        ?? string.Join(" ", new[]
                        {
                            nameNode.Element(ns + "Title")?.Value,
                            nameNode.Element(ns + "GivenName")?.Value,
                            nameNode.Element(ns + "FamilyName")?.Value,
                        }.Where(s => !string.IsNullOrWhiteSpace(s)));

                    if (!string.IsNullOrWhiteSpace(fullName))
                        entry.Names.Add(fullName);
                }

                // Addresses
                foreach (var addr in des.Descendants(ns + "Address"))
                {
                    var parts = new[]
                    {
                        addr.Element(ns + "AddressLine1")?.Value,
                        addr.Element(ns + "AddressLine2")?.Value,
                        addr.Element(ns + "AddressLine3")?.Value,
                        addr.Element(ns + "AddressPostalCode")?.Value,
                        addr.Element(ns + "AddressCountry")?.Value,
                    }.Where(s => !string.IsNullOrWhiteSpace(s));
                    var addrStr = string.Join(", ", parts);
                    if (!string.IsNullOrWhiteSpace(addrStr))
                        entry.Addresses.Add(addrStr);
                }

                // Phone numbers
                foreach (var phone in des.Descendants(ns + "PhoneNumber"))
                    if (!string.IsNullOrWhiteSpace(phone.Value))
                        entry.PhoneNumbers.Add(phone.Value);

                // Emails
                foreach (var email in des.Descendants(ns + "EmailAddress"))
                    if (!string.IsNullOrWhiteSpace(email.Value))
                        entry.EmailAddresses.Add(email.Value);

                // Positions
                foreach (var pos in des.Descendants(ns + "Position"))
                    if (!string.IsNullOrWhiteSpace(pos.Value))
                        entry.Positions.Add(pos.Value);

                // IDs
                foreach (var id in des.Descendants(ns + "IndividualDocument")
                    .Concat(des.Descendants(ns + "EntityDocument")))
                {
                    var idVal = $"{id.Element(ns + "DocumentType")?.Value}: {id.Element(ns + "DocumentNumber")?.Value}";
                    if (!string.IsNullOrWhiteSpace(idVal.Trim(':', ' ')))
                        entry.IdList.Add(idVal);
                }

                entries.Add(entry);
            }

            return entries;
        }
    }
}
