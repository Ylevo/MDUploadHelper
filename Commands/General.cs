using MangaDexSharp;
using Pastel;
using Serilog;
using System.Text;

namespace MDUploadHelper
{
    partial class Program
    {
        static void GetGroupsNames()
        {
            Log.Verbose("Getting groups' names from folders operation.");

            if (!LoadAndCheckMap()) { PressKeyContinue(); return; }

            var groupsAggregate = GetLocalNameIdMap().Group;
            Dictionary<string, string> groupsDic = mainFolderMap.Group;

            foreach (string mangoName in mangosFolders)
            {
                var chaptersFolders = Directory.GetDirectories(Path.Combine(mainFolder, mangoName)).Select(d => new DirectoryInfo(d).Name);
                Log.Verbose("List of folders: {0}", chaptersFolders);

                foreach (string folderName in chaptersFolders)
                {
                    if (!ParseFolderName(folderName, out var parsedFolderName)) { continue; }

                    var chapterGroups = parsedFolderName["group"].Value.Split('+').OrderBy(t => t).ToArray();

                    foreach (var chapterGroup in chapterGroups)
                    {
                        if (!groupsDic.Keys.Contains(chapterGroup) && !string.IsNullOrWhiteSpace(chapterGroup))
                        {
                            groupsDic.Add(chapterGroup, groupsAggregate.ContainsKey(chapterGroup) ? groupsAggregate[chapterGroup] : "");
                        }
                    }
                }
            }

            Log.Verbose("List of groups: {0}", groupsDic);
            SerializeFolderMap(mainFolderMap);
            Log.Verbose("Done getting groups' names.");
        }

        static async Task TitleMatching()
        {
            Log.Verbose("Title matching operation.");

            if (!LoadAndCheckMap()) { PressKeyContinue(); return; }

            if (mainFolderMap.Manga.Values.Any(val => !string.IsNullOrEmpty(val)))
            {
                if (!AskForConfirmation("This will override the current title id mapping. Confirm? (Y/n)")) { return; };
            }

            string titleLog = "", id = "", log = "", selection = "", titles = "";
            int notFound = 0, matched = 0, nonePicked = 0;
            Dictionary<string, string> mangoDic = mainFolderMap.Manga;
            var mangoLocalMapping = GetLocalNameIdMap().Manga;

            Log.Verbose("Mangos folders: {0}", mangosFolders);

            foreach (var mangoName in mangosFolders)
            {
                cancelToken.Token.ThrowIfCancellationRequested();

                if (mangoLocalMapping.ContainsKey(mangoName))
                {
                    Log.Information("Found \"{0}\" in local name_id_map.json.", mangoName);
                    id = mangoLocalMapping[mangoName];
                    titleLog = id + " (mupl)";
                    matched++;
                }
                else
                {
                    var results = await api.Manga.List(new MangaFilter { Title = mangoName, ContentRating = [ContentRating.safe, ContentRating.suggestive, ContentRating.erotica, ContentRating.pornographic] });
                    await Task.Delay(350);

                    switch (results.Total)
                    {
                        case int x when x == 0:
                            id = titleLog = "Not found";
                            notFound++;
                            Log.Information("No title found for " + mangoName);
                            break;
                        case int x when x >= 1:
                            Console.WriteLine();
                            Log.Information("Matching for: {0}", mangoName);

                            for (int i = 0; i < results.Data.Count && i < 5; i++)
                            {
                                var altTitles = results.Data[i].Attributes.AltTitles;
                                titles = results.Data[i].Attributes.Title.First().Value;

                                foreach (string language in settings.Languages)
                                {
                                    if (altTitles.Any(alt => alt.ContainsKey(language)))
                                    {
                                        titles += "\n" + new string(' ', 11)
                                            + altTitles.First(alt => alt.ContainsKey(language)).First().Value
                                            + " (" + language.ToUpper() + ")";
                                    }
                                }

                                Console.WriteLine("Result " + $"{i + 1}".Pastel(mdOrange) + ": " + titles);
                            }

                            selection = Console.ReadLine();

                            switch (selection)
                            {
                                case "1":
                                case "2":
                                case "3":
                                case "4":
                                case "5":
                                    int selectionParsed = int.Parse(selection);

                                    if (results.Data.Count < selectionParsed) { goto default; }

                                    id = results.Data[selectionParsed - 1].Id;
                                    titleLog = results.Data[selectionParsed - 1].Attributes.Title.First().Value;
                                    matched++;
                                    break;
                                case string s when !string.IsNullOrWhiteSpace(s):
                                    id = s;
                                    titleLog = s + " (manual input)";
                                    matched++;
                                    break;
                                default:
                                    id = "None picked";
                                    titleLog = "None picked";
                                    nonePicked++;
                                    break;
                            }

                            break;
                    }
                }

                Log.Verbose("{0} id picked.", id);
                log += $"{mangoName} : {titleLog}" + Environment.NewLine;
                mangoDic[mangoName] = id;
            }

            Console.WriteLine();
            Log.Information("Matched: {0}\r\nNone picked: {1}\r\nNot found: {2}", matched, nonePicked, notFound);
            File.WriteAllText(Path.Combine(mainFolder, "titleMatchingLog.txt"), log, uTF8Encoding);
            SerializeFolderMap(mainFolderMap);
            CheckForDuplicates();
            Log.Verbose("Done title matching.");
        }

