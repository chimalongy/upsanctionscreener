using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Upsanctionscreener.Models;
namespace Upsanctionscreener.Classess.Interfaces
{

    public interface ISanctionParser
    {
        string Source { get; }
        List<SanctionEntry> Parse(string xmlContent);
    }
}
