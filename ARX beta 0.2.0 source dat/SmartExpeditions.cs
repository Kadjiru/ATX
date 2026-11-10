using AionMeter.Core.Combat;
using AionMeter.Core.Events;

namespace ArxBeta;

internal sealed record ExpeditionActivity(string Id, string Name, int[] Maps, int[] Finals)
{
    public override string ToString() => Name;

    // The bundled Global map and NPC tables use different IDs for some difficulty variants.
    public static readonly ExpeditionActivity[] All =
    [
        new("krao", "Пещера Крао", [600001, 600002, 600003, 600004],
            [2300171, 2300172, 2300173, 2300174, 2300178, 2310171, 2310172, 2310173, 2310174, 2310178,
             2320171, 2320172, 2320173, 2320174, 2320178, 2330171]),
        new("urugugu", "Каньон Уругугу", [600011, 600012, 600013, 600014], [2300218, 2310218, 2320218, 2330218]),
        new("fire-temple", "Храм Огня", [600021, 600022, 600023, 600024], [2300371, 2310371, 2320371, 2330371]),
        new("draupnir", "Пещера Драупнир", [600031, 600032, 600033, 600034], [2300471, 2310471, 2320471, 2330424]),
        new("vakron", "Небесный остров Вакрона", [600071, 600072, 600073, 600074], [2300582, 2300812, 2310812, 2320812]),
        new("horn-den", "Логово Свирепого Рога", [600091, 600092, 600093], [2300910, 2310910, 2320910])
    ];
}

internal sealed class SmartExpeditions(LegionStore store)
{
    private int _map;
    private bool _finishedHere;
    private ExpeditionActivity? Activity => ExpeditionActivity.All.FirstOrDefault(a => a.Maps.Contains(_map));
    internal static bool KnownCharacter(CharacterIdentity? player) => player is { Confirmed: true, ServerId: > 0, Name.Length: > 0 };

    public void ZoneChanged(ZoneChangedEvent zone)
    {
        if (zone.IsTeleport || zone.MapId == _map) return;
        EndAutomatic();
        _map = zone.MapId;
        _finishedHere = false;
    }

    public void CharacterChanged() => EndAutomatic();

    public void EndAutomatic()
    {
        if (store.Profile.CurrentExpedition?.Automatic != true) return;
        EndRun(interrupted: true);
    }

    public void EndRun(bool interrupted)
    {
        if (store.Profile.CurrentExpedition?.Automatic == true) _finishedHere = true;
        store.EndExpedition(interrupted);
    }

    public void Archive(Fight fight, CharacterIdentity? player)
    {
        var profile = store.Profile;
        if (profile.History.Any(item => item.Id == fight.Id) ||
            profile.CurrentExpedition?.Segments.Any(item => item.FightId == fight.Id) == true) return;
        var activity = Activity;
        var participates = KnownCharacter(player) &&
            fight.Captured?.Summary.Combatants.Any(member => member.ActorId == player!.ActorId && member.IsSelf &&
                (member.Damage > 0 || member.Healing is > 0)) == true;
        if (profile.AutoExpeditions && !_finishedHere && profile.CurrentExpedition is null && activity is not null && participates)
            profile.CurrentExpedition = new Expedition(Guid.NewGuid(), activity.Name, profile.DeclaredBuild, fight.Date, [],
                ActivityId: activity.Id, Character: player!.Name, ServerId: player.ServerId, MapId: _map, Automatic: true);

        var run = profile.CurrentExpedition;
        var matchesRun = run?.Automatic == true && _map == run.MapId && participates &&
            string.Equals(run.Character, player!.Name, StringComparison.OrdinalIgnoreCase) && run.ServerId == player.ServerId;
        var win = matchesRun && activity is not null && fight.Captured?.Summary is
            { Reason: EncounterEndReason.Kill, Boss: { NpcCode: var boss } } && activity.Finals.Contains(boss);
        store.Archive(fight, attachToRun: run?.Automatic != true || matchesRun, confirmedWin: win);
        if (win) _finishedHere = true;
    }
}