        static async Task CompareTitles()
        {
            Log.Verbose("Comparing titles operation.");

            if (!LoadAndCheckMap(mangosNeeded: true)) { PressKeyContinue(); return; }

            foreach (var mangoName in mainFolderMap.Manga)
            {
                cancelToken.Token.ThrowIfCancellationRequested();

                if (!mangoName.Value.Contains("Not found") && !mangoName.Value.Contains("None picked") && !string.IsNullOrWhiteSpace(mangoName.Value))
                {
                    Log.Verbose("Fetching titles for {0}.", mangoName.Value);
                    var mangT = await api.Manga.Get(mangoName.Value);
                    await Task.Delay(600);

                    if (string.IsNullOrEmpty(mangT.Data.Id))
                    {
                        Log.Error("Skipping {0} with ID \'{1}\'; mango not found on MD.", mangoName.Key, mangoName.Value);
                        Console.WriteLine();
                        continue;
                    }

                    var altTitles = mangT.Data.Attributes.AltTitles;
                    string titles = mangT.Data.Attributes.Title.First().Value;
                    Log.Verbose("{0} main title: {1}Alt titles: {2}.", mangoName, titles, altTitles);

                    foreach (string language in settings.Languages)
                    {
                        if (altTitles.Any(alt => alt.ContainsKey(language)))
                        {
                            titles += "\n" + new string(' ', mangoName.Key.Length + 2)
                                + altTitles.First(alt => alt.ContainsKey(language)).First().Value
                                + " (" + language.ToUpper() + ")";
                        }
                    }

                    Console.WriteLine(mangoName.Key.Pastel(mdOrange) + ": " + titles);
                    Console.WriteLine();
                }
            }

            PressKeyContinue();
            Log.Verbose("Done comparing titles.");
        }

        static void CheckForDuplicates()
        {
            Log.Verbose("Checking for duplicates operation.");

            if (!LoadAndCheckMap(mangosNeeded: true)) { PressKeyContinue(); return; }

            var mangaAggregate = GetLocalNameIdMap().Manga;
            Dictionary<string, string> mangoDic = mainFolderMap.Manga;
            int foundDuplicates = 0;

            foreach (var mango in mainFolderMap.Manga)
            {
                Log.Verbose("Self checking.");

                foreach (var foundDuplicate in mangoDic.Where(m => m.Value == mango.Value && m.Key != mango.Key && m.Value != "Not found" && m.Value != "None picked" && !string.IsNullOrWhiteSpace(m.Value)))
                {
                    Log.Warning("\"{0}\" and \"{1}\" have the same ID.", mango.Key, foundDuplicate.Key);
                    foundDuplicates++;
                }

                Log.Verbose("Checking in uploader's local map.");

                foreach (var foundDuplicate in mangaAggregate.Where(m => m.Value == mango.Value && m.Key != mango.Key && m.Value != "Not found" && m.Value != "None picked" && !string.IsNullOrWhiteSpace(m.Value)))
                {
                    Log.Warning("ID found for \"{0}\" is already matched with \"{1}\" in the uploader's map.", mango.Key, foundDuplicate.Key);
                    foundDuplicates++;
                }
            }

            Log.Information("{0} duplicates found.", foundDuplicates);
            PressKeyContinue();
            Log.Verbose("Done checking for duplicates.");
        }

