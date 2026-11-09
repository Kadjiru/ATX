using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using static ArxBeta.Ui;

namespace ArxBeta;

public sealed record Ingredient(string Id, int Count);
public sealed record Recipe(string Id, string Name, string Profession, string Type, int Faction, string OutputId,
    int Yield, Ingredient[] Materials);
public sealed record CatalogItem(string Name, string Source);
public sealed record CraftCatalog(Recipe[] Recipes, Dictionary<string, CatalogItem> Items);

public sealed partial class Dashboard
{
    private static readonly (string Name, string Role, string Focus, string Notes)[] Builds =
    [
        ("Гладиатор · Gladiator", "Урон в ближнем бою", "Двуручный меч · Ruinous Blow",
            "В группе держите AoE для пачек, а основной удар — для уязвимости босса. Защиту используйте до механики, которая вынуждает оставаться рядом."),
        ("Страж · Templar", "Танк", "Меч и щит · Judgment, Taunt, Doom Shield",
            "Начните с угрозы, разверните босса от группы и оставьте щит под опасный удар. Согласуйте смену танков и не тратьте защиту одновременно."),
        ("Убийца · Assassin", "Короткий бурст", "Кинжалы · Heart Gore, Shadowstrike",
            "Выбирайте безопасное окно за спиной, синхронизируйте серию ударов с баффами и оставьте мобильность для выхода из зоны урона."),
        ("Стрелок · Ranger", "Дальний физический урон", "Лук · Deadshot, Snipe",
            "Сохраняйте дистанцию и линию обзора. Совмещайте сильный выстрел с баффом точности и следите за временем до следующего перемещения."),
        ("Чародей · Chanter", "Поддержка и ближний урон", "Посох · Dark Crush, Undefeated Mantra",
            "Синхронизируйте мантры с окном урона группы. При освоении сохраняйте часть поддержки для механик, требующих перегруппировки."),
        ("Целитель · Cleric", "Лечение и снятие эффектов", "Булава · Condemnation для урона между механиками",
            "Распределите массовое лечение и очищение по опасным фазам. Следите за расстоянием до танка и не ставьте свой DPS выше выживания группы."),
        ("Волшебник · Sorcerer", "Дальний магический бурст", "Гримуар · Hellfire, Firestorm",
            "Начинайте длинные применения перед безопасными окнами. При частых перемещениях сначала отрабатывайте позицию, затем добивайте ротацию."),
        ("Заклинатель · Spiritmaster", "Духи и периодический урон", "Орб · Fire Spirit, Elemental Fusion",
            "Обновляйте духа и эффекты после смены цели. Переносите длинные эффекты на приоритетную цель, которая останется доступной достаточно долго.")
    ];

    private UIElement CodexPage()
    {
        var reference = _catalog.Reference;
        var page = Heading("Билды и гайды", "Выберите класс, чтобы увидеть роль, оружие и ключевые навыки.");
        var classes = new UniformGrid { Columns = 4 };
        var buttons = new List<Button>();
        var buildDetail = new ContentControl();
        for (var i = 0; i < Builds.Length; i++)
        {
            var index = i;
            var title = Builds[i].Name.Split(" · ", 2);
            var button = Button("", () => SelectBuild(index));
            button.Content = Stack(Text(title[0], 15, bold: true), Text(title[1], 11, "Muted"));
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 7, 7);
            buttons.Add(button);
            classes.Children.Add(button);
        }
        page.Children.Add(Space(classes, 7));
        page.Children.Add(Space(buildDetail, 14));
        SelectBuild(0);
        page.Children.Add(Space(Card(Stack(
            Space(Label("МЕТА · " + reference.MetaWindow.ToUpperInvariant()), 5),
            Space(Text(reference.Composition, 19, "Mint", true), 7),
            Hint($"Частый состав в загруженных боях AionFlex. Срез от {reference.ReferenceDate}."))), 12));
        var gear = new WrapPanel();
        foreach (var item in reference.Gear)
        {
            var tile = Card(Text(item, 13), 10);
            tile.MaxWidth = 320;
            tile.Margin = new Thickness(0, 4, 7, 3);
            gear.Children.Add(tile);
        }
        page.Children.Add(Space(Card(new Expander { Header = $"Снаряжение · {reference.GameBuild}",
            Foreground = Ui.Brush("Text"), Content = gear }, 14), 12));
        page.Children.Add(Card(new Expander { Header = $"Гайд группы · {reference.Raid.Title}", Foreground = Ui.Brush("Text"),
            Content = Stack(
            Space(Hint(reference.Raid.Entry), 7),
            Space(Text(reference.Raid.Tactics, 14), 6),
            Hint(reference.Raid.Status)) }, 14));
        return page;

