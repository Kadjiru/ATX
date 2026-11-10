using System.Windows;
using System.Windows.Controls;
using static ArxBeta.Ui;

namespace ArxBeta;

internal sealed class BossHudPanel : StackPanel
{
    private static readonly WorldBossCatalog.Choice Placeholder = new("", Loc.T("Выберите босса"), "");
    private readonly LegionStore _store;
    private readonly ComboBox _picker = new() { MinHeight = 32 };
    private readonly TextBlock _time = HudWindow.Ink(Text("—", 28, "Mint", true));
    private readonly TextBlock _state = HudWindow.Ink(Label(""));
    private readonly TextBlock _detail = HudWindow.Ink(Hint(""));
    private readonly Border _panel;
    private WorldBossCatalog.Choice[] _options = [];
    private bool _updating;

    public BossHudPanel(LegionStore store)
    {
        _store = store;
        System.Windows.Automation.AutomationProperties.SetName(_picker, Loc.T("Мировой босс"));
        _picker.SelectionChanged += (_, _) =>
        {
            if (_updating || _picker.SelectedItem is not WorldBossCatalog.Choice choice) return;
            _store.Profile.BossHudSelection = choice.Id.Length == 0 ? null : choice.Id;
            _store.Save();
            Refresh();
        };
        _panel = HudWindow.Plate(Stack(Space(_state, 3), Space(_time, 4), _detail), Brush("Raised"), Brush("Mint"), 1);
        Children.Add(Space(_picker, 8));
        Children.Add(_panel);
        Refresh();
    }

    public void SetSurfaceOpacity(double opacity)
    {
        var surface = Brush("Raised", this);
        _picker.Background = HudWindow.Faded(surface, opacity);
        _picker.BorderBrush = HudWindow.Faded(Brush("Line", this), opacity);
        HudWindow.StylePlate(_panel, surface, Brush("Mint", this), opacity);
    }

    public void Refresh()
    {
        var selectedId = _store.Profile.BossHudSelection;
        var options = WorldBossCatalog.Choices.Concat(_store.LiveBosses.Where(b => b.CatalogId is null)
                .Select(b => new WorldBossCatalog.Choice(b.SelectionId, b.Name, b.Zone)))
            .DistinctBy(c => c.Id).OrderBy(c => c.Zone, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).Prepend(Placeholder).ToArray();
        _updating = true;
        if (!_options.SequenceEqual(options))
        {
            _options = options;
            _picker.ItemsSource = options;
        }
        var choice = _options.FirstOrDefault(c => c.Id == selectedId);
        _picker.SelectedItem = choice ?? Placeholder;
        _updating = false;

        var boss = _store.LiveBosses.FirstOrDefault(b => b.SelectionId == selectedId);
        var now = DateTimeOffset.UtcNow;
        if (boss is null)
        {
            _state.Text = selectedId is null ? Loc.T("ВЫБЕРИТЕ БОССА") : Loc.T("ОЖИДАНИЕ ДАННЫХ");
            _time.Text = "—";
            _detail.Text = Loc.T("В игре откройте карту → Исследование → Полевые монстры.");
        }
        else if (boss.Alive)
        {
            _state.Text = Loc.T("СТАТУС ПО ДАННЫМ ИГРЫ");
            _time.Text = Loc.T("В мире");
            _detail.Text = Loc.F($"Появился {boss.When.ToLocalTime():dd.MM HH:mm}");
        }
        else
        {
            var remaining = boss.When - now;
            _state.Text = remaining > TimeSpan.Zero ? Loc.T("ДО РЕСПАВНА") : Loc.T("ВРЕМЯ ПОДОШЛО");
            _time.Text = Clock(remaining);
            _detail.Text = remaining > TimeSpan.Zero ? Loc.F($"Ожидается {boss.When.ToLocalTime():dd.MM HH:mm}")
                : Loc.T("Ждём подтверждения от игры.");
        }
    }
}
