using MangaDexSharp;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Serilog;


System.Globalization.CultureInfo customCulture = (System.Globalization.CultureInfo)System.Threading.Thread.CurrentThread.CurrentCulture.Clone();
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
var api = MangaDex.Create();
string mainFolder = "";
List<string>? mangosFolders = null;
MangaList results;


Dictionary<string, string>?
    mangos = null,
    groups = null;

try
{
    if (!Init()) return;

    while (true)
    {
        Console.WriteLine("Menu :");
        Console.WriteLine();
        Console.WriteLine("0 - Update your name_id_map.json");
        Console.WriteLine("1 - Get groups names");
        Console.WriteLine("2 - Find mangos ids");
        Console.WriteLine("3 - Check for duplicates");
        Console.WriteLine("4 - Compare mango titles");
        Console.WriteLine("5 - Find volume numbers");
        Console.WriteLine("6 - Check for already uploaded chapters");
        Console.WriteLine("7 - Log chapters");
        Console.WriteLine("8 - Move folders to uploader");
        Console.WriteLine("9 - Move folders BACK from uploader");
        Console.WriteLine("10 - Fetch chapters' ids using datetimes");
        Console.WriteLine("11 - Check scrap status of titles");
        Console.WriteLine("12 - Exit");
        Console.WriteLine();

        string input = Console.ReadLine();
        Console.WriteLine();

        switch (input)
        {
            case "0":
                UpdateAggregate();
                break;
            case "1":
                GetGroupsNames();
                break;
            case "2":
                await TitleMatching();
                break;
            case "3":
                CheckForDuplicates();
                break;
            case "4":
                await CompareTitles();
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
                MoveToUploader();
                break;
            case "9":
                MoveBackFromUploader();
                break;
            case "10":
                await GetIds();
                break;
            case "11":
                await CheckScrapStatus();
                break;
            case "12":
                return;
        }

        Console.WriteLine();
    }
}
catch (Exception ex)
{
    Console.WriteLine(ex.ToString());
    Console.ReadLine();
}

bool Init()
{
    var logConf = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Logger(lc => lc
                    .Filter.ByExcluding(log => log.Level == Serilog.Events.LogEventLevel.Fatal)
                    .WriteTo.Console(outputTemplate: "{Level}: {Message:lj}{NewLine}"));
    logConf.WriteTo.File(
    "Logs/mdshLog_.txt",
    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
    fileSizeLimitBytes: 50000000,
    rollingInterval: RollingInterval.Day,
    rollOnFileSizeLimit: true,
    retainedFileTimeLimit: TimeSpan.FromDays(7)
    );

    Log.Logger = logConf.CreateLogger();

    try
    {
        Settings.LoadSettings();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Could not load the settings from settings.json, check the logs for more info. Exiting.");
        Console.ReadLine();
        return false;
    }

    while (!Directory.Exists(mainFolder))
    {
        Console.WriteLine("Enter the main folder's path.");
        mainFolder = Console.ReadLine();
    }

    mangosFolders = Directory.GetDirectories(mainFolder).Select(d => new DirectoryInfo(d).Name).ToList();

    return true;
}

void UpdateAggregate()
{
    Console.WriteLine("WARNING : this will erase your current name_id_map.json. Confirm? (y/n)");

    if (Console.ReadLine().ToLower() != "y")
    {
        Console.WriteLine("Update aborted.");
        return;
    }

    var aggregate = GetOnlineNameIdMapping();
    File.WriteAllText(Path.Combine(Settings.UploaderFolder, "name_id_map.json"), JsonSerializer.Serialize(aggregate, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true }), new UTF8Encoding(false));
}

