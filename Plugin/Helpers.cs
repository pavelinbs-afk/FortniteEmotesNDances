using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CSSTimer = CounterStrikeSharp.API.Modules.Timers.Timer;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Core.Translations;
using Microsoft.Extensions.Logging;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;
using System.Drawing;
using System.Runtime.InteropServices;
using FortniteEmotes.API;
using System.Numerics;

namespace FortniteEmotes;

public partial class Plugin
{
    public class PlayerSettings
    {
        public uint CloneModelIndex { get; set; } = 0;
        public uint EmoteModelIndex { get; set; } = 0;
        public int PlayerAlpha { get; set; } = 255;
        public MoveType_t PlayerMoveType { get; set; } = MoveType_t.MOVETYPE_WALK;
        public CDynamicProp? CameraProp { get; set; } = null;
        public CDynamicProp? CloneProp { get; set; } = null;
        public uint CameraPropIndex { get; set; } = 0;
        public DateTime Cooldown { get; set; } = DateTime.Now;
        public CSSTimer? Timer { get; set; } = null;
        public CSSTimer? DefaultAnimTimer { get; set; } = null;
        public CSSTimer? SoundTimer { get; set; } = null;
        public bool IsDancing { get; set; } = false;
        public string CurrentSound { get; set; } = "";

        public PlayerSettings()
        {
            CloneModelIndex = 0;
            CloneProp = null;
            EmoteModelIndex = 0;
            PlayerAlpha = 255;
            PlayerMoveType = MoveType_t.MOVETYPE_WALK;
            CameraProp = null;
            CameraPropIndex = 0;
            Cooldown = DateTime.Now;
            Timer = null;
            DefaultAnimTimer = null;
            SoundTimer = null;
            IsDancing = false;
            CurrentSound = "";
        }

        public void Reset()
        {
            CloneProp = null;
            CloneModelIndex = 0;
            EmoteModelIndex = 0;
            PlayerAlpha = 255;
            PlayerMoveType = MoveType_t.MOVETYPE_WALK;
            CameraProp = null;
            CameraPropIndex = 0;
            IsDancing = false;
            Timer?.Kill();
            Timer = null;
            DefaultAnimTimer?.Kill();
            DefaultAnimTimer = null;
            SoundTimer?.Kill();
            SoundTimer = null;
            CurrentSound = "";
        }
    }

