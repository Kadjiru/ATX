using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using static ArxBeta.Ui;

namespace ArxBeta;

internal sealed record PetEntry(int Id, string Name, string English, string Category, bool HasSoul, string[] Habitats)
{
    public string DisplayName => Loc.Name(Name, English);
}

public sealed partial class Dashboard
{
    private sealed record PetStat(string Value, string Label);
    private sealed record PetFamily(string Id, string Name, ImageSource Icon, PetStat[] Stats);
    private sealed record PetCard(PetEntry Pet, PetFamily? Family)
    {
        public string Name => Pet.DisplayName;
        public string FamilyName => Family?.Name ?? Pet.Category;
        public ImageSource Icon => Family?.Icon ?? PetIcons.Special;
        public override string ToString() => Name;
    }

    private static readonly PetFamily[] PetFamilies =
    [
        new("Intellect", Loc.T("Когни · Cogni"), PetIcons.Cogni,
            [new("+30", "HP"), new("+2", Loc.T("Критический удар")), new("+1", Loc.T("Сила"))]),
        new("Feral", Loc.T("Фера · Fera"), PetIcons.Fera,
            [new("+3", Loc.T("Скорость маунта по земле")), new("+2", Loc.T("Бонус точности")), new("+1", Loc.T("Ловкость"))]),
        new("Nature", Loc.T("Натура · Natura"), PetIcons.Natura,
            [new("+900", Loc.T("Выносливость")), new("+2", Loc.T("Сопротивление криту")), new("+1", Loc.T("Точность (Precision)"))]),
        new("Trans", Loc.T("Вариан · Varian"), PetIcons.Varian,
            [new("+30", Loc.T("Экономия выносливости при рывке маунта")), new("+2", Loc.T("Бонус уклонения")), new("+1", Loc.T("Телосложение"))]),
        new("Special", Loc.T("Особые · Special"), PetIcons.Special, [])
    ];

