using System.Runtime.CompilerServices;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Xaml;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Служба XAML глазами того, кто её берёт: аренды документов, сохранение через службу файлов, чужая
/// запись, показ и замена поколения, конец решения.
/// </summary>
/// <remarks>
/// Обе службы подняты хостом, как в студии, диск настоящий, модель — провайдера теста. Поколение здесь
/// без сборок проекта: формы стоят на встроенных контролах, и им хватает Avalonia самой студии. Типы
/// проекта и их выгрузку проверяет <see cref="XamlGenerationTests"/>. В общей очереди: модули поднимает
/// хост, а контракты модулей живут на процесс.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class XamlServiceTests
{
    private const string Form = """
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <StackPanel>
            <Button Content="Привет" />
          </StackPanel>
        </UserControl>
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Первая открытая форма поднимает поколение — оценкой профиля дизайна, со свойствами, которые
    /// уводят сборки дизайнера от выхода IDE.
    /// </summary>
    [AvaloniaFact]
    public async Task Opening_a_form_raises_a_generation_from_the_design_profile()
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("MainView.axaml", Form);

        await studio.OpenAsync();

        Assert.Equal(XamlDesignState.Idle, studio.Design.State);

        await using var handle = await studio.Documents.OpenAsync(path, Token);

        Assert.Equal(XamlDesignState.Live, studio.Design.State);
        Assert.Equal("UserControl", handle.Syntax.Root?.Name.LocalName);
        Assert.False(handle.IsModified);
        // Профиль читает то, что хосту нужно знать о проекте: тестовый ли он — такие в поколение не
        // входят, — и компилируются ли привязки по умолчанию.
        Assert.Contains(
            studio.Provider.Requests,
            request => request.GlobalProperties.TryGetValue("OutputPath", out var output) && output == "bin/ArxisStudio/"
                && request.Options.AdditionalProperties.Contains("IsTestProject")
                && request.Options.AdditionalProperties.Contains("AvaloniaUseCompiledBindingsByDefault"));
    }

    /// <summary>
    /// Документ один на файл: правка через одну аренду видна в другой, и документ живёт, пока не
    /// отпущена последняя.
    /// </summary>
    [AvaloniaFact]
    public async Task A_document_is_one_per_file_and_lives_while_it_is_leased()
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("MainView.axaml", Form);

        await studio.OpenAsync();

        var first = await studio.Documents.OpenAsync(path, Token);
        var second = await studio.Documents.OpenAsync(path, Token);
        var heard = new List<XamlDocumentChanges>();

        second.Changed += (_, e) => heard.Add(e.Changes);

        var outcome = await first.EditAsync("Ширина формы", editor => SetWidth(editor, "200"), Token);

        Assert.True(outcome.TextChanged);
        Assert.True(second.IsModified, "правка через одну аренду не видна в другой");
        Assert.Contains("Width=\"200\"", second.Syntax.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains(heard, changes => changes.HasFlag(XamlDocumentChanges.Text));
        Assert.Equal("Ширина формы", second.UndoLabel);

        await first.DisposeAsync();

        Assert.Single(studio.Session.Documents);
        Assert.True(first.IsClosed);
        Assert.False(second.IsClosed);

        await second.DisposeAsync();

        Assert.Empty(studio.Session.Documents);
    }

    /// <summary>
    /// Сохранение пишет службой файлов: файл держит правку, документ читается сохранённым, а своя же
    /// запись, вернувшаяся слежением, не принята за чужую.
    /// </summary>
    [AvaloniaFact]
    public async Task Saving_writes_through_the_files_service_and_its_echo_is_not_taken_for_a_change()
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("MainView.axaml", Form);

        await studio.OpenAsync();

        await using var handle = await studio.Documents.OpenAsync(path, Token);
        var files = Assert.IsAssignableFrom<IStudioFiles>(studio.Exports.Get(typeof(IStudioFiles)));
        var echoes = new List<FileContentChange>();

        files.ContentChanged += (_, e) => echoes.AddRange(e.Changes.Where(change => change.Change.Path == path));

        await handle.EditAsync("Ширина формы", editor => SetWidth(editor, "200"), Token);
        await handle.SaveAsync(Token);

        Assert.False(handle.IsModified);
        Assert.Contains("Width=\"200\"", File.ReadAllText(path.Value), StringComparison.Ordinal);

        await XamlStudio.UntilAsync(() => echoes.Count > 0, "слежение не сказало о записи");

        Assert.All(echoes, echo => Assert.Equal(FileChangeOrigin.Studio, echo.Origin));

        await studio.Session.Host.ReloadAsync(LiveOf(studio, path), Token);

        Assert.False(handle.HasConflict);
        Assert.False(handle.IsModified);
        Assert.Equal("Ширина формы", handle.UndoLabel);
    }

    /// <summary>
    /// Файл переписали мимо несохранённого документа — сохранение отказывает и чужого не затирает.
    /// </summary>
    [AvaloniaFact]
    public async Task A_file_rewritten_under_the_document_refuses_the_save()
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("MainView.axaml", Form);

        await studio.OpenAsync();

        await using var handle = await studio.Documents.OpenAsync(path, Token);

        await handle.EditAsync("Ширина формы", editor => SetWidth(editor, "200"), Token);

        var rider = Form.Replace("Привет", "Rider", StringComparison.Ordinal);

        File.WriteAllText(path.Value, rider);

        await Assert.ThrowsAsync<IOException>(() => handle.SaveAsync(Token));

        Assert.Equal(rider, File.ReadAllText(path.Value));
        Assert.True(handle.IsModified);
    }

    /// <summary>Чистый документ берёт чужую запись молча — шагом своей истории.</summary>
    [AvaloniaFact]
    public async Task Text_written_outside_a_clean_document_is_taken_as_a_step_of_its_history()
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("MainView.axaml", Form);

        await studio.OpenAsync();

        await using var handle = await studio.Documents.OpenAsync(path, Token);
        var rider = Form.Replace("Привет", "Rider", StringComparison.Ordinal);

        File.WriteAllText(path.Value, rider);

        await XamlStudio.UntilAsync(() => handle.Syntax.SourceText.ToString() == rider, "документ не взял текст файла");

        Assert.False(handle.IsModified);
        Assert.False(handle.HasConflict);
        Assert.True(handle.CanUndo, "взятый текст не лёг в историю");
    }

    /// <summary>
    /// Чужая запись поверх несохранённого — вопрос, а не перезапись; оставленные правки сохранение
    /// пишет поверх файла.
    /// </summary>
    [AvaloniaFact]
    public async Task Text_written_outside_unsaved_edits_asks_and_keeps_mine_on_request()
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("MainView.axaml", Form);

        await studio.OpenAsync();

        await using var handle = await studio.Documents.OpenAsync(path, Token);
        var asked = new List<string>();

        handle.ExternalConflict += (_, e) => asked.Add(e.DiskText);

        await handle.EditAsync("Ширина формы", editor => SetWidth(editor, "200"), Token);

        var rider = Form.Replace("Привет", "Rider", StringComparison.Ordinal);

        File.WriteAllText(path.Value, rider);

        await XamlStudio.UntilAsync(() => asked.Count > 0, "о чужой записи не спросили");

        Assert.Equal(rider, asked[0]);
        Assert.True(handle.HasConflict);
        Assert.Contains("Привет", handle.Syntax.SourceText.ToString(), StringComparison.Ordinal);

        await handle.ResolveConflictAsync(XamlConflictChoice.KeepMine, Token);

        Assert.False(handle.HasConflict);
        Assert.True(handle.IsModified);

        await handle.SaveAsync(Token);

        Assert.Contains("Width=\"200\"", File.ReadAllText(path.Value), StringComparison.Ordinal);
    }

    /// <summary>Взятый на вопрос файл — шаг истории: отмена возвращает правки.</summary>
    [AvaloniaFact]
    public async Task Taking_theirs_can_be_undone()
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("MainView.axaml", Form);

        await studio.OpenAsync();

        await using var handle = await studio.Documents.OpenAsync(path, Token);

        await handle.EditAsync("Ширина формы", editor => SetWidth(editor, "200"), Token);

        var mine = handle.Syntax.SourceText.ToString();
        var rider = Form.Replace("Привет", "Rider", StringComparison.Ordinal);

        File.WriteAllText(path.Value, rider);

        await XamlStudio.UntilAsync(() => handle.HasConflict, "о чужой записи не спросили");
        await handle.ResolveConflictAsync(XamlConflictChoice.TakeTheirs, Token);

        Assert.Equal(rider, handle.Syntax.SourceText.ToString());
        Assert.False(handle.IsModified);

        await handle.UndoAsync(Token);

        Assert.Equal(mine, handle.Syntax.SourceText.ToString());
    }

    /// <summary>
    /// На замену поколения показ отдаёт корень раньше участников и берёт новый раньше них: участник,
    /// отпускающий своё, корня уже не видит, а взявшийся за своё — видит новый.
    /// </summary>
    [AvaloniaFact]
    public async Task The_view_gives_its_root_up_before_the_participants_and_takes_the_new_one_first()
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("MainView.axaml", Form);

        await studio.OpenAsync();

        await using var handle = await studio.Documents.OpenAsync(path, Token);
        using var view = await handle.ShowAsync(null, Token);
        // Слабой ссылкой: корень, построенный сессией, — объект поколения, и тест, державший его через
        // замену, держал бы поколение сам.
        var before = RootOf(view);
        var order = new List<string>();

        view.RootChanged += (_, _) => order.Add(view.Root is null ? "корень отдан" : "корень взят");

        using var participant = studio.Design.Register(new Participant(
            release: () => order.Add(view.Root is null ? "участник отпустил без корня" : "участник отпустил при корне"),
            restore: () => order.Add(view.Root is null ? "участник взялся без корня" : "участник взялся при корне")));

        var report = await studio.Session.Host.SwapAsync("проверка порядка", Token);

        Assert.True(report.Reclaimed, report.ToString());
        Assert.Equal(["корень отдан", "участник отпустил без корня", "корень взят", "участник взялся при корне"], order);
        Assert.IsType<UserControl>(view.Root);
        Assert.False(before.IsAlive, "прежний корень пережил замену поколения");
        Assert.Equal(XamlDocumentState.Live, handle.State);
    }

    /// <summary>
    /// Показ ведёт от объекта к пути элемента и обратно, а объявленными считает только то, что
    /// документ объявил сам.
    /// </summary>
    [AvaloniaFact]
    public async Task The_view_leads_from_an_object_to_its_element_and_back()
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("MainView.axaml", Form);

        await studio.OpenAsync();

        await using var handle = await studio.Documents.OpenAsync(path, Token);
        using var view = await handle.ShowAsync(null, Token);

        var declared = view.GetDeclaredObjects();
        var button = Assert.Single(declared.OfType<Button>());

        Assert.Equal(2, declared.Count);

        var element = Assert.IsType<XamlElementPath>(view.PathOf(button));

        Assert.Same(button, view.ObjectAt(element));
        Assert.Equal("Button", element.Resolve(handle.Syntax)?.Name.LocalName);
    }

    /// <summary>Закрытое решение закрывает документы: аренды узнают об этом, поколения больше нет.</summary>
    [AvaloniaFact]
    public async Task Closing_the_solution_closes_the_documents_and_the_generation()
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("MainView.axaml", Form);

        await studio.OpenAsync();

        var handle = await studio.Documents.OpenAsync(path, Token);
        var view = await handle.ShowAsync(null, Token);
        var heard = new List<XamlDocumentChanges>();

        handle.Changed += (_, e) => heard.Add(e.Changes);

        await studio.Projects.CloseAsync();
        await XamlStudio.UntilAsync(() => handle.IsClosed, "аренда не узнала о закрытии решения");
        await XamlStudio.UntilAsync(() => studio.Service.Session is null, "сессия пережила решение");

        Assert.Contains(heard, changes => changes.HasFlag(XamlDocumentChanges.Closed));
        Assert.Null(view.Root);
        Assert.Equal(XamlDesignState.Idle, studio.Design.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handle.EditAsync("Ширина формы", editor => SetWidth(editor, "1"), Token));

        // Прощаются в своё время и своей дорогой — после конца службы тоже.
        view.Dispose();
        await handle.DisposeAsync();
    }

    /// <summary>
    /// Решение закрыли, пока поколение поднималось, — открытие кончается отказом «решение не открыто», а
    /// не отменой, которой открывавший не просил, и сессии не остаётся.
    /// </summary>
    [AvaloniaFact]
    public async Task A_solution_closed_while_the_generation_rises_refuses_the_open()
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("MainView.axaml", Form);

        await studio.OpenAsync();

        // Профиль дизайна читается своей загрузкой — она и встанет на воротах.
        var gate = new LoadGate();

        studio.Provider.Gate = gate;

        var opening = studio.Documents.OpenAsync(path, Token);

        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(30), Token);

        Assert.Equal(XamlDesignState.Starting, studio.Design.State);

        // Отпуск движка встаёт в очередь за задержанной загрузкой: ждать его можно, только отпустив ворота.
        var closing = studio.Projects.CloseAsync();

        await XamlStudio.UntilAsync(() => opening.IsCompleted, "открытие пережило закрытие решения");
        await Assert.ThrowsAsync<InvalidOperationException>(() => opening);

        gate.Release();
        await closing;

        Assert.Null(studio.Service.Session);
        Assert.Equal(XamlDesignState.Idle, studio.Design.State);
    }

    /// <summary>Отпущен последний документ — после простоя кончается и сессия решения.</summary>
    [AvaloniaFact]
    public async Task The_last_document_let_go_ends_the_session_after_the_idle_pause()
    {
        await using var studio = new XamlStudio(idleRelease: TimeSpan.Zero);
        var path = studio.Write("MainView.axaml", Form);

        await studio.OpenAsync();

        var handle = await studio.Documents.OpenAsync(path, Token);

        Assert.NotNull(studio.Service.Session);

        await handle.DisposeAsync();
        await XamlStudio.UntilAsync(() => studio.Service.Session is null, "сессия пережила простой");

        Assert.Equal(XamlDesignState.Idle, studio.Design.State);
    }

    /// <summary>Упавший подписчик аренды соседям не мешает, а сбой уходит студии.</summary>
    [AvaloniaFact]
    public async Task A_failing_subscriber_does_not_keep_the_others_from_hearing()
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("MainView.axaml", Form);

        await studio.OpenAsync();

        await using var handle = await studio.Documents.OpenAsync(path, Token);
        var heard = 0;

        handle.Changed += (_, _) => throw new InvalidOperationException("подписчик упал");
        handle.Changed += (_, _) => heard++;

        await handle.EditAsync("Ширина формы", editor => SetWidth(editor, "200"), Token);

        Assert.True(heard > 0, "второй подписчик ничего не услышал");
        Assert.Contains(studio.Failures, failure => failure.Message == "подписчик упал");
    }

    /// <summary>
    /// Проект, собранный против другого старшего номера Avalonia, поколения не получает, а документ
    /// открывается текстом.
    /// </summary>
    [AvaloniaFact]
    public async Task A_project_built_against_another_avalonia_opens_as_text()
    {
        await using var studio = new XamlStudio { AvaloniaVersion = "11.2.0" };
        var path = studio.Write("MainView.axaml", Form);

        await studio.OpenAsync();

        await using var handle = await studio.Documents.OpenAsync(path, Token);

        Assert.Equal(XamlDesignState.Unsupported, studio.Design.State);
        Assert.Contains("11.2.0", studio.Design.StateReason, StringComparison.Ordinal);
        Assert.Equal(XamlDocumentState.Detached, handle.State);
        Assert.Equal("UserControl", handle.Syntax.Root?.Name.LocalName);
    }

    /// <summary>Пересобрать нечего, пока ничего не открыто.</summary>
    [AvaloniaFact]
    public async Task Nothing_is_rebuilt_before_a_document_is_open()
    {
        await using var studio = new XamlStudio();

        await studio.OpenAsync();

        Assert.Equal(XamlDesignState.Idle, studio.Design.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => studio.Design.RebuildAsync(Token));
    }

    /// <summary>Файл, которого решение не объявляет, служба не открывает.</summary>
    [AvaloniaFact]
    public async Task A_file_outside_the_solution_is_refused()
    {
        await using var studio = new XamlStudio();

        studio.Write("MainView.axaml", Form);

        await studio.OpenAsync();

        var stranger = CanonicalPath.Create(Path.Combine(studio.Root, "Elsewhere.axaml"));

        await Assert.ThrowsAsync<ArgumentException>(() => studio.Documents.OpenAsync(stranger, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => studio.Documents.OpenAsync(studio.PathOf("Program.cs"), Token));
    }

    /// <summary>
    /// Вид файла — по корню: формы — тем же правилом, что у доски, а приложение, стили и словари —
    /// своими видами.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Window", XamlFileKind.Window)]
    [InlineData("local:MainWindow", XamlFileKind.Window)]
    [InlineData("UserControl", XamlFileKind.UserControl)]
    [InlineData("Border", XamlFileKind.Control)]
    [InlineData("Application", XamlFileKind.Application)]
    [InlineData("Styles", XamlFileKind.Styles)]
    [InlineData("Style", XamlFileKind.Styles)]
    [InlineData("ResourceDictionary", XamlFileKind.Resources)]
    public async Task A_file_is_classified_by_its_root(string root, XamlFileKind kind)
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("Probe.axaml", $"""<{root} xmlns="https://github.com/avaloniaui" xmlns:local="using:App" />""");

        Assert.Equal(kind, studio.Documents.Classify(path));

        if (kind is XamlFileKind.Window or XamlFileKind.UserControl or XamlFileKind.Control)
            Assert.Equal(kind.ToString(), FormRoot.KindOf(root.Split(':')[^1]).ToString());
    }

    /// <summary>Не разметка — неизвестно: файла нет или это не XML.</summary>
    [AvaloniaFact]
    public async Task What_is_not_markup_is_unknown()
    {
        await using var studio = new XamlStudio();

        Assert.Equal(XamlFileKind.Unknown, studio.Documents.Classify(studio.PathOf("Missing.axaml")));
        Assert.Equal(XamlFileKind.Unknown, studio.Documents.Classify(studio.Write("Broken.axaml", "не разметка")));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RootOf(IXamlDesignView view) => new(Assert.IsType<UserControl>(view.Root));

    private static void SetWidth(XamlDocumentEditor editor, string width) =>
        editor.SetAttribute(editor.Document.Root!, XamlQualifiedName.Unprefixed("Width"), width);

    private static Markup.Xaml.Loader.XamlLiveDocument LiveOf(XamlStudio studio, CanonicalPath path) =>
        studio.Session.Documents.Single(entry => entry.Path == path).Live;

    /// <summary>Участник, записывающий, что видел.</summary>
    private sealed class Participant(Action release, Action restore) : IXamlDesignParticipant
    {
        public ValueTask ReleaseAsync(CancellationToken cancellationToken)
        {
            release();
            return ValueTask.CompletedTask;
        }

        public ValueTask RestoreAsync(CancellationToken cancellationToken)
        {
            restore();
            return ValueTask.CompletedTask;
        }
    }
}
