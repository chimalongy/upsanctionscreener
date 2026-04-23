using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Upsanctionscreener.Models;

namespace Upsanctionscreener.Classess.Search
{
    public class PEPBKTree
    {
        internal class BKNode
        {
            public string Key { get; }
            public string EntryId { get; }
            public Dictionary<int, BKNode> Children { get; } = new Dictionary<int, BKNode>();

            public BKNode(string key, string entryId)
            {
                Key = key;
                EntryId = entryId;
            }
        }

        public class PEPSearchResult
        {
            public string EntryId { get; }
            public string MatchedName { get; }
            public double Similarity { get; }
            public int EditDistance { get; }

            public PEPSearchResult(string entryId, string matchedName, double similarity, int editDistance)
            {
                EntryId = entryId;
                MatchedName = matchedName;
                Similarity = similarity;
                EditDistance = editDistance;
            }

            public override string ToString() =>
                $"[{Similarity:P1}] {MatchedName} (ID={EntryId}, dist={EditDistance})";
        }

        public class PEPSanctionBKTree
        {
            // ── Configuration ────────────────────────────────────────────────────
            private readonly double _threshold;
            private readonly bool _caseSensitive;

            // ── Internal state ───────────────────────────────────────────────────
            private BKNode? _root;
            private int _nodeCount;

            public PEPSanctionBKTree(double threshold = 0.90, bool caseSensitive = false)
            {
                if (threshold <= 0 || threshold > 1)
                    throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be in (0, 1].");

                _threshold = threshold;
                _caseSensitive = caseSensitive;
            }

            // ── Public API ───────────────────────────────────────────────────────

            public void Load(IEnumerable<PepEntry> entries)
            {
                foreach (var entry in entries)
                {
                    // Index FullName
                    if (!string.IsNullOrWhiteSpace(entry.FullName))
                        Insert(Normalise(entry.FullName), entry.Id.ToString());

                    // Index MerchantName
                    if (!string.IsNullOrWhiteSpace(entry.MerchantName))
                        Insert(Normalise(entry.MerchantName), entry.Id.ToString());
                }
            }

            public List<PEPSearchResult> Search(string query)
            {
                if (_root == null) return new List<PEPSearchResult>();
                if (string.IsNullOrWhiteSpace(query)) return new List<PEPSearchResult>();

                var normQuery = Normalise(query);
                var results = new List<PEPSearchResult>();

                RecursiveSearch(_root, normQuery, results);

                return results
                    .OrderByDescending(r => r.Similarity)
                    .ThenBy(r => r.MatchedName)
                    .ToList();
            }

            public int NodeCount => _nodeCount;

            // ── Private helpers ──────────────────────────────────────────────────

            private void Insert(string key, string entryId)
            {
                if (string.IsNullOrEmpty(key)) return;

                if (_root == null)
                {
                    _root = new BKNode(key, entryId);
                    _nodeCount++;
                    return;
                }

                var current = _root;
                while (true)
                {
                    int dist = LevenshteinDistance(key, current.Key);

                    if (dist == 0) return;

                    if (current.Children.TryGetValue(dist, out var child))
                        current = child;
                    else
                    {
                        current.Children[dist] = new BKNode(key, entryId);
                        _nodeCount++;
                        return;
                    }
                }
            }

            private void RecursiveSearch(BKNode node, string query, List<PEPSearchResult> results)
            {
                int dist = LevenshteinDistance(query, node.Key);
                int maxLen = Math.Max(query.Length, node.Key.Length);
                int maxAllowed = maxLen == 0 ? 0 : (int)Math.Floor((1.0 - _threshold) * maxLen);

                if (dist <= maxAllowed && maxLen > 0)
                {
                    double similarity = 1.0 - (double)dist / maxLen;
                    results.Add(new PEPSearchResult(node.EntryId, node.Key, similarity, dist));
                }

                int lo = dist - maxAllowed;
                int hi = dist + maxAllowed;

                foreach (var kvp in node.Children)
                    if (kvp.Key >= lo && kvp.Key <= hi)
                        RecursiveSearch(kvp.Value, query, results);
            }

            // ── Distance & normalisation ─────────────────────────────────────────

            private string Normalise(string s)
            {
                s = s.Trim();
                return _caseSensitive ? s : s.ToLowerInvariant();
            }

            private static int LevenshteinDistance(string a, string b)
            {
                if (a.Length == 0) return b.Length;
                if (b.Length == 0) return a.Length;
                if (a == b) return 0;

                if (a.Length < b.Length) { var t = a; a = b; b = t; }

                var prev = new int[b.Length + 1];
                var curr = new int[b.Length + 1];

                for (int j = 0; j <= b.Length; j++) prev[j] = j;

                for (int i = 1; i <= a.Length; i++)
                {
                    curr[0] = i;
                    for (int j = 1; j <= b.Length; j++)
                    {
                        int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                        curr[j] = Math.Min(
                            Math.Min(curr[j - 1] + 1,
                                     prev[j] + 1),
                            prev[j - 1] + cost);
                    }
                    Array.Copy(curr, prev, b.Length + 1);
                }

                return prev[b.Length];
            }
        }
    }
}