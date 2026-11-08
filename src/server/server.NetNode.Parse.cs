using System.Globalization;
using System.Text;
using DeadCellsMultiplayerMod.Interaction;
using DeadCellsMultiplayerMod.Mobs.MobsSynchronization;

public sealed partial class NetNode
{
    private static void ParseAnimPayload(string payload, out int? parsedId, out string animName, out int? queue, out bool? gFlag)
    {
        parsedId = null;
        animName = string.Empty;
        queue = null;
        gFlag = null;

        if (string.IsNullOrWhiteSpace(payload))
            return;

        var parts = payload.Split('|');
        var startIndex = 0;
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idValue))
        {
            if (idValue > 0)
                parsedId = idValue;
            startIndex = 1;
        }

        if (parts.Length > startIndex)
            animName = ClampProtocolText(parts[startIndex], MaxIdentityFieldChars);

        if (parts.Length > startIndex + 1 &&
            int.TryParse(parts[startIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedQ))
            queue = parsedQ;

        if (parts.Length > startIndex + 2 && TryParseBool(parts[startIndex + 2], out var parsedBool))
            gFlag = parsedBool;
    }

    private static void ParseRoomPayload(string payload, out int? parsedId, out string levelId, out int roomId)
    {
        parsedId = null;
        levelId = string.Empty;
        roomId = -1;

        if (string.IsNullOrWhiteSpace(payload))
            return;

        var parts = payload.Split('|');
        if (parts.Length >= 3 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRemoteId))
        {
            if (parsedRemoteId > 0)
                parsedId = parsedRemoteId;
            levelId = ClampProtocolText(parts[1], MaxIdentityFieldChars);
            if (int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rId) && rId >= 0)
                roomId = rId;
            return;
        }

        if (parts.Length >= 2)
        {
            levelId = ClampProtocolText(parts[0], MaxIdentityFieldChars);
            if (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rId) && rId >= 0)
                roomId = rId;
        }
    }

    private static void ParseHeadAnimPayload(string payload, out int? parsedId, out string animName)
    {
        parsedId = null;
        animName = string.Empty;

        if (string.IsNullOrWhiteSpace(payload))
            return;

        var parts = payload.Split('|');
        var startIndex = 0;
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idValue))
        {
            if (idValue > 0)
                parsedId = idValue;
            startIndex = 1;
        }

        if (parts.Length > startIndex)
            animName = ClampProtocolText(parts[startIndex], MaxIdentityFieldChars);
    }

    private static void ParseWeaponPayload(string payload, out int? parsedId, out string kind, out int slot, out int permanentId, out int? ammo)
    {
        parsedId = null;
        kind = string.Empty;
        slot = -1;
        permanentId = 0;
        ammo = null;

        if (string.IsNullOrWhiteSpace(payload))
            return;

        var parts = payload.Split('|');
        var startIndex = 0;
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idValue))
        {
            if (idValue > 0)
                parsedId = idValue;
            startIndex = 1;
        }

        if (parts.Length > startIndex)
            kind = ClampProtocolText(parts[startIndex], MaxIdentityFieldChars);

        if (parts.Length > startIndex + 1 &&
            int.TryParse(parts[startIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSlot))
            slot = parsedSlot >= 0 ? parsedSlot : -1;

        if (parts.Length > startIndex + 2 &&
            int.TryParse(parts[startIndex + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPermanent))
            permanentId = parsedPermanent;

        if (parts.Length > startIndex + 3 &&
            int.TryParse(parts[startIndex + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAmmo))
            ammo = parsedAmmo >= 0 ? parsedAmmo : 0;
    }

    private static void ParseAttackPayload(
        string payload,
        out int? parsedId,
        out string kind,
        out int slot,
        out int permanentId,
        out int? ammo,
        out RemoteAttackAction action)
    {
        ParseWeaponPayload(payload, out parsedId, out kind, out slot, out permanentId, out ammo);
        action = RemoteAttackAction.Attack;

        if (string.IsNullOrWhiteSpace(payload))
            return;

        var parts = payload.Split('|');
        var startIndex = 0;
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            startIndex = 1;
        }

        var firstOptionalIndex = startIndex + 3;
        var actionIndex = -1;
        if (parts.Length > firstOptionalIndex)
        {
            if (int.TryParse(parts[firstOptionalIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                actionIndex = firstOptionalIndex + 1;
            else
                actionIndex = firstOptionalIndex;
        }

        if (actionIndex >= 0 && parts.Length > actionIndex)
            action = ParseAttackActionToken(parts[actionIndex]);
    }

    private static RemoteAttackAction ParseAttackActionToken(string? rawAction)
    {
        if (string.IsNullOrWhiteSpace(rawAction))
            return RemoteAttackAction.Attack;

        var action = rawAction.Trim();
        if (action.Equals("INT", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("INTERRUPT", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("I", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            return RemoteAttackAction.Interrupt;
        }

        return RemoteAttackAction.Attack;
    }

    private static void ParseHpPayload(string payload, out int? parsedId, out int life, out int maxLife, out int lif, out int bonusLife, out int recover)
    {
        parsedId = null;
        life = 0;
        maxLife = 0;
        lif = 0;
        bonusLife = 0;
        recover = 0;

        if (string.IsNullOrWhiteSpace(payload))
            return;

        var parts = payload.Split('|');
        var startIndex = 0;
        if (parts.Length >= 6 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idValue))
        {
            if (idValue > 0)
                parsedId = idValue;
            startIndex = 1;
        }

        if (parts.Length > startIndex &&
            int.TryParse(parts[startIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLife))
            life = Math.Max(0, parsedLife);
        if (parts.Length > startIndex + 1 &&
            int.TryParse(parts[startIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMaxLife))
            maxLife = Math.Max(0, parsedMaxLife);
        if (parts.Length > startIndex + 2 &&
            int.TryParse(parts[startIndex + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLif))
            lif = Math.Max(0, parsedLif);
        if (parts.Length > startIndex + 3 &&
            int.TryParse(parts[startIndex + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBonusLife))
            bonusLife = Math.Max(0, parsedBonusLife);
        if (parts.Length > startIndex + 4 &&
            int.TryParse(parts[startIndex + 4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRecover))
            recover = Math.Max(0, parsedRecover);
    }

    private static void ParseCoopStatePayload(string payload, out string coopId, out bool hasContinueSave)
    {
        coopId = string.Empty;
        hasContinueSave = false;
        if (string.IsNullOrWhiteSpace(payload))
            return;

        var parts = payload.Split(new[] { '|' }, 2);
        coopId = SanitizeProtocolToken(parts[0], 128);
        if (parts.Length >= 2)
            hasContinueSave = string.Equals(parts[1], "1", StringComparison.Ordinal) ||
                              string.Equals(parts[1], "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// MOBREG payload: generation|netId,escapedType,x,y;...
    /// </summary>
    private static bool TryParseMobRegistryPayload(string payload, out List<MobRegistryEntry> entries)
    {
        entries = new List<MobRegistryEntry>();
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var genSep = payload.IndexOf('|');
        if (genSep <= 0)
            return false;

        if (!int.TryParse(payload.AsSpan(0, genSep), NumberStyles.Integer, CultureInfo.InvariantCulture, out var generation))
            return false;

        var table = payload[(genSep + 1)..];
        if (string.IsNullOrWhiteSpace(table))
            return true;

        var chunks = table.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var maxEntries = Math.Min(chunks.Length, PendingMobStateLimit);
        for (var i = 0; i < maxEntries; i++)
        {
            var parts = chunks[i].Split(',');
            if (parts.Length < 4)
                continue;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var netId) || netId < 0)
                continue;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
                continue;
            if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
                continue;

            string type;
            try
            {
                type = Uri.UnescapeDataString(parts[1] ?? string.Empty);
            }
            catch
            {
                type = parts[1] ?? string.Empty;
            }
            type = ClampProtocolText(type, MaxIdentityFieldChars);

            entries.Add(new MobRegistryEntry(netId, generation, type, x, y));
        }

        return true;
    }

    private static List<MobStateSnapshot> ParseMobStatesPayload(string payload)
    {
        var states = new List<MobStateSnapshot>();
        if (string.IsNullOrWhiteSpace(payload))
            return states;

        var entries = payload.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var maxEntries = Math.Min(entries.Length, PendingMobStateLimit);
        for (var i = 0; i < maxEntries; i++)
        {
            var parts = entries[i].Split(',');
            if (parts.Length < 10)
                continue;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index < 0)
                continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
                continue;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
                continue;
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dir))
                continue;
            if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var life) || life < 0)
                continue;
            if (!int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxLife) || maxLife < 0)
                continue;
            if (!int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var generation))
                continue;

            var animPayload = ClampProtocolText(parts[7], MaxIdentityFieldChars);
            var type = parts.Length > 8 ? ClampProtocolText(parts[8], MaxIdentityFieldChars) : string.Empty;
            var statePayload = parts.Length > 9 ? ClampProtocolText(parts[9], 512) : string.Empty;
            var time = parts.Length > 10 && double.TryParse(parts[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var pt) && double.IsFinite(pt) ? pt : 0.0;
            var dx = parts.Length > 11 && double.TryParse(parts[11], NumberStyles.Float, CultureInfo.InvariantCulture, out var pdx) && double.IsFinite(pdx) ? pdx : 0.0;
            var dy = parts.Length > 12 && double.TryParse(parts[12], NumberStyles.Float, CultureInfo.InvariantCulture, out var pdy) && double.IsFinite(pdy) ? pdy : 0.0;

            states.Add(new MobStateSnapshot(index, x, y, dir, life, maxLife, animPayload, type, statePayload, generation, time, dx, dy));
        }

        return states;
    }

    private static List<MobMoveSnapshot> ParseMobMovesPayload(string payload)
    {
        var moves = new List<MobMoveSnapshot>();
        if (string.IsNullOrWhiteSpace(payload))
            return moves;

        var entries = payload.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var maxEntries = Math.Min(entries.Length, PendingMobMoveLimit);
        for (var i = 0; i < maxEntries; i++)
        {
            var parts = entries[i].Split(',');
            if (parts.Length < 6)
                continue;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index < 0)
                continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
                continue;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
                continue;
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dir))
                continue;
            if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var generation))
                continue;

            var animPayload = ClampProtocolText(parts[5], MaxIdentityFieldChars);
            var time = parts.Length > 6 && double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var pt) && double.IsFinite(pt) ? pt : 0.0;
            var dx = parts.Length > 7 && double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var pdx) && double.IsFinite(pdx) ? pdx : 0.0;
            var dy = parts.Length > 8 && double.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out var pdy) && double.IsFinite(pdy) ? pdy : 0.0;

            moves.Add(new MobMoveSnapshot(index, x, y, dir, animPayload, generation, time, dx, dy));
        }

        return moves;
    }

    private static bool TryParseMobHitPayload(string payload, int? senderId, bool forceSenderId, out MobHit hit)
    {
        hit = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 5)
            return false;

        int parsedUserId = 0;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedUserId))
            parsedUserId = senderId ?? 0;

        if (forceSenderId && senderId.HasValue)
            parsedUserId = senderId.Value;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mobIndex) || mobIndex < 0)
            return false;
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hp) || hp < 0)
            return false;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        var generation = 0;
        var typeIndex = 5;
        if (parts.Length > 5 &&
            int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedGeneration))
        {
            generation = parsedGeneration;
            typeIndex = 6;
        }

        var type = parts.Length > typeIndex ? ClampProtocolText(parts[typeIndex], MaxIdentityFieldChars) : string.Empty;
        hit = new MobHit(parsedUserId, mobIndex, hp, x, y, type, generation);
        return true;
    }

    private static bool TryParseMobDiePayload(string payload, int? senderId, bool forceSenderId, out MobDie die)
    {
        die = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 4)
            return false;

        int parsedUserId = 0;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedUserId))
            parsedUserId = senderId ?? 0;

        if (forceSenderId && senderId.HasValue)
            parsedUserId = senderId.Value;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mobIndex) || mobIndex < 0)
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        var generation = 0;
        if (parts.Length > 4)
            int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out generation);

        var type = string.Empty;
        if (parts.Length > 5 && !string.IsNullOrWhiteSpace(parts[5]))
        {
            try
            {
                var rawBase64 = parts[5].Trim();
                if (rawBase64.Length <= 512)
                {
                    type = ClampProtocolText(Encoding.UTF8.GetString(Convert.FromBase64String(rawBase64)), MaxIdentityFieldChars);
                }
            }
            catch
            {
                type = string.Empty;
            }
        }

        die = new MobDie(parsedUserId, mobIndex, x, y, generation, type);
        return true;
    }

    private static bool TryParseMobAttackPayload(string payload, out MobAttack attack)
    {
        attack = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split(',');
        if (parts.Length < 7)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mobIndex) || mobIndex < 0)
            return false;

        string skillId;
        try
        {
            skillId = Uri.UnescapeDataString(parts[1]);
        }
        catch
        {
            skillId = parts[1];
        }

        skillId = ClampProtocolText(skillId, MaxIdentityFieldChars);
        if (string.IsNullOrWhiteSpace(skillId))
            return false;

        var requiresTargetInArea = parts[2] == "1";
        var hasData = parts[3] == "1";

        int? data = null;
        if (hasData)
        {
            if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedData))
                return false;
            data = parsedData;
        }

        if (!double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        var targetUserId = 0;
        if (parts.Length > 7)
            int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out targetUserId);

        var dir = 0;
        if (parts.Length > 8)
            int.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out dir);

        var generation = 0;
        if (parts.Length > 9)
            int.TryParse(parts[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out generation);

        attack = new MobAttack(mobIndex, skillId, requiresTargetInArea, data, x, y, targetUserId, dir, generation: generation);
        return true;
    }

    /// <summary>Parse attack event: attack|skillId|blockSec|forcedDirSec|reqTarget|data|targetUid|dir[|attackSeq] (8-9 parts)</summary>
    private static bool TryParseMobAttackEvent(string ev, int index, double x, double y, int dir, string type, int generation, out MobAttack attack)
    {
        attack = default;
        if (string.IsNullOrEmpty(ev) || !ev.StartsWith("attack|", StringComparison.Ordinal))
            return false;

        if (!double.IsFinite(x) || !double.IsFinite(y) || index < 0)
            return false;

        var parts = ev.Split('|');
        if (parts.Length < 8)
            return false;

        string skillId;
        try
        {
            skillId = Uri.UnescapeDataString(parts[1]);
        }
        catch
        {
            skillId = parts[1];
        }

        skillId = ClampProtocolText(skillId, MaxIdentityFieldChars);
        if (string.IsNullOrWhiteSpace(skillId))
            return false;

        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var blockSec) || !double.IsFinite(blockSec) || blockSec < 0)
            blockSec = 0;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var forcedDirSec) || !double.IsFinite(forcedDirSec) || forcedDirSec < 0)
            forcedDirSec = 0;
        var requiresTargetInArea = parts[4] == "1";
        var dataVal = 0;
        int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out dataVal);
        int? data = dataVal != 0 ? dataVal : null;
        var targetUserId = 0;
        int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out targetUserId);
        var attackDir = 0;
        if (parts.Length > 7)
            int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out attackDir);

        var attackSeq = 0;
        if (parts.Length > 8)
            int.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out attackSeq);

        attack = new MobAttack(index, skillId, requiresTargetInArea, data, x, y, targetUserId, attackDir != 0 ? attackDir : dir, blockSec, forcedDirSec, ClampProtocolText(type, MaxIdentityFieldChars), generation, attackSeq);
        return true;
    }

    /// <summary>Parse hit event: hit|life or hit|life|damageHint.</summary>
    private static bool TryParseMobHitEvent(string ev, int index, double x, double y, int userId, string? mobType, int generation, out MobHit hit)
    {
        hit = default;
        if (string.IsNullOrEmpty(ev) || !ev.StartsWith("hit|", StringComparison.Ordinal))
            return false;

        if (!double.IsFinite(x) || !double.IsFinite(y) || index < 0)
            return false;

        var parts = ev.Split('|');
        if (parts.Length < 2)
            return false;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var life) || life < 0)
            return false;

        var damageHint = 0.0;
        if (parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedHint) && double.IsFinite(parsedHint))
            damageHint = parsedHint;

        hit = new MobHit(userId, index, life, x, y, ClampProtocolText(mobType, MaxIdentityFieldChars), generation, damageHint);
        return true;
    }

    /// <summary>Parse MOBEVENT payload. Format: idx,x,y,dir[,type]§event1§event2;idx2,x2,y2,dir2[,type2]§event1. Events use § separator (they contain |).</summary>
    private static List<MobEventUpdate> ParseMobEventsPayload(string payload)
    {
        const char EventSep = '\u00A7';
        var result = new List<MobEventUpdate>();
        if (string.IsNullOrWhiteSpace(payload))
            return result;

        var mobEntries = payload.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var maxEntries = Math.Min(mobEntries.Length, PendingMobStateLimit);
        for (var k = 0; k < maxEntries; k++)
        {
            var entry = mobEntries[k];
            var sepIndex = entry.IndexOf(EventSep);
            var basePart = sepIndex >= 0 ? entry[..sepIndex] : entry;
            var eventsPart = sepIndex >= 0 && sepIndex + 1 < entry.Length ? entry[(sepIndex + 1)..] : string.Empty;

            var baseParts = basePart.Split(',');
            if (baseParts.Length < 4)
                continue;

            if (!int.TryParse(baseParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index < 0)
                continue;
            if (!double.TryParse(baseParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
                continue;
            if (!double.TryParse(baseParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
                continue;
            if (!int.TryParse(baseParts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dir))
                continue;

            var generation = 0;
            var typeStart = 4;
            if (baseParts.Length >= 5 &&
                int.TryParse(baseParts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedGeneration))
            {
                generation = parsedGeneration;
                typeStart = 5;
            }

            var type = baseParts.Length > typeStart ? ClampProtocolText(string.Join(",", baseParts.Skip(typeStart)), MaxIdentityFieldChars) : string.Empty;

            var events = new List<string>();
            var rawEvents = eventsPart.Split(EventSep, StringSplitOptions.RemoveEmptyEntries);
            var maxEv = Math.Min(rawEvents.Length, 64);
            for (var j = 0; j < maxEv; j++)
            {
                var ev = ClampProtocolText(rawEvents[j], 256);
                if (!string.IsNullOrEmpty(ev))
                    events.Add(ev);
            }

            result.Add(new MobEventUpdate(index, x, y, dir, events, type, generation));
        }

        return result;
    }

    private static bool TryParseMobDrawPayload(string payload, int? senderId, bool forceSenderId, out List<MobDraw> draws)
    {
        draws = new List<MobDraw>();
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var entries = payload.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length == 0)
            entries = new[] { payload };

        var maxDraws = Math.Min(entries.Length, PendingMobDrawLimit);
        for (int i = 0; i < maxDraws; i++)
        {
            if (!TryParseSingleMobDrawPayload(entries[i], senderId, forceSenderId, out var draw))
                continue;

            draws.Add(draw);
        }

        return draws.Count > 0;
    }

    private static bool TryParseSingleMobDrawPayload(string payload, int? senderId, bool forceSenderId, out MobDraw draw)
    {
        draw = default;
        var parts = payload.Split('|');
        if (parts.Length < 4)
            return false;

        int parsedUserId = 0;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedUserId))
            parsedUserId = senderId ?? 0;

        if (forceSenderId && senderId.HasValue)
            parsedUserId = senderId.Value;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mobIndex) || mobIndex < 0)
            return false;
        if (!TryParseBool(parts[2], out var isOutOfGame))
            return false;
        if (!TryParseBool(parts[3], out var isOnScreen))
            return false;

        var generation = 0;
        if (parts.Length > 4)
            int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out generation);

        draw = new MobDraw(parsedUserId, mobIndex, isOutOfGame, isOnScreen, generation);
        return true;
    }

    private static bool TryParseExitCommitPayload(string payload, out ExitTransitionCommit commit)
    {
        commit = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 5)
            return false;

        if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence) || sequence <= 0)
            return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var doorCx))
            return false;
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var doorCy))
            return false;

        var fromLevelId = ClampProtocolText(parts[3], MaxIdentityFieldChars);
        var destLevelId = ClampProtocolText(parts[4], MaxIdentityFieldChars);

        commit = new ExitTransitionCommit(sequence, doorCx, doorCy, fromLevelId, destLevelId);
        return true;
    }

    private static bool TryParseExitReadyPayload(string payload, int? senderId, bool forceSenderId, out ExitReadyState state)
    {
        state = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 7)
            return false;

        int parsedUserId = 0;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedUserId))
            parsedUserId = senderId ?? 0;

        if (forceSenderId && senderId.HasValue)
            parsedUserId = senderId.Value;
        if (parsedUserId <= 0)
            return false;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var doorCx))
            return false;
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var doorCy))
            return false;
        if (!TryParseBool(parts[3], out var pressed))
            return false;
        if (!TryParseBool(parts[4], out var insideCircle))
            return false;
        if (!TryParseBool(parts[5], out var isOutOfGame))
            return false;
        if (!TryParseBool(parts[6], out var isOnScreen))
            return false;

        var levelId = parts.Length >= 8 ? ClampProtocolText(parts[7], MaxIdentityFieldChars) : string.Empty;

        state = new ExitReadyState(parsedUserId, doorCx, doorCy, pressed, insideCircle, isOutOfGame, isOnScreen, levelId);
        return true;
    }

    private static bool TryParsePlayerDownPayload(string payload, int? senderId, bool forceSenderId, out PlayerDownState state)
    {
        state = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 5)
            return false;

        int parsedUserId;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedUserId))
            parsedUserId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            parsedUserId = senderId.Value;
        if (parsedUserId <= 0)
            return false;

        if (!TryParseBool(parts[1], out var isDowned))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        var levelId = parts[4] ?? string.Empty;
        levelId = ClampProtocolText(levelId.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim(), MaxIdentityFieldChars);

        var hasHeadPosition = false;
        var headX = 0d;
        var headY = 0d;
        var hasHeadAnim = false;
        string? headAnim = null;
        if (parts.Length >= 7 &&
            double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedHeadX) && double.IsFinite(parsedHeadX) &&
            double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedHeadY) && double.IsFinite(parsedHeadY))
        {
            hasHeadPosition = true;
            headX = parsedHeadX;
            headY = parsedHeadY;

            if (parts.Length >= 8)
            {
                var parsedAnim = (parts[7] ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
                parsedAnim = ClampProtocolText(parsedAnim, MaxIdentityFieldChars);
                if (!string.IsNullOrWhiteSpace(parsedAnim))
                {
                    hasHeadAnim = true;
                    headAnim = parsedAnim;
                }
            }
        }

        state = new PlayerDownState(parsedUserId, isDowned, x, y, levelId, hasHeadPosition, headX, headY, hasHeadAnim, headAnim);
        return true;
    }

    private static bool TryParsePlayerRevivePayload(string payload, int? senderId, bool forceSenderId, out PlayerReviveRequest request)
    {
        request = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 2)
            return false;

        int reviverId;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out reviverId))
            reviverId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            reviverId = senderId.Value;
        if (reviverId <= 0)
            return false;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var targetId) || targetId <= 0)
            return false;

        request = new PlayerReviveRequest(reviverId, targetId);
        return true;
    }

    private static bool TryParseInterDoorPayload(string payload, int? senderId, bool forceSenderId, out InterDoorEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 5)
            return false;

        int userId = 0;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId))
            userId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;
        if (userId <= 0)
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        var action = ClampProtocolText((parts[3] ?? string.Empty).Trim(), MaxIdentityFieldChars);
        if (string.IsNullOrEmpty(action))
            return false;

        if (!TryParseBool(parts[4], out var broken))
            return false;

        var levelId = parts.Length >= 6
            ? ClampProtocolText((parts[5] ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim(), MaxIdentityFieldChars)
            : string.Empty;

        ev = new InterDoorEvent(userId, x, y, action, broken, levelId);
        return true;
    }

    private static bool TryParseInterElevatorPayload(
        string payload,
        int? senderId,
        bool forceSenderId,
        out InterElevatorEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 2)
            return false;

        // Backwards compatibility: old packets were simply X|Y.
        if (parts.Length == 2)
        {
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var legacyX) || !double.IsFinite(legacyX) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var legacyY) || !double.IsFinite(legacyY))
            {
                return false;
            }

            ev = new InterElevatorEvent(senderId ?? 0, legacyX, legacyY, 0, string.Empty);
            return true;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            userId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;
        if (userId <= 0)
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
        {
            return false;
        }

        long sequence = 0;
        if (parts.Length >= 4)
            long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence);

        var levelId = parts.Length >= 5
            ? ClampProtocolText((parts[4] ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim(), MaxIdentityFieldChars)
            : string.Empty;

        ev = new InterElevatorEvent(userId, x, y, sequence, levelId);
        return true;
    }

    private static bool TryParseInterElevatorStatePayload(
        string payload,
        int? senderId,
        bool forceSenderId,
        out InterElevatorStateEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 7)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            userId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;
        if (userId <= 0)
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var anchorX) || !double.IsFinite(anchorX) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var anchorY) || !double.IsFinite(anchorY) ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence) ||
            !double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var platformX) || !double.IsFinite(platformX) ||
            !double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var platformY) || !double.IsFinite(platformY) ||
            !TryParseBool(parts[6], out var moving))
        {
            return false;
        }

        var levelId = parts.Length >= 8
            ? ClampProtocolText((parts[7] ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim(), MaxIdentityFieldChars)
            : string.Empty;

        ev = new InterElevatorStateEvent(userId, anchorX, anchorY, sequence, platformX, platformY, moving, levelId);
        return true;
    }

    private static bool TryParseInterPressurePlatePayload(
        string payload,
        int? senderId,
        bool forceSenderId,
        out InterPressurePlateEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 2)
            return false;

        // Backwards compatibility: old packets were simply X|Y.
        if (parts.Length == 2)
        {
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var legacyX) || !double.IsFinite(legacyX) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var legacyY) || !double.IsFinite(legacyY))
            {
                return false;
            }

            ev = new InterPressurePlateEvent(senderId ?? 0, legacyX, legacyY, 0, string.Empty);
            return true;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            userId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;
        if (userId <= 0)
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
        {
            return false;
        }

        long sequence = 0;
        if (parts.Length >= 4)
            long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence);

        var levelId = parts.Length >= 5
            ? ClampProtocolText((parts[4] ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim(), MaxIdentityFieldChars)
            : string.Empty;

        ev = new InterPressurePlateEvent(userId, x, y, sequence, levelId);
        return true;
    }

    private static bool TryParseInterTreasureChestPayload(string payload, out InterTreasureChestEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 2)
            return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        var levelId = parts.Length >= 3 ? ClampProtocolText(parts[2], MaxIdentityFieldChars) : string.Empty;
        ev = new InterTreasureChestEvent(x, y, levelId);
        return true;
    }

    private static bool TryParseInterVineLadderPayload(string payload, out InterVineLadderEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 2)
            return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        var levelId = parts.Length >= 3 ? ClampProtocolText(parts[2], MaxIdentityFieldChars) : string.Empty;
        ev = new InterVineLadderEvent(x, y, levelId);
        return true;
    }

    private static bool TryParseInterTeleportPayload(string payload, out InterTeleportEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 2)
            return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        var levelId = parts.Length >= 3 ? ClampProtocolText(parts[2], MaxIdentityFieldChars) : string.Empty;
        ev = new InterTeleportEvent(x, y, levelId);
        return true;
    }

    private static bool TryParseBossHeroTeleportPayload(string payload, int? senderId, bool forceSenderId, out BossHeroTeleportEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 4)
            return false;

        int userId;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId))
            userId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;
        if (userId <= 0)
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;
        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dir))
            return false;

        ev = new BossHeroTeleportEvent(userId, x, y, dir);
        return true;
    }

    private static bool TryParseInterBreakableGroundPayload(string payload, out InterBreakableGroundEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 2)
            return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        var levelId = parts.Length >= 3 ? ClampProtocolText(parts[2], MaxIdentityFieldChars) : string.Empty;
        ev = new InterBreakableGroundEvent(x, y, levelId);
        return true;
    }

    private static bool TryParseInterPortalPayload(string payload, out InterPortalEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 3)
            return false;

        var action = ClampProtocolText(parts[0]?.Trim(), MaxIdentityFieldChars);
        if (string.IsNullOrEmpty(action) || (action != "show" && action != "close"))
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        var levelId = parts.Length >= 4 ? ClampProtocolText(parts[3], MaxIdentityFieldChars) : string.Empty;
        ev = new InterPortalEvent(x, y, action, levelId);
        return true;
    }

    internal static bool TryParseInterBossRuneUpdateCellsPayload(string payload, out InterBossRuneUpdateCellsEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 3)
            return false;

        if (parts.Length >= 5 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedUid) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var uX) && double.IsFinite(uX) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var uY) && double.IsFinite(uY) &&
            int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var uAdd))
        {
            var levelId = ClampProtocolText(parts[4], MaxIdentityFieldChars);
            int? targetBc = null;
            int? maxAllowed = null;
            if (parts.Length >= 6 && int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBc))
            {
                if (parts.Length >= 7 && int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMax))
                {
                    maxAllowed = Math.Clamp(parsedMax, 0, 5);
                }
                targetBc = Math.Clamp(parsedBc, 0, maxAllowed ?? 5);
            }
            ev = new InterBossRuneUpdateCellsEvent(parsedUid, uX, uY, uAdd != 0, levelId, targetBc, maxAllowed);
            return true;
        }

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var addInt))
            return false;

        var lvlId = parts.Length >= 4 ? ClampProtocolText(parts[3], MaxIdentityFieldChars) : string.Empty;
        int? targetBc4 = null;
        int? maxAllowed4 = null;
        if (parts.Length >= 5 && int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBc4))
        {
            if (parts.Length >= 6 && int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMax4))
            {
                maxAllowed4 = Math.Clamp(parsedMax4, 0, 5);
            }
            targetBc4 = Math.Clamp(parsedBc4, 0, maxAllowed4 ?? 5);
        }
        ev = new InterBossRuneUpdateCellsEvent(0, x, y, addInt != 0, lvlId, targetBc4, maxAllowed4);
        return true;
    }

    private static bool TryParseInterEliteShrinePayload(
        string payload,
        int? senderId,
        bool forceSenderId,
        out InterEliteShrineEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return false;

        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;

        if (userId <= 0)
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        var levelId = parts.Length >= 4 ? ClampProtocolText(parts[3], MaxIdentityFieldChars) : string.Empty;
        ev = new InterEliteShrineEvent(userId, x, y, levelId);
        return true;
    }

    internal static bool TryParseInterCollectorDepositPayload(
        string payload,
        int? senderId,
        bool forceSenderId,
        out InterCollectorDepositEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 5)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return false;

        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;

        if (userId <= 0)
            return false;

        var itemId = ClampProtocolText(parts[1].Trim(), InterCollectorDepositEvent.MaxItemIdLength);
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cellsDeposited) || cellsDeposited < 0)
            return false;

        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalInvested) || totalInvested < 0)
            return false;

        if (!long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence) || sequence < 0)
            return false;

        var levelId = parts.Length >= 6 ? ClampProtocolText(parts[5], MaxIdentityFieldChars) : string.Empty;
        ev = new InterCollectorDepositEvent(userId, itemId, cellsDeposited, totalInvested, sequence, levelId);
        return true;
    }

    internal static bool TryParseInterBlueprintUnlockPayload(
        string payload,
        int? senderId,
        bool forceSenderId,
        out InterBlueprintUnlockEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return false;

        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;

        if (userId <= 0)
            return false;

        var itemId = ClampProtocolText(parts[1].Trim(), InterBlueprintUnlockEvent.MaxItemIdLength);
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence) || sequence < 0)
            return false;

        var levelId = parts.Length >= 4 ? ClampProtocolText(parts[3], MaxIdentityFieldChars) : string.Empty;
        ev = new InterBlueprintUnlockEvent(userId, itemId, sequence, levelId);
        return true;
    }

    internal static bool TryParseInterForgeUpgradePayload(
        string payload,
        int? senderId,
        bool forceSenderId,
        out InterForgeUpgradeEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 5)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return false;

        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;

        if (userId <= 0)
            return false;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var upgradeLevel) || upgradeLevel < 0 || upgradeLevel > InterForgeUpgradeEvent.MaxForgeTier)
            return false;

        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cellsDeposited) || cellsDeposited < 0)
            return false;

        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalInvested) || totalInvested < 0)
            return false;

        if (!long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence) || sequence < 0)
            return false;

        var levelId = parts.Length >= 6 ? ClampProtocolText(parts[5], MaxIdentityFieldChars) : string.Empty;
        ev = new InterForgeUpgradeEvent(userId, upgradeLevel, cellsDeposited, totalInvested, sequence, levelId);
        return true;
    }

    internal static bool TryParseInterCollectorLockPayload(
        string payload,
        int? senderId,
        bool forceSenderId,
        out InterCollectorLockEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 4)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return false;

        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;

        if (userId <= 0)
            return false;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var inUseInt))
            return false;

        var kind = ClampProtocolText(parts[2].Trim(), 16);
        if (!long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence) || sequence < 0)
            return false;

        var levelId = parts.Length >= 5 ? ClampProtocolText(parts[4], MaxIdentityFieldChars) : string.Empty;
        ev = new InterCollectorLockEvent(userId, inUseInt != 0, kind, sequence, levelId);
        return true;
    }

    private static bool TryParseInterShopPurchasePayload(
        string payload,
        int? senderId,
        bool forceSenderId,
        out InterShopPurchaseEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 4)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return false;

        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;

        if (userId <= 0)
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var price) || price < 0)
            return false;

        long sequence = 0;
        if (parts.Length >= 5)
            long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence);

        bool isMimic = false;
        if (parts.Length >= 6)
            TryParseBool(parts[5], out isMimic);

        var levelId = parts.Length >= 7 ? ClampProtocolText(parts[6], MaxIdentityFieldChars) : string.Empty;
        ev = new InterShopPurchaseEvent(userId, x, y, price, sequence, isMimic, levelId);
        return true;
    }

    private static bool TryParseInterShopRerollPayload(
        string payload,
        int? senderId,
        bool forceSenderId,
        out InterShopRerollEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 5)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return false;

        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;

        if (userId <= 0)
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cost) || cost < 0)
            return false;

        if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var remainUses) || remainUses < 0)
            return false;

        long sequence = 0;
        if (parts.Length >= 6)
            long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence);

        var levelId = parts.Length >= 7 ? ClampProtocolText(parts[6], MaxIdentityFieldChars) : string.Empty;
        ev = new InterShopRerollEvent(userId, x, y, cost, remainUses, sequence, levelId);
        return true;
    }

    private static bool TryParseInterPedestalPickupPayload(
        string payload,
        int? senderId,
        bool forceSenderId,
        out InterPedestalPickupEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return false;

        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;

        if (userId <= 0)
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        long sequence = 0;
        if (parts.Length >= 4)
            long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence);

        var levelId = parts.Length >= 5 ? ClampProtocolText(parts[4], MaxIdentityFieldChars) : string.Empty;
        ev = new InterPedestalPickupEvent(userId, x, y, sequence, levelId);
        return true;
    }

    private static bool TryParseInterShopRejectPayload(
        string payload,
        int? senderId,
        bool forceSenderId,
        out InterShopRejectEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return false;

        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;

        if (userId <= 0)
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        long sequence = 0;
        if (parts.Length >= 4)
            long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence);

        var levelId = parts.Length >= 5 ? ClampProtocolText(parts[4], MaxIdentityFieldChars) : string.Empty;
        ev = new InterShopRejectEvent(userId, x, y, sequence, levelId);
        return true;
    }

    private static bool TryParseInterPedestalRejectPayload(
        string payload,
        int? senderId,
        bool forceSenderId,
        out InterPedestalRejectEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return false;

        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;

        if (userId <= 0)
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(y))
            return false;

        long sequence = 0;
        if (parts.Length >= 4)
            long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence);

        var levelId = parts.Length >= 5 ? ClampProtocolText(parts[4], MaxIdentityFieldChars) : string.Empty;
        ev = new InterPedestalRejectEvent(userId, x, y, sequence, levelId);
        return true;
    }

    private static bool TryParsePositionLine(
        string line,
        int? senderId,
        out int remoteId,
        out double rx,
        out double ry,
        out int dir,
        out bool hasDir,
        out string? levelId,
        out long positionSequence)
    {
        remoteId = 0;
        rx = 0;
        ry = 0;
        dir = 0;
        hasDir = false;
        levelId = null;
        positionSequence = 0;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        var parts = line.Split('|');
        if (parts.Length >= 4 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRemoteIdWithDir) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var cxWithDir) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var cyWithDir) &&
            double.IsFinite(cxWithDir) && double.IsFinite(cyWithDir) &&
            int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDir))
        {
            if (parsedRemoteIdWithDir <= 0)
                return false;
            remoteId = parsedRemoteIdWithDir;
            rx = cxWithDir;
            ry = cyWithDir;
            dir = parsedDir < 0 ? -1 : parsedDir > 0 ? 1 : 0;
            hasDir = true;
            if (parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4]))
                levelId = ClampProtocolText(parts[4].Trim(), MaxIdentityFieldChars);
            if (parts.Length >= 6)
                long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out positionSequence);
            return true;
        }

        if (parts.Length >= 3 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRemoteId) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var cx) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var cy) &&
            double.IsFinite(cx) && double.IsFinite(cy))
        {
            if (parsedRemoteId <= 0)
                return false;
            remoteId = parsedRemoteId;
            rx = cx;
            ry = cy;
            return true;
        }

        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var cxFallback) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var cyFallback) &&
            double.IsFinite(cxFallback) && double.IsFinite(cyFallback) &&
            senderId.HasValue && senderId.Value > 0)
        {
            remoteId = senderId.Value;
            rx = cxFallback;
            ry = cyFallback;
            return true;
        }

        return false;
    }

    private static bool TryParseBool(string text, out bool value)
    {
        if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }
        if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }
        value = false;
        return false;
    }

    internal bool TestTryParseInterCollectorDeposit(string payload, int? senderId, bool forceSenderId, out InterCollectorDepositEvent ev)
        => TryParseInterCollectorDepositPayload(payload, senderId, forceSenderId, out ev);

    internal bool TestTryParseInterBlueprintUnlock(string payload, int? senderId, bool forceSenderId, out InterBlueprintUnlockEvent ev)
        => TryParseInterBlueprintUnlockPayload(payload, senderId, forceSenderId, out ev);

    internal bool TestTryParseInterForgeUpgrade(string payload, int? senderId, bool forceSenderId, out InterForgeUpgradeEvent ev)
        => TryParseInterForgeUpgradePayload(payload, senderId, forceSenderId, out ev);

    internal bool TestTryParseInterCollectorLock(string payload, int? senderId, bool forceSenderId, out InterCollectorLockEvent ev)
        => TryParseInterCollectorLockPayload(payload, senderId, forceSenderId, out ev);

    internal bool TestTryParseInterShopPurchase(string payload, int? senderId, bool forceSenderId, out InterShopPurchaseEvent ev)
        => TryParseInterShopPurchasePayload(payload, senderId, forceSenderId, out ev);

    internal bool TestTryParseInterShopReroll(string payload, int? senderId, bool forceSenderId, out InterShopRerollEvent ev)
        => TryParseInterShopRerollPayload(payload, senderId, forceSenderId, out ev);

    internal bool TestTryParseInterPedestalPickup(string payload, int? senderId, bool forceSenderId, out InterPedestalPickupEvent ev)
        => TryParseInterPedestalPickupPayload(payload, senderId, forceSenderId, out ev);

    internal bool TestTryParseInterShopReject(string payload, int? senderId, bool forceSenderId, out InterShopRejectEvent ev)
        => TryParseInterShopRejectPayload(payload, senderId, forceSenderId, out ev);

    internal bool TestTryParseInterPedestalReject(string payload, int? senderId, bool forceSenderId, out InterPedestalRejectEvent ev)
        => TryParseInterPedestalRejectPayload(payload, senderId, forceSenderId, out ev);
}
