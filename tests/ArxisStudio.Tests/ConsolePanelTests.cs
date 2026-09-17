using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Console;
using ArxisStudio.Modules.Console.Feed;
using ArxisStudio.Modules.Console.Log;
using ArxisStudio.Modules.Console.Panels;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Панели консоли на живом дереве контролов.
/// </summary>
/// <remarks>
/// Здесь проверяется то, чего не видно в чистой работе отбора: следует ли
/// список за журналом, доходит ли до него запись из чужого потока, отпускает
/// ли панель ленту, когда студия с ней прощается.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ConsolePanelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-console-{Guid.NewGuid():N}");

    private IStudioSettings _settings = null!;
    private IStudioStrings _strings = null!;

    public ConsolePanelTests()
    {
        Directory.CreateDirectory(_root);
        ConsoleHub.Reset();
    }

    public void Dispose()
    {
        // Хаб держит панель статикой: не отпустив её, следующий тест получил бы
        // чужую. Ровно та причина, по которой у панели есть Release.
        ConsoleHub.Reset();

        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Панель показывает то, что записали до её рождения.
    /// </summary>
    /// <remarks>
    /// Ради этого журнал и сведён к одному экземпляру: этапы запуска пишут в
    /// него раньше, чем появляется окно, и панель обязана их застать.
    /// </remarks>
    [AvaloniaFact]
    public void The_log_panel_shows_what_was_written_before_it_was_built()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Info, "Startup", "запуск 1257 мс");
        log.Write(StudioLogLevel.Warning, "Plugins", "сосед устарел");

        var panel = LogPanel(log);

        Assert.Equal(2, Records(panel).ItemCount);
    }

    /// <summary>Запись, сделанная после, доходит до списка.</summary>
    [AvaloniaFact]
    public void A_record_written_afterwards_reaches_the_panel()
    {
        var log = new StudioLog();
        var panel = LogPanel(log);

        log.Write(StudioLogLevel.Error, "Plugins", "расширение упало");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, Records(panel).ItemCount);
    }

    /// <summary>
    /// Пачка записей перестраивает список один раз.
    /// </summary>
    /// <remarks>
    /// Журнал зовёт событие на каждую запись, и писать в него могут пачкой.
    /// Перестроение на каждую значило бы работу, итог которой человек не
    /// увидит: он увидит только последний.
    /// </remarks>
    [AvaloniaFact]
    public void A_burst_of_records_rebuilds_the_list_once()
    {
        var log = new StudioLog();
        var panel = LogPanel(log);

        var before = panel.Rebuilds;

        for (var index = 0; index < 500; index++)
            log.Write(StudioLogLevel.Info, "Плагин", $"строка {index}");

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before + 1, panel.Rebuilds);
        Assert.Equal(500, Records(panel).ItemCount);
    }

    /// <summary>
    /// Запись из чужого потока доходит до панели и ничего не роняет.
    /// </summary>
    /// <remarks>
    /// Писать в журнал могут из пула: фоновая задача расширения отчитывается о
    /// своей отмене именно оттуда. Пока журнал никто не наблюдал, это было
    /// незаметно; панель делает это настоящим — трогать дерево контролов из
    /// чужого потока нельзя.
    /// </remarks>
    [AvaloniaFact]
    public void A_record_written_off_the_ui_thread_reaches_the_panel()
    {
        var log = new StudioLog();
        var panel = LogPanel(log);

        var writer = new Thread(() => log.Write(StudioLogLevel.Debug, "Tasks", "отменено"));

        writer.Start();
        writer.Join();

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, Records(panel).ItemCount);
    }

    /// <summary>
    /// Просьба показаться, пришедшая из чужого потока, панель не роняет.
    /// </summary>
    /// <remarks>
    /// Просьба приходит в потоке того, кто позвал команду, а команду сосед волен позвать и из
    /// фоновой работы. Панель по ней прокручивает список к хвосту — контрол, который трогают только
    /// из потока интерфейса. Панель терминала на такие просьбы отвечала переходом в свой поток с
    /// самого начала, консоль — нет.
    /// </remarks>
    [AvaloniaFact]
    public void Being_asked_to_show_up_from_another_thread_is_safe()
    {
        var log = new StudioLog();

        // Журнал длиннее окна, а панель стоит в окне: без этого прокрутка к хвосту не трогает ни
        // одного контрола, и из какого потока её позвали, не видно.
        for (var index = 0; index < 60; index++)
            log.Write(StudioLogLevel.Info, "Studio", $"запись {index}");

        var panel = LogPanel(log);
        var window = new Window { Width = 900, Height = 200, Content = panel.Content };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Exception? thrown = null;

        var caller = new Thread(() =>
        {
            try
            {
                ConsoleHub.Show();
            }
            catch (Exception e)
            {
                thrown = e;
            }
        });

        caller.Start();
        caller.Join();

        Dispatcher.UIThread.RunJobs();

        Assert.Null(thrown);

        window.Close();
    }

    /// <summary>Очистка журнала опустошает панель.</summary>
    [AvaloniaFact]
    public void Clearing_the_log_empties_the_panel()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Info, "A", "раз");

        var panel = LogPanel(log);

        Assert.Equal(1, Records(panel).ItemCount);

        log.Clear();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, Records(panel).ItemCount);
        Assert.True(Part<TextBlock>(panel, "Empty").IsVisible);
    }

    /// <summary>
    /// Выбранная запись показывается целиком.
    /// </summary>
    /// <remarks>
    /// Это единственная дорога, по которой до человека доезжает стек: в записи
    /// журнала отдельного поля под него нет, и исключение приходит в сообщении
    /// многострочным. Список показывает первую строку, подробности — всё.
    /// </remarks>
    [AvaloniaFact]
    public void Selecting_a_record_shows_its_whole_text()
    {
        var message = "Не вышло" + Environment.NewLine + "   в методе Раз()" + Environment.NewLine + "   в методе Два()";

        var log = new StudioLog();
        log.Write(StudioLogLevel.Error, "Settings", message);

        var panel = LogPanel(log);

        Records(panel).SelectedIndex = 0;

        Assert.Equal(message, Part<AxTextArea>(panel, "DetailsText").Text);
    }

    /// <summary>
    /// Подробности не съедают невысокую панель целиком.
    /// </summary>
    /// <remarks>
    /// Панель в доке бывает ростом в полтораста пикселей, и подробности,
    /// заданные числом пикселей, забирали бы её целиком — список пропадал бы
    /// с глаз. Найдено на живой студии: с включёнными подробностями список
    /// оставался без единой строки.
    /// </remarks>
    [AvaloniaFact]
    public void Showing_the_details_leaves_room_for_the_list()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Info, "A", "раз");

        var panel = LogPanel(log);

        // Окно нужно настоящее: без него шаблон корня панели не разворачивается
        // и мерить нечего. Высота — та, при которой беда и вылезла.
        var window = new Window { Width = 900, Height = 140, Content = panel.Content };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Part<AxToggleButton>(panel, "Details").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var details = Part<Border>(panel, "DetailsPane");
        var records = Records(panel);

        Assert.True(details.IsVisible, "подробности не показались");
        Assert.True(details.Bounds.Height > 0, $"подробностям не досталось места: {details.Bounds}");

        // Не «больше нуля»: при заданной пикселями высоте списку доставалась
        // пара пикселей, и такая проверка прошла бы, ничего не заметив.
        Assert.True(records.Bounds.Height >= 20, $"списку осталось {records.Bounds.Height:F0} пикселей");

        window.Close();
    }

    /// <summary>
    /// Подробности приводят с собой границу и уводят её обратно.
    /// </summary>
    /// <remarks>
    /// Долю между списком и подробностями правит человек, а не только
    /// переключатель, — для этого между ними стоит разделитель. Пока
    /// подробностей нет, границе не с чем граничить, и стоять ей незачем.
    /// </remarks>
    [AvaloniaFact]
    public void The_details_bring_a_grip_and_take_it_away()
    {
        var panel = LogPanel(new StudioLog());

        var handle = Part<AxSplitter>(panel, "Handle");
        var details = Part<AxToggleButton>(panel, "Details");

        Assert.False(handle.IsVisible, "граница стоит, а граничить ей не с чем");

        details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(handle.IsVisible, "подробности показаны, а границы нет");

        details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(handle.IsVisible, "подробности убрали, а граница осталась");
    }

    /// <summary>
    /// Следование за хвостом переключается, не пересобирая список.
    /// </summary>
    /// <remarks>
    /// Настройки пишутся парой, поэтому щелчок по прокрутке будит и ключ
    /// времени — с прежним значением. Панель, перестраивавшая список на любой
    /// из двух, собирала его заново новыми строками: выделение слетало, а
    /// открытые подробности гасли — на кнопке, которая решает только, куда
    /// смотреть.
    /// </remarks>
    [AvaloniaFact]
    public void Following_the_tail_is_switched_without_rebuilding_the_list()
    {
        var log = new StudioLog();
        var panel = LogPanel(log);

        log.Write(StudioLogLevel.Error, "Плагин", "не вышло");
        log.Write(StudioLogLevel.Info, "Плагин", "и это тоже");
        Dispatcher.UIThread.RunJobs();

        var records = Records(panel);

        records.SelectedIndex = 0;

        var chosen = records.SelectedItem;
        var before = panel.Rebuilds;

        Part<AxToggleButton>(panel, "Autoscroll").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before, panel.Rebuilds);
        Assert.Same(chosen, records.SelectedItem);
    }

    /// <summary>
    /// Показ времени меняет сами строки, и список пересобирается.
    /// </summary>
    /// <remarks>
    /// Обратная половина того же правила: перестраивать на всякую настройку
    /// нельзя, а на эту — обязательно. Время впечатано в строку при сборке, и
    /// без пересборки столбец остался бы на месте.
    /// </remarks>
    [AvaloniaFact]
    public void Turning_the_timestamps_off_rebuilds_the_rows()
    {
        var log = new StudioLog();
        var panel = LogPanel(log);

        log.Write(StudioLogLevel.Info, "Плагин", "строка");
        Dispatcher.UIThread.RunJobs();

        Assert.True(Shown(panel)[0].HasStamp, "время не показано с самого начала");

        _settings.Set(ConsoleSettings.TimestampsKey, false);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Shown(panel)[0].HasStamp, "столбец времени остался после того, как его выключили");
    }

    /// <summary>
    /// Отпущенная панель больше не следует за журналом.
    /// </summary>
    /// <remarks>
    /// Студия зовёт <c>Release</c> перед тем, как отпустить панель. Панель,
    /// оставшаяся подписанной, продолжала бы перестраивать список, которого
    /// никто не видит, — и держала бы собой всё, до чего дотянулась.
    /// </remarks>
    [AvaloniaFact]
    public void The_panel_lets_go_of_the_log_when_it_is_released()
    {
        var log = new StudioLog();
        var panel = LogPanel(log);

        panel.Release();

        var after = panel.Rebuilds;

        log.Write(StudioLogLevel.Info, "A", "уже неинтересно");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(after, panel.Rebuilds);
    }

    /// <summary>
    /// Ctrl+C копирует все выделенные записи, а не одну.
    /// </summary>
    /// <remarks>
    /// Копировала запись только кнопка полосы и только выделенную: в панели, куда смотрят, чтобы
    /// показать ошибку коллеге, это половина дела. Порядок — показанный, а не порядок выделения.
    /// </remarks>
    [AvaloniaFact]
    public void Copying_with_the_keyboard_takes_every_chosen_record()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Info, "A", "раз");
        log.Write(StudioLogLevel.Warning, "B", "два");
        log.Write(StudioLogLevel.Error, "C", "три");

        var panel = LogPanel(log);
        var window = new Window { Width = 900, Height = 300, Content = panel.Content };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var records = Records(panel);
        var shown = Shown(panel);

        // Выделяем снизу вверх: в буфер записи обязаны уйти сверху вниз.
        records.SelectedItems!.Add(shown[2]);
        records.SelectedItems!.Add(shown[0]);
        Dispatcher.UIThread.RunJobs();

        var taken = Press(records, Key.C, KeyModifiers.Control);

        Assert.True(taken, "Ctrl+C прошёл мимо панели");

        var text = Clipboard(window);

        if (text is null)
            return;

        Assert.Equal($"{LogText.Of(shown[0])}{Environment.NewLine}{LogText.Of(shown[2])}", text);

        window.Close();
    }

    /// <summary>
    /// Меню строки открывается на той записи, на которую показали.
    /// </summary>
    /// <remarks>
    /// Иначе «скопировать» относилось бы к записи, о которой человек не думал: он показал на
    /// одну, а выделена оставалась другая. Так ведут себя списки Windows и оба редактора, на
    /// которые мы смотрим.
    /// </remarks>
    [AvaloniaFact]
    public void Asking_for_the_menu_stands_on_the_record_under_the_pointer()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Info, "A", "раз");
        log.Write(StudioLogLevel.Info, "A", "два");

        var panel = LogPanel(log);
        var window = new Window { Width = 900, Height = 300, Content = panel.Content };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var records = Records(panel);

        records.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        var second = records.ContainerFromIndex(1)!;

        second.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = InputElement.ContextRequestedEvent });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Shown(panel)[1], records.SelectedItem);

        window.Close();
    }

    /// <summary>
    /// Прокрутка вверх отпускает хвост, возврат к низу берёт его обратно.
    /// </summary>
    /// <remarks>
    /// Человек, уехавший вверх читать давнюю ошибку, не хочет, чтобы его утащило вниз следующей же
    /// записью. Прежде решала только кнопка — прокрутка её не трогала, — и панель тянула человека
    /// обратно, пока он не догадывался нажать. Так же ведут себя консоли Rider и VS Code.
    /// </remarks>
    [AvaloniaFact]
    public void Scrolling_up_lets_go_of_the_tail_and_coming_back_takes_it_again()
    {
        var log = new StudioLog();

        for (var index = 0; index < 60; index++)
            log.Write(StudioLogLevel.Info, "A", $"запись {index}");

        var panel = LogPanel(log);
        var window = new Window { Width = 900, Height = 200, Content = panel.Content };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tail = Part<AxToggleButton>(panel, "Autoscroll");

        // Своя область прокрутки есть и у поля поиска: берётся та, что внутри списка.
        var scroll = Records(panel).GetVisualDescendants().OfType<ScrollViewer>().First();

        Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "список уместился целиком — прокручивать нечего");
        Assert.True(tail.IsChecked, "панель начала, не следуя за хвостом");

        // Место ставится руками: в безголовом прогоне список стоит наверху — ScrollIntoView без
        // настоящего кадра его не двигает, и проверять было бы нечего.
        Bottom();

        Assert.True(tail.IsChecked, "у хвоста, а галочка слетела");

        scroll.Offset = new Vector(scroll.Offset.X, 0);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.False(tail.IsChecked, "уехали вверх, а панель всё ещё держится за хвост");

        Bottom();

        Assert.True(tail.IsChecked, "вернулись к низу, а хвост не взялся обратно");

        void Bottom()
        {
            scroll.Offset = new Vector(scroll.Offset.X, scroll.Extent.Height - scroll.Viewport.Height);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        window.Close();
    }

    /// <summary>
    /// Уровень записи назван значком, а не английским словом.
    /// </summary>
    /// <remarks>
    /// Слово приходит из SDK — ERROR, WARN, INFO, DEBUG — и в русском окне стояло как есть, забирая
    /// полсотни точек ширины у сообщения. Значок называет уровень рисунком, и в строке он ровно
    /// один: три соседних скрыты.
    /// </remarks>
    [AvaloniaFact]
    public void A_record_names_its_level_by_an_icon()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Error, "A", "не вышло");

        var panel = LogPanel(log);
        var window = new Window { Width = 900, Height = 200, Content = panel.Content };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var row = Records(panel).ContainerFromIndex(0)!;
        var icons = row.GetVisualDescendants().OfType<AxIcon>().Where(icon => icon.IsVisible).ToList();

        Assert.Single(icons);

        Assert.DoesNotContain(
            row.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "ERROR", StringComparison.Ordinal));

        window.Close();
    }

    /// <summary>Подробности переносят длинные строки, а не возят их вбок.</summary>
    /// <remarks>
    /// В подробностях лежит стек исключения, и путь к файлу длиннее панели — обычное дело.
    /// </remarks>
    [AvaloniaFact]
    public void The_details_wrap_what_does_not_fit()
    {
        var panel = LogPanel(new StudioLog());

        Assert.Equal(TextWrapping.Wrap, Part<AxTextArea>(panel, "DetailsText").TextWrapping);
    }

    /// <summary>Нажимает клавишу там, где стоит человек, и говорит, взяла ли её панель.</summary>
    /// <summary>
    /// Меню источников — список флажков: щелчок прячет один источник, не трогая остальных.
    /// </summary>
    /// <remarks>
    /// Прежде пункты были переключателями: показать можно было либо всех, либо одного, и «студия
    /// и мой плагин, без шума запуска» не выражалось никак. Отбор из нескольких — обычная работа,
    /// и так собран отбор по источнику в консоли браузера и в журналах Rider.
    /// </remarks>
    [AvaloniaFact]
    public void Clicking_a_source_in_the_menu_hides_only_that_source()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Info, "Plugins", "раз");
        log.Write(StudioLogLevel.Info, "Startup", "два");
        log.Write(StudioLogLevel.Info, "Terminal", "три");

        var panel = LogPanel(log);
        var items = panel.SourceItems();

        // Первый пункт — «все источники», дальше сами источники по алфавиту.
        Assert.Equal(4, items.Count);
        Assert.Equal(["Plugins", "Startup", "Terminal"], items.Skip(1).Select(item => item.Header));

        Click(items, "Startup");

        Assert.Equal(["раз", "три"], Shown(panel).Select(row => row.Text));

        Click(items, "Terminal");

        Assert.Equal(["раз"], Shown(panel).Select(row => row.Text));
    }

    /// <summary>
    /// Меню остаётся открытым, а флажки говорят правду после каждого щелчка.
    /// </summary>
    /// <remarks>
    /// Отбор из трёх источников, закрывающий меню на каждом щелчке, стоил бы трёх открытий
    /// подряд. Правда флажков при этом берётся у отбора, а не у самого пункта: пункт
    /// переворачивает свой флажок сам, и на «всех источниках» этот переворот — ложь.
    /// </remarks>
    [AvaloniaFact]
    public void The_source_menu_stays_open_and_its_checks_tell_the_truth()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Info, "Plugins", "раз");
        log.Write(StudioLogLevel.Info, "Startup", "два");

        var panel = LogPanel(log);
        var items = panel.SourceItems();

        Assert.All(items, item => Assert.True(item.StaysOpenOnClick, "Меню закрылось бы на первом же щелчке"));
        Assert.All(items, item => Assert.True(item.IsChecked, "Сначала показаны все"));

        Click(items, "Startup");

        Assert.False(Item(items, "Startup").IsChecked, "Спрятанный источник остался отмеченным");
        Assert.True(Item(items, "Plugins").IsChecked, "Соседний источник потерял отметку");
        Assert.False(items[0].IsChecked, "«Все источники» отмечены при суженном отборе");

        Click(items, "Startup");

        Assert.True(Item(items, "Startup").IsChecked, "Второй щелчок не вернул источник");
        Assert.True(items[0].IsChecked, "Вернувшийся источник не вернул «все источники»");
    }

    /// <summary>
    /// «Все источники» возвращают отбор к пустому одним щелчком.
    /// </summary>
    /// <remarks>
    /// Снимать семь флажков руками, чтобы вернуться к тому, с чего панель начала, человек не
    /// должен. Кнопка полосы при этом гаснет: нажатой она стоит ровно пока отбор сужен.
    /// </remarks>
    [AvaloniaFact]
    public void All_sources_brings_everyone_back()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Info, "Plugins", "раз");
        log.Write(StudioLogLevel.Info, "Startup", "два");

        var panel = LogPanel(log);
        var funnel = Part<AxToggleButton>(panel, "Sources");
        var items = panel.SourceItems();

        Assert.False(funnel.IsChecked, "Воронка нажата при нетронутом отборе");

        Click(items, "Startup");

        Assert.True(funnel.IsChecked, "Воронка не показала суженный отбор");

        Click(items, (string)items[0].Header!);

        Assert.Equal(["раз", "два"], Shown(panel).Select(row => row.Text));
        Assert.False(funnel.IsChecked, "Воронка осталась нажатой при полном отборе");
    }

    /// <summary>
    /// «Только этот источник» прячет известных, но не того, кто придёт после.
    /// </summary>
    /// <remarks>
    /// Ради этого отбор и перечисляет спрятанных: плагин просыпается щелчком, и его первая же
    /// ошибка обязана дойти до глаз — даже если человек сузил отбор час назад.
    /// </remarks>
    [AvaloniaFact]
    public void Only_this_source_hides_the_known_ones_but_not_a_newcomer()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Info, "Plugins", "раз");
        log.Write(StudioLogLevel.Info, "Startup", "два");

        var panel = LogPanel(log);

        Click(panel.RowItems(Shown(panel)[0]), _strings["console.source.only"]);

        Assert.Equal(["раз"], Shown(panel).Select(row => row.Text));

        log.Write(StudioLogLevel.Error, "Hello", "упал");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["раз", "упал"], Shown(panel).Select(row => row.Text));
    }

    /// <summary>«Скрыть этот источник» убирает его, оставляя остальных.</summary>
    [AvaloniaFact]
    public void Hiding_a_source_from_the_row_menu_drops_it_alone()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Info, "Plugins", "раз");
        log.Write(StudioLogLevel.Info, "Startup", "два");
        log.Write(StudioLogLevel.Info, "Terminal", "три");

        var panel = LogPanel(log);

        Click(panel.RowItems(Shown(panel)[1]), _strings["console.source.hide"]);

        Assert.Equal(["раз", "три"], Shown(panel).Select(row => row.Text));
    }

    /// <summary>
    /// Меню строки несёт тот же список источников — подменю с теми же флажками.
    /// </summary>
    /// <remarks>
    /// До кнопки полосы от строки далеко, а решение о показе принимают, читая записи. Список тот
    /// же и собирается тем же кодом: два входа к одному отбору, а не две его копии.
    /// </remarks>
    [AvaloniaFact]
    public void The_row_menu_carries_the_same_source_list_in_a_submenu()
    {
        var log = new StudioLog();

        log.Write(StudioLogLevel.Info, "Plugins", "раз");
        log.Write(StudioLogLevel.Info, "Startup", "два");

        var panel = LogPanel(log);
        var items = panel.RowItems(Shown(panel)[0]);
        var sources = Item(items, _strings["console.sources"]).Items.OfType<AxMenuItem>().ToList();

        Assert.Equal(["Plugins", "Startup"], sources.Skip(1).Select(item => item.Header));

        Click(sources, "Plugins");

        Assert.Equal(["два"], Shown(panel).Select(row => row.Text));
        Assert.True(Part<AxToggleButton>(panel, "Sources").IsChecked, "Воронка не показала отбор из меню строки");
    }

    /// <summary>
    /// Нажимает пункт меню по его подписи.
    /// </summary>
    /// <remarks>
    /// Событием, а не указателем: меню живёт в попапе — отдельном окне, которого у безголового
    /// прогона нет, — и пункты берутся там, где собираются. Проверять при этом остаётся то же
    /// самое: что панель сделала по нажатию.
    /// </remarks>
    private static void Click(IEnumerable<AxMenuItem> items, string header)
    {
        Item(items, header).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static AxMenuItem Item(IEnumerable<AxMenuItem> items, string header) =>
        items.Single(item => Equals(item.Header, header));

    private static bool Press(Control where, Key key, KeyModifiers modifiers)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = where,
        };

        where.RaiseEvent(args);
        Dispatcher.UIThread.RunJobs();

        return args.Handled;
    }

    /// <summary>
    /// Что лежит в буфере обмена; <c>null</c> — буфера в этом прогоне нет.
    /// </summary>
    /// <remarks>
    /// Буфер обмена — дело платформы, и в безголовом прогоне его может не быть вовсе. Тогда
    /// проверять остаётся то, что панель клавишу взяла: сам текст проверен там, где он и
    /// собирается, — в <c>ConsoleRowsTests</c>.
    /// </remarks>
    private static string? Clipboard(TopLevel window)
    {
        var clipboard = window.Clipboard;

        if (clipboard is null)
            return null;

        var reading = clipboard.TryGetTextAsync();

        return reading.Wait(TimeSpan.FromSeconds(1)) ? reading.Result : null;
    }

    private LogPanel LogPanel(StudioLog log)
    {
        var panel = new LogPanel();

        panel.Attach(Context(log, new Dictionary<Type, object> { [typeof(IStudioLogFeed)] = log }));

        // Обращение к содержимому и строит панель — так же, как это делает студия.
        _ = panel.Content;

        return panel;
    }

    /// <summary>
    /// Контекст, какой студия выдаёт встроенному модулю.
    /// </summary>
    /// <remarks>
    /// Собирается настоящей фабрикой, а не подделкой: обёртки, которые она
    /// надевает на службы, — часть того, что проверяется. Хранилище настроек
    /// подставлено своё, во временную папку: настоящее лежит в данных
    /// пользователя, и тест не вправе его трогать.
    /// </remarks>
    private IStudioContext Context(StudioLog log, IReadOnlyDictionary<Type, object> services)
    {
        var (manifest, error) = ModuleManifest.Load(typeof(ConsoleModule).Assembly);

        Assert.Null(error);

        var installed = new InstalledPlugin(AppContext.BaseDirectory, manifest, null, IsEnabled: true, IsBuiltIn: true);
        var store = new PluginSettingsStore(null, Path.Combine(_root, "plugin-settings.json"));

        var context = new StudioContextFactory(log, new StudioCommands(), null, services, settings: store)
            .Create(installed);

        // Настройки запоминаются: менять их надо той же службой, что и панель.
        // Запись мимо неё — прямо в хранилище — панель бы не разбудила: о
        // правке со стороны модулю говорит студия, отдельным уведомлением.
        _settings = context.Settings;

        // Словарь — тот же, которым панель подписывает пункты меню: искать их по подписи, взятой
        // из другого места, значило бы проверять совпадение двух словарей, а не работу панели.
        _strings = context.Strings;

        return context;
    }

    private static AxListBox Records(LogPanel panel) => Part<AxListBox>(panel, "Records");

    private static List<LogRow> Shown(LogPanel panel) =>
        [.. (Records(panel).ItemsSource as IEnumerable<LogRow>)!];

    private static T Part<T>(ToolWindow panel, string name) where T : Control =>
        panel.Content.GetLogicalDescendants().OfType<T>().Single(part => part.Name == name);
}
