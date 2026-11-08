using System.Windows;
using System.Windows.Controls;
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
        var page = Heading("Мета и билды", $"Памятки легиона ARX · {reference.GameBuild} · сводка {reference.ReferenceDate}");
        page.Children.Add(Space(Card(Stack(
            Space(Label("СОСТАВ ГРУППЫ · " + reference.MetaWindow.ToUpperInvariant()), 5),
            Space(Text(reference.Composition, 19, "Mint", true), 7),
            Hint("Наиболее частый состав среди боёв, загруженных пользователями AionFlex за неделю. Частота состава зависит от контента и состава выборки."))), 12));
        var choose = Choice(Builds.Select(b => b.Name), 265);
        var buildDetail = new StackPanel();
        var block = Stack(Space(Text("Стартовые PvE-билды", 20, bold: true), 7), Space(choose, 10), buildDetail);
        page.Children.Add(Space(Card(block), 12));
        choose.SelectionChanged += (_, _) => RenderBuild();
        RenderBuild();
        var gear = Stack(Space(Label("СНАРЯЖЕНИЕ · " + reference.GameBuild), 6),
            Space(Text("Предметы для планирования", 20, bold: true), 7));
        foreach (var item in reference.Gear) gear.Children.Add(Space(Text(item, 14), 6));
        page.Children.Add(Space(Card(gear), 12));
        page.Children.Add(Card(Stack(
            Space(Label("ГАЙД ЛЕГИОНА"), 6),
            Space(Text(reference.Raid.Title, 20, bold: true), 6),
            Space(Hint(reference.Raid.Entry), 7),
            Space(Text(reference.Raid.Tactics, 14), 6),
            Hint(reference.Raid.Status))));
        return page;

        void RenderBuild()
        {
            buildDetail.Children.Clear();
            if (choose.SelectedIndex < 0) return;
            var build = Builds[choose.SelectedIndex];
            buildDetail.Children.Add(Space(Text(build.Role, 16, "Mint", true), 4));
            buildDetail.Children.Add(Space(Text(build.Focus, 14, bold: true), 6));
            buildDetail.Children.Add(Space(Hint(build.Notes), 8));
            buildDetail.Children.Add(Hint("Приоритеты для первого PvE-билда. Подберите стигмы и точный порядок навыков под доступные очки и боссов."));
        }
    }

    private UIElement CraftPage()
    {
        var page = Heading("Мастерская", "Поиск по локальной базе предметов, рецепты и расчёт недостающих ресурсов");
        var catalog = _catalog.Crafting;

        var search = Input("", 240);
        var profession = Choice(new[] { "Все профессии" }.Concat(catalog.Recipes.Select(r => r.Profession).Distinct().Order()), 168);
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
                var recipes = catalog.Recipes.Where(r => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                    (profession.SelectedIndex == 0 || r.Profession == (string)profession.SelectedItem))
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

    private static Border Calculator(CraftCatalog catalog, Recipe recipe)
    {
        var count = Input("1", 92, 6);
        var summary = Text("", 13, "Mint", true);
        var panel = Stack(Space(Text(recipe.Name, 20, bold: true), 5),
            Space(Hint($"{recipe.Profession}  ·  {recipe.Type}  ·  Выход за изготовление: {recipe.Yield}  ·  " +
                (recipe.Faction == 1 ? "Вариант фракции 1" : "Основной рецепт")), 9),
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
                summary.Text = "Введите количество от 1 до 100 000.";
                foreach (var row in inputRows) row.Total.Text = row.Missing.Text = "—";
                return;
            }
            var batches = (target + recipe.Yield - 1) / recipe.Yield;
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
