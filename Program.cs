using MangaDexSharp;
using System.Text;
using System.Text.RegularExpressions;
using Serilog;
using System.Globalization;
using iluvadev.ConsoleProgressBar;
using TMOScrapHelper;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Configuration;
using Pastel;
using Serilog.Sinks.SystemConsole.Themes;
using System.Linq;


CultureInfo customCulture = (CultureInfo)System.Threading.Thread.CurrentThread.CurrentCulture.Clone();
customCulture.NumberFormat.NumberDecimalSeparator = ".";
System.Threading.Thread.CurrentThread.CurrentCulture = customCulture;

const string FILENAME_REGEX = @"(?:\[(?<artist>.+?)?\])?\s?"  // Artist
            + @"(?<title>.+?)"  // Manga title
            + @"(?:\s?\[(?<language>[a-z]{2}(?:-[a-z]{2})?|[a-zA-Z]{3}|[a-zA-Z]+)?\])?\s-\s"  // Language
            + @"(?<prefix>(?:[c](?:h(?:a?p?(?:ter)?)?)?\.?\s?))?(?<chapter>\d+(?:\.\d+)?)"  // Chapter number and prefix
            + @"(?:\s?\((?:[v](?:ol(?:ume)?(?:s)?)?\.?\s?)?(?<volume>\d+(?:\.\d+)?)?\))?"  // Volume number
            + @"(?:\s?\((?<chapter_title>.+)\))?"  // Chapter title
            + @"(?:\s?\{(?<publish_date>(?<publish_year>\d{4})-(?<publish_month>\d{2})-(?<publish_day>\d{2})(?:[T\s](?<publish_hour>\d{2})[\:\-](?<publish_minute>\d{2})(?:[\:\-](?<publish_microsecond>\d{2}))?(?:(?<publish_offset>[+-])(?<publish_timezone>\d{2}[\:\-]?\d{2}))?)?)\})?"  // Publish date
            + @"(?:\s?\[(?:(?<group>.+))?\])?"  // Groups
            + @"(?:\s?\{v?(?<version>\d)?\})?"  // Chapter version
            + @"(?:\.(?<extension>zip|cbz))?$";  // File extension

IMangaDex? api = null;
string mainFolder = "";
List<string>? mangosFolders = null;
MangaList results;
Settings? settings = null;
JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };
UTF8Encoding uTF8Encoding = new(false);
NameIdMap? mainFolderMap = null;
CancellationTokenSource? cancelToken = null;

if (!Init()) return;

