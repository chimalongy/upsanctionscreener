using System.Data;
using System.Text.Json;
using Upsanctionscreener.Classess.Utils; // wherever SubFieldMapping lives

namespace  Upsanctionscreener.Classess.Utils
{
    public static class SubFieldExtractor
    {
        
        public static DataTable ExtractSubFields(DataTable sourceTable, string jsonColumnName, List<SubFieldMapping> subFields)
        {
            if (!sourceTable.Columns.Contains(jsonColumnName))
                throw new ArgumentException($"Column '{jsonColumnName}' not found in table.");

            // Start from a full structural + data copy of the source table.
            var result = sourceTable.Copy();

            if (subFields is null || subFields.Count == 0)
                return result;

            var columnNames = ResolveColumnNames(result, subFields);

            foreach (var colName in columnNames.Values)
            {
                if (!result.Columns.Contains(colName))
                    result.Columns.Add(colName, typeof(string));
            }

            foreach (DataRow row in result.Rows)
            {
                if (row[jsonColumnName] == DBNull.Value)
                {
                    foreach (var colName in columnNames.Values)
                        row[colName] = DBNull.Value;
                    continue;
                }

                string rawJson = row[jsonColumnName].ToString() ?? "";
                JsonDocument? doc = null;

                try
                {
                    if (!string.IsNullOrWhiteSpace(rawJson))
                        doc = JsonDocument.Parse(rawJson);
                }
                catch (JsonException)
                {
                    doc = null; // malformed JSON — leave every sub-field DBNull for this row
                }

                using (doc)
                {
                    foreach (var subField in subFields)
                    {
                        var colName = columnNames[subField];
                        row[colName] = doc is not null
                            ? TryExtractValue(doc.RootElement, subField.Key)
                            : (object)DBNull.Value;
                    }
                }
            }

            return result;
        }

        // ── Column naming ────────────────────────────────────────────────────
        // Priority: explicit "As" rename > sanitized last segment of the key.
        // Falls back to a numeric suffix if two sub-fields would otherwise
        // collide on the same output column name (or an existing column).
        private static Dictionary<SubFieldMapping, string> ResolveColumnNames(DataTable table, List<SubFieldMapping> subFields)
        {
            var result = new Dictionary<SubFieldMapping, string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DataColumn col in table.Columns)
                used.Add(col.ColumnName);

            foreach (var subField in subFields)
            {
                var baseName = !string.IsNullOrWhiteSpace(subField.As)
                    ? SanitizeColumnName(subField.As)
                    : SanitizeColumnName(LastSegment(subField.Key));

                var candidate = baseName;
                var suffix = 2;
                while (used.Contains(candidate))
                {
                    candidate = $"{baseName}_{suffix}";
                    suffix++;
                }

                used.Add(candidate);
                result[subField] = candidate;
            }

            return result;
        }

        private static string LastSegment(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "Field";
            var parts = key.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[^1] : key;
        }

        private static string SanitizeColumnName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Field";
            var cleaned = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            if (string.IsNullOrEmpty(cleaned)) return "Field";
            return char.ToUpperInvariant(cleaned[0]) + cleaned.Substring(1);
        }

        // ── JSON traversal ───────────────────────────────────────────────────
        // Supports dot-notation paths, e.g. "contact.email" walks root -> contact -> email.
        private static object TryExtractValue(JsonElement root, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return DBNull.Value;

            var segments = key.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var current = root;

            foreach (var segment in segments)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                    return DBNull.Value;

                current = next;
            }

            return current.ValueKind switch
            {
                JsonValueKind.String => current.GetString() ?? (object)DBNull.Value,
                JsonValueKind.Number => current.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => DBNull.Value,
                JsonValueKind.Undefined => DBNull.Value,
                _ => current.GetRawText() // nested object/array — keep raw JSON rather than dropping it
            };
        }

        public static List<FieldMapping> FlattenFieldMappings(List<FieldMapping> mappings)
        {
            var flattened = new List<FieldMapping>();

            if (mappings is null)
                return flattened;

            foreach (var m in mappings)
            {
                if (!m.IsJson)
                {
                    flattened.Add(new FieldMapping
                    {
                        ColumnName = m.ColumnName,
                        MatchAs = m.MatchAs,
                        IsJson = false,
                        SubFields = new List<SubFieldMapping>()
                    });
                    continue;
                }

                foreach (var subField in m.SubFields ?? new List<SubFieldMapping>())
                {
                    flattened.Add(new FieldMapping
                    {
                        ColumnName = subField.Key,
                        MatchAs = subField.MatchAs,
                        IsJson = false,
                        SubFields = new List<SubFieldMapping>()
                    });
                }
            }

            return flattened;
        }











    }
}