using System.Data;

namespace Upsanctionscreener.Models

{
    public class TaskFileReadResult
    {
        public bool Success { get; set; }
        public DataTable? Data { get; set; }
        public string? Error { get; set; }
    }
}
