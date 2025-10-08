using MangaDexSharp;
using Pastel;
using Serilog;
using System.Text;
using System.Text.Json;

namespace MDUploadHelper
{
    partial class Program
    {
        static async Task GetIdsWithCreatedAt()
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
            Console.WriteLine("Enter language code, using + as a separator if more than one:");
            var languages = Console.ReadLine().Split('+');
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

        static async Task ListTitleUploadsFromUser()
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

        static async Task CheckChapterCount()
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

                Log.Information("Checking: " + url.Pastel(mdOrange));
                string id = url.Contains('/') ? url.Split("/")[4] : url;
                jsonLog.Add(id, new Dictionary<string, TitleChapterCount>());

                foreach (string language in settings.Languages)
                {
                    var aggregate = await api.Manga.Aggregate(id, [language], includeUnavailable:true);
                    await Task.Delay(300);
                    var chapters = aggregate.Volumes.SelectMany(volume => volume.Value.Chapters.Values);
                    int unavailableChaptersCount = chapters.Where(chapData => chapData.IsUnavailable).Count();
                    var chaptersNumbersList = chapters.Where(chapData => !chapData.IsUnavailable).Select(chap => chap.Chapter).Reverse().Distinct().ToList();
                    Log.Information("- {0}: {1} available chapters.", language.ToUpper(), chaptersNumbersList.Count);

                    if (unavailableChaptersCount > 0)
                    {
                        Log.Warning("{0} unavailable chapters.", unavailableChaptersCount);
                    }

                    var gaps = CheckForGaps(chaptersNumbersList);

                    if (gaps.Count > 0)
                    {
                        Log.Information("Gaps found:");

                        foreach (string gap in gaps)
                        {
                            Log.Information(gap);
                        }
                    }

                    jsonLog[id].Add(language, new(chaptersNumbersList.Count, unavailableChaptersCount, gaps.ToArray()));
                }

                Console.WriteLine();
            }

            File.WriteAllText("chapterCountLog.json", JsonSerializer.Serialize(jsonLog, jsonSerializerOptions), uTF8Encoding);
            Console.WriteLine("Log file \"chapterCountLog.json\" has been created.");
            PressKeyContinue();
            Log.Verbose("Done checking chapter count of titles.");
        }
    }
}
