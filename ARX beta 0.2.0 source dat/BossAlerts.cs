using System.Media;
using System.Windows.Controls;
using static ArxBeta.Ui;

namespace ArxBeta;

public partial class App
{
    private readonly HashSet<(string Key, DateTimeOffset When)> _bossAlerts = [];

    private void CheckBossAlerts()
    {
        var now = DateTimeOffset.UtcNow;
        _bossAlerts.RemoveWhere(item => item.When < now.AddDays(-1));
        foreach (var boss in _store.LiveBosses)
        {
            if (boss.Alive || !_store.Profile.BossAlarms.TryGetValue(boss.Key, out var lead)) continue;
            if (boss.When < now.AddSeconds(-10) || boss.When > now.AddMinutes(lead) || !_bossAlerts.Add((boss.Key, boss.When))) continue;
            _store.SetBossNotice(Loc.F($"{boss.Name} · ожидается в {boss.When.ToLocalTime():HH:mm}"));
            SystemSounds.Asterisk.Play();
        }
    }
}

public sealed partial class Dashboard
{
    private Border ObservedBossPanel()
    {
        var rows = new StackPanel();
        var notice = Text("", 13, "Gold", true);
        var filter = Input("", 200, 70);
        var starred = new CheckBox { Content = Loc.T("Только с напоминанием") };
        var panel = Stack(Space(Text(Loc.T("Полевые боссы · данные игры"), 19, bold: true), 6),
            Space(Hint(Loc.T("Откройте список полевых монстров на карте игры. ARX показывает полученные от сервера времена.")), 9),
            Actions(Button(Loc.T("HUD боссов"), () => Hud(HudKind.Bosses), true), Field(Loc.T("Поиск"), filter), starred, Button(Loc.T("Скрыть напоминание"), () =>
            {
                _store.SetBossNotice(null);
                notice.Text = "";
            })),
            Space(notice, 7), rows);
        IReadOnlyList<ObservedBoss> previous = [];
        filter.TextChanged += (_, _) => Rebuild();
        starred.Click += (_, _) => Rebuild();
        _refreshLive = () =>
        {
            notice.Text = _store.BossNotice ?? "";
            if (!previous.SequenceEqual(_store.LiveBosses)) Rebuild();
        };
        Rebuild();
        return Card(panel);

        void Rebuild()
        {
            previous = _store.LiveBosses;
            notice.Text = _store.BossNotice ?? "";
            rows.Children.Clear();
            foreach (var boss in previous.Where(b => b.Name.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (starred.IsChecked != true || _store.Profile.BossAlarms.ContainsKey(b.Key))))
            {
                var reminder = Choice([Loc.T("Без звука"), Loc.T("При появлении"), Loc.T("За 5 мин"), Loc.T("За 10 мин")], 143);
                reminder.SelectedIndex = _store.Profile.BossAlarms.GetValueOrDefault(boss.Key, -1) switch { 0 => 1, 5 => 2, 10 => 3, _ => 0 };
                reminder.SelectionChanged += (_, _) =>
                {
                    if (reminder.SelectedIndex <= 0) _store.Profile.BossAlarms.Remove(boss.Key);
                    else _store.Profile.BossAlarms[boss.Key] = reminder.SelectedIndex switch { 2 => 5, 3 => 10, _ => 0 };
                    _store.Save();
                    if (starred.IsChecked == true) Rebuild();
                };
                var when = boss.Alive ? Loc.F($"В мире с {boss.When.ToLocalTime():HH:mm}") : Loc.F($"Ожидается {boss.When.ToLocalTime():dd.MM HH:mm}");
                rows.Children.Add(Actions(Text(boss.Name, 14, "Text", true), Hint($"{boss.Zone} · {when}"), reminder,
                    Button(Loc.T("Следить в HUD"), () => App.CurrentApp.TrackBoss(boss))));
            }
            if (rows.Children.Count == 0) rows.Children.Add(Hint(previous.Count == 0 ? Loc.T("Список от сервера ещё не получен.") : Loc.T("Метки по фильтру не найдены.")));
        }
    }
}
