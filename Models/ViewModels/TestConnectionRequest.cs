using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Upsanctionscreener.Models.ViewModels
{
    public class TestConnectionRequest
    {
        public string DbType { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string DbName { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }
}
