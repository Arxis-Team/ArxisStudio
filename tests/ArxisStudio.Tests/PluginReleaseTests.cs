using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Уборка перед выгрузкой плагина: что студия отпускает и в каком порядке.
/// </summary>
/// <remarks>
/// Порядок здесь не вкусовщина. Работающая задача держит типы плагина, а через
/// них его контекст загрузки, — значит останавливать её надо раньше всего
/// прочего, иначе студия выгрузит плагин только на словах и сама же потом
/// пожалуется, что прежняя копия осталась в памяти.
/// </remarks>
public class PluginReleaseTests
{
    private static readonly TimeSpan Blink = TimeSpan.FromMilliseconds(80);

    /// <summary>Сперва задачи, потом документы, потом экран.</summary>
    [Fact]
    public async Task Tasks_go_first_then_documents_then_views()
    {
        var tasks = new StudioTaskRegistry();
        var order = new List<string>();

        var task = tasks.Start("hello", "Обход");

        // Задача уходит по отмене — как уходит всякая, что смотрит в токен.
        task.Changed += (_, _) =>
        {
            if (task.IsCancelling)
            {
                order.Add("tasks");
                tasks.Finish(task);
            }
        };

        var release = new PluginRelease(tasks, Blink)
        {
            Documents = _ =>
            {
                order.Add("documents");
                return Task.CompletedTask;
            },
            Views = _ => order.Add("views"),
        };

        Assert.True(await release.LetGoAsync("hello"), "задача не ушла");
        Assert.Equal(["tasks", "documents", "views"], order);
    }

    /// <summary>
    /// Упрямая задача названа по имени, но уборку не отменяет.
    /// </summary>
    /// <remarks>
    /// Работа, не смотрящая в токен, доработает до конца — это её беда. Оставить
    /// из-за неё панель плагина на экране значило бы наказать человека за чужую
    /// ошибку дважды.
    /// </remarks>
    [Fact]
    public async Task A_task_that_will_not_go_is_named_but_stops_nothing()
    {
        var tasks = new StudioTaskRegistry();
        var lingered = new List<string>();
        var order = new List<string>();

        // Заводим и не заканчиваем: ровно так ведёт себя работа, которая в
        // токен не смотрит.
        tasks.Start("hello", "Упрямая");

        var release = new PluginRelease(tasks, Blink)
        {
            Documents = _ =>
            {
                order.Add("documents");
                return Task.CompletedTask;
            },
            Views = _ => order.Add("views"),
        };

        release.Lingered += (_, id) => lingered.Add(id);

        Assert.False(await release.LetGoAsync("hello"), "задача вдруг ушла");
        Assert.Equal(["hello"], lingered);
        Assert.Equal(["documents", "views"], order);
    }

    /// <summary>Отпускают одного, а не всех: задача соседа остаётся работать.</summary>
    [Fact]
    public async Task Only_the_tasks_of_that_plugin_are_stopped()
    {
        var tasks = new StudioTaskRegistry();
        var mine = tasks.Start("hello", "Моя");
        var neighbour = tasks.Start("friend", "Соседняя");

        mine.Changed += (_, _) =>
        {
            if (mine.IsCancelling)
                tasks.Finish(mine);
        };

        var release = new PluginRelease(tasks, Blink);

        Assert.True(await release.LetGoAsync("hello"));

        Assert.Equal([neighbour], tasks.Running);
        Assert.False(neighbour.IsCancelling, "сосед получил отмену");
    }

    /// <summary>
    /// Без документов и без экрана отпускать тоже можно.
    /// </summary>
    /// <remarks>
    /// Уборка заводится раньше окна и его списков, и плагин может уйти прежде,
    /// чем ей дадут, что закрывать.
    /// </remarks>
    [Fact]
    public async Task Releasing_without_documents_or_views_is_fine()
    {
        var release = new PluginRelease(new StudioTaskRegistry(), Blink);

        Assert.True(await release.LetGoAsync("hello"));
    }
}
