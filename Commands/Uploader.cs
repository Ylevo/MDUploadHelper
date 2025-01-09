using Serilog;
using System.Text.Json;

namespace MDUploadHelper
{
    partial class Program
    {
        static void MergeJsonMaps()
        {
            Log.Verbose("Merging main folder's json map with uploader's operation.");

            if (!LoadAndCheckMap() || !settings.CheckUploader()) { PressKeyContinue(); return; }

            if (!AskForConfirmation("This will merge the main folder's json map with the uploader's. Existing entries' value will be overwritten. A backup will be created. Confirm? (Y/n)")) { return; }

            var uploaderMap = JsonSerializer.Deserialize<NameIdMap>(File.ReadAllText(settings.PathToUploaderMap));
            uploaderMap.Merge(mainFolderMap);
            File.Copy(settings.PathToUploaderMap, Path.Combine(settings.UploaderFolder, "name_id_map_backup.json"), true);
            File.WriteAllText(settings.PathToUploaderMap, JsonSerializer.Serialize(uploaderMap, jsonSerializerOptions), uTF8Encoding);
            Log.Verbose("Done merging main folder's json maps with uploader's.");
        }

        static void MoveToUploader()
        {
            Log.Verbose("Moving folders to uploader operation.");

            if (!LoadAndCheckMap() || !settings.CheckUploader()) { PressKeyContinue(); return; }

            foreach (var currentMangoFolder in mangosFolders)
            {
                var chaptersFolders = Directory.GetDirectories(Path.Combine(mainFolder, currentMangoFolder)).Select(d => new DirectoryInfo(d).Name);

                foreach (var currentChapterFolder in chaptersFolders)
                {
                    Directory.Move(Path.Combine(mainFolder, currentMangoFolder, currentChapterFolder), Path.Combine(settings.UploaderFolder, "to_upload", currentChapterFolder));
                }
            }

            Log.Verbose("Done moving to uploader.");
        }

        static void MoveBackFromUploader()
        {
            Log.Verbose("Moving folders back from uploader operation.");

            if (!LoadAndCheckMap() || !settings.CheckUploader()) { PressKeyContinue(); return; }

            var movedFolders = Directory.GetDirectories(Path.Combine(settings.UploaderFolder, "to_upload"));

            foreach (string folder in movedFolders)
            {
                if (!ParseFolderName(folder, out var parsedFolderName)) { continue; }

                string title = parsedFolderName["title"].Value;
                string backto = Path.Combine(mainFolder, title + "\\", Path.GetFileName(folder));
                Directory.Move(folder, backto);
            }

            Log.Verbose("Done moving folders back from uploader.");
        }
    }
}
