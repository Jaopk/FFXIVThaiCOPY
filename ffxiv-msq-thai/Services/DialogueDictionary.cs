using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FfxivMsqThai.Services;

public class DialogueDictionary
{
    private readonly Dictionary<string, string> _globalExactMatch = new(StringComparer.Ordinal);
    private readonly IPluginLog _log;

    public int Count => _globalExactMatch.Count;

    public DialogueDictionary(string contentRoot, IPluginLog log)
    {
        _log = log;
        var root = Path.Combine(contentRoot, "content");
        if (!Directory.Exists(root))
        {
            _log.Warning($"[ffxiv-msq-thai] content/ not found at: {root}");
            return;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "alignment.json") continue;
            LoadIntoGlobalIndex(file);
        }

        LoadAlignmentMap(root);

        _log.Information($"[ffxiv-msq-thai] Dictionary index ready — {_globalExactMatch.Count} sentences loaded.");
    }

    private void LoadAlignmentMap(string contentRoot)
    {
        var alignmentPath = Path.Combine(contentRoot, "alignment.json");
        if (!File.Exists(alignmentPath)) return;

        try
        {
            var alignmentJson = File.ReadAllText(alignmentPath);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(alignmentJson);
            if (map == null) return;

            int count = 0;
            foreach (var pair in map)
            {
                var gameKey = pair.Key;
                var transKey = pair.Value;

                if (_globalExactMatch.TryGetValue(transKey, out var translation))
                {
                    if (_globalExactMatch.TryAdd(gameKey, translation))
                    {
                        count++;
                    }
                }
            }
            _log.Information($"[ffxiv-msq-thai] Loaded alignment.json: {count} phrase aliases mapped.");
        }
        catch (Exception ex)
        {
            _log.Error($"[ffxiv-msq-thai] Failed to load alignment.json: {ex.Message}");
        }
    }

    private static readonly Regex SlashRegex = new(@"\b(\w+)\s*/\s*(\w+)\b", RegexOptions.Compiled);

    public static List<string> GetKeyVariants(string input)
    {
        var cleanInput = SeControlRegex.Replace(input, string.Empty).Replace("*", string.Empty);
        var matches = SlashRegex.Matches(cleanInput);

        var results = new List<string> { cleanInput };

        foreach (Match match in matches)
        {
            var fullMatch = match.Value;
            var left = match.Groups[1].Value;
            var right = match.Groups[2].Value;

            var nextResults = new List<string>();
            foreach (var r in results)
            {
                nextResults.Add(r.Replace(fullMatch, left));
                nextResults.Add(r.Replace(fullMatch, right));
            }
            results = nextResults;
        }

        var keys = new List<string>();
        foreach (var r in results)
        {
            var key = ToPureAlphanumericKey(r);
            if (!string.IsNullOrEmpty(key) && !keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private void LoadIntoGlobalIndex(string filePath)
    {
        var questFile = Read<QuestFile>(filePath);
        if (questFile?.Dialogues == null) return;

        foreach (var d in questFile.Dialogues)
        {
            var enText = d.TextEn ?? string.Empty;
            var thText = d.TextTh ?? string.Empty;

            if (string.IsNullOrWhiteSpace(enText) || string.IsNullOrWhiteSpace(thText)) continue;

            // Generate keys (handling slash pronoun variations)
            var keys = GetKeyVariants(enText);
            if (!string.IsNullOrEmpty(d.Key) && !keys.Contains(d.Key))
            {
                keys.Add(d.Key);
            }

            foreach (var key in keys)
            {
                _globalExactMatch.TryAdd(key, thText);
            }
        }
    }

    public string? GetTranslation(string gameTextEn)
    {
        var exactKey = ToPureAlphanumericKey(gameTextEn);
        if (_globalExactMatch.TryGetValue(exactKey, out var exactTranslation))
        {
            return exactTranslation;
        }

        return null;
    }

    private static readonly Regex SeControlRegex = new(@"[\x02][\s\S]{1,4}[\x03]|[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);

    public static string ToPureAlphanumericKey(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var noPayload = SeControlRegex.Replace(input, string.Empty);

        var sb = new StringBuilder(noPayload.Length);
        foreach (var c in noPayload)
        {
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        
        var key = sb.ToString();
        key = key.Replace("forenamesurname", string.Empty)
                 .Replace("forename", string.Empty)
                 .Replace("surname", string.Empty)
                 .Replace("playerplayer", string.Empty)
                 .Replace("player", string.Empty);
        return key;
    }

    private static readonly Regex MultiSpace = new(@" {2,}", RegexOptions.Compiled);

    public static string NormalizeEnglishKey(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var s = input.Trim().Replace("'", "'").Replace("“", "\"").Replace("”", "\"").Replace("\r", "").Replace("\n", " ");
        return MultiSpace.Replace(s, " ");
    }

    private static T? Read<T>(string path)
    {
        try
        {
            using var s = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(s, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (Exception)
        {
            return default;
        }
    }

    private class QuestFile
    {
        [JsonPropertyName("dialogues")] public List<Dialogue>? Dialogues { get; set; }
    }
    private class Dialogue
    {
        [JsonPropertyName("key")] public string? Key { get; set; }
        [JsonPropertyName("text_en")] public string? TextEn { get; set; }
        [JsonPropertyName("text")] public string? TextTh { get; set; }
    }
}