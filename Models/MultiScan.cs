using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Upsanctionscreener.Models
{
    public class MultiScanDeleteRequest
    {
        public string FilePath { get; set; } = string.Empty;
    }


    public class MultiScanPasteRequest
    {
        public string ScanType { get; set; } = "non-document";
        public string? NameList { get; set; }   // newline-delimited names
    }


    public class MultiScanTask
    {
       public  int Id { get; set; }  
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string ResultFileName { get; set; }
        public string ResultPath { get; set; }
        public string ScanType { get; set; }
        public string RowCount { get; set; }
        public string AutoGenerateId { get; set; }
        public string IdColumn { get; set; }
        
        public string ScanColumn { get; set; }
        public string Status { get; set; }
        public string StartTime { get; set; }
        public  string CompletionTIme { get; set; }
        public string ErrorMessage { get; set; }    

    }


    public class MultiScanReadFileRequest
    {
        public string FilePath { get; set; } = "";
        public string ScanType { get; set; } = "document";
        public string? ScanColumn { get; set; }
        public string? IdColumn { get; set; }
        public bool AutoGenerateId { get; set; }
    }








}
