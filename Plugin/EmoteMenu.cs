using System.Text.Json;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Translations;
using FortniteEmotes.API;
using Microsoft.Extensions.Logging;

namespace FortniteEmotes;

public partial class Plugin
{
    private void Emotes_RegisterSkinChangerCapabilities()
    {
        Capabilities.RegisterPluginCapability(
            new PluginCapability<Func<CCSPlayerController, string?>>("skinchanger:get_emote_list"),
            () => GetEmoteListJsonForVip);
        Capabilities.RegisterPluginCapability(
            new PluginCapability<Func<CCSPlayerController, string, bool>>("skinchanger:play_emote"),
            () => PlayEmoteForVip);
        Capabilities.RegisterPluginCapability(
            new PluginCapability<Func<CCSPlayerController, string, bool>>("skinchanger:play_dance"),
            () => PlayDanceForVip);
        Capabilities.RegisterPluginCapability(
            new PluginCapability<Func<CCSPlayerController, bool>>("skinchanger:stop_emote"),
            () => StopEmoteForVip);
    }

    /// <summary>VIP/SkinChanger: JSON со списками emotes + dances.</summary>
    internal string? GetEmoteListJsonForVip(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid)
            return null;

        var emotes = Config.EmoteDances
            .Where(e => e.IsEmote)
            .Select(e => new
            {
                id = e.Name,
                title = Localizer.ForPlayer(player, e.Name),
                isEmote = true
            })
            .ToList();

        var dances = Config.EmoteDances
            .Where(e => !e.IsEmote)
            .Select(e => new
            {
                id = e.Name,
                title = Localizer.ForPlayer(player, e.Name),
                isEmote = false
            })
            .ToList();

        if (emotes.Count == 0 && dances.Count == 0)
            return null;

