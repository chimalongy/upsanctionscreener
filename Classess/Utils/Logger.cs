namespace Upsanctionscreener.Classess.Utils
{
    public static class Logger
    {
        private const string Demarcator =
            "------------------------------------------------------------------------------------------";

        private static readonly object _lock = new object();

        public static void LogToFile(string folderName, string fileName, string message)
        {
            lock (_lock)
            {
               

                if (!Directory.Exists(folderName))
                    Directory.CreateDirectory(folderName);

                string filePath = Path.Combine(folderName, $"{fileName}.logs");

                string timestampedMessage = $"{Demarcator}{Environment.NewLine}" +
                                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}" +
                                            $"{Demarcator}{Environment.NewLine}";

                File.AppendAllText(filePath, timestampedMessage);
            }
        }
    }
}