async Task GetIds()
{
    List<Chapter> chapterList = new();
    var filter = new ChaptersFilter() { Limit = 500 };

    Console.WriteLine("Enter starting datetime (format YYYY/MM/DD h:mm:ss) :");
    DateTime minDate = DateTime.Parse(Console.ReadLine());
    Console.WriteLine("Enter ending datetime (format YYYY/MM/DD h:mm:ss) :");
    DateTime maxDate = DateTime.Parse(Console.ReadLine());
    Console.WriteLine("Enter mango ID :");
    filter.Manga = Console.ReadLine();
    Console.WriteLine("Enter uploader ID :");
    filter.Uploader = Console.ReadLine();
    filter.Limit = 100;
    Console.WriteLine("Enter language code :");
    filter.TranslatedLanguage = new string[] { Console.ReadLine() };

    var currentMangoChapters = await api.Chapter.List(filter);
    chapterList.AddRange(currentMangoChapters.Data);

    while (chapterList.Count < currentMangoChapters.Total)
    {
        filter.Offset = chapterList.Count;
        currentMangoChapters = await api.Chapter.List(filter);
        chapterList.AddRange(currentMangoChapters.Data);
        await Task.Delay(350);
    }

    string chaptersFound = "";

    foreach (var chapter in chapterList)
    {
        if (chapter.Attributes.CreatedAt <= maxDate && chapter.Attributes.CreatedAt >= minDate)
        {
            chaptersFound += chapter.Id + Environment.NewLine;
        }
    }

    File.WriteAllText("chaptersIds.txt", chaptersFound, new UTF8Encoding(false));
    Console.WriteLine("File chaptersIds.txt created.");
}

void LoadFiles()
{
    if (File.Exists(Path.Combine(mainFolder, "mangosId.json")))
    {
        using StreamReader temp = new StreamReader(Path.Combine(mainFolder, "mangosId.json"));
        mangos = JsonSerializer.Deserialize<Dictionary<string, string>>(temp.ReadToEnd());
    }

    if (File.Exists(Path.Combine(mainFolder, "groupsId.json")))
    {
        using StreamReader temp = new StreamReader(Path.Combine(mainFolder, "groupsId.json"));
        groups = JsonSerializer.Deserialize<Dictionary<string, string>>(temp.ReadToEnd());
    }

    mangosFolders = Directory.GetDirectories(mainFolder).Select(d => new DirectoryInfo(d).Name).ToList();
}

void GetGroupsNames()
{
    LoadFiles();

    var groupsAggregate = GetLocalNameIdMapping()["group"];

    Dictionary<string, string> groupsDic = new();

    foreach (string mangoName in mangosFolders)
    {
        var chaptersFolders = Directory.GetDirectories(Path.Combine(mainFolder, mangoName)).Select(d => new DirectoryInfo(d).Name);

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

    File.WriteAllText(Path.Combine(mainFolder, "groupsId.json"), JsonSerializer.Serialize(groupsDic, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true }), new UTF8Encoding(false));
}