        static async Task FindVolumeNumbers()
        {
            Log.Verbose("Finding volume numbers operation.");

            if (!LoadAndCheckMap(mangosNeeded: true)) { PressKeyContinue(); return; }

            Dictionary<string, string> mangoDic = mainFolderMap.Manga;
            int totalVolumeNumbersFound = 0;

            foreach (var currentMangoFolder in mangosFolders)
            {
                cancelToken.Token.ThrowIfCancellationRequested();

                int volumeNumbersFound = 0;
                Console.WriteLine(currentMangoFolder);
                var volumes = (await api.Manga.Aggregate(mangoDic[currentMangoFolder])).Volumes.Where(v => v.Key != "none");
                await Task.Delay(350);

                if (!volumes.Any())
                {
                    Log.Information("Couldn't find any volumes for {0}.\r\n", currentMangoFolder);
                    continue;
                }

                var volumeNumberFuckery = volumes.SelectMany(vol => vol.Value.Chapters.Values).GroupBy(chData => chData.Chapter).Where(g => g.Count() > 1);

                if (volumeNumberFuckery.Any())
                {
                    void additionalInfo() => Log.Information("List of chapter numbers found in multiple volumes :\r\n{0}", volumeNumberFuckery.Select(f => f.Key).Order().ToArray());

                    if (!AskForConfirmation("Some chapter numbers are present in more than one volume. Try to volume number anyway? (Y/n)", additionalInfo)) { continue; }
                }

                var chaptersFolders = Directory.GetDirectories(Path.Combine(mainFolder, currentMangoFolder)).Select(d => new DirectoryInfo(d).Name);

                foreach (var currentChapterFolder in chaptersFolders)
                {
                    Log.Verbose("Folder {0}.", currentChapterFolder);
                    if (!ParseFolderName(currentChapterFolder, out var parsedFolderName)) { continue; }

                    if (!parsedFolderName["prefix"].Success)
                    {
                        Log.Verbose("Skipped oneshot");
                        continue;
                    }

                    string chapterNumber = decimal.Parse(parsedFolderName["chapter"].Value).ToString();
                    string? foundChapter = volumes.Select(volume => volume.Value).FirstOrDefault(volData => volData.Chapters.Values.Any(chData => chData.Chapter == chapterNumber))?.Volume;

                    if (foundChapter != null && !parsedFolderName["volume"].Success)
                    {
                        int chapGroupPosition = parsedFolderName["chapter"].Index + parsedFolderName["chapter"].Length;
                        string newChapterFolder = currentChapterFolder.Contains("(v)") ? currentChapterFolder.Replace("(v)", $"(v{foundChapter})")
                                                : $"{currentChapterFolder.Substring(0, chapGroupPosition + 1)}(v{foundChapter}){currentChapterFolder[chapGroupPosition..]}";
                        Directory.Move(Path.Combine(mainFolder, currentMangoFolder, currentChapterFolder), Path.Combine(mainFolder, currentMangoFolder, newChapterFolder));
                        Log.Verbose("Volume numbered {0} into {1}.", currentChapterFolder, newChapterFolder);
                        volumeNumbersFound++;
                    }
                }

                totalVolumeNumbersFound += volumeNumbersFound;
                Log.Information("{0} chapters volume numbered.", volumeNumbersFound);
                Console.WriteLine();
            }

            Log.Information("Total of {0} chapters volume numbered.", totalVolumeNumbersFound);
            PressKeyContinue();
            Log.Verbose("Done finding volume numbers.");
        }

