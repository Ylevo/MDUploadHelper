using System.Text.Json.Serialization;
using System.Text.Json;

namespace MDUploadHelper
{
    public class GistResponseParser : JsonConverter<NameIdMap>
    {
        public override NameIdMap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            while (reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("content"))
            {
                if (!reader.Read())
                {
                    throw new JsonException();
                }
            }

            reader.Read();
            string content = reader.GetString();

            while (reader.Read()) { }

            return JsonSerializer.Deserialize<NameIdMap>(content);
        }
        public override void Write(Utf8JsonWriter writer, NameIdMap value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, typeof(NameIdMap), options);
        }
    }

    public class NameIdMap
    {
        [JsonPropertyName("manga")]
        public Dictionary<string, string> Manga { get; set; } = new();

        [JsonPropertyName("group")]
        public Dictionary<string, string> Group { get; set; } = new();

        [JsonPropertyName("formats")]
        public Dictionary<string, string[]> Formats { get; set; } = new() { { "longstrip", Array.Empty<string>() }, { "widestrip", Array.Empty<string>() } };

        [JsonIgnore]
        public bool IsMangoEmpty => Manga.Count == 0;

        [JsonIgnore]
        public bool IsGroupEmpty => Group.Count == 0;

        public NameIdMap() { }

        public NameIdMap(Dictionary<string, string>? manga, Dictionary<string, string>? group)
        {
            Manga = manga ?? new();
            Group = group ?? new();
        }

        public void Merge(NameIdMap toSuckFrom)
        {
            toSuckFrom.Manga.ToList().ForEach(x => { Manga[x.Key] = x.Value; });
            toSuckFrom.Group.ToList().ForEach(x => { Group[x.Key] = x.Value; });
            toSuckFrom.Formats.ToList().ForEach(x => { Formats[x.Key] = Formats[x.Key].Concat(x.Value).Distinct().ToArray(); });
        }
    }

    public record TitleChapterCount([property: JsonPropertyName("count")] int Count, [property: JsonPropertyName("gaps")] string[] Gaps);

}
