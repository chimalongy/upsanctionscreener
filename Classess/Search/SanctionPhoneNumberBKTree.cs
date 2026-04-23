using System;
using System.Collections.Generic;
using System.Linq;
using Upsanctionscreener.Models;

namespace Upsanctionscreener.Classess.Search
{
    public class SanctionPhoneNumberBKTree
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

        public class PhoneSearchResult
        {
            public string EntryId { get; }
            public string Matched { get; }
            public double Similarity { get; }
            public int EditDistance { get; }

            public PhoneSearchResult(string entryId, string matched, double similarity, int editDistance)
            {
                EntryId = entryId;
                Matched = matched;
                Similarity = similarity;
                EditDistance = editDistance;
            }
        }

        private readonly double _threshold;
        private readonly bool _caseSensitive;

        private BKNode? _root;
        private int _nodeCount;

        public SanctionPhoneNumberBKTree(double threshold = 0.90, bool caseSensitive = false)
        {
            if (threshold <= 0 || threshold > 1)
                throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be in (0, 1].");

            _threshold = threshold;
            _caseSensitive = caseSensitive;
        }

        public void Load(IEnumerable<SanctionEntry> entries)
        {
            foreach (var entry in entries)
            {
                foreach (var phone in entry.PhoneNumbers.Where(p => !string.IsNullOrWhiteSpace(p)))
                    Insert(Normalise(phone), entry.ID);
            }
        }

        public List<PhoneSearchResult> Search(string query, double? threshold = null)
        {
            if (_root == null) return new List<PhoneSearchResult>();
            if (string.IsNullOrWhiteSpace(query)) return new List<PhoneSearchResult>();

            var norm = Normalise(query);
            var results = new List<PhoneSearchResult>();
            double th = threshold ?? _threshold;
            RecursiveSearch(_root, norm, results, th);

            return results.OrderByDescending(r => r.Similarity).ThenBy(r => r.Matched).ToList();
        }

        public int NodeCount => _nodeCount;

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

        private void RecursiveSearch(BKNode node, string query, List<PhoneSearchResult> results, double threshold)
        {
            int dist = LevenshteinDistance(query, node.Key);
            int maxLen = Math.Max(query.Length, node.Key.Length);
            int maxAllowed = maxLen == 0 ? 0 : (int)Math.Floor((1.0 - threshold) * maxLen);

            if (dist <= maxAllowed && maxLen > 0)
            {
                double similarity = 1.0 - (double)dist / maxLen;
                results.Add(new PhoneSearchResult(node.EntryId, node.Key, similarity, dist));
            }

            int lo = dist - maxAllowed;
            int hi = dist + maxAllowed;

            foreach (var kvp in node.Children)
                if (kvp.Key >= lo && kvp.Key <= hi)
                    RecursiveSearch(kvp.Value, query, results, threshold);
        }

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
