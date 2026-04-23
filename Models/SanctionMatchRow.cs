using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Upsanctionscreener.Models
{
    public class SingleSearchSanctionMatchRow
    {
      public string similarity { get; set; }  
      public SanctionEntry sanction_item { get; set; }



    }


    public  class PepSearchSanctionMatchRow 
    {
        public string similarity { get; set; }
        public PepEntry pep_item { get; set; }
       
    }
}