    private static readonly Lazy<PetEntry[]> PetCatalog = new(() =>
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "Pets.json");
            return JsonSerializer.Deserialize<PetEntry[]>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    });

    private UIElement PetsPage()
    {
        var page = Heading(Loc.T("Питомцы"), Loc.T("Найдите питомца, узнайте его бонусы и спланируйте прокачку семейства."));
        var body = new ContentControl();
        var catalog = new Button { Content = Loc.T("Справочник") };
        var training = new Button { Content = Loc.T("Как прокачивать") };
        UIElement? catalogPanel = null, trainingPanel = null;
        catalog.Click += (_, _) => Show(false);
        training.Click += (_, _) => Show(true);
        page.Children.Add(Space(Actions(catalog, training), 7));
        page.Children.Add(body);
        Show(false);
        return page;

        void Show(bool help)
        {
            Highlight(catalog, !help);
            Highlight(training, help);
            body.Content = help ? (trainingPanel ??= PetTraining()) : (catalogPanel ??= PetCatalogPanel());
        }
    }

    private static UIElement PetCatalogPanel()
    {
        var families = PetFamilies.ToDictionary(family => family.Id);
        var pets = PetCatalog.Value.Select(pet => new PetCard(pet, families.GetValueOrDefault(pet.Category)))
            .OrderBy(card => card.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (pets.Length == 0)
            return GuideCard(Loc.T("Справочник недоступен"), Loc.T("Файл данных питомцев отсутствует или повреждён. Используйте полную сборку ARX."));
        var search = Input(width: 250);
        var category = Choice(new[] { Loc.T("Все семейства") }.Concat(PetFamilies.Select(x => x.Name)), 170);
        var rows = new Style(typeof(ListBoxItem), (Style)Application.Current.FindResource("ReferenceRow"));
        rows.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 4)));
        var list = new ListBox { Height = 418, ItemTemplate = PetTileTemplate(), ItemContainerStyle = rows };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        AutomationName(list, Loc.T("Список питомцев"));
        var count = Hint("");
        var detail = new ContentControl();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(275) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var index = Card(Stack(Space(Label(Loc.T("Список питомцев")), 3), Space(count, 8), list), 10);
        index.VerticalAlignment = VerticalAlignment.Top;
        index.Margin = new Thickness(0, 0, 10, 0);
        Place(grid, index, 0, 0);
        Place(grid, detail, 0, 1);
        search.TextChanged += (_, _) => Filter();
        category.SelectionChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) => ShowDetail();
        Filter();
        return Stack(Space(Actions(Field(Loc.T("Имя питомца или регион"), search), Field(Loc.T("Семейство"), category)), 6), Space(grid, 10),
            Space(Hint(Loc.T("Aion 2 Global · каталог 09.11.2026. Регионы показаны по доступным данным появления монстров.")), 0));

        void Filter()
        {
            var family = category.SelectedIndex > 0 ? PetFamilies[category.SelectedIndex - 1].Id : null;
            var query = search.Text.Trim();
            var current = list.SelectedItem as PetCard;
            var matches = pets.Where(card => (family is null || card.Pet.Category == family) &&
                (Matches(card.Pet.Name, query) || Matches(card.Pet.English, query) ||
                 card.Pet.Habitats.Any(zone => Matches(zone, query) || Matches(Loc.T(zone), query)))).ToArray();
            list.ItemsSource = matches;
            list.SelectedItem = matches.Contains(current) ? current : matches.FirstOrDefault();
            if (list.SelectedItem is { } item) list.ScrollIntoView(item);
            count.Text = Loc.F($"{matches.Length} из {pets.Length} питомцев");
        }

        void ShowDetail()
        {
            detail.Content = list.SelectedItem is PetCard card ? PetDetails(card)
                : GuideCard(Loc.T("Питомец не найден"), Loc.T("Проверьте название, регион или выбранное семейство."));
        }
    }

    private static DataTemplate PetTileTemplate()
    {
        var tile = new FrameworkElementFactory(typeof(Border));
        tile.SetValue(Border.BackgroundProperty, Brush("Surface"));
        tile.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        tile.SetValue(Border.PaddingProperty, new Thickness(7));
        var row = new FrameworkElementFactory(typeof(DockPanel));
        tile.AppendChild(row);
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetBinding(Image.SourceProperty, new Binding(nameof(PetCard.Icon)));
        image.SetValue(FrameworkElement.WidthProperty, 35.0);
        image.SetValue(FrameworkElement.HeightProperty, 35.0);
        image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        row.AppendChild(image);
        var captions = new FrameworkElementFactory(typeof(StackPanel));
        captions.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(captions);
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(PetCard.Name)));
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        captions.AppendChild(title);
        var family = new FrameworkElementFactory(typeof(TextBlock));
        family.SetBinding(TextBlock.TextProperty, new Binding(nameof(PetCard.FamilyName)));
        family.SetValue(TextBlock.ForegroundProperty, Brush("Muted"));
        family.SetValue(TextBlock.FontSizeProperty, 11.0);
        captions.AppendChild(family);
        return new DataTemplate { VisualTree = tile };
    }

    private static UIElement PetDetails(PetCard card)
    {
        var pet = card.Pet;
        var headings = Stack(Space(Label(card.FamilyName), 4), Text(card.Name, 25, "Mint", true));
        headings.VerticalAlignment = VerticalAlignment.Center;
        if (!Loc.English && pet.Name != pet.English) headings.Children.Add(Hint(pet.English));
        var hero = Card(PetHeading(card.Icon, headings, 66), 15);
        hero.Background = Brush("Raised");

        var source = pet switch
        {
            { Id: 1001 } => Loc.T("Чёрный дракон входил в набор основателя Global."),
            { Id: 1112 } => Loc.T("Бродячий торговец руги встречается среди наград магазина фестиваля шуго."),
            { Id: 1343 or 1363 } => Loc.T("Этот облик предлагался в сезонном ассортименте Ателье. Проверьте сроки в игровом магазине."),
            { Habitats.Length: > 0 } => Loc.T("В этих зонах встречаются связанные монстры. Сверьте источник души в окне питомца в игре."),
            { Category: "Special" } => Loc.T("Особый источник. Такие питомцы могут выдаваться за событие, комплект или покупку. Проверьте описание в игре."),
            _ => Loc.T("В каталоге нет привязки к известной зоне. Проверьте подсказку в игре, сундуки и ассортимент торговцев.")
        };
        var location = Stack(Hint(source));
        if (pet.Habitats.Length > 0)
        {
            var regions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            foreach (var zone in pet.Habitats)
                regions.Children.Add(new Border
                {
                    Background = Brush("Raised"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 0, 6, 4), Child = Text(Loc.T(zone), 12, "Mint", true)
                });
            location.Children.Add(regions);
        }

        UIElement bonus = card.Family switch
        {
            { Stats.Length: > 0 } family => Stack(Space(Hint(Loc.T("На 3-м уровне")), 6), PetMetrics(family.Stats)),
            { Id: "Special" } => Hint(Loc.T("Отдельное семейство. У этих питомцев нет обычного бонуса характеристик за владение.")),
            _ => Hint(Loc.T("Данные о бонусе отсутствуют."))
        };
        UIElement progress = pet.HasSoul ? Stack(PetSoulCosts(), Hint(Loc.T("Всего 105 душ до 3-го уровня.")))
            : Hint(Loc.T("В этой записи нет обычной души питомца. Условия открытия и повышения смотрите в игре."));
        return Stack(Space(hero, 9),
            Space(PetSection(PetIcons.Location, Loc.T("Где искать"), location), 8),
            Space(PetSection(card.Icon, Loc.T("Бонус за владение"), bonus), 8),
            PetSection(PetIcons.Levels, Loc.T("Прокачка питомца"), progress));
    }

    private static UniformGrid PetSoulCosts() => PetMetrics(
        [new("5", Loc.T("Открытие")), new("+25", Loc.T("Уровень 2")), new("+75", Loc.T("Уровень 3"))]);

    private static UniformGrid PetMetrics(IEnumerable<PetStat> stats)
    {
        var tiles = new UniformGrid { Columns = 3 };
        foreach (var stat in stats)
        {
            var tile = Card(Stack(Space(Text(stat.Value, 21, "Mint", true), 3), Text(stat.Label, 12, "Muted")), 9);
            tile.Background = Brush("Base");
            tile.Margin = new Thickness(0, 0, 6, 6);
            tiles.Children.Add(tile);
        }
        return tiles;
    }

    private static DockPanel PetHeading(ImageSource icon, UIElement content, double size)
    {
        var panel = new DockPanel();
        var image = new Image { Source = icon, Width = size, Height = size, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(image, Dock.Left);
        panel.Children.Add(image);
        panel.Children.Add(content);
        return panel;
    }

    private static Border PetSection(ImageSource icon, string title, UIElement content)
    {
        var caption = Text(title, 16, bold: true);
        caption.VerticalAlignment = VerticalAlignment.Center;
        return Card(Stack(Space(PetHeading(icon, caption, 34), 8), content), 12);
    }

    private static UIElement PetTraining()
    {
        var tiles = new UniformGrid { Columns = 2 };
        Add(PetIcons.Soul, Loc.T("1   Откройте питомца"),
            Loc.T("Обычного питомца можно открыть за 5 душ соответствующего вида. Души дают связанные монстры, в том числе в ряде подземелий. Проверяйте предметы питомцев в сундуках Возвышения и текущем ассортименте торговцев Чёрного Облака."));
        Add(PetIcons.Levels, Loc.T("2   Поднимите уровень и бонус коллекции"),
            Loc.T("Для обычного питомца 2-й уровень стоит ещё 25 душ, 3-й — ещё 75. Бонусы за владение дают собранные питомцы, поэтому для характеристик можно призывать понравившегося маунта. Награды коллекции забирайте по мере выполнения этапов."));
        Add(PetIcons.Board, Loc.T("3   Развивайте доску семейства"),
            Loc.T("У каждого семейства своя доска. Уровни питомцев дают опыт понимания своего семейства. Анализ тоже даёт опыт и меняет незакреплённые строки за кину и кристаллы душ. Повышение понимания открывает слоты и улучшает шансы на редкие строки."));
        Add(PetIcons.Lock, Loc.T("4   Закрепляйте строки, когда это окупится"),
            Loc.T("Закрепление повышает цену каждого следующего анализа. При небольшом бюджете сначала открывайте слоты без закреплений. Для PvE ищите урон PvE, если уже стабильно попадаете, а для освоения подбирайте защитные строки под конкретный бой. Сверяйте цену анализа в игре."));
        var mounts = PetSection(PetIcons.Varian, Loc.T("5   Езда и превращение"),
            Hint(Loc.T("Собранных питомцев можно использовать как маунтов. Для превращения нужен питомец 3-го уровня и конфета эмпатии. У особых питомцев способы получения и условия могут различаться.")));
        return Stack(Space(PetSection(PetIcons.Soul, Loc.T("Стоимость прокачки обычного питомца"),
                Stack(PetSoulCosts(), Hint(Loc.T("Всего 105 душ до 3-го уровня.")))), 9),
            tiles, Space(mounts, 9), Hint(Loc.T("Названия и значения приведены для Global. После патча сверяйте условия и стоимость в игровом меню питомцев.")));

        void Add(ImageSource icon, string title, string body)
        {
            var tile = PetSection(icon, title, Hint(body));
            tile.Margin = new Thickness(0, 0, 8, 8);
            tiles.Children.Add(tile);
        }
    }
}
