using Upsanctionscreener.Classess.Interfaces;
using Upsanctionscreener.Models;
using System.Collections.Generic;
using System.Linq;
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

            bool isVersion1_1 = doc.Root?.Attribute("xmlns") != null
                && doc.Root.GetDefaultNamespace().NamespaceName.Contains("1_1");

            foreach (var subject in doc.Descendants(ns + "sanctionEntity"))
            {
                // ── Identity ──────────────────────────────────────────────
                // v1.1 uses euReferenceNumber; FULL uses logicalId
                string id = subject.Attribute("euReferenceNumber")?.Value
                         ?? subject.Attribute("logicalId")?.Value
                         ?? string.Empty;

                // SubjectType: v1.1 is an attribute on <sanctionEntity>;
                //              FULL is a child element <subjectType code="person" classificationCode="P"/>
                string subjectType = subject.Attribute("subjectType")?.Value
                                  ?? subject.Element(ns + "subjectType")?.Attribute("code")?.Value
                                  ?? string.Empty;

                // DateDesignated: v1.1 has it on <sanctionEntity>;
                //                 FULL puts it on the first <regulation> child
                string dateDesignated = subject.Attribute("designationDate")?.Value
                                     ?? subject.Element(ns + "regulation")?.Attribute("entryIntoForceDate")?.Value
                                     ?? string.Empty;

                // Programme / sanction regime lives on <regulation> in both schemas
                string programme = subject.Element(ns + "regulation")?.Attribute("programme")?.Value
                                ?? string.Empty;

                var entry = new SanctionEntry
                {
                    Source = Source,
                    ID = id,
                    ReferenceNumber = id,
                    SubjectType = subjectType,
                    DateDesignated = dateDesignated,
                    SanctionImposed = subject.Attribute("unitedNationId")?.Value ?? programme,
                    Comments = subject.Element(ns + "remark")?.Value ?? string.Empty,
                };

                // ── Names ─────────────────────────────────────────────────
                foreach (var nameAlias in subject.Descendants(ns + "nameAlias"))
                {
                    var fullName = nameAlias.Attribute("wholeName")?.Value;

                    if (string.IsNullOrWhiteSpace(fullName))
                    {
                        fullName = string.Join(" ", new[]
                        {
                            nameAlias.Attribute("firstName")?.Value,
                            nameAlias.Attribute("middleName")?.Value,
                            nameAlias.Attribute("lastName")?.Value,
                        }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    }

                    if (!string.IsNullOrWhiteSpace(fullName))
                        entry.Names.Add(fullName);
                }

                // ── Addresses ─────────────────────────────────────────────
                // Attribute names are identical in both schemas
                foreach (var addr in subject.Descendants(ns + "address"))
                {
                    var parts = new[]
                    {
                        addr.Attribute("street")?.Value,
                        addr.Attribute("city")?.Value,
                        addr.Attribute("zipCode")?.Value,
                        addr.Attribute("countryDescription")?.Value,
                    }.Where(s => !string.IsNullOrWhiteSpace(s));

                    var addrStr = string.Join(", ", parts);
                    if (!string.IsNullOrWhiteSpace(addrStr))
                        entry.Addresses.Add(addrStr);
                }

                // ── Identifications ───────────────────────────────────────
                // Attribute names are identical in both schemas
                foreach (var idEl in subject.Descendants(ns + "identification"))
                {
                    var typeDesc = idEl.Attribute("identificationTypeDescription")?.Value;
                    var number = idEl.Attribute("number")?.Value;

                    if (!string.IsNullOrWhiteSpace(typeDesc) || !string.IsNullOrWhiteSpace(number))
                        entry.IdList.Add($"{typeDesc}: {number}");
                }

                entries.Add(entry);
            }

            return entries;
        }
    }
}