using System;
using System.Collections.Generic;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Threading.Tasks;
using System.Xml;

namespace Upsanctionscreener.Services
{
    public static class GoogleNewsRssService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

       
        public static async Task<List<RssNewsItem>> SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<RssNewsItem>();

            try
            {
                var encodedQuery = Uri.EscapeDataString(query);

                var url = $"https://news.google.com/rss/search?q={encodedQuery}&hl=en-US&gl=US&ceid=US:en";

                using var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = XmlReader.Create(stream);

                var feed = SyndicationFeed.Load(reader);

                var results = new List<RssNewsItem>();

                foreach (var item in feed.Items)
                {
                    results.Add(new RssNewsItem
                    {
                        Title = item.Title?.Text,
                        Summary = item.Summary?.Text,
                        PublishedDate = item.PublishDate.DateTime,
                        Url = item.Links.Count > 0 ? item.Links[0].Uri.ToString() : null,
                        Source = item.SourceFeed?.Title?.Text
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Google RSS error: {ex.Message}");
                return new List<RssNewsItem>();
            }
        }
    }

    
    public class RssNewsItem
    {
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public string? Url { get; set; }
        public DateTime PublishedDate { get; set; }
        public string? Source { get; set; }
    }
}