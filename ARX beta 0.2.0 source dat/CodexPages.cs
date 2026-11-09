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