        static async Task CheckForAlreadyUploadedChapters()
        {
            Log.Verbose("Check for already uploaded chapters operation.");

            if (!LoadAndCheckMap(mangosNeeded: true, groupsNeeded: true)) { PressKeyContinue(); return; }

            Dictionary<string, string> mangoDic = mainFolderMap.Manga;
            Dictionary<string, string> groupsDic = mainFolderMap.Group;

            Console.WriteLine("Enter the path where already uploaded chapters should be moved to :");
            string movedChaptersFolder = Console.ReadLine();
            Console.WriteLine();

            if (string.IsNullOrWhiteSpace(movedChaptersFolder) || !Directory.Exists(movedChaptersFolder))
            {
                Log.Error("No path entered or folder doesn't exist. Aborting (the operation).");
                PressKeyContinue();
                return;
            }

            int totalMoved = 0;
            int movedChapters = 0;
            int errors = 0;
            List<string> errorsRecord = new();

            foreach (var currentMangoFolder in mangosFolders)
            {
                cancelToken.Token.ThrowIfCancellationRequested();

                movedChapters = 0;
                Log.Information(currentMangoFolder);
                (List<Chapter> chapterList, bool success) = await FetchChapters(endpoint: FetchEndpoint.Manga, languages: settings.Languages, titleId: mangoDic[currentMangoFolder]);
                await Task.Delay(350);

                if (!success) { continue; }

                var chaptersFolders = Directory.GetDirectories(Path.Combine(mainFolder, currentMangoFolder)).Select(d => new DirectoryInfo(d).Name);

                foreach (var currentChapterFolder in chaptersFolders)
                {
                    if (!ParseFolderName(currentChapterFolder, out var parsedFolderName)) { continue; }

                    var chapterGroups = parsedFolderName["group"].Value.Split('+').Where(g => !string.IsNullOrWhiteSpace(g)).OrderBy(g => g);

                    if (chapterGroups.Any(g => !groupsDic.ContainsKey(g) || string.IsNullOrWhiteSpace(groupsDic[g])))
                    {
                        if (!errorsRecord.Contains(parsedFolderName["group"].Value))
                        {
                            errors++;
                            Log.Error("Group missing in the name id map: {0}", chapterGroups);
                            errorsRecord.Add(parsedFolderName["group"].Value);
                        }

                        continue;
                    }

                    var groupsIds = chapterGroups.Select(g => groupsDic[g]);
                    string chapterNumber = decimal.Parse(parsedFolderName["chapter"].Value).ToString();
                    string chapterLanguage = parsedFolderName["language"].Value;
                    string volumeNumber = parsedFolderName["volume"].Success ? decimal.Parse(parsedFolderName["volume"].Value).ToString() : string.Empty;
                    bool foundChapter = chapterList.Any(c =>
                                                       c.Attributes.Chapter == chapterNumber &&
                                                       c.ScanlationGroups().Select(g => g.Id).Intersect(groupsIds).Any() &&
                                                       c.Attributes.TranslatedLanguage == chapterLanguage &&
                                                       (string.IsNullOrEmpty(volumeNumber) || c.Attributes.Volume == volumeNumber || c.Attributes.Volume == null)
                                                       );

                    if (foundChapter)
                    {
                        Directory.CreateDirectory(Path.Combine(movedChaptersFolder, currentMangoFolder));
                        Directory.Move(Path.Combine(mainFolder, currentMangoFolder, currentChapterFolder), Path.Combine(movedChaptersFolder, currentMangoFolder, currentChapterFolder));
                        Log.Verbose("Moved {0}", currentChapterFolder);
                        movedChapters++;
                    }
                }

                totalMoved += movedChapters;
                Log.Information("{0} chapters moved.", movedChapters);
                Console.WriteLine();
            }

            Log.Information("Total of {0} chapters moved.", totalMoved);
            if (errors > 0) { Log.Warning("{0} errors.", errors); PressKeyContinue(); }
            Log.Verbose("Done checking for already uploaded chapters.");
        }

        static void LogChapters()
        {
            Log.Verbose("Logging chapters operation.");

            if (!LoadAndCheckMap(mangosNeeded: true)) { PressKeyContinue(); return; }

            StringBuilder logOutput = new();
            int total = 0;
            Dictionary<string, string> mangoDic = mainFolderMap.Manga;
            var mangoFolders = Directory.GetDirectories(mainFolder).Select(d => new DirectoryInfo(d).Name);

            foreach (string mangoFolder in mangoFolders)
            {
                var chapterFolders = Directory.GetDirectories(Path.Combine(mainFolder, mangoFolder)).Select(d => new DirectoryInfo(d).Name);
                logOutput.AppendLine($"{mangoFolder}:{mangoDic[mangoFolder]}\r{chapterFolders.Count()} chapters");
                logOutput.AppendLine();
                total += chapterFolders.Count();

                foreach (string chapterFolder in chapterFolders)
                {
                    logOutput.AppendLine(chapterFolder);
                }

                logOutput.AppendLine();
            }

            logOutput.AppendLine($"Total:{total}");

            File.WriteAllText(Path.Combine(mainFolder, $"chaptersLog_{DateTime.Now:yyyyMMddHHmmss}.txt"), logOutput.ToString(), uTF8Encoding);
            Log.Verbose("Done logging chapters.");
        }
    }
}
