using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace FortniteEmotes;

public partial class Plugin
{
    [GameEventHandler(HookMode.Pre)]
    public HookResult OnRoundPreStart(EventRoundPrestart @event, GameEventInfo @info)
    {
        EmitSoundExtension.ClearSounds();
        playerWeapons.Clear();
        playerItems.Clear();
        g_bRoundEnd = false;
        foreach (var player in g_PlayerSettings)
        {
            if (player.Value.IsDancing)
            {
                player.Value.Reset();
            }
        }
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo @info)
    {
        g_bRoundEnd = true;

        var roundRestartDelay = ConVar.Find("mp_round_restart_delay")?.GetPrimitiveValue<float>() ?? 7.0f;
        AddTimer(roundRestartDelay - 0.2f, () =>
        {
            foreach (var settings in g_PlayerSettings)
            {
                if (settings.Value.IsDancing)
                {
                    var player = Utilities.GetPlayerFromSteamId(settings.Key);

                    if (player != null)
                    {
                        StopEmote(player);
                    }
                }
            }
        });
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo @info)
    {
        if (!Config.StopEmoteAfterFreezetimeEnd)
            return HookResult.Continue;

        foreach (var settings in g_PlayerSettings)
        {
            if (settings.Value.IsDancing)
            {
                var player = Utilities.GetPlayerFromSteamId(settings.Key);

                if (player != null)
                {
                    StopEmote(player);
                }
            }
        }
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo @info)
    {
        var player = @event.Userid;

        if (player == null || !player.IsValid)
            return HookResult.Continue;

        StopEmote(player, true);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo @info)
    {
        var player = @event.Userid;

        if (player == null || !player.IsValid)
            return HookResult.Continue;

        var steamID = player.SteamID;
        if (g_PlayerSettings.ContainsKey(steamID) && g_PlayerSettings[steamID].IsDancing)
        {
            StopEmote(player);
        }

        // Конец раунда по вайпу команды — до EventRoundEnd, чтобы танцы сразу работали.
        if (!g_bRoundEnd && IsOneTeamEliminated())
            g_bRoundEnd = true;

        return HookResult.Continue;
    }

    /// <summary>Одна сторона жива, другая — нет (после вайпа T или CT).</summary>
    private static bool IsOneTeamEliminated()
    {
        int t = 0, ct = 0;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid || p.IsHLTV || !p.PawnIsAlive)
                continue;
            if (p.Team == CsTeam.Terrorist) t++;
            else if (p.Team == CsTeam.CounterTerrorist) ct++;
        }
        return (t == 0 && ct > 0) || (ct == 0 && t > 0);
    }

    [GameEventHandler]
    public HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo @info)
    {
        var player = @event.Userid;

        if (player == null || !player.IsValid)
            return HookResult.Continue;

        var steamID = player.SteamID;
        if (g_PlayerSettings.ContainsKey(steamID) && g_PlayerSettings[steamID].IsDancing)
        {
            StopEmote(player);
        }
        return HookResult.Continue;
    }
}