using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Translations;
using FortniteEmotes.API;
using Microsoft.Extensions.Logging;

namespace FortniteEmotes;

public partial class Plugin
{
    public PluginCapability<IFortniteEmotesAPI> g_PluginCapability = new("FortniteEmotes");
    public FortniteEmotesApi FortniteEmotesApi { get; private set; } = null!;

    public void API_OnLoad()
    {
        FortniteEmotesApi = new FortniteEmotesApi(this);
        Capabilities.RegisterPluginCapability(g_PluginCapability, () => FortniteEmotesApi);
        RegisterFortniteSoftCapabilities();
    }

    /// <summary>
    /// Soft-caps для SkinChanger/VIP без AssemblyReference на FortniteEmotesNDancesAPI
    /// (иначе SkinChanger не грузится, пока shared-API не в CSS shared/).
    /// </summary>
    private void RegisterFortniteSoftCapabilities()
    {
        try
        {
            Capabilities.RegisterPluginCapability(
                new PluginCapability<Func<CCSPlayerController, string?>>("fortnite_emotes:get_catalog"),
                () => p => BuildEmoteCatalogJson(p));
            Capabilities.RegisterPluginCapability(
                new PluginCapability<Func<string>>("fortnite_emotes:get_title_map"),
                () => GetEmoteTitleMapJson);
            Capabilities.RegisterPluginCapability(
                new PluginCapability<Func<CCSPlayerController, string, bool>>("fortnite_emotes:play_emote"),
                () => SoftPlayEmote);
            Capabilities.RegisterPluginCapability(
                new PluginCapability<Func<CCSPlayerController, string, bool>>("fortnite_emotes:play_dance"),
                () => SoftPlayDance);
            Capabilities.RegisterPluginCapability(
                new PluginCapability<Func<CCSPlayerController, bool>>("fortnite_emotes:stop"),
                () => SoftStopEmote);
            Capabilities.RegisterPluginCapability(
                new PluginCapability<Func<CCSPlayerController, bool>>("fortnite_emotes:is_dancing"),
                () => IsDancing);
            Logger.LogInformation("[FortniteEmotes] Soft capabilities registered (fortnite_emotes:*)");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[FortniteEmotes] Failed to register soft capabilities");
        }
    }

    private bool SoftPlayEmote(CCSPlayerController player, string name)
    {
        string error = "";
        try
        {
            var ok = FortniteEmotesApi.PlayEmote(player, name, ref error);
            if (!ok && !string.IsNullOrWhiteSpace(error) && player.IsValid)
                player.PrintToChat(error);
            return ok;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "SoftPlayEmote failed name={Name}", name);
            if (player.IsValid)
                player.PrintToChat($" {Localizer.ForPlayer(player, "emote.prefix")} {ex.Message}");
            return false;
        }
    }

    private bool SoftPlayDance(CCSPlayerController player, string name)
    {
        string error = "";
        try
        {
            var ok = FortniteEmotesApi.PlayDance(player, name, ref error);
            if (!ok && !string.IsNullOrWhiteSpace(error) && player.IsValid)
                player.PrintToChat(error);
            return ok;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "SoftPlayDance failed name={Name}", name);
            if (player.IsValid)
                player.PrintToChat($" {Localizer.ForPlayer(player, "emote.prefix")} {ex.Message}");
            return false;
        }
    }

    private bool SoftStopEmote(CCSPlayerController player)
    {
        try
        {
            FortniteEmotesApi.StopEmote(player);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public class FortniteEmotesApi : IFortniteEmotesAPI
{
    public Plugin plugin;

    public event IFortniteEmotesAPI.OnPlayerEmoteFunc? OnPlayerEmotePre;

    public FortniteEmotesApi(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public HookResult InvokeOnPlayerEmote(CCSPlayerController player, Emote emote)
    {
        if (OnPlayerEmotePre == null)
            return HookResult.Continue;

        return OnPlayerEmotePre.Invoke(player, emote);
    }

    public bool PlayEmote(CCSPlayerController player, string emote, ref string error)
    {
        // Exact Name id first (IsEmote ignored), then typed/partial lookup.
        var emoteObj = plugin.FindEmoteById(emote)
                       ?? plugin.GetEmoteByName(emote, true, partial: true)
                       ?? plugin.GetEmoteByName(emote, true);

        if (emoteObj == null)
        {
            error = plugin.Config.EmoteDances.Count == 0
                ? "Emote config empty — replace configs/plugins/FortniteEmotesNDances/FortniteEmotesNDances.json"
                : $"Emote '{emote}' not found.";
            return false;
        }

        return plugin.PlayEmote(player, emoteObj, ref error, null);
    }

    public bool PlayDance(CCSPlayerController player, string dance, ref string error)
    {
        var danceObj = plugin.FindEmoteById(dance)
                       ?? plugin.GetEmoteByName(dance, false, partial: true)
                       ?? plugin.GetEmoteByName(dance, false);

        if (danceObj == null)
        {
            error = plugin.Config.EmoteDances.Count == 0
                ? "Emote config empty — replace configs/plugins/FortniteEmotesNDances/FortniteEmotesNDances.json"
                : $"Dance '{dance}' not found.";
            return false;
        }

        return plugin.PlayEmote(player, danceObj, ref error, null);
    }

    public void StopEmote(CCSPlayerController player)
    {
        plugin.StopEmote(player);
    }

    public bool IsDancing(CCSPlayerController player)
    {
        return plugin.IsDancing(player);
    }

    public bool IsReadyForDancing(CCSPlayerController player)
    {
        return plugin.IsReadyForDancing(player);
    }

    public List<Emote> GetDanceList()
    {
        return plugin.GetDanceList();
    }

    public List<Emote> GetEmoteList()
    {
        return plugin.GetEmoteList();
    }

    public string? GetEmoteCatalogJson(CCSPlayerController player)
    {
        return plugin.BuildEmoteCatalogJson(player);
    }
}