    public bool PlayEmote(CCSPlayerController target, Emote emote, ref string error, CCSPlayerController? player = null)
    {
        if (!IsEmotePeriodAllowed())
        {
            error = $" {Localizer.ForPlayer(player, "emote.prefix")} {Localizer.ForPlayer(player, "emote.notallowed.warmupftrecheck")}";
            return false;
        }

        if (!target.IsValidPlayer() || !target.PlayerPawn.IsValidPawnAlive() || target.ControllingBot || target.PlayerPawn.Value!.AbsOrigin == null || target.PlayerPawn.Value.AbsRotation == null || target.PlayerPawn.Value.CameraServices == null)
        {
            error = $" {Localizer.ForPlayer(player, "emote.prefix")} {Localizer.ForPlayer(player, $"emote{(player == null ? "" : ".player")}.alivecheck")}";
            return false;
        }

        if (target.PlayerPawn.Value.IsScoped)
        {
            error = $" {Localizer.ForPlayer(player, "emote.prefix")} {Localizer.ForPlayer(player, $"emote{(player == null ? "" : ".player")}.scopecheck")}";
            return false;
        }

        var steamID = target.SteamID;

        if (!g_PlayerSettings.ContainsKey(steamID))
            g_PlayerSettings[steamID] = new PlayerSettings();

        var now = DateTime.Now;

        DebugLogs($"{target.PlayerName} cd: {g_PlayerSettings[steamID].Cooldown} > {now}");

        if (g_PlayerSettings[steamID].Cooldown > now && player == null)
        {
            error = $" {Localizer.ForPlayer(player, "emote.prefix")} {Localizer.ForPlayer(player, "emote.cooldowncheck", (int)(g_PlayerSettings[steamID].Cooldown - now).TotalSeconds)}";
            return false;
        }

        if (target.Pawn.IsValidPawn())
        {
            // Только FL_ONGROUND. Старый OR по GroundEntity.Index != 0 отклонял
            // почти всех стоящих на полу (у world/пропа Index часто > 0).
            if (((PlayerFlags)target.Pawn.Value!.Flags & PlayerFlags.FL_ONGROUND) != PlayerFlags.FL_ONGROUND)
            {
                error = $" {Localizer.ForPlayer(player, "emote.prefix")} {Localizer.ForPlayer(player, $"emote{(player == null ? "" : ".player")}.groundcheck")}";
                return false;
            }
            if (((PlayerFlags)target.Pawn.Value!.Flags & PlayerFlags.FL_DUCKING) == PlayerFlags.FL_DUCKING)
            {
                error = $" {Localizer.ForPlayer(player, "emote.prefix")} {Localizer.ForPlayer(player, $"emote{(player == null ? "" : ".player")}.duckcheck")}";
                return false;
            }
        }

        // DebugLogs($"GroundEntity Info: {(target.PlayerPawn.Value.GroundEntity == null ? "null" : "not-null")} | {target.PlayerPawn.Value.GroundEntity?.IsValid ?? false} | {target.PlayerPawn.Value.GroundEntity?.Index ?? 1337420}");

        var result = FortniteEmotesApi.InvokeOnPlayerEmote(target, emote);
        if (result == HookResult.Handled || result == HookResult.Stop)
        {
            string message = $"{Localizer.ForPlayer(player, $"emote.stoppedbyapi")}";

            if (string.IsNullOrEmpty(message))
            {
                error = "";
            }
            else
            {
                error = $" {Localizer.ForPlayer(player, "emote.prefix")} {Localizer.ForPlayer(player, $"emote.stoppedbyapi")}";
            }
            return false;
        }

        if (!EmotesEnable.Value)
        {
            error = $" {Localizer.ForPlayer(player, "emote.prefix")} {Localizer.ForPlayer(player, $"emote{(player == null ? "" : ".player")}.disabledbyadmin")}";
            return false;
        }

        if (g_PlayerSettings[steamID].IsDancing)
        {
            DebugLogs("Player already dancing, stopping emote");
            StopEmote(target);
        }

        var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");

        if (prop == null || prop.Entity == null || prop.Entity.Handle == IntPtr.Zero || !prop.IsValid)
        {
            error = $" {Localizer.ForPlayer(player, "emote.prefix")} {Localizer.ForPlayer(player, "emote.unknownerror")}";
            return false;
        }

        string propName = "emoteEnt_" + new Random().Next(1000000, 9999999).ToString();
        prop.Entity.Name = propName;

        // CS2: SetModel на entity в staging list → EF_IN_STAGING_LIST (часто с кастомными моделями SkinChanger).
        ClearEntityStagingFlag(prop);
        SetCollision(prop, CollisionGroup.COLLISION_GROUP_NEVER, SolidType_t.SOLID_NONE, 12);

        try { RefreshPlayerGloves(target); }
        catch (Exception ex) { Logger.LogWarning(ex, "RefreshPlayerGloves before emote failed"); }

        SetPropInvisible(prop);

        if (string.IsNullOrWhiteSpace(emote.Model))
        {
            prop.Remove();
            error = $" {Localizer.ForPlayer(player, "emote.prefix")} Emote model empty in config ({emote.Name}).";
            return false;
        }

        // prop_dynamic нельзя спавнить без модели → "has no model name!"
        string pawnModel = target.PlayerPawn.Value.GetModel() ?? string.Empty;
        try
        {
            prop.SetModel(!string.IsNullOrWhiteSpace(pawnModel) ? pawnModel : emote.Model);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Initial emote prop SetModel failed, falling back to emote model");
            try { prop.SetModel(emote.Model); }
            catch
            {
                prop.Remove();
                error = $" {Localizer.ForPlayer(player, "emote.prefix")} {Localizer.ForPlayer(player, "emote.unknownerror")}";
                return false;
            }
        }

        prop.DispatchSpawn();
        try
        {
            prop.Teleport((Vector3)target.PlayerPawn.Value.AbsOrigin, (Vector3)target.PlayerPawn.Value.AbsRotation);
        }
        catch { /* ignore */ }
        prop.UseAnimGraph = false;

        var cloneprop = CreateClone(target, prop, propName);
        g_PlayerSettings[steamID].CloneModelIndex = cloneprop?.Index ?? 0;
        g_PlayerSettings[steamID].CloneProp = prop;

        ClearPlayerWeapons(target);

        g_PlayerSettings[steamID].EmoteModelIndex = prop.Index;

        var emoteModel = emote.Model;
        var animName = emote.AnimationName;
        Server.NextWorldUpdate(() =>
        {
            try
            {
                if (prop == null || !prop.IsValid) return;
                ClearEntityStagingFlag(prop);
                if (!string.IsNullOrWhiteSpace(emoteModel))
                    prop.SetModel(emoteModel);
                if (!string.IsNullOrWhiteSpace(animName))
                    prop.AcceptInput("SetAnimation", value: animName);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Emote prop SetModel/SetAnimation failed");
            }
        });

        if (!string.IsNullOrEmpty(emote.DefaultAnimationName))
        {
            if (emote.SetToDefaultAnimationDuration > 0)
            {
                g_PlayerSettings[steamID].DefaultAnimTimer?.Kill();
                g_PlayerSettings[steamID].DefaultAnimTimer = AddTimer(emote.SetToDefaultAnimationDuration, () =>
                {
                    if (!target.IsValidPlayer() || !g_PlayerSettings[steamID].IsDancing)
                    {
                        g_PlayerSettings[steamID].Reset();
                        return;
                    }

                    prop.AcceptInput("SetAnimation", value: emote.DefaultAnimationName);

                    if (emote.AnimationDuration > 0)
                    {
                        g_PlayerSettings[steamID].Timer?.Kill();
                        g_PlayerSettings[steamID].Timer = AddTimer(emote.AnimationDuration, () => StopEmote(target));
                    }
                });
            }
            else
            {
                HookSingleEntityOutput(prop, "OnAnimationDone", EndAnimation);
            }
        }
        else if (emote.AnimationDuration > 0)
        {
            g_PlayerSettings[steamID].Timer?.Kill();
            g_PlayerSettings[steamID].Timer = AddTimer(emote.AnimationDuration, () => StopEmote(target));
        }
        else
        {
            HookSingleEntityOutput(prop, "OnAnimationDone", EndAnimation);
            // HookSingleEntityOutput(prop, "OnAnimationLoopCycleDone", Test);
            // HookSingleEntityOutput(prop, "OnAnimationReachedEnd", Test);
        }

        g_PlayerSettings[steamID].CameraProp = SetCam(target);
        g_PlayerSettings[steamID].CameraPropIndex = g_PlayerSettings[steamID].CameraProp?.Index ?? 0;

        g_PlayerSettings[steamID].IsDancing = true;

        if (player == null)
        {
            bool hasVIP = HasPermision(target, Config.VIPPerm);
            float cdTime = hasVIP ? Config.EmoteVIPCooldown : Config.EmoteCooldown;
            if (cdTime <= 0)
            {
                cdTime = 0.1f;
            }
            g_PlayerSettings[steamID].Cooldown = now.AddSeconds(cdTime);
            DebugLogs($"{target.PlayerName} added cd: {cdTime}s");
        }

        string emoteName = $"{Localizer.ForPlayer(target, $"{emote.Name}")}";

        target.PrintToChat($" {Localizer.ForPlayer(target, "emote.prefix")} {Localizer.ForPlayer(target, "emote.playing", emoteName)}");

        if (Config.SoundModuleEnabled)
        {
            g_PlayerSettings[steamID].SoundTimer?.Kill();
            if (!string.IsNullOrEmpty(emote.Sound))
            {
                DebugLogs("SoundPlayed: " + emote.Sound);
                EmitSound(target, emote.Sound, emote.SoundVolume);
                if (emote.LoopSoundAfterSeconds > 0)
                {
                    g_PlayerSettings[steamID].SoundTimer = AddTimer(emote.LoopSoundAfterSeconds, () =>
                    {
                        if (!target.IsValidPlayer() || !g_PlayerSettings[steamID].IsDancing)
                        {
                            g_PlayerSettings[steamID].Reset();
                            return;
                        }

                        DebugLogs("SoundPlayed: " + emote.Sound);
                        EmitSound(target, emote.Sound, emote.SoundVolume);
                    }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
                }
            }
        }

        return true;
    }

    private bool HasPermision(CCSPlayerController player, List<string> permissions)
    {
        bool bAccess = false;
        foreach (var perm in permissions)
        {
            if (string.IsNullOrEmpty(perm))
            {
                bAccess = true;
                break;
            }
            if (perm[0] == '@' && AdminManager.PlayerHasPermissions(player, perm))
            {
                bAccess = true;
                break;
            }
            else if (perm[0] == '#' && AdminManager.PlayerInGroup(player, perm))
            {
                bAccess = true;
                break;
            }
        }
        return bAccess;
    }

    private static void ClearEntityStagingFlag(CBaseEntity entity)
    {
        try
        {
            if (entity.Entity != null)
                entity.Entity.Flags = (uint)(entity.Entity.Flags & ~(1u << 2));
        }
        catch
        {
            try
            {
                if (entity.CBodyComponent?.SceneNode?.Owner?.Entity != null)
                    entity.CBodyComponent.SceneNode.Owner.Entity.Flags &= unchecked((uint)~(1 << 2));
            }
            catch { /* ignore */ }
        }
    }

    private CDynamicProp? CreateClone(CCSPlayerController player, CDynamicProp prop, string propName)
    {
        string model = player.PlayerPawn.Value?.GetModel() ?? string.Empty;
        ulong meshgroupmask = player.PlayerPawn.Value?.GetMeshGroup() ?? 0;

        DebugLogs("Model: " + model);

        if (string.IsNullOrEmpty(model))
            return null;

        var clone = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (clone == null || clone.Entity == null || clone.Entity.Handle == IntPtr.Zero || !clone.IsValid)
            return null;

        clone.Entity.Name = propName + "_clone";
        ClearEntityStagingFlag(clone);
        SetCollision(clone, CollisionGroup.COLLISION_GROUP_NEVER, SolidType_t.SOLID_NONE, 12);

        try
        {
            clone.SetModel(model);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "CreateClone SetModel failed model={Model}", model);
            clone.Remove();
            return null;
        }

        if (meshgroupmask != 0)
        {
            try { clone.SetMeshGroup(meshgroupmask); }
            catch { /* ignore */ }
        }

        clone.DispatchSpawn();

        try
        {
            clone.Teleport((Vector3)player.PlayerPawn.Value!.AbsOrigin!, (Vector3)player.PlayerPawn.Value.AbsRotation!);
        }
        catch { /* ignore */ }
        clone.UseAnimGraph = false;

        var steamID = player.SteamID;
        var followName = propName;

        Server.NextWorldUpdate(() =>
        {
            try
            {
                if (!g_PlayerSettings.ContainsKey(steamID) || !g_PlayerSettings[steamID].IsDancing)
                    return;
                if (clone == null || !clone.IsValid || prop == null || !prop.IsValid)
                    return;

                ClearEntityStagingFlag(clone);
                // Повторный SetModel после spawn — стабильнее с кастомными скинами.
                try
                {
                    clone.SetModel(model);
                    if (meshgroupmask != 0)
                        clone.SetMeshGroup(meshgroupmask);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "CreateClone re-SetModel failed");
                }

                clone.AcceptInput("FollowEntity", prop, prop, followName);

                clone.Render = Color.FromArgb(255, 255, 255, 255);
                Utilities.SetStateChanged(clone, "CBaseModelEntity", "m_clrRender");

                if (Config.EmoteFreezePlayer && player.IsValidPlayer() && player.PlayerPawn.IsValidPawnAlive())
                {
                    g_PlayerSettings[steamID].PlayerMoveType = player.PlayerPawn.Value!.ActualMoveType;
                    SetPlayerMoveType(player, MoveType_t.MOVETYPE_OBSOLETE);
                }

                if (player.IsValidPlayer() && player.PlayerPawn.IsValidPawnAlive())
                {
                    g_PlayerSettings[steamID].PlayerAlpha = player.PlayerPawn.Value!.Render.A;
                    SetPlayerInvisible(player);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "CreateClone NextWorldUpdate failed");
            }
        });

