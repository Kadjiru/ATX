using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using static ArxBeta.Ui;

namespace ArxBeta;

internal sealed record PetEntry(int Id, string Name, string English, string Category, bool HasSoul, string[] Habitats)
{
    public string DisplayName => Loc.Name(Name, English);
}

public sealed partial class Dashboard
{
    private sealed record PetFamily(string Id, string Name, string Stats);

    private static readonly PetFamily[] PetFamilies =
    [
        new("Intellect", Loc.T("Когни · Cogni"), Loc.T("На 3-м уровне: +30 HP, +2 критического удара, +1 силы.")),
        new("Feral", Loc.T("Фера · Fera"), Loc.T("На 3-м уровне: +3 скорости маунта по земле, +2 к бонусу точности, +1 ловкости.")),
        new("Nature", Loc.T("Натура · Natura"), Loc.T("На 3-м уровне: +900 выносливости, +2 сопротивления криту, +1 точности (Precision).")),
        new("Trans", Loc.T("Вариан · Varian"), Loc.T("На 3-м уровне: снижение расхода выносливости при рывке маунта +30, бонус уклонения +2, телосложение +1.")),
        new("Special", Loc.T("Особые · Special"), Loc.T("Отдельное семейство. У этих питомцев нет обычного бонуса характеристик за владение."))
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
            body.Content = help ? PetTraining() : PetCatalogPanel();
        }
    }

    private static UIElement PetCatalogPanel()
    {
        var pets = PetCatalog.Value.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (pets.Length == 0)
            return GuideCard(Loc.T("Справочник недоступен"), Loc.T("Файл данных питомцев отсутствует или повреждён. Используйте полную сборку ARX."));
        var search = Input(width: 250);
        var category = Choice(new[] { Loc.T("Все семейства") }.Concat(PetFamilies.Select(x => x.Name)), 170);
        var list = new ListBox { Height = 335, DisplayMemberPath = nameof(PetEntry.DisplayName),
            ItemContainerStyle = (Style)Application.Current.FindResource("ReferenceRow") };
        AutomationName(list, Loc.T("Список питомцев"));
        var count = Hint("");
        var detail = new ContentControl();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(252) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var index = Card(Stack(Space(count, 7), list), 10);
        index.Margin = new Thickness(0, 0, 10, 0);
        Place(grid, index, 0, 0);
        Place(grid, detail, 0, 1);
        search.TextChanged += (_, _) => Filter();
        category.SelectionChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) => ShowDetail();
        Filter();
        return Stack(Space(Actions(Field(Loc.T("Имя питомца или регион"), search), Field(Loc.T("Семейство"), category)), 6), grid,
            Space(Hint(Loc.T("Aion 2 Global · каталог 09.11.2026. Регионы показаны по доступным данным появления монстров.")), 0));

        void Filter()
        {
            var family = category.SelectedIndex > 0 ? PetFamilies[category.SelectedIndex - 1].Id : null;
            var query = search.Text.Trim();
            var current = list.SelectedItem as PetEntry;
            var matches = pets.Where(x => (family is null || x.Category == family) &&
                (Matches(x.Name, query) || Matches(x.English, query) ||
                 x.Habitats.Any(zone => Matches(zone, query) || Matches(Loc.T(zone), query)))).ToArray();
            list.ItemsSource = matches;
            list.SelectedItem = matches.Contains(current) ? current : matches.FirstOrDefault();
            count.Text = Loc.F($"{matches.Length} из {pets.Length} питомцев");
            ShowDetail();
        }

        void ShowDetail()
        {
            if (list.SelectedItem is not PetEntry pet)
            {
                detail.Content = GuideCard(Loc.T("Питомец не найден"), Loc.T("Проверьте название, регион или выбранное семейство."));
                return;
            }
            var family = PetFamilies.FirstOrDefault(x => x.Id == pet.Category);
            var hero = Card(Stack(Space(Label(family?.Name ?? pet.Category), 5),
                Space(Text(pet.DisplayName, 23, "Mint", true), 4), Hint(Loc.English ? "" : pet.English)), 15);
            hero.Background = Brush("Raised");
            var source = pet switch
            {
                { Id: 1001 } => Loc.T("Чёрный дракон входил в набор основателя Global."),
                { Id: 1112 } => Loc.T("Бродячий торговец руги встречается среди наград магазина фестиваля шуго."),
                { Id: 1343 or 1363 } => Loc.T("Этот облик предлагался в сезонном ассортименте Ателье. Проверьте сроки в игровом магазине."),
                { Habitats.Length: > 0 } => Loc.F($"Связанные монстры встречаются здесь: {string.Join(", ", pet.Habitats.Select(Loc.T))}. Сверьте источник души в окне питомца в игре."),
                { Category: "Special" } => Loc.T("Особый источник. Такие питомцы могут выдаваться за событие, комплект или покупку. Проверьте описание в игре."),
                _ => Loc.T("В каталоге нет привязки к известной зоне. Проверьте подсказку в игре, сундуки и ассортимент торговцев.")
            };
            detail.Content = Stack(Space(hero, 9),
                Space(GuideCard(Loc.T("Где искать"), source), 8),
                Space(GuideCard(Loc.T("Бонус за владение"), family?.Stats ?? Loc.T("Данные о бонусе отсутствуют.")), 8),
                GuideCard(Loc.T("Прогресс"),
                    pet.HasSoul ? Loc.T("Открытие — 5 душ. Второй уровень — ещё 25. Третий — ещё 75. Всего 105.")
                        : Loc.T("В этой записи нет обычной души питомца. Условия открытия и повышения смотрите в игре.")));
        }
    }

    private static UIElement PetTraining() => Stack(
        Space(GuideCard(Loc.T("1   Откройте питомца"),
            Loc.T("Обычного питомца можно открыть за 5 душ соответствующего вида. Души дают связанные монстры, в том числе в ряде подземелий. Проверяйте предметы питомцев в сундуках Возвышения и текущем ассортименте торговцев Чёрного Облака.")), 8),
        Space(GuideCard(Loc.T("2   Поднимите уровень и бонус коллекции"),
            Loc.T("Для обычного питомца 2-й уровень стоит ещё 25 душ, 3-й — ещё 75. Бонусы за владение дают собранные питомцы, поэтому для характеристик можно призывать понравившегося маунта. Награды коллекции забирайте по мере выполнения этапов.")), 8),
        Space(GuideCard(Loc.T("3   Развивайте доску семейства"),
            Loc.T("У каждого семейства своя доска. Уровни питомцев дают опыт понимания своего семейства. Анализ тоже даёт опыт и меняет незакреплённые строки за кину и кристаллы душ. Повышение понимания открывает слоты и улучшает шансы на редкие строки.")), 8),
        Space(GuideCard(Loc.T("4   Закрепляйте строки, когда это окупится"),
            Loc.T("Закрепление повышает цену каждого следующего анализа. При небольшом бюджете сначала открывайте слоты без закреплений. Для PvE ищите урон PvE, если уже стабильно попадаете, а для освоения подбирайте защитные строки под конкретный бой. Сверяйте цену анализа в игре.")), 8),
        Space(GuideCard(Loc.T("5   Езда и превращение"),
            Loc.T("Собранных питомцев можно использовать как маунтов. Для превращения нужен питомец 3-го уровня и конфета эмпатии. У особых питомцев способы получения и условия могут различаться.")), 8),
        Hint(Loc.T("Названия и значения приведены для Global. После патча сверяйте условия и стоимость в игровом меню питомцев.")));
}