while (true)
{
    try
    {
        Console.WriteLine();
        Console.WriteLine("[- - - - - - MD Upload Helper - - - - - -]".Pastel(ConsoleColor.White).PastelBg("EA471D"));
        Console.WriteLine();
        Console.WriteLine("[- - - - - General - - - - -]".Pastel(ConsoleColor.White).PastelBg("EA471D"));
        Console.WriteLine($"{"1".Pastel("FF6740")} - Get groups' names from folders");
        Console.WriteLine($"{"2".Pastel("FF6740")} - Match titles");
        Console.WriteLine($"{"3".Pastel("FF6740")} - Compare mango titles");
        Console.WriteLine($"{"4".Pastel("FF6740")} - Check for duplicates");
        Console.WriteLine($"{"5".Pastel("FF6740")} - Find volume numbers");
        Console.WriteLine($"{"6".Pastel("FF6740")} - Check for already uploaded chapters");
        Console.WriteLine($"{"7".Pastel("FF6740")} - Log chapters");
        Console.WriteLine();
        Console.WriteLine("[- - - - - Bulk Uploader - - - - -]".Pastel(ConsoleColor.White).PastelBg("EA471D"));
        Console.WriteLine($"{"8".Pastel("FF6740")} - Merge json maps");
        Console.WriteLine($"{"9".Pastel("FF6740")} - Move folders to uploader");
        Console.WriteLine($"{"10".Pastel("FF6740")} - Move folders BACK from uploader");
        Console.WriteLine();
        Console.WriteLine("[- - - - - Misc - - - - - ]".Pastel(ConsoleColor.White).PastelBg("EA471D"));
        Console.WriteLine($"{"11".Pastel("FF6740")} - Fetch chapter ids using creation time");
        Console.WriteLine($"{"12".Pastel("FF6740")} - List titles with uploads from a user");
        Console.WriteLine($"{"13".Pastel("FF6740")} - Check chapter count of titles");
        Console.WriteLine();
        Console.WriteLine("[- - - - - TMO Related - - - - - ]".Pastel(ConsoleColor.White).PastelBg("EA471D"));
        Console.WriteLine($"{"14".Pastel("FF6740")} - Merge local json with online aggregate");
        Console.WriteLine($"{"15".Pastel("FF6740")} - Check for duplicates in aggregate");
        Console.WriteLine();
        Console.WriteLine($"{"16".Pastel("FF6740")} - Exit ");
        Console.WriteLine();
        
        cancelToken = null;
        string? input = Console.ReadLine();

        if (input == null ) { return; }

        Console.WriteLine();
        cancelToken = new();

        switch (input)
        {
            case "1":
                GetGroupsNames();
                break;
            case "2":
                await TitleMatching();
                break;
            case "3":
                await CompareTitles();
                break;
            case "4":
                CheckForDuplicates();
                break;
            case "5":
                await FindVolumeNumbers();
                break;
            case "6":
                await CheckForAlreadyUploadedChapters();
                break;
            case "7":
                LogChapters();
                break;
            case "8":
                MergeJsonMaps();
                break;
            case "9":
                MoveToUploader();
                break;
            case "10":
                MoveBackFromUploader();
                break;
            case "11":
                await GetIdsWithCreatedAt();
                break;
            case "12":
                await ListTitleUploadsFromUser();
                break;
            case "13":
                await CheckChapterCount();
                break;
            case "14":
                await UpdateWithTMOAggregate();
                break;
            case "15":
                await CheckTMOAggregateForDuplicates();
                break;
            case "16":
                return;
        }
    }
    catch(OperationCanceledException)
    {
        Log.Information("Aborted operation");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "An unexpected error has occurred.");
        PressKeyContinue();
    }
}

bool Init()
{
    try
    {
        SetupLogger();
        api = MangaDex.Create();

        try
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("settings.json").Build();

            settings = config.Get<Settings>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not load the settings from settings.json. Exiting.");
            Console.ReadKey();
            return false;
        }

        if (!settings.CheckSettings()) { PressKeyContinue(); }

        if (!settings.SkipMainFolder)
        {
            Console.WriteLine("Enter the main folder's path:".Pastel(ConsoleColor.White));
            mainFolder = Console.ReadLine();

            while (!Directory.Exists(mainFolder))
            {
                Console.WriteLine();
                Console.WriteLine("Folder does not exist. Try again.");
                Console.WriteLine("(you can enable skipping in settings.json if not needed)");
                mainFolder = Console.ReadLine();
            }

            Log.Verbose(mainFolder + " selected.");
            Log.Verbose("List of subfolders : {0}", mangosFolders);
        }

        Console.CancelKeyPress += delegate (object? sender, ConsoleCancelEventArgs e)
        {
            if (cancelToken != null)
            {
                if (!cancelToken.IsCancellationRequested)
                {
                    cancelToken.Cancel();
                    Console.WriteLine();
                    Log.Information("Keyboard interrupt detected. Aborting...".Pastel(ConsoleColor.White));
                    Console.WriteLine();
                }

                e.Cancel = true;
            }
        };
    }
    catch (Exception e)
    {
        Console.WriteLine("Error: Could not even initialize properly. Exiting.");
        Console.WriteLine($"{e.Message}\r\n{e.StackTrace}");
        Console.ReadKey();
        return false;
    }

    return true;
}

