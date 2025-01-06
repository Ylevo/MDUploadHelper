using Serilog;
using System.Text.Json;

namespace TMOScrapHelper
{
    public class Settings
    {
        public string UploaderFolder { get; set; } = "";
        public string PathToUploaderMap => Path.Combine(UploaderFolder, "name_id_map.json");
        public string AggregateGistId { get; set; } = "";
        public string[] Languages { get; set; } = Array.Empty<string>();
        public bool SkipMainFolder { get; set; } = false;

        public bool CheckSettings()
        {
            bool allClear = true;

            if (!Directory.Exists(UploaderFolder))
            {
                LogAndSetFlag("Bulk uploader folder not found or unassigned in settings. Some operations will be unavailable.", ref allClear);
            }

            if (Directory.Exists(UploaderFolder) && !File.Exists(PathToUploaderMap))
            {
                LogAndSetFlag("Could not find name_id_map.json in the assigned uploader folder. Some operations will fail without it.", ref allClear);
            }

            if (Languages.Length == 0)
            {
                LogAndSetFlag("Language array is empty.", ref allClear);
            }

            return allClear;
        }

        private static void LogAndSetFlag(string message, ref bool flag)
        {
            Log.Warning(message);
            flag = false;
        }
    }
}
