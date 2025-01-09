using Serilog;
using System.Text.Json;

namespace MDUploadHelper
{
    public class Settings
    {
        public string UploaderFolder { get; set; } = "";
        public string PathToUploaderMap => Path.Combine(UploaderFolder, "name_id_map.json");
        public string AggregateGistId { get; set; } = "";
        public string[] Languages { get; set; } = [];
        public bool SkipMainFolder { get; set; } = false;

        public bool CheckSettings()
        {
            return CheckUploader() && CheckLanguage();
        }

        public bool CheckUploader()
        {
            bool allClear = true;

            if (!Directory.Exists(UploaderFolder))
            {
                LogAndSetFlag("Bulk uploader folder not found or unassigned in settings.", ref allClear);
            }

            if (Directory.Exists(UploaderFolder) && !File.Exists(PathToUploaderMap))
            {
                LogAndSetFlag("Could not find name_id_map.json in the assigned uploader folder.", ref allClear);
            }

            return allClear;
        }

        public bool CheckLanguage()
        {
            bool allClear = true;

            if (Languages.Length == 0)
            {
                LogAndSetFlag("Language setting is empty.", ref allClear);
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