bool LoadAndCheckMap(bool mangosNeeded = false, bool groupsNeeded = false)
{
    if (string.IsNullOrWhiteSpace(mainFolder))
    {
        Log.Error("No main folder selected. Disable skipping main folder selection to do this.");
        return false;
    }

    mainFolderMap = File.Exists(Path.Combine(mainFolder, "name_id_map.json"))
                    ? JsonSerializer.Deserialize<NameIdMap>(File.ReadAllText(Path.Combine(mainFolder, "name_id_map.json")))
                    : new();
    mangosFolders = Directory.GetDirectories(mainFolder).Select(d => new DirectoryInfo(d).Name).ToList();

    if (mangosFolders.Count == 0)
    {
        Log.Information("Main folder is empty, nothing to do here.");
        return false;
    }

    Log.Verbose("List of subfolders : {0}", mangosFolders);

    if (groupsNeeded && mainFolderMap.IsGroupEmpty)
    {
        Log.Error("No group detected in the main folder's map. Get the groups' names and assign their id first to proceed.");
        return false;
    }

    if (mangosNeeded && mainFolderMap.IsMangoEmpty)
    {
        Log.Error("No titles detected in the main folder's map. Title match first to proceed.");
        return false;
    }

    return true;
}

#region General

void GetGroupsNames()
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
            var parsedFolderName = ParseFolderName(folderName);
            var chapterGroups = parsedFolderName["group"].Value.Split('+').OrderBy(t => t).ToArray();

            foreach (var chapterGroup in chapterGroups)
            {
                if (!groupsDic.Keys.Contains(chapterGroup))
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

async Task TitleMatching()
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
            titleLog = id + " (aggregate)";
            matched++;
        }
        else
        {
            results = await api.Manga.List(new MangaFilter { Title = mangoName, ContentRating = [ContentRating.safe, ContentRating.suggestive, ContentRating.erotica, ContentRating.pornographic] });
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
                                titles += "\n" + new string(' ', 10)
                                    + altTitles.First(alt => alt.ContainsKey(language)).First().Value
                                    + " (" + language.ToUpper() + ")";
                            }
                        }

                        Console.WriteLine("Result " + (i + 1) + " : " + titles);
                    }

                    selection = Console.ReadLine();

                    switch (selection)
                    {
                        case "1":
                        case "2":
                        case "3":
                        case "4":
                        case "5":
                            id = results.Data[int.Parse(selection) - 1].Id;
                            titleLog = results.Data[int.Parse(selection) - 1].Attributes.Title.First().Value;
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
    PressKeyContinue();
    Log.Verbose("Done title matching.");
}

async Task CompareTitles()
{
    Log.Verbose("Comparing titles operation.");

    if (!LoadAndCheckMap(mangosNeeded: true)) { PressKeyContinue(); return; }

    foreach (var mangoName in mainFolderMap.Manga)
    {
        cancelToken.Token.ThrowIfCancellationRequested();

        if (!mangoName.Value.Contains("Not found") && !mangoName.Value.Contains("None picked"))
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
                    titles += "\n" + new string(' ', mangoName.Key.Length + 3)
                        + altTitles.First(alt => alt.ContainsKey(language)).First().Value
                        + " (" + language.ToUpper() + ")";
                }
            }

            Console.WriteLine(mangoName.Key + " : " + titles);
            Console.WriteLine();
        }
    }

    PressKeyContinue();
    Log.Verbose("Done comparing titles.");
}

void CheckForDuplicates()
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

