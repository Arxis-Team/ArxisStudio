using ArxisStudio.LocalHistory;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Хранилище локальной истории: содержимое по адресу, журнал по дням, известное состояние и срок.
/// </summary>
/// <remarks>
/// Каждый тест — своя временная папка: история человека лежит в его машинной папке, и тест её не
/// трогает.
/// </remarks>
public sealed class LocalHistoryStoreTests : IDisposable
{
    private readonly string _root = TempFolder.Create("history");

    private string History => Path.Combine(_root, "history");

    public void Dispose()
    {
        TempFolder.Erase(_root);
    }

    /// <summary>Одинаковое содержимое хранится одним объектом, с какого бы файла его ни сняли.</summary>
    [Fact]
    public void The_same_content_is_stored_once()
    {
        using var store = LocalHistoryStore.Open(History);

        var first = File("a.cs", "class A { }");
        var second = File("b.cs", "class A { }");

        var one = store.Capture(first);
        var two = store.Capture(second);

        Assert.NotNull(one?.Content);
        Assert.Equal(one.Content, two?.Content);
        Assert.Single(Objects());
        Assert.Equal("class A { }"u8.ToArray(), store.Read(one.Content.Value));
    }

    /// <summary>
    /// Оборванная последняя строка журнала стоит одного действия, а не всего журнала.
    /// </summary>
    /// <remarks>
    /// Процесс, упавший посреди записи, оставляет половину строки. Журнал, который из-за неё не
    /// читается целиком, потерял бы всю историю ради одного недописанного действия.
    /// </remarks>
    [Fact]
    public void A_torn_last_line_costs_only_itself()
    {
        using (var store = LocalHistoryStore.Open(History))
        {
            store.Record("Первое", HistoryOrigin.Studio, [Created("a.cs")]);
            store.Record("Второе", HistoryOrigin.External, [Created("b.cs")]);
        }

        var journal = Directory.GetFiles(Path.Combine(History, "journal")).Single();

        System.IO.File.AppendAllText(journal, "{\"id\":3,\"time\":\"2026-09-2");

        using (var reopened = LocalHistoryStore.Open(History))
        {
            Assert.Equal(["Первое", "Второе"], reopened.Actions.Select(action => action.Label));
            Assert.Equal(3, reopened.Record("Третье", HistoryOrigin.Studio, [Created("c.cs")]).Id);
        }

        // Дописанное после обрывка не слилось с ним: третье действие читается.
        using var again = LocalHistoryStore.Open(History);

        Assert.Equal(["Первое", "Второе", "Третье"], again.Actions.Select(action => action.Label));
    }

    /// <summary>Действие и известное состояние переживают закрытие истории.</summary>
    [Fact]
    public void Actions_and_the_known_state_outlive_the_store()
    {
        var path = File("a.cs", "было");
        HistoryFileState state;
        HistoryAction action;

        using (var store = LocalHistoryStore.Open(History))
        {
            state = store.Capture(path)!;
            store.Learn(path, state);
            action = store.Record("Правка", HistoryOrigin.External,
            [
                new HistoryChange { Kind = HistoryChangeKind.Modified, Path = path, Before = state.Content, After = state.Content },
            ]);
        }

        using var reopened = LocalHistoryStore.Open(History);
        var read = Assert.Single(reopened.Actions);

        Assert.Equal(action.Id, read.Id);
        Assert.Equal(HistoryOrigin.External, read.Origin);
        Assert.Equal(action.Changes, read.Changes);
        Assert.Equal(state, reopened.Known(path));
        Assert.Equal(state, reopened.Known(path.ToUpperInvariant()));
    }

