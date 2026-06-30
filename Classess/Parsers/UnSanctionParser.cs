using Upsanctionscreener.Classess.Interfaces;
using Upsanctionscreener.Models;
using System.Collections.Generic;
using System.Linq;
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
                    ID = GetValue(node, ns, "DATAID"),
                    ReferenceNumber = GetValue(node, ns, "REFERENCE_NUMBER"),
                    DateDesignated = GetValue(node, ns, "LISTED_ON"),
                    Comments = GetValue(node, ns, "COMMENTS1"),
                };

                // ── NAMES ────────────────────────────────────────────────
                var primaryName = BuildPrimaryName(node, ns, isIndividual);
                if (!string.IsNullOrWhiteSpace(primaryName))
                    entry.Names.Add(primaryName);

                // BUG 2 FIX: aliases use <ALIAS_NAME>, not FIRST/SECOND/THIRD_NAME
                var aliasTag = isIndividual ? "INDIVIDUAL_ALIAS" : "ENTITY_ALIAS";
                foreach (var aliasNode in node.Elements(ns + aliasTag))
                {
                    var aliasName = GetValue(aliasNode, ns, "ALIAS_NAME");
                    if (!string.IsNullOrWhiteSpace(aliasName) && !entry.Names.Contains(aliasName))
                        entry.Names.Add(aliasName);
                }

                // ── ADDRESSES ────────────────────────────────────────────
                var addrTag = isIndividual ? "INDIVIDUAL_ADDRESS" : "ENTITY_ADDRESS";
                foreach (var addr in node.Elements(ns + addrTag))
                {
                    var parts = new[]
                    {
                        GetValue(addr, ns, "STREET"),
                        GetValue(addr, ns, "CITY"),
                        GetValue(addr, ns, "STATE_PROVINCE"),
                        GetValue(addr, ns, "ZIP_CODE"),
                        GetValue(addr, ns, "COUNTRY"),
                    }.Where(s => !string.IsNullOrWhiteSpace(s));

                    var addrStr = string.Join(", ", parts);
                    if (!string.IsNullOrWhiteSpace(addrStr))
                        entry.Addresses.Add(addrStr);
                }

                // ── DOCUMENT IDs ─────────────────────────────────────────
                // BUG 3 FIX: no DATE_OF_ISSUE field — use NOTE for extra info
                var docTag = isIndividual ? "INDIVIDUAL_DOCUMENT" : "ENTITY_DOCUMENT";
                foreach (var docNode in node.Elements(ns + docTag))
                {
                    var docType = GetValue(docNode, ns, "TYPE_OF_DOCUMENT");
                    var docNumber = GetValue(docNode, ns, "NUMBER");
                    var docCountry = GetValue(docNode, ns, "ISSUING_COUNTRY");
                    var docNote = GetValue(docNode, ns, "NOTE");

                    var parts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(docType)) parts.Add(docType);
                    if (!string.IsNullOrWhiteSpace(docNumber)) parts.Add(docNumber);
                    if (!string.IsNullOrWhiteSpace(docCountry)) parts.Add($"Country: {docCountry}");
                    if (!string.IsNullOrWhiteSpace(docNote)) parts.Add($"Note: {docNote}");

                    var idStr = string.Join(", ", parts);
                    if (!string.IsNullOrWhiteSpace(idStr))
                        entry.IdList.Add(idStr);
                }

                // ── DESIGNATION / POSITIONS ──────────────────────────────
                // BUG 1 FIX: <DESIGNATION> wraps <VALUE>, not direct text
                foreach (var valEl in node.Elements(ns + "DESIGNATION")
                                          .SelectMany(d => d.Elements(ns + "VALUE")))
                {
                    var val = valEl.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(val))
                        entry.Positions.Add(val);
                }

                entries.Add(entry);
            }

            return entries;
        }

        /// <summary>Gets direct child element text, trimmed.</summary>
        private static string GetValue(XElement parent, XNamespace ns, string elementName)
            => parent.Element(ns + elementName)?.Value?.Trim() ?? string.Empty;

        /// <summary>
        /// Builds primary name.
        /// Individuals: FIRST_NAME + SECOND_NAME + THIRD_NAME + FOURTH_NAME (on the entity itself).
        /// Entities: FIRST_NAME holds the full organisation name.
        /// </summary>
        private static string BuildPrimaryName(XElement node, XNamespace ns, bool isIndividual)
        {
            if (isIndividual)
            {
                var parts = new[]
                {
                    GetValue(node, ns, "FIRST_NAME"),
                    GetValue(node, ns, "SECOND_NAME"),
                    GetValue(node, ns, "THIRD_NAME"),
                    GetValue(node, ns, "FOURTH_NAME"),
                }.Where(s => !string.IsNullOrWhiteSpace(s));

                return string.Join(" ", parts);
            }
            else
            {
                return GetValue(node, ns, "FIRST_NAME");
            }
        }
    }
}