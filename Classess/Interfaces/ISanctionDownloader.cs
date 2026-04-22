using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Upsanctionscreener.Models;
namespace Upsanctionscreener.Classess.Interfaces
{
    public interface ISanctionDownloader
    {
        Task<List<SanctionEntry>> DownloadAndParseAsync();
        Task ExportToExcelAsync(List<SanctionEntry> entries, string outputPath);
    }
}
