using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Upsanctionscreener.Classess.Utils
{
    internal class GlobalVariables
    {
        static string systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
        public static string root_folder = Path.Combine(systemDrive, "UpSanctions");
        public static string nigerian_sanction_list_path = Path.Combine(root_folder, "Lists");
        public static bool refetching_sanction_database = false;
    }
}
