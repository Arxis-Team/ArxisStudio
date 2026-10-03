using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Несохранённое и закрытие документа (SDK 7.14): точка на вкладке, вето документа, один вопрос
/// человеку на все несохранённые и сохранение перед перезапуском.
/// </summary>
/// <remarks>
/// Раскладка настоящая, а не заглушка: точку несёт её вкладка, и вкладку закрывает она. Вопрос
/// человеку подменён ответом теста — модальный диалог в безголовом прогоне ждал бы вечно.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class DocumentClosingTests
{
    private const string Form = @"C:\проект\Окно.axaml";
    private const string Scheme = @"C:\проект\Схема.axaml";

    /// <summary>Правка ставит точку на вкладке, сохранение снимает.</summary>
    [AvaloniaFact]
    public async Task An_edit_puts_a_dot_on_the_tab_and_a_save_takes_it_away()
    {
        var studio = Studio();

        await studio.Documents.OpenAsync(Form);

        var view = View(studio, Form);
        var tab = Tab(studio, "Окно.axaml");

        Assert.False(tab.IsModified, "чистый документ с точкой");

        view.Edit();
        Assert.True(tab.IsModified, "правка не поставила точку");

        await studio.Documents.SaveShownAsync();

        Assert.Equal(1, view.Saves);
        Assert.False(tab.IsModified, "сохранение не сняло точку");
    }

    /// <summary>Документ, отказавшийся закрыться, оставляет вкладку — и сам остаётся жив.</summary>
    [AvaloniaFact]
    public async Task A_document_that_refuses_keeps_its_tab()
    {
        var studio = Studio();

        await studio.Documents.OpenAsync(Form);

        var view = View(studio, Form);

        view.Refuses = true;
        studio.Dock.Shut(StudioDocuments.Name(Form));
        Dispatcher.UIThread.RunJobs();

        Assert.Single(studio.Documents.Opened);
        Assert.False(view.Disposed, "отказавший документ отпустили");
        Assert.Equal([DocumentCloseReason.Tab], view.Asked);
    }

    /// <summary>
    /// О несохранённом спрашивают один раз на все документы, и ответ решает: «Отмена» оставляет всё,
    /// «Не сохранять» закрывает как есть, «Сохранить» сохраняет каждый.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(StudioSaveChoice.Cancel, false, 0)]
    [InlineData(StudioSaveChoice.Discard, true, 0)]
    [InlineData(StudioSaveChoice.Save, true, 1)]
    public async Task Unsaved_documents_are_asked_about_once_and_the_answer_rules(StudioSaveChoice answer, bool closes, int saves)
    {
        var studio = Studio();
        var asked = new List<IReadOnlyList<string>>();

        studio.Documents.Ask = names =>
        {
            asked.Add(names);
            return Task.FromResult(answer);
        };

        await studio.Documents.OpenAsync(Form);
        await studio.Documents.OpenAsync(Scheme);

        View(studio, Form).Edit();
        View(studio, Scheme).Edit();

        var agreed = await studio.Documents.ConfirmAsync([.. studio.Documents.Opened], DocumentCloseReason.Window);

        Assert.Equal(closes, agreed);
        Assert.Equal(["Окно.axaml", "Схема.axaml"], Assert.Single(asked));
        Assert.Equal(saves, View(studio, Form).Saves);
        Assert.Equal(saves, View(studio, Scheme).Saves);
    }

    /// <summary>
    /// Перед перезапуском несохранённое сохраняется без вопроса: новая копия вернёт вкладки, и вернуть
    /// их надо с тем, что в них было.
    /// </summary>
    [AvaloniaFact]
    public async Task A_restart_saves_without_asking()
    {
        var studio = Studio();
        var asked = false;

        studio.Documents.Ask = _ =>
        {
            asked = true;
            return Task.FromResult(StudioSaveChoice.Cancel);
        };

        await studio.Documents.OpenAsync(Form);

        var view = View(studio, Form);

        view.Edit();

        Assert.True(await studio.Documents.ConfirmAsync([.. studio.Documents.Opened], DocumentCloseReason.Restart));
        Assert.False(asked, "перезапуск спросил человека");
        Assert.Equal(1, view.Saves);
        Assert.Equal([DocumentCloseReason.Restart], view.Asked);
    }

    /// <summary>
    /// Редактор с автосохранением сохраняет в своём ответе на закрытие — и человека о нём не спрашивают.
    /// </summary>
    [AvaloniaFact]
    public async Task An_autosaving_document_saves_when_asked_and_nobody_is_bothered()
    {
        var studio = Studio();
        var asked = false;

        studio.Documents.Ask = _ =>
        {
            asked = true;
            return Task.FromResult(StudioSaveChoice.Cancel);
        };

        await studio.Documents.OpenAsync(Form);

        var view = View(studio, Form);

        view.Edit();
        view.SavesOnClose = true;

        Assert.True(await studio.Documents.ConfirmAsync([.. studio.Documents.Opened], DocumentCloseReason.Tab));
        Assert.False(asked, "о сохранённом в ответе спросили человека");
    }

    /// <summary>
    /// Упавшее сохранение останавливает закрытие, называет документ человеку и приписывается плагину;
    /// упавший ответ на закрытие — не отказ: сломанный документ не держит окно.
    /// </summary>
    [AvaloniaFact]
    public async Task A_falling_save_stops_the_close_and_a_falling_answer_does_not()
    {
        var guard = new PluginGuard();
        var failures = new List<PluginFailure>();

        guard.Failed += (_, failure) => failures.Add(failure);

        var studio = Studio(guard);

        studio.Documents.Ask = _ => Task.FromResult(StudioSaveChoice.Save);

        await studio.Documents.OpenAsync(Form);

        var view = View(studio, Form);

        view.Edit();
        view.FallsOnSave = true;

        Assert.False(await studio.Documents.ConfirmAsync([.. studio.Documents.Opened], DocumentCloseReason.Window));
        Assert.Equal(string.Format(Localizer.Instance["documents.save.failed"], "Окно.axaml"), studio.Status.Last);
        Assert.Equal("arxis.designer", Assert.Single(failures).PluginId);

        view.FallsOnSave = false;
        view.FallsOnAsking = true;

        Assert.True(await studio.Documents.ConfirmAsync([.. studio.Documents.Opened], DocumentCloseReason.Window));
    }

    /// <summary>
    /// Вопрос перед закрытием окна нужен, только когда есть несохранённое или документ, решающий о себе.
    /// </summary>
    /// <remarks>
    /// Иначе закрытие отменялось бы ради вопроса, на который заранее известен ответ.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_window_asks_only_when_someone_may_object()
    {
        var plain = Studio(editor: new PlainEditor());

        await plain.Documents.OpenAsync(Form);

        Assert.False(plain.Documents.NeedsConfirmation, "документ, согласный всегда, требует вопроса");

        var studio = Studio();

        await studio.Documents.OpenAsync(Form);

        Assert.True(studio.Documents.NeedsConfirmation, "документ, решающий о закрытии сам, вопроса не требует");

        var edited = Studio(editor: new PlainEditor());

        await edited.Documents.OpenAsync(Form);
        ((PlainView)edited.Documents.Opened[0].View).Edit();

        Assert.True(edited.Documents.NeedsConfirmation, "несохранённое не требует вопроса");
    }

    /// <summary>Цель каретки документа становится целью его вкладки в раскладке.</summary>
    [AvaloniaFact]
    public async Task The_focus_target_of_a_document_is_where_its_caret_goes()
    {
        var studio = Studio();

        await studio.Documents.OpenAsync(Form);

        var view = View(studio, Form);

        Assert.Same(view.Target, DockFocus.GetTarget(view.Content));
    }

    /// <summary>
    /// Окно, закрываемое с документами, которые не согласились, остаётся открытым; согласились —
    /// закрывается заново, уже без вопроса.
    /// </summary>
    [AvaloniaFact]
    public void A_window_closes_only_after_its_documents_agree()
    {
        var answer = false;
        var asked = 0;
        var farewells = 0;
        var window = new Window { Width = 400, Height = 300 };

        StudioClosing.Attach(
            window,
            () => true,
            () =>
            {
                asked++;
                return Task.FromResult(answer);
            },
            () => farewells++);

        window.Show();
        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsVisible, "окно закрылось, хотя документы отказали");
        Assert.Equal(0, farewells);

        answer = true;
        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsVisible, "согласие не закрыло окно");
        Assert.Equal(2, asked);
        Assert.Equal(1, farewells);
    }

    /// <summary>Окно без нужды в вопросе закрывается сразу, как прежде.</summary>
    [AvaloniaFact]
    public void A_window_with_nothing_to_ask_closes_at_once()
    {
        var asked = false;
        var window = new Window { Width = 400, Height = 300 };

        StudioClosing.Attach(window, () => false, () =>
        {
            asked = true;
            return Task.FromResult(true);
        }, () => { });

        window.Show();
        window.Close();

        Assert.False(window.IsVisible, "окно без вопроса не закрылось сразу");
        Assert.False(asked, "спросили, хотя спрашивать было нечего");
    }

    private static TestStudio Studio(PluginGuard? guard = null, DocumentEditor? editor = null)
    {
        var dockView = new DockView();
        var dock = new StudioDock(dockView);

        new Window { Content = dockView, Width = 1200, Height = 800 }.Show();
        Dispatcher.UIThread.RunJobs();

        var status = new StatusProbe();
        var opened = editor ?? new EditableEditor();
        var documents = new StudioDocuments(dock, _ => new EditorMatch(opened, "arxis.designer"), status, guard);

        return new TestStudio(documents, dock, dockView, status);
    }

    private static EditableView View(TestStudio studio, string path) =>
        Assert.IsType<EditableView>(studio.Documents.Opened.Single(document => document.Path == path).View);

    private static AxTabItem Tab(TestStudio studio, string title)
    {
        Dispatcher.UIThread.RunJobs();

        return studio.View.GetVisualDescendants().OfType<AxTabItem>().Single(tab => Equals(tab.Content, title));
    }

    /// <summary>Служба документов над живой раскладкой.</summary>
    private sealed record TestStudio(StudioDocuments Documents, StudioDock Dock, DockView View, StatusProbe Status);

    /// <summary>Редактор правимых документов.</summary>
    private sealed class EditableEditor : DocumentEditor
    {
        public override bool CanOpen(string filePath) => true;

        public override Task<(DocumentView? View, string? Error)> OpenAsync(string filePath) =>
            Task.FromResult<(DocumentView?, string?)>((new EditableView(filePath), null));
    }

    /// <summary>Документ, который правят, сохраняют и спрашивают о закрытии.</summary>
    private sealed class EditableView(string filePath) : DocumentView
    {
        public override Control Content { get; } = new StackPanel { Children = { new Button(), new TextBox() } };

        public override string Title { get; } = Path.GetFileName(filePath);

        public override Control? FocusTarget => Target;

        /// <summary>Цель каретки: поле, а не первая кнопка.</summary>
        public Control Target => ((StackPanel)Content).Children[1];

        public int Saves { get; private set; }

        public bool Refuses { get; set; }

        public bool SavesOnClose { get; set; }

        public bool FallsOnSave { get; set; }

        public bool FallsOnAsking { get; set; }

        public bool Disposed { get; private set; }

        public List<DocumentCloseReason> Asked { get; } = [];

        public void Edit() => SetModified(true);

        public override Task<bool> SaveAsync()
        {
            if (FallsOnSave)
                throw new IOException("диск отказал");

            Saves++;
            SetModified(false);

            return Task.FromResult(true);
        }

        public override async ValueTask<bool> CanCloseAsync(DocumentCloseReason reason)
        {
            Asked.Add(reason);

            if (FallsOnAsking)
                throw new InvalidOperationException("вопрос упал");

            if (SavesOnClose && IsModified)
                await SaveAsync();

            return !Refuses;
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Редактор документов, согласных закрыться всегда.</summary>
    private sealed class PlainEditor : DocumentEditor
    {
        public override bool CanOpen(string filePath) => true;

        public override Task<(DocumentView? View, string? Error)> OpenAsync(string filePath) =>
            Task.FromResult<(DocumentView?, string?)>((new PlainView(), null));
    }

    /// <summary>Документ без своего ответа на закрытие.</summary>
    private sealed class PlainView : DocumentView
    {
        public override Control Content { get; } = new Border();

        public override string Title => "Простой";

        public void Edit() => SetModified(true);
    }
}
