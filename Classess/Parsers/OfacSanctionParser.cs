using Upsanctionscreener.Classess.Interfaces;
using Upsanctionscreener.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
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
                // NOTE: OFAC has no dedicated gender element — it's smuggled in here as
                // an <id> entry with idType "Gender" (idNumber "Male"/"Female"). That
                // entry is pulled out into entry.Gender below and excluded from IdList,
                // rather than left in as a misleading "Gender: Male" identifier.
                foreach (var id in sdn.Descendants(ns + "id"))
                {
                    var idType = id.Element(ns + "idType")?.Value;

                    if (string.Equals(idType?.Trim(), "Gender", StringComparison.OrdinalIgnoreCase))
                    {
                        var genderVal = id.Element(ns + "idNumber")?.Value?.Trim();
                        if (!string.IsNullOrWhiteSpace(genderVal))
                            entry.Gender = genderVal;
                        continue;
                    }

                    var idVal = $"{idType}: {id.Element(ns + "idNumber")?.Value}";
                    if (!string.IsNullOrWhiteSpace(idVal.Trim(':', ' ')))
                        entry.IdList.Add(idVal);
                }

                // Date of birth
                // OFAC's raw text comes in several shapes ("10 Dec 1948", "1948",
                // "Apr 1961", "circa 1957", "circa 07 Jul 1966", or ranges like
                // "01 Jan 1946 to 31 Dec 1946" / "1943 to 1944" / "Mar 1955 to Mar 1956").
                // NormalizeDob converts each piece to ISO (yyyy-MM-dd / yyyy-MM / yyyy)
                // wherever the precision allows, matching the UN/EU parsers' style,
                // and falls back to the original text if a shape isn't recognised
                // rather than silently dropping data.
                // An entry can have several <dateOfBirthItem> blocks; each is flagged
                // <mainEntry>true/false so we surface the main one first, mirroring
                // how PrimaryName treats Names[0] as canonical.
                var dobItems = sdn.Descendants(ns + "dateOfBirthItem")
                    .Select(d => new
                    {
                        Value = d.Element(ns + "dateOfBirth")?.Value?.Trim(),
                        IsMain = string.Equals(d.Element(ns + "mainEntry")?.Value?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
                    })
                    .Where(d => !string.IsNullOrWhiteSpace(d.Value))
                    .OrderByDescending(d => d.IsMain);

                foreach (var dob in dobItems)
                    entry.DateofBirth.Add(NormalizeDob(dob.Value));

                // Programs as SanctionImposed
                var programs = sdn.Descendants(ns + "program").Select(p => p.Value);
                entry.SanctionImposed = string.Join("; ", programs);

                entries.Add(entry);
            }

            return entries;
        }

        /// <summary>
        /// Converts an OFAC date-of-birth string to ISO format (yyyy-MM-dd, or
        /// yyyy-MM / yyyy when the source is less precise). Strips any "circa "
        /// qualifier (only the underlying date is kept) and normalizes "X to Y"
        /// ranges by handling each side independently.
        /// Falls back to the original text unchanged if it doesn't match any
        /// recognised shape, so nothing is silently lost.
        /// </summary>
        private static string NormalizeDob(string raw)
        {
            var value = raw.Trim();

            // Strip a leading "circa " qualifier — only the underlying date value
            // is kept, not the approximation marker.
            if (value.StartsWith("circa ", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("circa ".Length).Trim();

            var toSplit = value.Split(new[] { " to " }, StringSplitOptions.None);
            if (toSplit.Length == 2)
                return $"{NormalizeSingleDate(toSplit[0].Trim())} to {NormalizeSingleDate(toSplit[1].Trim())}";

            return NormalizeSingleDate(value);
        }

        /// <summary>
        /// Normalizes a single (non-range) date token to ISO format:
        ///   "10 Dec 1948" -> "1948-12-10"
        ///   "Apr 1961"    -> "1961-04"
        ///   "1948"        -> "1948"
        /// Returns the original token unchanged if none of these shapes match.
        /// </summary>
        private static string NormalizeSingleDate(string token)
        {
            if (DateTime.TryParseExact(token, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fullDate))
                return fullDate.ToString("yyyy-MM-dd");

            if (DateTime.TryParseExact(token, "MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthYear))
                return monthYear.ToString("yyyy-MM");

            if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out _) && token.Length == 4)
                return token;

            return token;
        }
    }

}