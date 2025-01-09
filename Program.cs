using MangaDexSharp;
using System.Text;
using System.Text.RegularExpressions;
using Serilog;
using System.Globalization;
using iluvadev.ConsoleProgressBar;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Configuration;
using Pastel;
using Serilog.Sinks.SystemConsole.Themes;
using MDUploadHelper;

namespace MDUploadHelper
{
    partial class Program
    {
        [GeneratedRegex(@"(?:\[(?<artist>.+?)?\])?\s?(?<title>.+?)(?:\s?\[(?<language>[a-z]{2}(?:-[a-z]{2})?|[a-zA-Z]{3}|[a-zA-Z]+)?\])?\s-\s(?<prefix>(?:[c](?:h(?:a?p?(?:ter)?)?)?\.?\s?))?(?<chapter>\d+(?:\.\d+)?)(?:\s?\((?:[v](?:ol(?:ume)?(?:s)?)?\.?\s?)?(?<volume>\d+(?:\.\d+)?)?\))?(?:\s?\((?<chapter_title>.+)\))?(?:\s?\{(?<publish_date>(?<publish_year>\d{4})-(?<publish_month>\d{2})-(?<publish_day>\d{2})(?:[T\s](?<publish_hour>\d{2})[\:\-](?<publish_minute>\d{2})(?:[\:\-](?<publish_microsecond>\d{2}))?(?:(?<publish_offset>[+-])(?<publish_timezone>\d{2}[\:\-]?\d{2}))?)?)\})?(?:\s?\[(?:(?<group>.+))?\])?(?:\s?\{v?(?<version>\d)?\})?(?:\.(?<extension>zip|cbz))?$", RegexOptions.IgnoreCase, "fr-FR")]
        private static partial Regex Filename_Regex();

        static IMangaDex? api = null;
        static Settings? settings = null;
        static string mainFolder = "";
        static List<string>? mangosFolders = null;
        static JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };
        static NameIdMap? mainFolderMap = null;
        static UTF8Encoding uTF8Encoding = new(false);
        static CancellationTokenSource? cancelToken = null;
        static readonly string mdOrange = "FF6740";

        private static async Task Main()
        {
            CultureInfo customCulture = (CultureInfo)System.Threading.Thread.CurrentThread.CurrentCulture.Clone();
            customCulture.NumberFormat.NumberDecimalSeparator = ".";
            System.Threading.Thread.CurrentThread.CurrentCulture = customCulture;

            if (!Init()) return;

            while (true)
            {
                try
                {
                    Console.WriteLine();
                    Console.WriteLine("[- - - - - - MD Upload Helper - - - - - -]".Pastel(ConsoleColor.White).PastelBg("EA471D"));
                    Console.WriteLine();
                    Console.WriteLine("[- - - - - General - - - - -]".Pastel(ConsoleColor.White).PastelBg("EA471D"));
                    Console.WriteLine($"{"1".Pastel(mdOrange)} - Get groups' names");
                    Console.WriteLine($"{"2".Pastel(mdOrange)} - Match titles");
                    Console.WriteLine($"{"3".Pastel(mdOrange)} - Compare mango titles");
                    Console.WriteLine($"{"4".Pastel(mdOrange)} - Check for duplicates");
                    Console.WriteLine($"{"5".Pastel(mdOrange)} - Find volume numbers");
                    Console.WriteLine($"{"6".Pastel(mdOrange)} - Check for already uploaded chapters");
                    Console.WriteLine($"{"7".Pastel(mdOrange)} - Log chapters");
                    Console.WriteLine();
                    Console.WriteLine("[- - - - - Bulk Uploader - - - - -]".Pastel(ConsoleColor.White).PastelBg("EA471D"));
                    Console.WriteLine($"{"8".Pastel(mdOrange)} - Merge json maps");
                    Console.WriteLine($"{"9".Pastel(mdOrange)} - Move chapters to uploader");
                    Console.WriteLine($"{"10".Pastel(mdOrange)} - Move chapters back from uploader");
                    Console.WriteLine();
                    Console.WriteLine("[- - - - - Miscellaneous - - - - - ]".Pastel(ConsoleColor.White).PastelBg("EA471D"));
                    Console.WriteLine($"{"11".Pastel(mdOrange)} - Fetch chapter ids using creation time");
                    Console.WriteLine($"{"12".Pastel(mdOrange)} - List titles with uploads from a user");
                    Console.WriteLine($"{"13".Pastel(mdOrange)} - Check chapter count of titles");
                    Console.WriteLine();
                    Console.WriteLine("[- - - - - TMO Related - - - - - ]".Pastel(ConsoleColor.White).PastelBg("EA471D"));
                    Console.WriteLine($"{"14".Pastel(mdOrange)} - Merge local json with online aggregate");
                    Console.WriteLine($"{"15".Pastel(mdOrange)} - Check for duplicates in aggregate");
                    Console.WriteLine();
                    Console.WriteLine($"{"16".Pastel(mdOrange)} - Exit ");
                    Console.WriteLine();

                    cancelToken = null;
                    string? input = Console.ReadLine();

                    if (input == null) { return; }

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
                catch (OperationCanceledException)
                {
                    Console.WriteLine();
                    Log.Information("Aborted operation".Pastel(ConsoleColor.White));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "An unexpected error has occurred.");
                    PressKeyContinue();
                }
            }
        }

