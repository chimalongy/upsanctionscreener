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
        public static string certificate_path = Path.Combine(root_folder, "certs", "upsanctionscreenercert.pfx");
        public static string nigerian_sanction_list_path = Path.Combine(root_folder, "Lists");
        public static bool refetching_sanction_database = false;
        public static string base_sanction_db_path = Path.Combine(root_folder, "SanctionDatabase", "basesource", "UPSanctionDB.xlsx");
        public static string MFASecretsFilePath = Path.Combine(root_folder, "Mfa", "mfa-secrets.json");

        public static readonly List<string> NigerianStates = new List<string>
{
    "Abia",
    "Adamawa",
    "Akwa Ibom",
    "Anambra",
    "Bauchi",
    "Bayelsa",
    "Benue",
    "Borno",
    "Cross River",
    "Delta",
    "Ebonyi",
    "Edo",
    "Ekiti",
    "Enugu",
    "Gombe",
    "Imo",
    "Jigawa",
    "Kaduna",
    "Kano",
    "Katsina",
    "Kebbi",
    "Kogi",
    "Kwara",
    "Lagos",
    "Nasarawa",
    "Niger",
    "Ogun",
    "Ondo",
    "Osun",
    "Oyo",
    "Plateau",
    "Rivers",
    "Sokoto",
    "Taraba",
    "Yobe",
    "Zamfara",
    "Federal Capital Territory",
    "Abuja"
};
    }
}