        void SelectBuild(int index)
        {
            for (var i = 0; i < buttons.Count; i++)
            {
                buttons[i].Background = Ui.Brush(i == index ? "Raised" : "Surface");
                buttons[i].BorderBrush = Ui.Brush(i == index ? "Mint" : "Line");
            }
            var build = Builds[index];
            var focus = build.Focus.Split(" · ", 2);
            var facts = new UniformGrid { Columns = 3 };
            facts.Children.Add(BuildTile("РОЛЬ", build.Role, "Mint"));
            facts.Children.Add(BuildTile("ОРУЖИЕ", focus[0], "Text"));
            facts.Children.Add(BuildTile("КЛЮЧЕВЫЕ НАВЫКИ", focus[1], "Gold"));
            buildDetail.Content = Stack(Space(Text($"{build.Name.Split(" · ", 2)[0]} · стартовый PvE", 20, bold: true), 8),
                Space(facts, 7), Card(Stack(Space(Label("ЧТО ДЕЛАТЬ В БОЮ"), 5), Text(build.Notes, 14)), 13));
        }
    }

    private static Border BuildTile(string title, string value, string color)
    {
        var tile = Card(Stack(Space(Label(title), 6), Text(value, 15, color, true)), 13);
        tile.Margin = new Thickness(0, 0, 7, 0);
        return tile;
    }

    private UIElement CraftPage()
    {
        var page = Heading("Мастерская", "Поиск по локальной базе предметов, рецепты и расчёт недостающих ресурсов");
        var catalog = _catalog.Crafting;

        var search = Input("", 240);
        var professions = catalog.Recipes.Select(r => r.Profession).Distinct().OrderBy(ProfessionName).ToArray();
        var profession = Choice(new[] { "Все профессии" }.Concat(professions.Select(ProfessionName)), 168);
        var mode = Choice(["Рецепты", "Предметы"], 105);
        var results = new ListBox { Height = 212, DisplayMemberPath = nameof(Recipe.Name) };
        var tally = Hint("");
        page.Children.Add(Space(Card(Stack(
            Space(Hint($"Срез Global LST от {_catalog.Reference.CraftingDate}. Английские названия сохранены из таблицы. Сверьте количества в клиенте после патчей."), 9),
            Actions(Field("Поиск по названию", search), Field("Профессия", profession), Field("Раздел", mode)),
            Space(tally, 5), results)), 12));
        var detail = new ContentControl();
        page.Children.Add(detail);
        search.TextChanged += (_, _) => Filter();
        profession.SelectionChanged += (_, _) => Filter();
        mode.SelectionChanged += (_, _) => Filter();
        results.SelectionChanged += (_, _) =>
        {
            detail.Content = results.SelectedItem switch
            {
                Recipe recipe => Calculator(catalog, recipe),
                CatalogItem item => Card(Stack(Text(item.Name, 19, bold: true), Hint(ItemSource(item.Source)))),
                _ => null
            };
        };
        Filter();
        return page;

        void Filter()
        {
            var term = search.Text.Trim();
            if (mode.SelectedIndex == 1)
            {
                var items = catalog.Items.Values.Where(i => i.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Name).ToArray();
                results.ItemsSource = items;
                tally.Text = $"Найдено предметов: {items.Length}";
            }
            else
            {
                var recipes = catalog.Recipes.Where(r => (r.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (catalog.Items.TryGetValue(r.OutputId, out var output) && output.Name.Contains(term, StringComparison.OrdinalIgnoreCase))) &&
                    (profession.SelectedIndex <= 0 || r.Profession == professions[profession.SelectedIndex - 1]))
                    .OrderBy(r => r.Name).ToArray();
                results.ItemsSource = recipes;
                tally.Text = $"Найдено рецептов: {recipes.Length}";
            }
            results.SelectedIndex = results.Items.Count > 0 ? 0 : -1;
        }
    }

    private static string ItemSource(string source) => source switch
    {
        "craft" => "Источник в базе: крафт",
        "gather" or "gathering" => "Источник в базе: сбор",
        "vendor" or "shop" => "Источник в базе: торговец",
        _ => "Проверьте источник в игре"
    };

    private static string ProfessionName(string profession) => profession switch
    {
        "alchemy" => "Алхимия", "blacksmithing" => "Кузнечное дело", "cooking" => "Кулинария",
        "jewelcrafting" => "Ювелирное дело", "tailoring" => "Портняжное дело", _ => profession
    };

    private static Border Calculator(CraftCatalog catalog, Recipe recipe)
    {
        var count = Input("1", 92, 6);
        var summary = Text("", 13, "Mint", true);
        var output = catalog.Items.GetValueOrDefault(recipe.OutputId)?.Name ?? recipe.Name;
        var produced = Text("", 13, "Mint", true);
        var result = new DockPanel { LastChildFill = true };
        var illustration = new ItemIllustration(recipe.Type) { Width = 70, Height = 70, Margin = new Thickness(0, 0, 13, 0) };
        DockPanel.SetDock(illustration, Dock.Left);
        result.Children.Add(illustration);
        result.Children.Add(Stack(Space(Label("РЕЗУЛЬТАТ КРАФТА"), 4), Space(Text(output, 19, bold: true), 4),
            produced, Hint($"{ProfessionName(recipe.Profession)} · {recipe.Yield:N0} за изготовление" +
                (recipe.Faction == 1 ? " · вариант фракции 1" : ""))));
        var outputCard = Card(result, 12);
        outputCard.Background = Ui.Brush("Base");
        var panel = Stack(Space(outputCard, 11),
            Space(Field("Сколько предметов нужно", count), 10));
        var grid = new Grid();
        foreach (var width in new[] { 3.0, 1.0, 1.0, 1.0, 1.0 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
        string[] columns = ["Материал", "Нужно", "В наличии", "Цена за 1", "Докупить"];
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < columns.Length; i++) Place(grid, Space(Label(columns[i]), 5), 0, i);
        var inputRows = new List<(int PerBatch, TextBlock Total, TextBox Have, TextBox Price, TextBlock Missing)>();
        foreach (var ingredient in recipe.Materials.GroupBy(m => m.Id))
        {
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = catalog.Items.TryGetValue(ingredient.Key, out var item) ? item.Name : $"Предмет {ingredient.Key}";
            var required = Text("", 13, "Gold", true);
            var have = Input("0", double.NaN, 8);
            var price = Input("0", double.NaN, 9);
            var missing = Text("", 13, "Mint", true);
            have.Margin = price.Margin = new Thickness(0, 0, 7, 4);
            System.Windows.Automation.AutomationProperties.SetName(have, "В наличии · " + label);
            System.Windows.Automation.AutomationProperties.SetName(price, "Цена · " + label);
            Place(grid, Text(label), row, 0);
            Place(grid, required, row, 1);
            Place(grid, have, row, 2);
            Place(grid, price, row, 3);
            Place(grid, missing, row, 4);
            inputRows.Add((ingredient.Sum(m => m.Count), required, have, price, missing));
            have.TextChanged += (_, _) => Recalculate();
            price.TextChanged += (_, _) => Recalculate();
        }
        count.TextChanged += (_, _) => Recalculate();
        panel.Children.Add(Space(grid, 9));
        panel.Children.Add(summary);
        Recalculate();
        return Card(panel);

        void Recalculate()
        {
            if (!int.TryParse(count.Text, out var target) || target is < 1 or > 100000)
            {
                produced.Text = "Количество —";
                summary.Text = "Введите количество от 1 до 100 000.";
                foreach (var row in inputRows) row.Total.Text = row.Missing.Text = "—";
                return;
            }
            var batches = (target + recipe.Yield - 1) / recipe.Yield;
            produced.Text = $"Получите ×{batches * recipe.Yield:N0}";
            decimal cost = 0;
            var valid = true;
            foreach (var row in inputRows)
            {
                var need = (long)batches * row.PerBatch;
                row.Total.Text = Number(need);
                if (!int.TryParse(row.Have.Text, out var own) || own < 0 ||
                    !int.TryParse(row.Price.Text, out var price) || price < 0)
                {
                    row.Missing.Text = "—";
                    valid = false;
                    continue;
                }
                var missing = Math.Max(0, need - own);
                row.Missing.Text = Number(missing);
                cost += (decimal)missing * price;
            }
            summary.Text = valid
                ? $"Изготовлений: {batches:N0}  ·  Получите: {batches * recipe.Yield:N0}  ·  Закупка: {cost:N0} кина"
                : "Укажите целые неотрицательные остатки и цены.";
        }
    }
}
