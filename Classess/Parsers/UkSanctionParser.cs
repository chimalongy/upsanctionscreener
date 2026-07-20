using Upsanctionscreener.Classess.Interfaces;
using Upsanctionscreener.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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
                    SubjectType = des.Element(ns + "IndividualEntityShip")?.Value ?? string.Empty,
                    ID = des.Element(ns + "UniqueID")?.Value,
                    ReferenceNumber = des.Element(ns + "UNReferenceNumber")?.Value
                                   ?? des.Element(ns + "OFSIGroupID")?.Value
                                   ?? string.Empty,
                    DateDesignated = des.Element(ns + "DateDesignated")?.Value,
                    SanctionImposed = des.Element(ns + "RegimeName")?.Value,
                    Comments = des.Element(ns + "OtherInformation")?.Value,
                };

                // ── Names ────────────────────────────────────────────────
                // Structure: <Names><Name><Name1>..<Name6> + <NameType>Primary Name/Alias</NameType></Name></Names>
                var primaryNames = new List<string>();
                var aliasNames = new List<string>();

                foreach (var nameNode in des.Element(ns + "Names")?.Elements(ns + "Name") ?? Enumerable.Empty<XElement>())
                {
                    var fullName = string.Join(" ", new[]
                    {
                        nameNode.Element(ns + "Name1")?.Value,
                        nameNode.Element(ns + "Name2")?.Value,
                        nameNode.Element(ns + "Name3")?.Value,
                        nameNode.Element(ns + "Name4")?.Value,
                        nameNode.Element(ns + "Name5")?.Value,
                        nameNode.Element(ns + "Name6")?.Value,
                    }.Where(s => !string.IsNullOrWhiteSpace(s)));

                    if (string.IsNullOrWhiteSpace(fullName))
                        continue;

                    var nameType = nameNode.Element(ns + "NameType")?.Value ?? string.Empty;

                    // NameType casing/wording is inconsistent in the feed
                    // (seen: "Primary Name", "Primary name", "Primary Name Variation", "Alias", "ALias")
                    if (nameType.Trim().Equals("Primary Name", StringComparison.OrdinalIgnoreCase)
                        || nameType.Trim().Equals("Primary name", StringComparison.OrdinalIgnoreCase))
                    {
                        primaryNames.Add(fullName);
                    }
                    else
                    {
                        aliasNames.Add(fullName);
                    }
                }

                // Primary name(s) first, so PrimaryName (Names.FirstOrDefault()) is correct
                foreach (var n in primaryNames.Concat(aliasNames))
                {
                    if (!entry.Names.Contains(n))
                        entry.Names.Add(n);
                }

                // ── Addresses ────────────────────────────────────────────
                // Include AddressLine4-6, which often carry city/district/province
                foreach (var addr in des.Descendants(ns + "Address"))
                {
                    var parts = new[]
                    {
                        addr.Element(ns + "AddressLine1")?.Value,
                        addr.Element(ns + "AddressLine2")?.Value,
                        addr.Element(ns + "AddressLine3")?.Value,
                        addr.Element(ns + "AddressLine4")?.Value,
                        addr.Element(ns + "AddressLine5")?.Value,
                        addr.Element(ns + "AddressLine6")?.Value,
                        addr.Element(ns + "AddressPostalCode")?.Value,
                        addr.Element(ns + "AddressCountry")?.Value,
                    }.Where(s => !string.IsNullOrWhiteSpace(s));

                    var addrStr = string.Join(", ", parts);
                    if (!string.IsNullOrWhiteSpace(addrStr))
                        entry.Addresses.Add(addrStr);
                }

                // ── Phone numbers ────────────────────────────────────────
                foreach (var phone in des.Descendants(ns + "PhoneNumber"))
                    if (!string.IsNullOrWhiteSpace(phone.Value))
                        entry.PhoneNumbers.Add(phone.Value);

                // ── Emails ────────────────────────────────────────────────
                foreach (var email in des.Descendants(ns + "EmailAddress"))
                    if (!string.IsNullOrWhiteSpace(email.Value))
                        entry.EmailAddresses.Add(email.Value);

                // ── Positions ─────────────────────────────────────────────
                foreach (var pos in des.Descendants(ns + "Position"))
                    if (!string.IsNullOrWhiteSpace(pos.Value))
                        entry.Positions.Add(pos.Value);

                // ── Gender ───────────────────────────────────────────────
                // Structure: IndividualDetails/Individual/Genders/Gender
                // Always a single value in the feed, but we defensively take the
                // first non-empty one in case that ever changes.
                entry.Gender = des.Descendants(ns + "Gender")
                                  .Select(g => g.Value)
                                  .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

                // ── Date(s) of birth ─────────────────────────────────────
                // Structure: IndividualDetails/Individual/DOBs/DOB (can repeat when OFSI
                // lists multiple possible birth dates). Raw values are normalised to
                // ISO 8601 (yyyy-MM-dd) where a full date is known.
                foreach (var dob in des.Descendants(ns + "DOB"))
                {
                    if (string.IsNullOrWhiteSpace(dob.Value))
                        continue;

                    entry.DateofBirth.Add(NormalizeUkDob(dob.Value));
                }

                // ── ID documents ──────────────────────────────────────────
                // Passports (individuals)
                foreach (var passport in des.Descendants(ns + "Passport"))
                {
                    var number = passport.Element(ns + "PassportNumber")?.Value;
                    var info = passport.Element(ns + "PassportAdditionalInformation")?.Value;

                    var parts = new List<string> { "Passport" };
                    if (!string.IsNullOrWhiteSpace(number)) parts.Add(number);
                    if (!string.IsNullOrWhiteSpace(info)) parts.Add(info);

                    var idStr = string.Join(": ", parts.Take(2)) + (parts.Count > 2 ? $" ({parts[2]})" : "");
                    if (!string.IsNullOrWhiteSpace(idStr))
                        entry.IdList.Add(idStr);
                }

                // National identifiers (individuals)
                foreach (var natId in des.Descendants(ns + "NationalIdentifier"))
                {
                    var number = natId.Element(ns + "NationalIdentifierNumber")?.Value;
                    var info = natId.Element(ns + "NationalIdentifierAdditionalInformation")?.Value;

                    var idStr = $"National ID: {number} {info}".Trim();
                    if (!string.IsNullOrWhiteSpace(number))
                        entry.IdList.Add(idStr);
                }

                // Business registration numbers (entities)
                foreach (var brn in des.Descendants(ns + "BusinessRegistrationNumber"))
                {
                    if (!string.IsNullOrWhiteSpace(brn.Value))
                        entry.IdList.Add($"Business Registration: {brn.Value.Trim()}");
                }

                entries.Add(entry);
            }

            return entries;
        }

        // The UK Sanctions List feed gives DOBs as dd/MM/yyyy, but OFSI substitutes
        // literal "dd" / "mm" / "yy" placeholders for date parts that aren't confirmed,
        // e.g. "dd/mm/1957" (only year known), "1973" (bare year, no slashes),
        // or "15/08/19yy" (day/month known, year partially unknown).
        // We convert to ISO 8601 (yyyy-MM-dd) whenever the full date is known;
        // otherwise we fall back to the best-known fragment rather than guessing.
        private static string NormalizeUkDob(string raw)
        {
            raw = raw.Trim();

            // Full date known, e.g. "30/01/1972" -> "1972-01-30"
            if (DateTime.TryParseExact(raw, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var fullDate))
            {
                return fullDate.ToString("yyyy-MM-dd");
            }

            // Only the year is known, day/month are literal placeholders: "dd/mm/1957" -> "1957"
            var yearOnly = Regex.Match(raw, @"^dd/mm/(\d{4})$", RegexOptions.IgnoreCase);
            if (yearOnly.Success)
                return yearOnly.Groups[1].Value;

            // Already a bare year, e.g. "1973"
            if (Regex.IsMatch(raw, @"^\d{4}$"))
                return raw;

            // Anything else (e.g. "15/08/19yy" — year partially unknown) can't be
            // safely converted to ISO without inventing data, so keep it as-is.
            return raw;
        }
    }
}