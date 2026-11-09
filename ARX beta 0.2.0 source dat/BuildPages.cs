using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using static ArxBeta.Ui;

namespace ArxBeta;

public sealed partial class Dashboard
{
    private bool _showFightGuides;

    private UIElement CodexPage()
    {
        var page = Heading("Билды и гайды", "Сборки для восьми классов и подсказки перед боем · Aion 2 Global");
        var body = new ContentControl();
        var buildTab = new Button { Content = "Мой билд" };
        var guideTab = new Button { Content = "Гайды по боям" };
        buildTab.Click += (_, _) => Show(false);
        guideTab.Click += (_, _) => Show(true);
        page.Children.Add(Space(Actions(buildTab, guideTab), 7));
        page.Children.Add(body);
        Show(_showFightGuides || _store.Preview && _page == "guides");
        return page;

        void Show(bool guides)
        {
            _showFightGuides = guides;
            Highlight(buildTab, !guides);
            Highlight(guideTab, guides);
            body.Content = guides ? FightGuidesPanel() : CharacterBuildPanel();
        }
    }

    private UIElement CharacterBuildPanel()
    {
        var profile = _store.Profile;
        var choices = profile.CharacterBuilds;
        var classes = BuildLibrary.Classes;
        var character = new ComboBox { Width = 225, MinHeight = 31 };
        var savedCharacters = Actions(Field("Сохранённые персонажи", character));
        var name = Input(width: 182, max: 40);
        var server = Input(width: 145, max: 40);
        var mode = Choice(["Группа · экспедиции и рейды", "Соло · кошмар и возвышение"], 240);
        var status = Hint("");
        var detail = new ContentControl();
        var classButtons = new List<Button>();
        var classGrid = new UniformGrid { Columns = 4 };
        PveBuild? build = null;
        CharacterBuild? selected = null;
        var resettingPicker = false;
        var adding = choices.Count == 0;
        var addCharacter = new Button { Content = "+ Персонаж" };
        var cancel = new Button { Content = "Отмена" };
        var fields = Actions(Field("Имя персонажа", name), Field("Сервер (если нужно)", server));
        var save = new Button
        {
            Content = "Сохранить билд", Background = Brush("Mint"), BorderBrush = Brush("Mint"), Foreground = Brush("Base")
        };
        addCharacter.Click += (_, _) => BeginAdd();
        cancel.Click += (_, _) => LoadCharacter(character.SelectedItem as CharacterBuild);
        save.Click += (_, _) => SaveBuild();
        foreach (var button in new[] { addCharacter, cancel, save })
            button.VerticalAlignment = VerticalAlignment.Bottom;
        savedCharacters.Children.Add(addCharacter);
        addCharacter.Margin = new Thickness(0, 0, 8, 7);
        fields.Children.Add(cancel);
        cancel.Margin = new Thickness(0, 0, 8, 7);
        foreach (var item in classes)
        {
            var button = Button(item.Name, () =>
            {
                build = item;
                status.Text = "";
                RefreshBuild();
            });
            button.Margin = new Thickness(0, 0, 6, 6);
            classButtons.Add(button);
            classGrid.Children.Add(button);
        }
        var panel = Stack(Space(Card(Stack(
            Space(Text("1   Персонаж", 17, bold: true), 8),
            savedCharacters,
            fields, Space(Text("2   Класс и тип боя", 17, bold: true), 8),
            Space(classGrid, 5), Actions(Field("Тип боя", mode), save), status)), 13), detail);
        AutomationName(classGrid, "Класс персонажа");

        ResetPicker(choices.FirstOrDefault(x => x.Id == profile.SelectedBuildCharacterId) ?? choices.FirstOrDefault());
        if (choices.Count == 0) BeginAdd();
        character.SelectionChanged += (_, _) =>
        {
            if (resettingPicker || character.SelectedItem is not CharacterBuild picked) return;
            LoadCharacter(picked);
            profile.SelectedBuildCharacterId = picked.Id;
            SaveProfile();
        };
        mode.SelectionChanged += (_, _) =>
        {
            status.Text = "";
            RefreshBuild();
        };
        return panel;

        void BeginAdd()
        {
            adding = true;
            selected = null;
            build = null;
            var detected = _store.LiveCharacter is { Confirmed: true, Name.Length: > 0 } live ? live.Name : profile.Character;
            name.Text = choices.Any(x => SameName(x.Name, detected)) ? "" : detected;
            server.Text = name.Text.Length > 0 && SameName(name.Text, profile.Character) ? profile.Server : "";
            mode.SelectedIndex = 0;
            status.Text = "Введите имя и выберите класс. ARX запомнит сборку для этого персонажа.";
            RefreshBuild();
        }

        void ResetPicker(CharacterBuild? value)
        {
            resettingPicker = true;
            character.ItemsSource = choices.OrderBy(x => x.ToString(), StringComparer.CurrentCultureIgnoreCase).ToArray();
            character.SelectedItem = value;
            resettingPicker = false;
            LoadCharacter(value);
        }

        void LoadCharacter(CharacterBuild? value)
        {
            selected = value;
            adding = value is null;
            build = classes.FirstOrDefault(x => x.Id == value?.ClassId);
            mode.SelectedIndex = value?.Solo == true ? 1 : 0;
            status.Text = value is null ? "" : "Измените класс или тип боя и сохраните новый выбор.";
            RefreshBuild();
        }

        void RefreshBuild()
        {
            fields.Visibility = adding ? Visibility.Visible : Visibility.Collapsed;
            cancel.Visibility = adding && choices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            savedCharacters.Visibility = adding ? Visibility.Collapsed : Visibility.Visible;
            save.IsEnabled = build is not null;
            for (var i = 0; i < classes.Length; i++)
                Highlight(classButtons[i], classes[i] == build);
            detail.Content = build is null
                ? Card(Stack(Text("Выберите класс", 19, bold: true), Hint("Появятся приоритеты характеристик, умения и план на бой.")))
                : BuildDetails(build, mode.SelectedIndex == 1);
        }

        void SaveBuild()
        {
            if (build is null) return;
            var characterName = adding ? name.Text.Trim() : selected!.Name;
            var characterServer = adding ? server.Text.Trim() : selected!.Server;
            if (characterName.Length == 0)
            {
                status.Text = "Укажите имя персонажа.";
                name.Focus();
                return;
            }
            if (adding && choices.Any(x => SameName(x.Name, characterName) && SameName(x.Server, characterServer)))
            {
                status.Text = "Этот персонаж уже есть в списке. Откройте его, чтобы обновить билд.";
                return;
            }
            var next = new CharacterBuild(selected?.Id ?? Guid.NewGuid(), characterName, characterServer,
                build.Id, mode.SelectedIndex == 1);
            if (selected is null) choices.Add(next);
            else choices[choices.IndexOf(selected)] = next;
            profile.SelectedBuildCharacterId = next.Id;
            ResetPicker(next);
            if (SaveProfile()) status.Text = $"Сохранено для {next}.";
        }

        bool SaveProfile()
        {
            if (_store.LoadError is not null)
            {
                status.Text = "Локальный профиль не удалось прочитать. Выбор доступен в текущем сеансе.";
                return false;
            }
            try { _store.Save(); return true; }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                status.Text = "Не удалось сохранить локальный профиль. Попробуйте ещё раз.";
                return false;
            }
        }
    }

    private static UIElement BuildDetails(PveBuild build, bool solo)
    {
        var stats = solo ? build.SoloStats : build.Stats;
        var priorities = new UniformGrid { Columns = 3 };
        foreach (var (stat, index) in stats.Select((stat, index) => (stat, index)))
        {
            var tile = GuideCard($"{index + 1:00} · {stat.Name}", stat.Reason);
            tile.Margin = new Thickness(0, 0, 7, 7);
            priorities.Children.Add(tile);
        }
        var hero = Card(Stack(
            Space(Label(solo ? "СОЛО PVE" : "ГРУППОВОЕ PVE"), 5),
            Space(Text(build.Name, 24, "Mint", true), 4),
            Hint($"{(solo ? build.SoloRole : build.Role)}   ·   {build.Weapon}")), 15);
        hero.Background = Brush("Raised");
        return Stack(Space(hero, 12),
            Space(Text("Характеристики по приоритету", 17, bold: true), 6),
            Space(priorities, 6),
            Space(BuildPair(GuideCard("Ключевые умения", solo ? build.SoloSkills : build.Skills),
                GuideCard("Четыре стигмы", solo ? build.SoloStigmas : build.Stigmas)), 8),
            Space(BuildPair(GuideCard("Первые секунды", solo ? build.SoloOpening : build.Opening),
                GuideCard("Что повторять в бою", solo ? build.SoloLoop : build.Loop)), 8),
            Space(GuideCard("Оружие, броня и камни", solo ? build.SoloGear : build.Gear), 8),
            Hint("Поднимайте уровни стигм до ближайшего бонуса, указанного в подсказке игры. Сравнивайте изменения на одной цели, в одинаковой сложности и с теми же баффами."));
    }

    private static UniformGrid BuildPair(Border left, Border right)
    {
        var pair = new UniformGrid { Columns = 2 };
        left.Margin = new Thickness(0, 0, 4, 0);
        right.Margin = new Thickness(4, 0, 0, 0);
        pair.Children.Add(left);
        pair.Children.Add(right);
        return pair;
    }

    private UIElement FightGuidesPanel()
    {
        var search = Input(width: 230);
        var category = Choice(["Все активности", .. FightGuideLibrary.Categories], 175);
        var list = new ListBox { Height = 340, DisplayMemberPath = nameof(FightGuide.Title),
            ItemContainerStyle = (Style)FindResource("ReferenceRow") };
        AutomationName(list, "Гайды по боям");
        var count = Hint("");
        var detail = new ContentControl();
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        var index = Card(Stack(Space(Label("ВЫБЕРИТЕ БОЙ"), 6), Space(count, 6), list), 9);
        index.VerticalAlignment = VerticalAlignment.Top;
        index.Margin = new Thickness(0, 0, 9, 0);
        Place(columns, index, 0, 0);
        Place(columns, detail, 0, 1);
        var reference = _catalog.Reference;
        var referenceCard = Card(new Expander
        {
            Header = "Мета и снаряжение · справочный срез",
            Foreground = Brush("Text"),
            Content = Stack(Space(Hint($"Состав из загруженных боёв AionFlex · {reference.MetaWindow}"), 5),
                Space(Text(reference.Composition, 15, "Mint", true), 6),
                Hint(string.Join("\n", reference.Gear)))
        }, 12);

        category.SelectionChanged += (_, _) => Filter();
        search.TextChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) => ShowDetail();
        search.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && list.SelectedItem is FightGuide)
                list.Focus();
        };
        Filter();
        return Stack(Space(Actions(Field("Активность", category), Field("Поиск по боссу или месту", search)), 5),
            Space(columns, 12), referenceCard);

        void Filter()
        {
            var query = search.Text.Trim();
            var current = list.SelectedItem as FightGuide;
            var matches = FightGuideLibrary.Guides.Where(x =>
                (category.SelectedIndex <= 0 || x.Category == category.SelectedItem as string) &&
                (Matches(x.Title, query) || Matches(x.English, query) || Matches(x.Target, query) ||
                 Matches(x.Category, query) || Matches(x.Brief, query))).ToArray();
            list.ItemsSource = matches;
            list.SelectedItem = matches.Contains(current) ? current : matches.FirstOrDefault();
            count.Text = $"Найдено: {matches.Length}";
            ShowDetail();
        }

        void ShowDetail()
        {
            if (list.SelectedItem is not FightGuide guide)
            {
                detail.Content = GuideCard("Ничего не найдено", "Попробуйте другое имя босса или выберите все активности.");
                return;
            }
            var hero = Card(Stack(Space(Label(guide.Category.ToUpperInvariant()), 4),
                Space(Text(guide.Title, 23, "Mint", true), 4),
                Space(Hint($"{guide.English} · {guide.Target}"), 6), Text(guide.Brief, 14, bold: true)), 15);
            hero.Background = Brush("Raised");
            var mechanics = Stack(Space(Text("Что увидите → что делать", 17, bold: true), 8));
            foreach (var (step, i) in guide.Steps.Select((step, i) => (step, i)))
                mechanics.Children.Add(Space(GuideCard($"{i + 1:00} · {step.Cue}", step.Action), 6));
            detail.Content = Stack(Space(hero, 8), Space(GuideCard("До входа", guide.Preparation), 10),
                Space(mechanics, 4), Space(GuideCard("По ролям", guide.Roles), 6),
                Space(GuideCard("Если попытка сорвалась", guide.Recovery), 6));
        }
    }

    private static bool SameName(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool Matches(string value, string query) =>
        value.Replace('ё', 'е').Replace('Ё', 'Е')
            .Contains(query.Replace('ё', 'е').Replace('Ё', 'Е'), StringComparison.OrdinalIgnoreCase);

    private static void AutomationName(DependencyObject control, string label) =>
        System.Windows.Automation.AutomationProperties.SetName(control, label);

    private static void Highlight(Button button, bool selected)
    {
        button.Background = Brush(selected ? "Raised" : "Surface");
        button.BorderBrush = Brush(selected ? "Mint" : "Line");
        button.Foreground = Brush(selected ? "Mint" : "Text");
    }

    private static Border GuideCard(string title, string body) =>
        Card(Stack(Space(Text(title, 15, "Mint", true), 5), Text(body, 13)), 13);
}