        return JsonSerializer.Serialize(new { emotes, dances });
    }

    internal bool PlayEmoteForVip(CCSPlayerController? player, string? name)
        => PlayNamedForVip(player, name, isEmote: true);

    internal bool PlayDanceForVip(CCSPlayerController? player, string? name)
        => PlayNamedForVip(player, name, isEmote: false);

    internal bool StopEmoteForVip(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid)
            return false;
        StopEmote(player);
        return true;
    }

    private bool PlayNamedForVip(CCSPlayerController? player, string? name, bool isEmote)
    {
        if (player == null || !player.IsValid)
            return false;

        name = (name ?? "").Trim();
        if (string.IsNullOrEmpty(name))
            return false;

        var emote = FindEmoteById(name)
                    ?? GetEmoteByName(name, isEmote, partial: true)
                    ?? GetEmoteByName(name, isEmote, partial: false);

        if (emote == null)
            return false;

        string error = "";
        return PlayEmote(player, emote, ref error);
    }

    public string GetEmoteTranslation(string name)
    {
        return g_EmoteTransMap.ContainsKey(name) ? g_EmoteTransMap[name] : name;
    }

    /// <summary>
    /// Русские названия из Plugin/lang/ru.json (не зависит от языка клиента / Localizer).
    /// </summary>
    private void RebuildEmoteTransMapFromRu()
    {
        g_EmoteTransMap = new(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string>? ru = null;
        try
        {
            var ruPath = Path.Combine(ModuleDirectory, "lang", "ru.json");
            if (!File.Exists(ruPath))
                ruPath = Path.Combine(ModuleDirectory, "..", "lang", "ru.json");
            if (File.Exists(ruPath))
            {
                ru = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(ruPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Не удалось прочитать lang/ru.json — названия останутся как id");
        }

        foreach (var emote in Config.EmoteDances)
        {
            if (string.IsNullOrWhiteSpace(emote.Name))
                continue;

            string title = emote.Name;
            if (ru != null && ru.TryGetValue(emote.Name, out var t) && !string.IsNullOrWhiteSpace(t))
                title = t.Trim();
            else
            {
                // запасной вариант через Localizer
                try
                {
                    var loc = Localizer[emote.Name];
                    if (!string.IsNullOrWhiteSpace(loc) && loc != emote.Name)
                        title = loc;
                }
                catch { /* ignore */ }
            }

            g_EmoteTransMap[emote.Name] = title;
        }

        Logger.LogInformation("[FortniteEmotes] Загружено русских названий: {Count}", g_EmoteTransMap.Count);
        InvalidateEmoteCatalogCache();
    }

    /// <summary>Точный id из конфига (Name), без фильтра IsEmote.</summary>
    public Emote? FindEmoteById(string name)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrEmpty(name))
            return null;
        return Config.EmoteDances.FirstOrDefault(x =>
            x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public Emote? GetEmoteByName(string name, bool emote, bool partial = false)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrEmpty(name))
            return null;

        // VIP/SkinChanger передают id вида Emote_ThumbsUp — сначала точное совпадение Name.
        var byId = FindEmoteById(name);
        if (byId != null && byId.IsEmote == emote)
            return byId;

        if (partial)
        {
            var sname = name.ToLower();

            var list = Config.EmoteDances
                .Where(s => s.IsEmote == emote)
                .OrderBy(s =>
                {
                    var emoteName = s.Name.ToLower();
                    var emoteNameTranslated = GetEmoteTranslation(s.Name).ToLower();

                    var ems = emoteName.Split('_').ToList();

                    foreach (var em in ems)
                    {
                        if (em.Equals("Emote_") || em.Equals("Dance_"))
                            continue;

                        if (em.Equals(sname))
                            return -1;
                    }

                    ems = emoteNameTranslated.Split(' ').ToList();

                    foreach (var em in ems)
                    {
                        if (em.Equals(sname))
                            return -1;
                    }

                    return 1;
                })
                .Where(s =>
                {
                    var ems = s.Name.ToLower().Split('_').ToList();
                    foreach (var em in ems)
                    {
                        if (em.Equals(sname))
                            return true;
                    }

                    ems = GetEmoteTranslation(s.Name).ToLower().Split(' ').ToList();
                    foreach (var em in ems)
                    {
                        if (em.Equals(sname))
                            return true;
                    }

                    return false;
                })
                .ToList();

            return list.Count > 0 ? list.First() : null;
        }

        return Config.EmoteDances.FirstOrDefault(x =>
            GetEmoteTranslation(x.Name).Equals(name, StringComparison.OrdinalIgnoreCase) &&
            x.IsEmote == emote);
    }

    /// <summary>Каталог для SkinChanger VIP-меню (!equip). Без Localizer.ForPlayer — только кэш переводов.</summary>
    public string? BuildEmoteCatalogJson(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid)
            return null;

        try
        {
            if (_cachedEmoteCatalogJson != null)
                return _cachedEmoteCatalogJson;

            var emotes = new List<object>();
            var dances = new List<object>();

            foreach (var e in Config.EmoteDances)
            {
                var title = GetEmoteTranslation(e.Name);
                if (string.IsNullOrWhiteSpace(title))
                    title = e.Name;

                if (e.IsEmote)
                    emotes.Add(new { id = e.Name, title, isEmote = true });
                else
                    dances.Add(new { id = e.Name, title, isEmote = false });
            }

            if (emotes.Count == 0 && dances.Count == 0)
                return null;

            _cachedEmoteCatalogJson = System.Text.Json.JsonSerializer.Serialize(new { emotes, dances });
            return _cachedEmoteCatalogJson;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "BuildEmoteCatalogJson failed");
            return null;
        }
    }

    private string? _cachedEmoteCatalogJson;
    private string? _cachedEmoteTitleMapJson;

    internal void InvalidateEmoteCatalogCache()
    {
        _cachedEmoteCatalogJson = null;
        _cachedEmoteTitleMapJson = null;
    }

    /// <summary>JSON-объект id→русское название (без Localizer.ForPlayer).</summary>
    public string GetEmoteTitleMapJson()
    {
        if (_cachedEmoteTitleMapJson != null)
            return _cachedEmoteTitleMapJson;

        _cachedEmoteTitleMapJson = System.Text.Json.JsonSerializer.Serialize(g_EmoteTransMap);
        return _cachedEmoteTitleMapJson;
    }
}
