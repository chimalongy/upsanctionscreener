using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Upsanctionscreener.Models.ViewModels
{
    public class SingleScreenRequest
    {
        public string SearchTerm { get; set; } = string.Empty;
        public string SearchField { get; set; } = "name";
        // Threshold between 0 and 1. If null, server-side default is used.
        public double? Threshold { get; set; } = null;
    }
}