        static bool Init()
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

                if (!settings.CheckSettings()) { Console.WriteLine(); }

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

        static void SetupLogger()
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

        static bool LoadAndCheckMap(bool mangosNeeded = false, bool groupsNeeded = false)
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

        static async Task<(List<Chapter> chapterList, bool success)> FetchChapters(FetchEndpoint endpoint, string[]? languages = null, string[]? groups = null, string titleId = "", string uploaderId = "", DateTime? createdAt = null)
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

        static NameIdMap GetLocalNameIdMap()
        {
            return File.Exists(settings.PathToUploaderMap) ? JsonSerializer.Deserialize<NameIdMap>(File.ReadAllText(settings.PathToUploaderMap)) : new NameIdMap();
        }

        static async Task<NameIdMap> GetOnlineNameIdMap()
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
            var gist = await client.GetStreamAsync($"https://api.github.com/gists/{settings.AggregateGistId}");
            var jsonSerOptions = new JsonSerializerOptions();
            jsonSerOptions.Converters.Add(new GistResponseParser());

            return JsonSerializer.Deserialize<NameIdMap>(gist, jsonSerOptions);
        }

        static bool ParseFolderName(string folderName, out GroupCollection parsed)
        {
            parsed = Filename_Regex().Match(folderName).Groups;
            return parsed[0].Success;
        }

        static List<string> CheckForGaps(List<string> chapterNumbersList)
        {
            var parsedList = chapterNumbersList.Select(val => Math.Floor(decimal.Parse(val, CultureInfo.InvariantCulture))).ToList();
            List<string> gapList = new();

            if (parsedList.Count > 0 && parsedList[0] > 1)
            {
                gapList.Add($"0-{chapterNumbersList[0]}");
            }

            for (int i = 1; i < parsedList.Count - 1; i++)
            {
                if (parsedList[i + 1] - parsedList[i] > 1)
                {
                    gapList.Add($"{chapterNumbersList[i]}-{chapterNumbersList[i + 1]}");
                }
            }

            return gapList;
        }

        static void PressKeyContinue()
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to continue.");
            Console.ReadKey(true);
            Console.WriteLine();
        }

        static bool AskForConfirmation(string message, Action? additionalLog = null)
        {
            Log.Warning(message);

            additionalLog?.Invoke();

            string? input = Console.ReadLine().ToLower();
            Console.WriteLine();

            return string.IsNullOrWhiteSpace(input) || input == "y";
        }

        static void SerializeFolderMap(NameIdMap map)
        {
            File.WriteAllText(Path.Combine(mainFolder, "name_id_map.json"), JsonSerializer.Serialize(map, jsonSerializerOptions), uTF8Encoding);
        }

        static IPaginateFilter GetChapterFilter(FetchEndpoint endpoint, string[]? languages = null, string[]? groups = null, string uploaderId = "", string titleId = "", DateTime? createdAt = null) => endpoint switch
        {
            FetchEndpoint.Manga => new MangaFeedFilter { TranslatedLanguage = languages ?? Array.Empty<string>(), Limit = 500, Order = { { MangaFeedFilter.OrderKey.chapter, OrderValue.asc } } },
            FetchEndpoint.Chapter => new ChaptersFilter() { Manga = titleId, Groups = groups ?? Array.Empty<string>(), TranslatedLanguage = languages ?? Array.Empty<string>(), CreatedAtSince = createdAt, Uploader = uploaderId, Limit = 100, Order = { { ChaptersFilter.OrderKey.chapter, OrderValue.asc } } },
            _ => throw new NotImplementedException()
        };

        enum FetchEndpoint { Manga, Chapter }
    }
}