        return clone;
    }

    public HookResult EndAnimation(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
    {
        if (caller == null || !caller.IsValid) return HookResult.Continue;

        DebugLogs("OnAnimationDone");

        foreach (var player in g_PlayerSettings)
        {
            if (player.Value.EmoteModelIndex == caller.Index)
            {
                var play = Utilities.GetPlayerFromSteamId(player.Key);
                if (play != null && play.IsValidPlayer())
                {
                    StopEmote(play);
                }
                else
                {
                    caller.Remove();
                }
                break;
            }
        }

        return HookResult.Continue;
    }

    public bool IsDancing(CCSPlayerController player)
    {
        if (!player.IsValidPlayer())
            return false;

        var steamID = player.SteamID;

        return g_PlayerSettings.ContainsKey(steamID) && g_PlayerSettings[steamID].IsDancing;
    }

    /// <summary>
    /// EmoteAllowedPeriod: 0 = всегда; 1 = разминка/freeze; 2 = разминка/freeze/конец раунда.
    /// </summary>
    private bool IsEmotePeriodAllowed()
    {
        EnsureGameRules();

        switch (Config.EmoteAllowedPeriod)
        {
            case 1:
                return g_GameRules != null && (g_GameRules.WarmupPeriod || g_GameRules.FreezePeriod);
            case 2:
                return g_GameRules != null &&
                       (g_GameRules.WarmupPeriod || g_GameRules.FreezePeriod || g_bRoundEnd);
            default:
                return true;
        }
    }

    private void EnsureGameRules()
    {
        if (g_GameRules != null)
            return;
        try
        {
            g_GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault()?.GameRules;
        }
        catch
        {
            g_GameRules = null;
        }
    }

    public bool IsReadyForDancing(CCSPlayerController player)
    {
        if (!IsEmotePeriodAllowed())
            return false;

        if (!player.IsValidPlayer() || !player.PlayerPawn.IsValidPawnAlive())
            return false;

        if (player.Pawn.IsValidPawn())
        {
            if (((PlayerFlags)player.Pawn.Value!.Flags & PlayerFlags.FL_ONGROUND) != PlayerFlags.FL_ONGROUND)
                return false;
            if (((PlayerFlags)player.Pawn.Value!.Flags & PlayerFlags.FL_DUCKING) == PlayerFlags.FL_DUCKING)
                return false;
        }

        if (player.PlayerPawn.Value!.IsScoped)
            return false;

        var steamID = player.SteamID;

        if (!g_PlayerSettings.ContainsKey(steamID))
            g_PlayerSettings[steamID] = new PlayerSettings();

        if (g_PlayerSettings[steamID].IsDancing)
            return false;

        return true;
    }

    public List<Emote> GetDanceList()
    {
        return Config.EmoteDances.Where(x => !x.IsEmote).ToList();
    }

    public List<Emote> GetEmoteList()
    {
        return Config.EmoteDances.Where(x => x.IsEmote).ToList();
    }

    public void StopAllEmotes()
    {
        foreach (var player in Utilities.GetPlayers().Where(p => !p.IsHLTV && !p.IsBot && p.PawnIsAlive))
        {
            var steamID = player.SteamID;

            if (!g_PlayerSettings.ContainsKey(steamID))
            {
                continue;
            }

            if (g_PlayerSettings[steamID].IsDancing)
            {
                StopEmote(player);
            }
        }
    }

    public void StopEmote(CCSPlayerController player, bool force = false)
    {
        if (!player.IsValidPlayer())
            return;

        DebugLogs("StopEmote");

        var steamID = player.SteamID;

        if (!g_PlayerSettings.TryGetValue(steamID, out var settings))
            return;

        if (!g_PlayerSettings[steamID].IsDancing && !force)
            return;

        g_PlayerSettings[steamID].IsDancing = false;

        if (g_PlayerSettings[steamID].Timer != null)
        {
            g_PlayerSettings[steamID].Timer?.Kill();
        }
        if (g_PlayerSettings[steamID].SoundTimer != null)
        {
            g_PlayerSettings[steamID].SoundTimer?.Kill();
        }
        if (g_PlayerSettings[steamID].DefaultAnimTimer != null)
        {
            g_PlayerSettings[steamID].DefaultAnimTimer?.Kill();
        }
        g_PlayerSettings[steamID].Timer = null;
        g_PlayerSettings[steamID].SoundTimer = null;
        g_PlayerSettings[steamID].DefaultAnimTimer = null;

        if (Config.SoundModuleEnabled)
        {
            if (!string.IsNullOrEmpty(g_PlayerSettings[steamID].CurrentSound))
            {
                player.StopSound(g_PlayerSettings[steamID].CurrentSound);
                g_PlayerSettings[steamID].CurrentSound = "";
            }
        }

        var emoteModels = Utilities.FindAllEntitiesByDesignerName<CDynamicProp>("prop_dynamic").Where(p => p != null
        && p.IsValid
        && ((settings.EmoteModelIndex != 0 && p.Index == settings.EmoteModelIndex) || (settings.CloneModelIndex != 0 && p.Index == settings.CloneModelIndex) || (settings.CameraPropIndex != 0 && p.Index == settings.CameraPropIndex))
        ).ToList();

        ResetCam(player);

        // SetPlayerWeaponVisible(player);

        GivePlayerWeaponsBack(player);

        SetPlayerVisible(player);

        if (g_PlayerSettings.ContainsKey(steamID)
            && player.PlayerPawn.IsValidPawnAlive()
            && g_PlayerSettings[steamID].PlayerMoveType != player.PlayerPawn.Value!.ActualMoveType)
            SetPlayerMoveType(player, g_PlayerSettings[steamID].PlayerMoveType);

        try { RefreshPlayerGloves(player, true); }
        catch (Exception ex) { Logger.LogWarning(ex, "RefreshPlayerGloves after StopEmote failed"); }

        var activeWeapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;

        if (activeWeapon != null && activeWeapon.IsValid)
        {
            activeWeapon.NextPrimaryAttackTick = -1;
            Utilities.SetStateChanged(activeWeapon, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
            activeWeapon.NextSecondaryAttackTick = -1;
            Utilities.SetStateChanged(activeWeapon, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
        }

        g_PlayerSettings[steamID].PlayerAlpha = 255;
        g_PlayerSettings[steamID].PlayerMoveType = MoveType_t.MOVETYPE_WALK;
        g_PlayerSettings[steamID].CameraProp = null;
        g_PlayerSettings[steamID].CloneProp = null;

        foreach (var model in emoteModels)
        {
            if (model != null && model.IsValid && model.Entity != null && (g_PlayerSettings[steamID].EmoteModelIndex == model.Index || g_PlayerSettings[steamID].CloneModelIndex == model.Index || g_PlayerSettings[steamID].CameraPropIndex == model.Index))
            {
                // player.PlayerPawn.Value?.AcceptInput("ClearParent", player.PlayerPawn.Value, player.PlayerPawn.Value, model.Entity.Name);
                // player.PlayerPawn.Value?.AcceptInput("StopFollowingEntity", player.PlayerPawn.Value, player.PlayerPawn.Value, model.Entity.Name);
                model.Remove();
            }
        }
        g_PlayerSettings[steamID].EmoteModelIndex = 0;
        g_PlayerSettings[steamID].CloneModelIndex = 0;
        g_PlayerSettings[steamID].CameraPropIndex = 0;
    }

    private int GetPlayerSpeed(CCSPlayerController player)
    {
        return (int)Math.Round(player.PlayerPawn.Value!.AbsVelocity.Length2D());
    }


    private static void SetCollision(CBaseEntity entity, CollisionGroup collisionGroup, SolidType_t solidType, byte solidFlags)
    {
        if (entity.Collision == null) return;

        entity.Collision.CollisionAttribute.CollisionGroup = (byte)collisionGroup;
        entity.Collision.CollisionGroup = (byte)collisionGroup;
        entity.Collision.SolidType = solidType;
        entity.Collision.SolidFlags = solidFlags;

        Utilities.SetStateChanged(entity, "CCollisionProperty", "m_CollisionGroup");
        Utilities.SetStateChanged(entity, "CCollisionProperty", "m_collisionAttribute");
        Utilities.SetStateChanged(entity, "CCollisionProperty", "m_nSolidType");
        Utilities.SetStateChanged(entity, "CCollisionProperty", "m_usSolidFlags");

        var collisionRulesChanged = GetCollisionRulesChanged(entity.Handle);

        // Invokes the updated CollisionRulesChanged information to ensure the player's collision is correctly set
        collisionRulesChanged.Invoke(entity.Handle);
    }

    private static int? _collisionRulesChangedOffset;

    private static int GetCollisionRulesChangedOffset()
    {
        if (_collisionRulesChangedOffset.HasValue)
            return _collisionRulesChangedOffset.Value;

        try
        {
            _collisionRulesChangedOffset = GameData.GetOffset("CBaseEntity_CollisionRulesChanged");
        }
        catch
        {
            // Нет в глобальном gamedata.json — fallback из Plugin/gamedata/fortnite_emotes.json
            // (файл должен лежать в addons/counterstrikesharp/gamedata/fortnite_emotes.json).
            _collisionRulesChangedOffset = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 186 : 185;
            Server.PrintToConsole(
                "[FortniteEmotes] CBaseEntity_CollisionRulesChanged not in gamedata — using fallback offset. " +
                "Copy plugins/.../gamedata/fortnite_emotes.json → addons/counterstrikesharp/gamedata/");
        }

        return _collisionRulesChangedOffset.Value;
    }

    private static VirtualFunctionVoid<nint> GetCollisionRulesChanged(nint handle)
    {
        return new VirtualFunctionVoid<nint>(handle, GetCollisionRulesChangedOffset());
    }

    private CDynamicProp? SetCam(CCSPlayerController player)
    {
        if (!player.IsValidPlayer() || !player.PlayerPawn.IsValidPawnAlive() || player.AbsOrigin == null || player.PlayerPawn.Value!.CameraServices == null)
            return null;

        var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (prop == null)
            return null;

        prop.Teleport(CalculatePositionInFront(player, -110, 75), (Vector3)player.PlayerPawn.Value.V_angle);

        prop.Entity!.Name = "cameraProp_" + new Random().Next(1000000, 9999999).ToString();
        prop.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags &= unchecked((uint)~(1 << 2));

        prop.SetModel("models/chicken/chicken.vmdl");

        SetPropInvisible(prop);

        prop.DispatchSpawn();
        prop.Teleport((Vector3)player.PlayerPawn.Value.AbsOrigin!, (Vector3)player.PlayerPawn.Value.V_angle);

        SetCollision(prop, CollisionGroup.COLLISION_GROUP_NEVER, SolidType_t.SOLID_VPHYSICS, 12);

        Server.NextWorldUpdate(() =>
        {
            if (prop.IsValid && player.IsValid && player.Connected == PlayerConnectedState.Connected)
            {
                var steamID = player.SteamID;
                if (!g_PlayerSettings.ContainsKey(steamID))
                {
                    return;
                }

                if (!g_PlayerSettings[steamID].IsDancing)
                {
                    return;
                }

                prop.Teleport(CalculatePositionInFront(player, -110, 90), (Vector3)player.PlayerPawn.Value.V_angle);

                player.PlayerPawn.Value.CameraServices.ViewEntity.Raw = prop.EntityHandle.Raw;

                Utilities.SetStateChanged(player.PlayerPawn.Value, "CBasePlayerPawn", "m_pCameraServices");
            }

        });

        return prop;
    }

    public void ResetCam(CCSPlayerController player)
    {
        if (!player.IsValidPlayer() || !player.PlayerPawn.IsValidPawnAlive() || player.AbsOrigin == null || player.PlayerPawn.Value!.CameraServices == null)
            return;

        Server.NextWorldUpdate(() =>
        {
            if (player.IsValid && player.Connected == PlayerConnectedState.Connected)
            {
                player.PlayerPawn.Value.CameraServices.ViewEntity.Raw = uint.MaxValue;

                Utilities.SetStateChanged(player.PlayerPawn.Value, "CBasePlayerPawn", "m_pCameraServices");
            }
        });
    }

    private void SetPlayerEffects(CCSPlayerController player, bool set)
    {
        if (!player.IsValidPlayer() || !player.PlayerPawn.IsValidPawnAlive())
            return;

        if (set)
        {
            // DebugLogs("Add effects Pre: " + player.PlayerPawn.Value!.Effects);
            var enteffects = player.PlayerPawn.Value!.Effects;

            enteffects |= 1; // This is EF_BONEMERGE
            //enteffects |= 16; // This is EF_NOSHADOW
            //enteffects |= 64; // This is EF_NORECEIVESHADOW
            enteffects |= 128; // This is EF_BONEMERGE_FASTCULL
            enteffects |= 512; // This is EF_PARENT_ANIMATES
            player.PlayerPawn.Value.Effects = enteffects;
            // DebugLogs("Add effects Post: " + player.PlayerPawn.Value!.Effects);
            Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseEntity", "m_fEffects");
        }
        else
        {
            // DebugLogs("Remove effects Pre: " + player.PlayerPawn.Value!.Effects);

            int enteffects = (int)player.PlayerPawn.Value!.Effects;

            enteffects &= ~1; // This is EF_BONEMERGE
            //enteffects &= ~16; // This is EF_NOSHADOW
            //enteffects &= ~64; // This is EF_NORECEIVESHADOW
            enteffects &= ~128; // This is EF_BONEMERGE_FASTCULL
            enteffects &= ~512; // This is EF_PARENT_ANIMATES
            player.PlayerPawn.Value.Effects = (uint)enteffects;
            // DebugLogs("Remove effects Post: " + player.PlayerPawn.Value!.Effects);
            Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseEntity", "m_fEffects");
        }
    }

    private static void SetPropInvisible(CDynamicProp entity)
    {
        if (entity == null || !entity.IsValid)
        {
            return;
        }

        var enteffects = entity.Effects;

        enteffects |= 16; // This is EF_NOSHADOW

        entity.Effects = enteffects;
        Utilities.SetStateChanged(entity, "CBaseEntity", "m_fEffects");

        // Model shadow started to appear after ag2 22/04/26 update
        entity.Render = Color.FromArgb(0, 255, 255, 255);
        Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_clrRender");
    }

    public static void SetPlayerInvisible(CCSPlayerController player)
    {
        if (!player.IsValidPlayer() || !player.PlayerPawn.IsValidPawnAlive())
            return;

        if (player.PlayerPawn.Value!.Render == Color.FromArgb(0, 255, 255, 255))
            return;

        player.PlayerPawn.Value.Render = Color.FromArgb(0, 255, 255, 255);
        Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseModelEntity", "m_clrRender");
    }

    public void SetPlayerVisible(CCSPlayerController player)
    {
        if (!player.IsValidPlayer() || !player.PlayerPawn.IsValidPawnAlive())
            return;

        var steamID = player.SteamID;
        if (g_PlayerSettings.ContainsKey(steamID))
        {
            player.PlayerPawn.Value!.Render = Color.FromArgb(g_PlayerSettings[steamID].PlayerAlpha, 255, 255, 255);
            Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseModelEntity", "m_clrRender");
        }
        else
        {
            player.PlayerPawn.Value!.Render = Color.FromArgb(255, 255, 255, 255);
            Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseModelEntity", "m_clrRender");
        }
    }

    public static void SetPlayerMoveType(CCSPlayerController player, MoveType_t type)
    {
        if (!player.IsValidPlayer() || !player.PlayerPawn.IsValidPawnAlive())
            return;

        player.PlayerPawn.Value!.MoveType = type;
        player.PlayerPawn.Value.ActualMoveType = type;
        Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseEntity", "m_MoveType");
    }

    public string GetWeaponClassname(CBasePlayerWeapon weapon)
    {
        string classname;
        int defIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;

        switch (defIndex)
        {
            case 23:
                {
                    classname = "weapon_mp5sd";
                    break;
                }
            case 41:
                {
                    classname = "weapon_knifegg";
                    break;
                }
            case 42:
                {
                    classname = "weapon_knife";
                    break;
                }
            case 59:
                {
                    classname = "weapon_knife_t";
                    break;
                }
            case 60:
                {
                    classname = "weapon_m4a1_silencer";
                    break;
                }
            case 61:
                {
                    classname = "weapon_usp_silencer";
                    break;
                }
            case 63:
                {
                    classname = "weapon_cz75a";
                    break;
                }
            case 64:
                {
                    classname = "weapon_revolver";
                    break;
                }
            default:
                {
                    classname = weapon.DesignerName;
                    break;
                }
        }
        return classname;
    }

    private void ClearPlayerWeapons(CCSPlayerController player)
    {
        if (!player.IsValidPlayer() || !player.PlayerPawn.IsValidPawnAlive())
            return;

        var weaponServices = player.PlayerPawn.Value!.WeaponServices;

        if (weaponServices == null)
            return;

        Dictionary<string, List<(int, int)>> weaponsWithAmmo = [];
        Dictionary<int, ushort> itemsWithAmmo = [];

        var myWeapons = weaponServices.MyWeapons;
        foreach (var gun in myWeapons)
        {
            var weapon = gun.Value;
            if (weapon != null)
            {
                int clip1 = weapon.Clip1;
                int reservedAmmo = weapon.ReserveAmmo[0];

                var weaponName = GetWeaponClassname(weapon);

                if (!weaponsWithAmmo.TryGetValue(weaponName, out var value))
                {
                    value = [];
                    weaponsWithAmmo.Add(weaponName, value);
                }

                value.Add((clip1, reservedAmmo));
                weapon?.AddEntityIOEvent("Kill", weapon, null, "", 0.1f);
            }
        }
        for (int i = 0; i < 32; i++)
        {
            itemsWithAmmo.Add(i, weaponServices.Ammo[i]);
        }
        playerWeapons[player.Slot] = weaponsWithAmmo;
        playerItems[player.Slot] = itemsWithAmmo;
    }

    private void GivePlayerWeaponsBack(CCSPlayerController player)
    {
        if (!player.IsValidPlayer() || !player.PlayerPawn.IsValidPawnAlive())
            return;

        var weaponServices = player.PlayerPawn.Value!.WeaponServices;

        if (weaponServices == null)
            return;

        if (playerWeapons.TryGetValue(player.Slot, out var weaponsWithAmmo) && playerItems.TryGetValue(player.Slot, out var itemsWithAmmo))
        {
            foreach (var weapon in weaponsWithAmmo)
            {
                foreach (var ammo in weapon.Value)
                {
                    try
                    {
                        var newWeapon = new CBasePlayerWeapon(player.GiveNamedItem(weapon.Key));
                        if (newWeapon == null) continue;
                        Server.NextWorldUpdate(() =>
                        {
                            if (!newWeapon.IsValid) return;

                            try
                            {
                                newWeapon.Clip1 = ammo.Item1;
                                newWeapon.ReserveAmmo[0] = ammo.Item2;
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning("Error setting weapon properties: " + ex.Message);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Error giving weapon: " + ex.Message);
                    }
                }
            }

            foreach (var item in itemsWithAmmo)
            {
                weaponServices.Ammo[item.Key] = item.Value;
            }
        }
        playerWeapons.Remove(player.Slot);
        playerItems.Remove(player.Slot);
    }

    private static void SetPlayerWeaponVisible(CCSPlayerController player)
    {
        if (!player.IsValidPlayer() || !player.PlayerPawn.IsValidPawnAlive())
            return;

        var playerPawnValue = player.PlayerPawn.Value;

        var activeWeapon = playerPawnValue!.WeaponServices?.ActiveWeapon.Value;
        if (activeWeapon != null && activeWeapon.IsValid)
        {
            activeWeapon.Render = Color.FromArgb(255, 255, 255, 255);
            activeWeapon.ShadowStrength = 1.0f;
            Utilities.SetStateChanged(activeWeapon, "CBaseModelEntity", "m_clrRender");
        }

        var myWeapons = playerPawnValue.WeaponServices?.MyWeapons;
        if (myWeapons != null)
        {
            foreach (var gun in myWeapons)
            {
                var weapon = gun.Value;
                if (weapon != null)
                {
                    weapon.Render = Color.FromArgb(255, 255, 255, 255);
                    weapon.ShadowStrength = 1.0f;
                    Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_clrRender");
                }
            }
        }
    }

    // Stopped working after 15/10/2025 patch
    private static void SetPlayerWeaponInvisible(CCSPlayerController player)
    {
        if (!player.IsValidPlayer() || !player.PlayerPawn.IsValidPawnAlive())
            return;

        var playerPawnValue = player.PlayerPawn.Value;

        var activeWeapon = playerPawnValue!.WeaponServices?.ActiveWeapon.Value;
        if (activeWeapon != null && activeWeapon.IsValid)
        {
            activeWeapon.Render = Color.FromArgb(0, 255, 255, 255);
            activeWeapon.ShadowStrength = 0.0f;
            Utilities.SetStateChanged(activeWeapon, "CBaseModelEntity", "m_clrRender");
        }

        var myWeapons = playerPawnValue.WeaponServices?.MyWeapons;
        if (myWeapons != null)
        {
            foreach (var gun in myWeapons)
            {
                var weapon = gun.Value;
                if (weapon != null)
                {
                    weapon.Render = Color.FromArgb(0, 255, 255, 255);
                    weapon.ShadowStrength = 0.0f;
                    Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_clrRender");
                }
            }
        }
    }

    private void RefreshPlayerGloves(CCSPlayerController player, bool update = false)
    {
        if (!Config.EmoteGlovesFix)
            return;

        if (!player.IsValidPlayer() || !player.PlayerPawn.IsValidPawnAlive())
            return;

        var playerPawnValue = player.PlayerPawn.Value;
        if (playerPawnValue == null)
            return;

        try
        {
            CEconItemView item = playerPawnValue.EconGloves;
            item.NetworkedDynamicAttributes.Attributes.RemoveAll();
            item.AttributeList.Attributes.RemoveAll();

            SetBodygroup(playerPawnValue, "first_or_third_person", 0);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "RefreshPlayerGloves bodygroup/attrs failed (SkinChanger gloves?)");
            return;
        }

        if (update) return;

        AddTimer(0.2f, () =>
        {
            try
            {
                if (!player.IsValidPlayer() || !player.PlayerPawn.IsValidPawnAlive())
                    return;
                var pawn = player.PlayerPawn.Value;
                if (pawn == null) return;
                SetBodygroup(pawn, "first_or_third_person", 1);
            }
            catch { /* ignore */ }
        });
    }

    private void SetBodygroup(CCSPlayerPawn pawn, string group, int value)
    {
        pawn.AcceptInput("SetBodygroup", value: $"{group},{value}");
    }

    public static void UpdateCamera(CDynamicProp cameraProp, CCSPlayerController target, bool blockcamera = true)
    {
        if (target.IsValidPlayer() && target.PlayerPawn.IsValidPawnAlive() && target.AbsOrigin != null)
        {
            Vector3 positionBehind = blockcamera ? CalculateSafeCameraPosition(target, 110f, 75f) : CalculatePositionInFront(target, -110f, 75f);
            Vector3 position = Lerp(GetPosition(cameraProp), positionBehind, blockcamera ? 0.3f : 0.1f);
            cameraProp.Teleport(position, (Vector3)target.PlayerPawn.Value!.V_angle);
        }
    }

    public static void UpdateAnimProp(CDynamicProp animProp, CCSPlayerController target)
    {
        if (target.IsValidPlayer() && target.PlayerPawn.IsValidPawnAlive()
        && target.PlayerPawn.Value!.AbsOrigin != null && target.PlayerPawn.Value.AbsRotation != null)
        {
            animProp.Teleport((Vector3)target.PlayerPawn.Value.AbsOrigin, (Vector3)target.PlayerPawn.Value.AbsRotation);
        }
    }

    public static Vector3 CalculatePositionInFront(CCSPlayerController player, float offSetXY, float offSetZ = 0)
    {
        var pawn = player.PlayerPawn.Value;
        // Extract yaw angle from player's rotation QAngle
        float yawAngle = pawn!.EyeAngles!.Y;

        // Convert yaw angle from degrees to radians
        float yawAngleRadians = (float)(yawAngle * Math.PI / 180.0);

        // Calculate offsets in x and y directions
        float offsetX = offSetXY * (float)Math.Cos(yawAngleRadians);
        float offsetY = offSetXY * (float)Math.Sin(yawAngleRadians);

        // Calculate position in front of the player
        var positionInFront = new Vector3
        {
            X = pawn!.AbsOrigin!.X + offsetX,
            Y = pawn!.AbsOrigin!.Y + offsetY,
            Z = pawn!.AbsOrigin!.Z + offSetZ
        };

        return positionInFront;
    }

    public static readonly Vector __eyePos = new();
    public static readonly Vector __camPos = new();

    public static Vector3 CalculateSafeCameraPosition(
        CCSPlayerController player,
        float desiredDistance,
        float verticalOffset
    )
    {
        if (player.PlayerPawn?.Value?.AbsOrigin == null)
            return new Vector3(0, 0, 0);

        var pawn = player.PlayerPawn.Value;
        Vector3 pawnPos = (Vector3)pawn.AbsOrigin;

        float yawRadians = pawn.V_angle.Y * (float)Math.PI / 180f;
        var backwardDir = new Vector3(-MathF.Cos(yawRadians), -MathF.Sin(yawRadians), 0);
        var eyePos = pawnPos + new Vector3(0, 0, verticalOffset);
        var targetCamPos = eyePos + backwardDir * desiredDistance;

        __camPos.X = targetCamPos.X;
        __camPos.Y = targetCamPos.Y;
        __camPos.Z = targetCamPos.Z;

        __eyePos.X = eyePos.X;
        __eyePos.Y = eyePos.Y;
        __eyePos.Z = eyePos.Z;

        Vector3 finalPos = targetCamPos;

        var result = Trace.TraceEndShape(__eyePos, __camPos, null, GetTraceOptions());

        // Server.PrintToChatAll($"RayTrace: Fraction: {result.Fraction} | DidHit: {result.DidHit()}");

        if (result.DidHit())
        {
            var hitVec = result.EndPos;
            float distanceToWall = (hitVec - __eyePos).Length();
            float clampedDistance = Math.Clamp(distanceToWall - 10f, 10f, desiredDistance);
            finalPos = eyePos + backwardDir * clampedDistance;
        }

        return finalPos;
    }

    private static readonly Contents OcclusionTraceMask =
        Contents.PhysicsProp |
        Contents.BlockLos |
        Contents.WorldGeometry |
        Contents.CsgoOpaque;

    private static TraceOptions GetTraceOptions()
    {
        return new TraceOptions
        {
            InteractsAs = Contents.Solid,
            InteractsWith = OcclusionTraceMask,
        };
    }

    public static Vector3 GetPosition(CDynamicProp prop)
    {
        return (Vector3)prop.AbsOrigin!;
    }

    public static Vector3 Lerp(Vector3 from, Vector3 to, float t)
    {
        Vector3 vector = new Vector3
        {
            X = from.X + (to.X - from.X) * t,
            Y = from.Y + (to.Y - from.Y) * t,
            Z = from.Z + (to.Z - from.Z) * t
        };

        return vector;
    }

    private void EmitSound(CCSPlayerController player, string sound, float volume = 1f, float pitch = 1f)
    {
        if (g_PlayerSettings.ContainsKey(player.SteamID))
        {
            if (!string.IsNullOrEmpty(g_PlayerSettings[player.SteamID].CurrentSound))
            {
                player.StopSound(g_PlayerSettings[player.SteamID].CurrentSound);
            }
            g_PlayerSettings[player.SteamID].CurrentSound = sound;
        }
        player.EmitEmoteSound(sound, volume);
    }

    private void DebugLogs(string message)
    {
        if (Config.DebugLogs)
            Logger.LogInformation(message);
    }

    private bool IsCS2FixesInstalled()
    {
        string vdfPath = Path.Combine(Server.GameDirectory, "csgo", "addons/metamod", "cs2fixes.vdf");
        string binaryPath = Path.Combine(Server.GameDirectory, "csgo", "addons/cs2fixes/bin", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win64/cs2fixes.dll" : "linuxsteamrt64/cs2fixes.so");

        return File.Exists(vdfPath) && File.Exists(binaryPath);
    }

    private readonly string[] _requiredShared = ["FortniteEmotesNDancesAPI"];
    private bool AreAllDependaciesInstalled(ref string error)
    {
        string vdfPath = Path.Combine(Server.GameDirectory, "csgo", "addons/metamod", "multiaddonmanager.vdf");
        string binaryPath = Path.Combine(Server.GameDirectory, "csgo", "addons/multiaddonmanager/bin", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "multiaddonmanager.dll" : "multiaddonmanager.so");

        if (!File.Exists(vdfPath) || !File.Exists(binaryPath))
        {
            error = "MultiAddonManager is not installed.";
            return false;
        }

        foreach (var depen in _requiredShared)
        {
            // Только глобальный CSS shared/, не рядом с плагином и не plugins/*/shared.
            string dllPath = Path.Combine(
                Server.GameDirectory,
                "csgo",
                "addons",
                "counterstrikesharp",
                "shared",
                depen,
                $"{depen}.dll");

            if (!File.Exists(dllPath))
            {
                error = $"{depen} is not installed at addons/counterstrikesharp/shared/{depen}/{depen}.dll";
                return false;
            }
        }
        return true;
    }
}

internal static class CBaseModelEntityEx
{
    internal static CSkeletonInstance? GetSkeletonInstance(this CBaseModelEntity entity)
    {
        return entity.CBodyComponent?.SceneNode?.As<CSkeletonInstance>();
    }
    internal static string? GetModel(this CBaseModelEntity entity)
    {
        return entity.GetSkeletonInstance()?.ModelState.ModelName;
    }
    internal static ulong? GetMeshGroup(this CBaseModelEntity entity)
    {
        return entity.GetSkeletonInstance()?.ModelState.MeshGroupMask;
    }
    internal static void SetMeshGroup(this CBaseModelEntity entity, ulong mesh)
    {
        var skeleton = entity.GetSkeletonInstance();

        if (skeleton == null) return;

        skeleton.ModelState.MeshGroupMask = mesh;
        Utilities.SetStateChanged(entity, "CBaseEntity", "m_CBodyComponent");
    }
}

internal static class CCSPlayerControllerEx
{
    internal static bool IsValidPlayer(this CCSPlayerController? controller)
    {
        return controller != null
        && controller.Entity != null
        && controller.Entity.Handle != IntPtr.Zero
        && controller.IsValid
        && controller.Connected == PlayerConnectedState.Connected
        && !controller.IsHLTV
        && !controller.IsBot;
    }
}

internal static class CHandleCCSPlayerPawnEx
{
    internal static bool IsValidPawn(this CHandle<CCSPlayerPawn>? pawn)
    {
        return pawn != null
        && pawn.IsValid
        && pawn.Value != null
        && pawn.Value.IsValid
        && pawn.Value.WeaponServices != null
        && pawn.Value.WeaponServices.MyWeapons != null;
    }

    internal static bool IsValidPawnAlive(this CHandle<CCSPlayerPawn>? pawn)
    {
        return IsValidPawn(pawn) && pawn!.Value!.LifeState == (byte)LifeState_t.LIFE_ALIVE && pawn.Value.Health > 0;
    }
}

internal static class CHandleCBasePlayerPawnEx
{
    internal static bool IsValidPawn(this CHandle<CBasePlayerPawn>? pawn)
    {
        return pawn != null
        && pawn.IsValid
        && pawn.Value != null
        && pawn.Value.IsValid;
    }

    internal static bool IsValidPawnAlive(this CHandle<CBasePlayerPawn>? pawn)
    {
        return IsValidPawn(pawn) && pawn!.Value!.LifeState == (byte)LifeState_t.LIFE_ALIVE && pawn.Value.Health > 0;
    }
}

internal static class CHandleCBasePlayerWeaponEx
{
    internal static bool IsValidWeapon(this CHandle<CBasePlayerWeapon>? weapon)
    {
        return weapon != null
        && weapon.IsValid
        && weapon.Value != null
        && weapon.Value.IsValid
        && weapon.Value.Entity != null
        && !string.IsNullOrEmpty(weapon.Value.DesignerName);
    }
}

public static class EmitSoundExtension
{
    // player -> soundName -> guid
    private static Dictionary<CCSPlayerController, Dictionary<string, uint>> _playerSounds = new();

    public static void ClearSounds()
    {
        _playerSounds = new();
    }

    public static void EmitEmoteSound(this CCSPlayerController player, string soundName, float volume = 1f)
    {
        var guid = player.EmitSound(soundName, volume: volume);

        if (!_playerSounds.ContainsKey(player))
        {
            _playerSounds[player] = new Dictionary<string, uint>();
        }

        _playerSounds[player][soundName] = guid;
    }

    public static void StopSound(this CCSPlayerController player, string soundName)
    {
        if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid)
        {
            throw new ArgumentException("Player is not valid.");
        }

        if (_playerSounds.TryGetValue(player, out var soundMap) && soundMap.TryGetValue(soundName, out var guid))
        {
            // GE_SosStopSoundEvent
            UserMessage message = UserMessage.FromId(209);
            message.SetUInt("soundevent_guid", guid);
            message.Recipients.Add(player);
            message.Send();

            soundMap.Remove(soundName);
        }
        else
        {
            Server.PrintToConsole($"[FortniteEmotes] Unable to call StopSound for sound '{soundName}' for player {player.PlayerName}.");
        }
    }
}