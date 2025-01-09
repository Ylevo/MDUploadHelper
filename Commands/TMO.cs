using Pastel;
using Serilog;
using System.Text.Json;

namespace MDUploadHelper
{
    partial class Program
    {
        static async Task UpdateWithTMOAggregate()
        {
            Log.Verbose("Updating using TMO aggregate operation.");

            if (!settings.CheckUploader()) { PressKeyContinue(); return; }

            if (!AskForConfirmation("Don't do this unless you're scraping TMO. Confirm? (Y/n)")) { return; }

            var onlineMap = await GetOnlineNameIdMap();
            var localMap = GetLocalNameIdMap();
            localMap.Merge(onlineMap);
            File.Copy(settings.PathToUploaderMap, Path.Combine(settings.UploaderFolder, "name_id_map_backup.json"));
            Log.Verbose("Backup created.");
            File.WriteAllText(settings.PathToUploaderMap, JsonSerializer.Serialize(localMap, jsonSerializerOptions), uTF8Encoding);
            Log.Verbose("Done updating.");
        }

        static async Task CheckTMOAggregateForDuplicates()
        {
            Log.Verbose("Checking TMO aggregate for duplicates operation.");

            var aggregate = (await GetOnlineNameIdMap()).Manga;
            var duplicates = aggregate.GroupBy(x => x.Value).Where(x => x.Count() > 1).ToList();

            if (duplicates.Count == 0)
            {
                Log.Information("No duplicate found.");
            }

            foreach (var duplicateId in duplicates)
            {
                Log.Information("- {0} duplicated in keys:", duplicateId.Key);

                foreach (var kvp in duplicateId)
                {
                    Log.Information($"{kvp.Key.Pastel(ConsoleColor.White)}");
                }

                Console.WriteLine();
            }

            PressKeyContinue();
            
            Log.Verbose("Done checking.");
        }
    }
}
