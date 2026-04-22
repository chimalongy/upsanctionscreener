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
    public class UnSanctionParser : ISanctionParser
    {
        public string Source => "UN";

        public List<SanctionEntry> Parse(string xmlContent)
        {
            var entries = new List<SanctionEntry>();
            var doc = XDocument.Parse(xmlContent);
            XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            var individuals = doc.Descendants(ns + "INDIVIDUAL");
            var entities = doc.Descendants(ns + "ENTITY");

            foreach (var node in individuals.Concat(entities))
            {
                var isIndividual = node.Name.LocalName == "INDIVIDUAL";
                var entry = new SanctionEntry
                {
                    Source = Source,
                    SubjectType = isIndividual ? "Individual" : "Entity",
                    ID = node.Element(ns + "DATAID")?.Value,
                    ReferenceNumber = node.Element(ns + "REFERENCE_NUMBER")?.Value,
                    DateDesignated = node.Element(ns + "LISTED_ON")?.Value,
                    Comments = node.Element(ns + "COMMENTS1")?.Value,
                };

                // Names
                foreach (var nameNode in node.Elements(ns + (isIndividual ? "INDIVIDUAL_ALIAS" : "ENTITY_ALIAS"))
                    .Prepend(node)) // include primary name
                {
                    string? name = isIndividual
                        ? string.Join(" ", new[]
                        {
                            nameNode.Element(ns + "FIRST_NAME")?.Value,
                            nameNode.Element(ns + "SECOND_NAME")?.Value,
                            nameNode.Element(ns + "THIRD_NAME")?.Value,
                            nameNode.Element(ns + "FOURTH_NAME")?.Value,
                        }.Where(s => !string.IsNullOrWhiteSpace(s)))
                        : nameNode.Element(ns + "FIRST_NAME")?.Value
                          ?? nameNode.Element(ns + "ALIAS_NAME")?.Value;

                    if (!string.IsNullOrWhiteSpace(name))
                        entry.Names.Add(name);
                }

                // Addresses
                foreach (var addr in node.Elements(ns + "INDIVIDUAL_ADDRESS")
                    .Concat(node.Elements(ns + "ENTITY_ADDRESS")))
                {
                    var parts = new[]
                    {
                        addr.Element(ns + "STREET")?.Value,
                        addr.Element(ns + "CITY")?.Value,
                        addr.Element(ns + "STATE_PROVINCE")?.Value,
                        addr.Element(ns + "ZIP_CODE")?.Value,
                        addr.Element(ns + "COUNTRY")?.Value
                    }.Where(s => !string.IsNullOrWhiteSpace(s));
                    var addrStr = string.Join(", ", parts);
                    if (!string.IsNullOrWhiteSpace(addrStr))
                        entry.Addresses.Add(addrStr);
                }

                // Document IDs
                foreach (var doc2 in node.Elements(ns + "INDIVIDUAL_DOCUMENT"))
                {
                    var id = $"{doc2.Element(ns + "TYPE_OF_DOCUMENT")?.Value}: {doc2.Element(ns + "NUMBER")?.Value}";
                    if (!string.IsNullOrWhiteSpace(id.Trim(':', ' ')))
                        entry.IdList.Add(id);
                }

                entries.Add(entry);
            }

            return entries;
        }
    }
}