    /// <summary>
    /// Дни старше срока снимаются, а содержимое, на которое больше никто не ссылается, убирается.
    /// </summary>
    /// <remarks>
    /// Содержимое, на которое ссылается известное состояние, остаётся, как бы старо ни было: без
    /// него следующая внешняя правка файла не вернулась бы к тому, что было до неё.
    /// </remarks>
    [Fact]
    public void Days_past_the_term_go_and_take_their_orphans()
    {
        var clock = new Clock(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        var old = File("old.cs", "старое");
        var fresh = File("fresh.cs", "свежее");
        var kept = File("kept.cs", "известное");

        using var store = LocalHistoryStore.Open(History, new LocalHistoryOptions { Days = 5, Time = clock });

        var oldState = store.Capture(old)!;
        var keptState = store.Capture(kept)!;

        store.Record("Давнее", HistoryOrigin.Studio, [Deleted(old, oldState)]);
        store.Learn(kept, keptState);

        clock.Now = clock.Now.AddDays(10);

        var freshState = store.Capture(fresh)!;

        store.Record("Сегодняшнее", HistoryOrigin.Studio, [Deleted(fresh, freshState)]);
        Age(Objects());

        var pruned = store.Prune();

        Assert.Equal(1, pruned.Days);
        Assert.Equal(["Сегодняшнее"], store.Actions.Select(action => action.Label));
        Assert.Null(store.Read(oldState.Content!.Value));
        Assert.NotNull(store.Read(freshState.Content!.Value));
        Assert.NotNull(store.Read(keptState.Content!.Value));
    }

    /// <summary>Предел объёма снимает старые дни, но сегодняшний не снимает никогда.</summary>
    [Fact]
    public void The_size_limit_drops_old_days_but_never_today()
    {
        var clock = new Clock(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

        using var store = LocalHistoryStore.Open(History, new LocalHistoryOptions { MaxTotalBytes = 1, Time = clock });

        store.Record("Позавчера", HistoryOrigin.Studio, [Created("a.cs")]);
        clock.Now = clock.Now.AddDays(1);
        store.Record("Вчера", HistoryOrigin.Studio, [Created("b.cs")]);
        clock.Now = clock.Now.AddDays(1);
        store.Record("Сегодня", HistoryOrigin.Studio, [Created("c.cs")]);

        store.Prune();

        Assert.Equal(["Сегодня"], store.Actions.Select(action => action.Label));
    }

    /// <summary>
    /// Правки файла идут от новой к старой сквозь его переименование и переезд папки.
    /// </summary>
    /// <remarks>
    /// Иначе история переименованного файла начиналась бы с переименования, а всё, что было с ним
    /// под прежним именем, осталось бы у имени, которого больше нет.
    /// </remarks>
    [Fact]
    public void Revisions_follow_a_file_through_renames_and_folder_moves()
    {
        var a = Path.Combine(_root, "a");
        var b = Path.Combine(_root, "b");

        using var store = LocalHistoryStore.Open(History);

        store.Record("Создан", HistoryOrigin.Studio, [Created(Path.Combine(a, "x.cs"))]);
        store.Record("Правка", HistoryOrigin.External, [new HistoryChange { Kind = HistoryChangeKind.Modified, Path = Path.Combine(a, "x.cs") }]);
        store.Record("Чужой файл", HistoryOrigin.External, [Created(Path.Combine(a, "z.cs"))]);
        store.Record("Переименование", HistoryOrigin.Studio,
            [new HistoryChange { Kind = HistoryChangeKind.Moved, Path = Path.Combine(a, "y.cs"), From = Path.Combine(a, "x.cs") }]);
        store.Record("Перенос папки", HistoryOrigin.Studio,
            [new HistoryChange { Kind = HistoryChangeKind.Moved, Path = b, From = a, IsDirectory = true }]);
        store.Record("Ещё правка", HistoryOrigin.External, [new HistoryChange { Kind = HistoryChangeKind.Modified, Path = Path.Combine(b, "y.cs") }]);

        var revisions = store.Revisions(Path.Combine(b, "Y.CS"));

        Assert.Equal(
            ["Ещё правка", "Перенос папки", "Переименование", "Правка", "Создан"],
            revisions.Select(revision => revision.Action.Label));
    }

    /// <summary>
    /// Метка и отмена переживают хранилище: метка — со своей папкой, отмена — с номером отменённого.
    /// </summary>
    /// <remarks>
    /// У метки правок нет, и прежний читатель журнала принял бы её пустой список за порчу строки —
    /// а метка, пропавшая при перезапуске, не дожила бы до того, ради чего её ставили.
    /// </remarks>
    [Fact]
    public void Labels_and_undo_links_outlive_the_store()
    {
        var folder = Path.Combine(_root, "solution");
        long undone;

        using (var store = LocalHistoryStore.Open(History))
        {
            undone = store.Record("Создан", HistoryOrigin.Studio, [Created(Path.Combine(folder, "a.cs"))]).Id;
            store.Record("Отмена: Создан", HistoryOrigin.Studio, [Created(Path.Combine(folder, "b.cs"))], undoes: undone);
            store.PutLabel("до переделки", folder);
        }

        using var reopened = LocalHistoryStore.Open(History);
        var actions = reopened.Actions;

        Assert.Equal(3, actions.Length);
        Assert.Equal(undone, actions[1].Undoes);
        Assert.True(actions[2].IsLabel, "метка прочиталась правкой");
        Assert.Equal(folder, actions[2].Scope);
        Assert.Equal(actions[1], reopened.Find(actions[1].Id));
        Assert.Null(reopened.Find(actions[2].Id + 1));
    }

    /// <summary>
    /// Отменённым считается то, чью отмену не отменили: отмена отмены возвращает действие в силу.
    /// </summary>
    [Fact]
    public void An_action_is_undone_only_while_its_undo_stands()
    {
        using var store = LocalHistoryStore.Open(History);

        var action = store.Record("Переименование", HistoryOrigin.Studio, [Created("a.cs")]);
        var undo = store.Record("Отмена: Переименование", HistoryOrigin.Studio, [Created("b.cs")], undoes: action.Id);

        Assert.Equal([action.Id], store.Undone());

        var redo = store.Record("Отмена: Отмена: Переименование", HistoryOrigin.Studio, [Created("c.cs")], undoes: undo.Id);

        Assert.Equal([undo.Id], store.Undone());

        store.Record("Отмена: Отмена: Отмена: Переименование", HistoryOrigin.Studio, [Created("d.cs")], undoes: redo.Id);

        Assert.Equal([action.Id, redo.Id], store.Undone().Order());
    }

    /// <summary>
    /// История папки — всё, что в ней было, сквозь переезд самой папки; метку видно под её папкой.
    /// </summary>
    [Fact]
    public void The_history_of_a_folder_follows_the_folder_and_shows_labels_put_above_it()
    {
        var a = Path.Combine(_root, "a");
        var b = Path.Combine(_root, "b");

        using var store = LocalHistoryStore.Open(History);

        store.Record("Создан", HistoryOrigin.Studio, [Created(Path.Combine(a, "x.cs"))]);
        store.Record("Вложенный", HistoryOrigin.Studio, [Created(Path.Combine(a, "sub", "w.cs"))]);
        store.Record("Рядом", HistoryOrigin.Studio, [Created(Path.Combine(_root, "c", "y.cs"))]);
        store.Record("Приехал", HistoryOrigin.Studio,
            [new HistoryChange { Kind = HistoryChangeKind.Moved, Path = Path.Combine(a, "z.cs"), From = Path.Combine(_root, "z.cs") }]);
        store.PutLabel("метка решения", _root);
        store.PutLabel("метка соседа", Path.Combine(_root, "c"));
        store.Record("Перенос папки", HistoryOrigin.Studio,
            [new HistoryChange { Kind = HistoryChangeKind.Moved, Path = b, From = a, IsDirectory = true }]);
        store.Record("Удалён", HistoryOrigin.External, [new HistoryChange { Kind = HistoryChangeKind.Deleted, Path = Path.Combine(b, "x.cs") }]);

        Assert.Equal(
            ["Удалён", "Перенос папки", "Приехал", "Вложенный", "Создан"],
            store.RevisionsUnder(b).Select(revision => revision.Action.Label));

        // Переехала не сама папка, а та, в которой она лежит: и это переезд вложенной.
        Assert.Equal(
            ["Перенос папки", "Вложенный"],
            store.RevisionsUnder(Path.Combine(b, "sub")).Select(revision => revision.Action.Label));
        Assert.Equal(["метка решения"], store.Labels(Path.Combine(b, "x.cs")).Select(label => label.Label));
    }

    /// <summary>
    /// Файл больше предела снимается без содержимого, в хранилище не ложится и не читается вовсе.
    /// </summary>
    /// <remarks>
    /// Не читается — не мелочь: база или архив в сотни мегабайт, прочитанный на каждую свою запись,
    /// держал бы очередь истории и память ради содержимого, которое всё равно не сохранится. Файл
    /// здесь держат открытым без права чтения: полезь снятие его читать, состояния бы не было.
    /// </remarks>
    [Fact]
    public void A_file_over_the_limit_is_captured_without_content()
    {
        using var store = LocalHistoryStore.Open(History, new LocalHistoryOptions { MaxFileBytes = 10 });

        var big = File("big.bin", new string('x', 20));

        using var held = new FileStream(big, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var state = store.Capture(big);

        Assert.NotNull(state);
        Assert.True(state.TooLarge);
        Assert.Equal(20, state.Size);
        Assert.Empty(Objects());
    }

    /// <summary>Папку истории держит одна студия; вторая узнаёт об этом при открытии.</summary>
    [Fact]
    public void A_second_holder_is_turned_away()
    {
        using var first = LocalHistoryStore.Open(History);

        Assert.Throws<LocalHistoryBusyException>(() => LocalHistoryStore.Open(History));
    }

    /// <summary>
    /// Содержимое, не совпавшее со своим адресом, человеку не отдаётся.
    /// </summary>
    /// <remarks>Испорченный на диске объект вернул бы чужие байты под видом прежней версии файла.</remarks>
    [Fact]
    public void Content_that_no_longer_matches_its_address_is_not_returned()
    {
        using var store = LocalHistoryStore.Open(History);

        var state = store.Capture(File("a.cs", "class A { }"))!;
        var id = state.Content!.Value;
        var other = store.Capture(File("b.cs", "class B { }"))!.Content!.Value;
        var target = Objects().Single(file => file.EndsWith(id.Value[2..], StringComparison.Ordinal));
        var source = Objects().Single(file => file.EndsWith(other.Value[2..], StringComparison.Ordinal));

        System.IO.File.Copy(source, target, overwrite: true);

        Assert.Null(store.Read(id));
    }

    private string File(string name, string text)
    {
        var path = Path.Combine(_root, name);

        System.IO.File.WriteAllText(path, text);

        return path;
    }

    private HistoryChange Created(string path) => new()
    {
        Kind = HistoryChangeKind.Created,
        Path = Path.IsPathRooted(path) ? path : Path.Combine(_root, path),
    };

    private static HistoryChange Deleted(string path, HistoryFileState state) => new()
    {
        Kind = HistoryChangeKind.Deleted,
        Path = path,
        Before = state.Content,
    };

    private string[] Objects()
    {
        var objects = Path.Combine(History, "objects");

        return Directory.Exists(objects) ? Directory.GetFiles(objects, "*", SearchOption.AllDirectories) : [];
    }

    /// <summary>Состаривает объекты за льготный срок очистки: свежий объект она не трогает.</summary>
    private static void Age(IEnumerable<string> files)
    {
        foreach (var file in files)
            System.IO.File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-1));
    }

    /// <summary>Часы, которые тест переводит сам.</summary>
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
