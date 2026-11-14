using System.Windows.Media;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;

namespace ArxBeta;

internal sealed record CharacterIdentity(uint ActorId, string Name, int ServerId, bool Confirmed = true)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"#{ActorId}" : Name;
}

internal sealed record PartyCombatRow(string Name, int ServerId, bool IsSelf, PlayerStats? Stats, Brush Color);

internal static class PartyCombat
{
    private static readonly Brush SelfColor = Tint(0xFFD58B);
    private static readonly Brush[] Colors = [
        Tint(0x79F0D0), Tint(0xB6C2FF), Tint(0xFFA9D1), Tint(0x9EDAFF),
        Tint(0xDBF387), Tint(0xFFB49A), Tint(0xDAB3FF), Tint(0xA9EBA9),
        Tint(0xF7F1BE), Tint(0x89E4F4), Tint(0xFFA9A9), Tint(0xD2DBEF)
    ];

    internal static PartyMemberInfo? Match(PartyRosterEvent roster, string name, int serverId, bool uniqueName)
    {
        var candidates = roster.Members.Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) &&
            (serverId > 0 ? m.ServerId == serverId : uniqueName)).Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    internal static IReadOnlyList<PartyCombatRow> Rows(FightAnalysis analysis,
        CharacterIdentity? character = null, PartyRosterEvent? roster = null, CombatHudScope? scope = null)
    {
        if (scope == CombatHudScope.Self) roster = null;
        var players = analysis.Players.Where(p => p.ActorId != Combatant.UnknownSummonsId).ToArray();
        var rows = new List<PartyCombatRow>();
        var matched = new HashSet<PartyMemberInfo>();
        foreach (var player in players)
        {
            var isSelf = player.IsSelf;
            var unique = players.Count(p => string.Equals(p.Player, player.Player, StringComparison.OrdinalIgnoreCase)) == 1;
            var member = roster is null ? null : Match(roster, player.Player, player.ServerId, unique);
            if (!isSelf && scope is not null &&
                (scope == CombatHudScope.Self || (!analysis.Fight.Demo && member is null))) continue;
            if (member is not null) matched.Add(member);
            rows.Add(new PartyCombatRow(player.Player, member?.ServerId ?? player.ServerId, isSelf, player, SelfColor));
        }
        if (character is not null && rows.All(r => !r.IsSelf))
        {
            var member = roster is null ? null : Match(roster, character.Name, character.ServerId, true);
            if (member is not null) matched.Add(member);
            rows.Add(new PartyCombatRow(character.DisplayName, character.ServerId, true, null, SelfColor));
        }
        if (roster is not null)
            foreach (var member in roster.Members.Distinct())
                if (!matched.Contains(member))
                    rows.Add(new PartyCombatRow(member.Name, member.ServerId, false, null, SelfColor));

        var colors = rows.Where(r => !r.IsSelf).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ServerId).ThenBy(r => r.Stats?.ActorId)
            .Select((row, index) => (row, color: Colors[index % Colors.Length]))
            .ToLookup(item => item.row, item => item.color);
        return rows.OrderByDescending(r => r.Stats?.Damage)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.ServerId).ThenBy(r => r.Stats?.ActorId)
            .Select(r => r.IsSelf ? r : r with { Color = colors[r].First() }).ToArray();
    }

    internal static string Label(IReadOnlyList<PartyCombatRow> rows, PartyCombatRow member)
    {
        var suffix = rows.Count(r => string.Equals(r.Name, member.Name, StringComparison.OrdinalIgnoreCase)) > 1
            ? member.ServerId > 0 ? $" · S{member.ServerId}" : $" · #{member.Stats?.ActorId}" : "";
        return member.IsSelf ? Loc.F($"ВЫ  ·  {member.Name}{suffix}") : member.Name + suffix;
    }

    private static Brush Tint(uint rgb)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        return brush;
    }
}