async Task TitleMatching()
{
    LoadFiles();

    string titleLog = "", id = "", log = "", selection = "", titles = "";
    Dictionary<string, string> foundIds = new();
    var mangoLocalMapping = GetLocalNameIdMapping()["manga"];

    foreach (var mangoName in mangosFolders)
    {
        if (mangoLocalMapping.ContainsKey(mangoName))
        {
            Console.WriteLine($"Found \"{mangoName}\" in local name_id_map.json.");
            id = mangoLocalMapping[mangoName];
        }
        else
        {
            results = await api.Manga.List(new MangaFilter { Title = mangoName, ContentRating = new ContentRating[] { ContentRating.safe, ContentRating.suggestive, ContentRating.erotica, ContentRating.pornographic } });

            switch (results.Total)
            {
                case int x when x == 0:
                    titleLog = "Not found";
                    id = "Not found";
                    Console.WriteLine("No title found for " + mangoName);
                    break;
                case int x when x == 1:
                    titleLog = results.Data[x - 1].Attributes.Title.First().Value;
                    id = results.Data[x - 1].Id;
                    Console.WriteLine(mangoName + " matched with " + titleLog);
                    break;
                case int x when x > 1:
                    Console.WriteLine();
                    Console.WriteLine("Matching for : " + mangoName);

                    for (int i = 0; i < results.Data.Count && i < 5; i++)
                    {
                        var altTitles = results.Data[i].Attributes.AltTitles;
                        titles = results.Data[i].Attributes.Title.First().Value;

                        foreach (string language in Settings.Languages)
                        {
                            if (altTitles.Any(alt => alt.ContainsKey(language)))
                            {
                                titles += "\n" + new string(' ', 11) 
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
                            break;
                        case string s when s != " " && s != string.Empty:
                            id = s;
                            titleLog = s + " (manual input)";
                            break;
                        default:
                            id = "None picked";
                            titleLog = "None picked";
                            break;
                    }
                    Console.WriteLine();
                    break;
            }
        }
        log += $"{mangoName} : {titleLog}" + Environment.NewLine;
        foundIds.Add(mangoName, id);
        await Task.Delay(250);
    }

    File.WriteAllText(Path.Combine(mainFolder, "mangosIdLog.txt"), log, new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(mainFolder, "mangosId.json"), JsonSerializer.Serialize(foundIds, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true }), new UTF8Encoding(false));
    CheckForDuplicates();
}

void CheckForDuplicates()
{
    LoadFiles();
    if (mangos == null) { Console.WriteLine("No mangos files, ending."); return; }
    var mangaAggregate = GetLocalNameIdMapping()["manga"];

    foreach (var mango in mangos)
    {
        foreach (var foundDuplicate in mangaAggregate.Where(m => m.Value == mango.Value && m.Key != mango.Key && m.Value != "Not found" && m.Value != "None picked"))
        {
            Console.WriteLine($"MISMATCH WARNING : ID found for \"{mango.Key}\" is already matched with \"{foundDuplicate.Key}\" in the aggregate.");
        }

        foreach (var foundDuplicate in mangos.Where(m => m.Value == mango.Value && m.Key != mango.Key && m.Value != "Not found" && m.Value != "None picked"))
        {
            Console.WriteLine($"MISMATCH WARNING : \"{mango.Key}\" and \"{foundDuplicate.Key}\" have the same ID.");
        }
    }
}

async Task CompareTitles()
{
    LoadFiles();
    if (mangos == null) { Console.WriteLine("No mangos files, ending."); return; }

    foreach (var mangoName in mangos)
    {
        await Task.Delay(350);

        if (!mangoName.Value.Contains("Not found") && !mangoName.Value.Contains("None picked"))
        {
            var mangT = await api.Manga.Get(mangoName.Value);

            if (mangT.Result == "error")
            {
                Console.WriteLine($"Error : skipping \"{mangoName.Key}\" ; mango not found on MD.");
                continue;
            }

            var altTitles = mangT.Data.Attributes.AltTitles;
            string titles = mangT.Data.Attributes.Title.First().Value;

            foreach (string language in Settings.Languages)
            {
                if (altTitles.Any(alt => alt.ContainsKey(language)))
                {
                    titles += "\n" + new string(' ', mangoName.Key.Length + 3)
                        + altTitles.First(alt => alt.ContainsKey(language)).First().Value
                        + " (" + language.ToUpper() + ")";
                }
            }
            Console.WriteLine(mangoName.Key + " : " + titles);
        }
    }
}

async Task FindVolumeNumbers()
{
    LoadFiles();
    if (mangos == null) { Console.WriteLine("No mangos or groups ids files, ending."); return; }

    string chapterNumber = "", newChapterFolder = "";

    foreach (var currentMangoFolder in mangosFolders)
    {
        Console.WriteLine(currentMangoFolder);
        List<Chapter> chapterList = new();
        var currentMangoChapters = await api.Manga.Feed(mangos[currentMangoFolder], new MangaFeedFilter { Limit = 500, Order = { { MangaFeedFilter.OrderKey.chapter, OrderValue.asc } } });
        chapterList.AddRange(currentMangoChapters.Data);

        while (chapterList.Count < currentMangoChapters.Total)
        {
            currentMangoChapters = await api.Manga.Feed(mangos[currentMangoFolder], new MangaFeedFilter { Limit = 500, Offset = chapterList.Count, Order = { { MangaFeedFilter.OrderKey.chapter, OrderValue.asc } } });
            chapterList.AddRange(currentMangoChapters.Data);
            await Task.Delay(350);
        }

        var chaptersFolders = Directory.GetDirectories(Path.Combine(mainFolder, currentMangoFolder)).Select(d => new DirectoryInfo(d).Name);

        foreach (var currentChapterFolder in chaptersFolders)
        {
            var arrayChapterName = currentChapterFolder.Split(' ');
            string volumeNumber = Regex.Match(currentChapterFolder, @"(?<=\(v)(\d){1,2}(?=\))").Value;
            chapterNumber = arrayChapterName[3];

            if (chapterNumber == "000")
            {
                continue;
            }

            chapterNumber = decimal.Parse(chapterNumber.Replace("c", "")).ToString();
            var foundChapter = chapterList.Where(c => c.Attributes.Chapter == chapterNumber && c.Attributes.Volume != null).FirstOrDefault();

            if (foundChapter != null && volumeNumber == "")
            {
                newChapterFolder = currentChapterFolder.Insert(GetNthIndex(currentChapterFolder, ' ', 4), $" (v{foundChapter.Attributes.Volume})");
                Directory.Move(Path.Combine(mainFolder, currentMangoFolder, currentChapterFolder), Path.Combine(mainFolder, currentMangoFolder, newChapterFolder));
            }
        }

        await Task.Delay(350);
    }
}

async Task CheckForAlreadyUploadedChapters()
{
    LoadFiles();
    if (mangos == null) { Console.WriteLine("No mangos or groups ids files, ending."); return; }

    Console.WriteLine("Enter the path where already uploaded chapters should be moved :");
    string movedChaptersFolder = Console.ReadLine();
    if (movedChaptersFolder == null || movedChaptersFolder == "")
    {
        Console.WriteLine("No path entered, aborting.");
        return;
    }

    string chapterNumber = "";

    foreach (var currentMangoFolder in mangosFolders)
    {
        Console.WriteLine(currentMangoFolder);
        List<Chapter> chapterList = new();
        var currentMangoChapters = await api.Manga.Feed(mangos[currentMangoFolder], new MangaFeedFilter { Limit = 500, TranslatedLanguage = Settings.Languages, Order = { { MangaFeedFilter.OrderKey.chapter, OrderValue.asc } } });
        chapterList.AddRange(currentMangoChapters.Data);

        while (chapterList.Count < currentMangoChapters.Total)
        {
            currentMangoChapters = await api.Manga.Feed(mangos[currentMangoFolder], new MangaFeedFilter { Limit = 500, TranslatedLanguage = Settings.Languages, Offset = chapterList.Count, Order = { { MangaFeedFilter.OrderKey.chapter, OrderValue.asc } } });
            chapterList.AddRange(currentMangoChapters.Data);
            await Task.Delay(350);
        }

        var chaptersFolders = Directory.GetDirectories(Path.Combine(mainFolder, currentMangoFolder)).Select(d => new DirectoryInfo(d).Name);

        foreach (var currentChapterFolder in chaptersFolders)
        {
            var arrayChapterName = currentChapterFolder.Split(' ');
            string volumeNumber = Regex.Match(currentChapterFolder, @"(?<=\(v)(\d*)(?=\))").Value;
            var chapterGroups = String.Join(" ", arrayChapterName.Skip(volumeNumber == "" ? 4 : 5)).Trim(new char[] { '[', ']' }).Split('+').OrderBy(t => t).ToArray();
            chapterNumber = arrayChapterName[3];
            chapterNumber = chapterNumber == "000" ? null : decimal.Parse(chapterNumber.Replace("c", "")).ToString();

            string[] groupsId = new string[chapterGroups.Length];
            for (int i = 0; i < chapterGroups.Length; i++)
            {
                groupsId[i] = groups[chapterGroups[i]];
            }
            bool foundChapter = chapterList.Any(c =>
                                               c.Attributes.Chapter == chapterNumber &&
                                               c.ScanlationGroups().Select(g => g.Id).ToArray().Intersect(groupsId).Any()
                                               );

            if (foundChapter)
            {
                Directory.CreateDirectory(Path.Combine(movedChaptersFolder, currentMangoFolder));
                Directory.Move(Path.Combine(mainFolder, currentMangoFolder, currentChapterFolder), Path.Combine(movedChaptersFolder, currentMangoFolder, currentChapterFolder));
                Console.WriteLine("Moved " + currentChapterFolder);
            }
        }

        await Task.Delay(450);
    }
}

void MoveToUploader()
{
    foreach (var currentMangoFolder in mangosFolders)
    {
        var chaptersFolders = Directory.GetDirectories(Path.Combine(mainFolder, currentMangoFolder)).Select(d => new DirectoryInfo(d).Name);

        foreach (var currentChapterFolder in chaptersFolders)
        {
            Directory.Move(Path.Combine(mainFolder, currentMangoFolder, currentChapterFolder), Path.Combine(Settings.UploaderFolder, "to_upload", currentChapterFolder));
        }
    }
}

void MoveBackFromUploader()
{
    var movedFolders = Directory.GetDirectories(Path.Combine(Settings.UploaderFolder, "to_upload"));
    foreach (string folder in movedFolders)
    {
        string title = Path.GetFileName(folder).Split(' ').First();
        string backto = Path.Combine(mainFolder, title + "\\", Path.GetFileName(folder));
        Directory.Move(folder, backto);
    }
}

void LogChapters()
{
    LoadFiles();
    if (mangos == null) { Console.WriteLine("No mangos file, ending."); return; }

    string logOutput = "";
    int total = 0;
    var mangoFolders = Directory.GetDirectories(mainFolder).Select(d => new DirectoryInfo(d).Name);

    foreach (string mangoFolder in mangoFolders)
    {
        var chapterFolders = Directory.GetDirectories(Path.Combine(mainFolder, mangoFolder)).Select(d => new DirectoryInfo(d).Name);
        logOutput += $"{mangoFolder}:{mangos[mangoFolder]}\r{chapterFolders.Count()} chapters\r\r";
        total += chapterFolders.Count();

        foreach (string chapterFolder in chapterFolders)
        {
            logOutput += $"{chapterFolder}\r";
        }

        logOutput += "\r";
    }

    logOutput += $"Total:{total}";

    File.WriteAllText(Path.Combine(mainFolder, "chaptersLog.txt"), logOutput, new UTF8Encoding(false));
}

async Task CheckScrapStatus()
{
    Console.WriteLine("Enter title urls or ids (one per line) then press enter :");
    List<string> urls = new();
    string input;

    while ((input = Console.ReadLine()) != "")
    {
        urls.Add(input);
    }

    foreach (string url in urls)
    {
        Console.WriteLine("Checking : " + url);
        string id = url.Contains('/') ? url.Split("/")[4] : url;
        List<Chapter> chapterList = new();
        var chapters = await api.Manga.Feed(id, new MangaFeedFilter { TranslatedLanguage = Settings.Languages, Order = { { MangaFeedFilter.OrderKey.chapter, OrderValue.asc } } });

        foreach (string language in Settings.Languages)
        {
            Console.WriteLine(chapters.Data.Count(d => d.Attributes.TranslatedLanguage == language) + " chapters in " + language.ToUpper() + ".");
        }
        await Task.Delay(350);

        Console.WriteLine();
    }

    Console.WriteLine("Press any key to continue.");
    Console.ReadKey();
}

Dictionary<string, Dictionary<string, string>> GetLocalNameIdMapping()
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
    if (Settings.UploaderFolder == "" ||  !Directory.Exists(Settings.UploaderFolder))
    {
        throw new DirectoryNotFoundException();
    }
    string localJson = File.ReadAllText(Path.Combine(Settings.UploaderFolder, "name_id_map.json"));

    return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(localJson);
}

Dictionary<string, Dictionary<string, string>> GetOnlineNameIdMapping()
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
    dynamic gist = JsonConvert.DeserializeObject(client.GetStringAsync("https://api.github.com/gists/741e9f9a9d97304a0e7cb5a656e8f401").Result);
    dynamic aggregate = JsonConvert.DeserializeObject(gist["files"]["name_id_map.json"]["content"].ToString());

    return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(aggregate.ToString());
}

GroupCollection ParseFolderName(string folderName)
{
    return Regex.Match(folderName, FILENAME_REGEX, RegexOptions.IgnoreCase).Groups;
}

int GetNthIndex(string s, char t, int n)
{
    int count = 0;
    for (int i = 0; i < s.Length; i++)
    {
        if (s[i] == t)
        {
            count++;
            if (count == n)
            {
                return i;
            }
        }
    }
    return -1;
}

public static class Settings
{
    public static string UploaderFolder { get; private set; }
    public static string[] Languages { get; private set; }

    public static void LoadSettings()
    {
        string jsonString = File.ReadAllText("settings.json");
        dynamic settings = JsonConvert.DeserializeObject(jsonString);
        UploaderFolder = settings.uploaderFolder;
        Languages = settings.languages.ToObject<string[]>();
    }
}