async Task FindVolumeNumbers()
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
            var parsedFolderName = ParseFolderName(currentChapterFolder);

            if (!parsedFolderName["prefix"].Success)
            {
                Log.Verbose("Skipped oneshot");
                continue;
            }

            if (!parsedFolderName["chapter"].Success)
            {
                Log.Error("Could not find the chapter number on the folder \"{0}\"", currentChapterFolder);
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

async Task CheckForAlreadyUploadedChapters()
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

        Log.Information(currentMangoFolder);
        (List<Chapter> chapterList, bool success) = await FetchChapters(endpoint: FetchEndpoint.Manga, languages: settings.Languages, titleId: mangoDic[currentMangoFolder]);
        await Task.Delay(350);

        if (!success) { continue; }

        var chaptersFolders = Directory.GetDirectories(Path.Combine(mainFolder, currentMangoFolder)).Select(d => new DirectoryInfo(d).Name);

        foreach (var currentChapterFolder in chaptersFolders)
        {
            var parsedFolderName = ParseFolderName(currentChapterFolder);

            if (!parsedFolderName["chapter"].Success || !parsedFolderName["language"].Success)
            {
                errors++;
                Log.Error("Could not find the chapter number or language on the folder \"{0}\"", currentChapterFolder);
                continue;
            }

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

            var groupsIds = chapterGroups.Select(g => groupsDic[g]).ToArray();
            string chapterNumber = decimal.Parse(parsedFolderName["chapter"].Value).ToString();
            string chapterLanguage = parsedFolderName["language"].Value;
            string volumeNumber = parsedFolderName["volume"].Success ? decimal.Parse(parsedFolderName["volume"].Value).ToString() : string.Empty;
            bool foundChapter = chapterList.Any(c =>
                                               c.Attributes.Chapter == chapterNumber &&
                                               c.ScanlationGroups().Select(g => g.Id).ToArray().Intersect(groupsIds).Any() &&
                                               c.Attributes.TranslatedLanguage == chapterLanguage &&
                                               (string.IsNullOrEmpty(volumeNumber) || c.Attributes.Volume == volumeNumber)
                                               );

            if (foundChapter)
            {
                Directory.CreateDirectory(Path.Combine(movedChaptersFolder, currentMangoFolder));
                Directory.Move(Path.Combine(mainFolder, currentMangoFolder, currentChapterFolder), Path.Combine(movedChaptersFolder, currentMangoFolder, currentChapterFolder));
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

void LogChapters()
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

#endregion

#region Uploader

void MergeJsonMaps()
{
    Log.Verbose("Merging main folder's json map with uploader's operation.");

    if (!LoadAndCheckMap()) { PressKeyContinue(); return; }

    if (!AskForConfirmation("This will merge the main folder's json map with the uploader's. Existing entries' value will be overwritten. A backup will be created. Confirm? (Y/n)")) { return; }

    var uploaderMap = JsonSerializer.Deserialize<NameIdMap>(File.ReadAllText(settings.PathToUploaderMap));
    uploaderMap.Merge(mainFolderMap);
    File.Copy(settings.PathToUploaderMap, Path.Combine(settings.UploaderFolder, "name_id_map_backup.json"), true);
    File.WriteAllText(settings.PathToUploaderMap, JsonSerializer.Serialize(uploaderMap, jsonSerializerOptions), uTF8Encoding);
    Log.Verbose("Done merging main folder's json maps with uploader's.");
}

void MoveToUploader()
{
    Log.Verbose("Moving folders to uploader operation.");

    if (!LoadAndCheckMap()) { PressKeyContinue(); return; }

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

void MoveBackFromUploader()
{
    Log.Verbose("Moving folders back from uploader operation.");

    if (!LoadAndCheckMap()) { PressKeyContinue(); return; }

    var movedFolders = Directory.GetDirectories(Path.Combine(settings.UploaderFolder, "to_upload"));

    foreach (string folder in movedFolders)
    {
        string title = ParseFolderName(Path.GetFileName(folder))["title"].Value;
        string backto = Path.Combine(mainFolder, title + "\\", Path.GetFileName(folder));
        Directory.Move(folder, backto);
    }

    Log.Verbose("Done moving folders back from uploader.");
}

#endregion

#region Misc

async Task GetIdsWithCreatedAt()
{
    Log.Verbose("Getting chapters ids operation.");

    Console.WriteLine("Enter minimum datetime (format YYYY/MM/DD h:mm:ss):");
    DateTime minDate = DateTime.Parse(Console.ReadLine());
    Console.WriteLine("Enter maximum datetime (format YYYY/MM/DD h:mm:ss):");
    DateTime maxDate = DateTime.Parse(Console.ReadLine());
    Console.WriteLine("Enter mango ID:");
    string titleId = Console.ReadLine();
    Console.WriteLine("Enter uploader ID (or don't):");
    string uploaderId = Console.ReadLine();
    Console.WriteLine("Enter group ID (or don't):");
    string groupId = Console.ReadLine();
    Console.WriteLine("Enter language code, using space as separator if more than one:");
    var languages = Console.ReadLine().Split(' ');
    Log.Verbose("Min date: {0}\r\nMax date: {1}\r\nMango ID: {2}\r\nUploader ID: {3}\r\nLanguage codes: {4}.", minDate, maxDate, languages, uploaderId, languages);

    (List<Chapter> chapterList, bool success) = await FetchChapters(endpoint: FetchEndpoint.Chapter, languages: languages, titleId: titleId, uploaderId: uploaderId);

    if (!success) { return; }

    StringBuilder chaptersFound = new();

    foreach (var chapter in chapterList)
    {
        if (chapter.Attributes.CreatedAt <= maxDate && chapter.Attributes.CreatedAt >= minDate)
        {
            chaptersFound.AppendLine(chapter.Id);
        }
    }

    File.WriteAllText("chaptersIds.txt", chaptersFound.ToString(), uTF8Encoding);
    Log.Information("File chaptersIds.txt created.");
    PressKeyContinue();
    Log.Verbose("Done fetching ids");
}

async Task ListTitleUploadsFromUser()
{
    Log.Verbose("List titles with uploads from a user operation.");

    Console.WriteLine("Enter a user id:");
    string uploaderId = Console.ReadLine();
    Log.Verbose("User id: {0}", uploaderId);
    (List<Chapter> chapterList, bool success) = await FetchChapters(endpoint: FetchEndpoint.Chapter, uploaderId: uploaderId);

    if (!success) { PressKeyContinue(); return; }

    var titleIds = chapterList.Select(ch => ch.Relationship<RelatedDataRelationship>()[0].Id).Distinct().ToList();
    File.WriteAllLines($"{uploaderId}.txt", titleIds);
    Console.WriteLine($"File {uploaderId}.txt has been created.");
    Log.Verbose("Done listing titles with uploads from a user.");
    PressKeyContinue();
}

async Task CheckChapterCount()
{
    Log.Verbose("Check chapter count of titles operation.");

    Console.WriteLine("Enter title urls or ids (one per line) then press enter :");
    List<string> urls = new();
    string input;
    Dictionary<string, Dictionary<string, TitleChapterCount>> jsonLog = new();

    while ((input = Console.ReadLine()) != "")
    {
        urls.Add(input);
    }

    foreach (string url in urls)
    {
        cancelToken.Token.ThrowIfCancellationRequested();

        Log.Information("Checking: " + url.Pastel("FF6740"));
        string id = url.Contains('/') ? url.Split("/")[4] : url;
        jsonLog.Add(id, new Dictionary<string, TitleChapterCount>());

        foreach (string language in settings.Languages)
        {
            var aggregate = await api.Manga.Aggregate(id, [language]);
            await Task.Delay(300);
            var chapters = aggregate.Volumes.SelectMany(volume => volume.Value.Chapters.Values.Select(chap => chap.Chapter)).Reverse().ToList();
            Log.Information("- {0}: {1} chapters.", language.ToUpper(), chapters.Count);
            var gaps = CheckForGaps(chapters);

            if (gaps.Count > 0)
            {
                Log.Information("Gaps found:");

                foreach (string gap in gaps)
                {
                    Log.Information(gap);
                }
            }
            jsonLog[id].Add(language, new(chapters.Count, gaps.ToArray()));
        }

        Console.WriteLine();
    }

    File.WriteAllText("Logs/chapterCountLog.json", JsonSerializer.Serialize(jsonLog, jsonSerializerOptions), uTF8Encoding);
    Console.WriteLine("Log file \"chapterCountLog.json\" has been created in the logs folder.");
    PressKeyContinue();
    Log.Verbose("Done checking chapter count of titles.");
}

#endregion

#region TMO

async Task UpdateWithTMOAggregate()
{
    Log.Verbose("Updating using TMO aggregate operation.");

    if (!AskForConfirmation("Don't do this unless you're scraping TMO. Confirm? (Y/n)")) { return; }

    var onlineMap = await GetOnlineNameIdMap();
    var localMap = GetLocalNameIdMap();
    localMap.Merge(onlineMap);
    File.Copy(settings.PathToUploaderMap, Path.Combine(settings.UploaderFolder, "name_id_map_backup.json"));
    Log.Verbose("Backup created.");
    File.WriteAllText(settings.PathToUploaderMap, JsonSerializer.Serialize(localMap, jsonSerializerOptions), uTF8Encoding);
    Log.Verbose("Done updating.");
}

async Task CheckTMOAggregateForDuplicates()
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

#endregion

void SetupLogger()
{
    Dictionary<ConsoleThemeStyle, string> themeDic = new()
    {
        [ConsoleThemeStyle.Text] = "\x1b[38;2;192;192;192m",
        [ConsoleThemeStyle.SecondaryText] = "\x1b[38;2;192;192;192m",
        [ConsoleThemeStyle.TertiaryText] = "\x1b[38;2;255;255;255m",
        [ConsoleThemeStyle.Invalid] = "\x1b[38;2;255;103;64m",
        [ConsoleThemeStyle.Null] = "\x1b[38;2;255;103;64m",
        [ConsoleThemeStyle.Name] = "\x1b[38;2;255;103;64m",
        [ConsoleThemeStyle.String] = "\x1b[38;2;255;103;64m",
        [ConsoleThemeStyle.Number] = "\x1b[38;2;255;103;64m",
        [ConsoleThemeStyle.Boolean] = "\x1b[38;2;255;103;64m",
        [ConsoleThemeStyle.Scalar] = "\x1b[38;2;255;103;64m",
        [ConsoleThemeStyle.LevelVerbose] = "\x1b[38;5;0007m",
        [ConsoleThemeStyle.LevelDebug] = "\x1b[38;5;0007m",
        [ConsoleThemeStyle.LevelInformation] = "\x1b[38;5;0015m",
        [ConsoleThemeStyle.LevelWarning] = "\x1b[38;5;0011;4m",
        [ConsoleThemeStyle.LevelError] = "\x1b[38;2;255;0;0;4m",
        [ConsoleThemeStyle.LevelFatal] = "\x1b[38;5;0015m",
    };
    var consoleTheme = new AnsiConsoleTheme(themeDic);
    var logConf = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(log => log.Level == Serilog.Events.LogEventLevel.Information)
                    .WriteTo.Console(outputTemplate: "{Message:lj}{NewLine}{Exception}", theme: consoleTheme))
                .WriteTo.Logger(lc => lc
                .Filter.ByExcluding(log => log.Level == Serilog.Events.LogEventLevel.Information || log.Level == Serilog.Events.LogEventLevel.Verbose)
                    .WriteTo.Console(outputTemplate: "{Level}: {Message:lj}{NewLine}{Exception}", theme: consoleTheme));
    logConf.WriteTo.File(
    "Logs/mdshLog_.txt",
    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
    fileSizeLimitBytes: 50000000,
    rollingInterval: RollingInterval.Day,
    rollOnFileSizeLimit: true,
    retainedFileTimeLimit: TimeSpan.FromDays(7)
    );

    Log.Logger = logConf.CreateLogger();
}

async Task<(List<Chapter> chapterList, bool success)> FetchChapters(FetchEndpoint endpoint, string[]? languages = null, string[]? groups = null, string titleId = "", string uploaderId = "", DateTime? createdAt = null)
{
    bool success = true;
    List<Chapter> chapterList = new();
    IPaginateFilter filter = GetChapterFilter(endpoint, languages, groups, uploaderId, titleId, createdAt);

    int totalChapters = 0;

    using (var progress = new ProgressBar() { Maximum = null })
    {
        progress.Text.Body.Processing.SetValue("Fetching chapters...");
        progress.Text.Description.Processing.AddNew().SetValue(pb => $"{progress.Value} of {progress.Maximum} in {progress.TimeProcessing.ToString("hh\\:mm\\:ss")}");

        do
        {
            cancelToken.Token.ThrowIfCancellationRequested();

            var fetchedChapters = endpoint switch
            {
                FetchEndpoint.Manga => await api.Manga.Feed(titleId, filter as MangaFeedFilter),
                FetchEndpoint.Chapter => await api.Chapter.List(filter as ChaptersFilter),
                _ => throw new NotImplementedException(),
            };

            if (fetchedChapters.ErrorOccurred)
            {
                progress.Text.Body.Done.SetValue(pb => "Failed.").SetForegroundColor(ConsoleColor.DarkRed);
                success = false;
                break;
            }

            chapterList.AddRange(fetchedChapters.Data);
            progress.Maximum = totalChapters = fetchedChapters.Total;
            filter.Offset = chapterList.Count;
            progress.PerformStep(fetchedChapters.Data.Count);
            await Task.Delay(350);
            Log.Verbose("Total chapters: {0}.\tChapters fetched: {1}.", totalChapters, chapterList.Count);

        } while (chapterList.Count < totalChapters);
    }

    Console.WriteLine("\r\n\r\n");

    if (!success)
    {
        Log.Error("Error while fetching chapters: wrong or missing ID/ratelimited/MD down/Mr. MangaDex hates you.");
        Log.Verbose("Last total: {0}. Chapter count: {1}", totalChapters, chapterList.Count);
        Console.WriteLine();
    }

    return (chapterList, success);
}

NameIdMap GetLocalNameIdMap()
{
    return JsonSerializer.Deserialize<NameIdMap>(File.ReadAllText(settings.PathToUploaderMap));
}

async Task<NameIdMap> GetOnlineNameIdMap()
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
    var gist = await client.GetStreamAsync($"https://api.github.com/gists/{settings.AggregateGistId}");
    var jsonSerOptions = new JsonSerializerOptions();
    jsonSerOptions.Converters.Add(new GistResponseParser());

    return JsonSerializer.Deserialize<NameIdMap>(gist, jsonSerOptions);
}

GroupCollection ParseFolderName(string folderName)
{
    return Regex.Match(folderName, FILENAME_REGEX, RegexOptions.IgnoreCase).Groups;
}

List<string> CheckForGaps(List<string> chapterNumbersList)
{
    var parsedList = chapterNumbersList.Select(val => Math.Floor(decimal.Parse(val, CultureInfo.InvariantCulture))).ToList();
    List<string> gapList = new();

    if (parsedList.Count > 0 && parsedList[0] > 1)
    {
        gapList.Add($"0-{chapterNumbersList[0]}");
    }

    for (int i = 1; i < parsedList.Count - 1; i++)
    {
        if ((parsedList[i + 1] - parsedList[i]) > 1)
        {
            gapList.Add($"{chapterNumbersList[i]}-{chapterNumbersList[i + 1]}");
        }
    }

    return gapList;
}

void PressKeyContinue()
{
    Console.WriteLine();
    Console.WriteLine("Press any key to continue.");
    Console.ReadKey(true);
    Console.WriteLine();
}

bool AskForConfirmation(string message, Action? additionalLog = null)
{
    Log.Warning(message);

    if (additionalLog != null)
    {
        additionalLog();
    }

    string? input = Console.ReadLine().ToLower();
    Console.WriteLine();

    return string.IsNullOrWhiteSpace(input) || input == "y";
}

void SerializeFolderMap(NameIdMap map)
{
    File.WriteAllText(Path.Combine(mainFolder, "name_id_map.json"), JsonSerializer.Serialize(map, jsonSerializerOptions), uTF8Encoding);
}


IPaginateFilter GetChapterFilter(FetchEndpoint endpoint, string[]? languages = null, string[]? groups = null, string uploaderId = "", string titleId = "", DateTime? createdAt = null) => endpoint switch
{
    FetchEndpoint.Manga => new MangaFeedFilter { TranslatedLanguage = languages ?? Array.Empty<string>(), Limit = 500, Order = { { MangaFeedFilter.OrderKey.chapter, OrderValue.asc } } },
    FetchEndpoint.Chapter => new ChaptersFilter() { Manga = titleId, Groups = groups ?? Array.Empty<string>(), TranslatedLanguage = languages ?? Array.Empty<string>(), CreatedAtSince = createdAt, Uploader = uploaderId, Limit = 100, Order = { { ChaptersFilter.OrderKey.chapter, OrderValue.asc } } },
    _ => throw new NotImplementedException()
};

enum FetchEndpoint { Manga, Chapter }



