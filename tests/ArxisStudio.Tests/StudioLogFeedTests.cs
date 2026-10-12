using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Журнал глазами расширения: обработчик его события зовётся через шов на счёт хозяина и уходит
/// вместе с ним.
/// </summary>
/// <remarks>
/// Журнал выдаёт фабрика контекстов, как выдаёт его студия: расширения здесь — записи без сборок, а
/// поднимать ради подписки на событие сборку незачем.
/// </remarks>
public class StudioLogFeedTests
{
    /// <summary>
    /// Бросивший обработчик расширения не бросает в того, кто пишет, остальные запись слышат, а сбой
    /// засчитан ему.
    /// </summary>
    [Fact]
    public void A_failing_log_handler_is_charged_to_its_plugin_and_the_others_still_hear()
    {
        var log = new StudioLog();
        var guard = new PluginGuard();
        var factory = Factory(log, guard);
        var failures = new List<PluginFailure>();
        var heard = 0;

        guard.Failed += (_, failure) => failures.Add(failure);

        Feed(factory, "arxis.broken").Changed += (_, _) => throw new InvalidOperationException("обработчик сломан");
        Feed(factory, "arxis.reader").Changed += (_, _) => heard++;

        log.Write(StudioLogLevel.Info, "Проверка", "запись");

        Assert.Equal(1, heard);
        Assert.Equal(["arxis.broken"], failures.Select(failure => failure.PluginId));
    }

    /// <summary>
    /// Шов докладывает о сбое в тот же журнал, и обработчик, бросающий на каждой записи, слышит доклад о себе: круг
    /// кончается третьим сбоем — расширение отключено, а запись дошла.
    /// </summary>
    [Fact]
    public void A_handler_failing_on_every_record_is_disabled_by_the_reports_of_its_own_failures()
    {
        var log = new StudioLog();
        var guard = new PluginGuard();
        var factory = Factory(log, guard);

        // Как у студии: доклад шва — запись того же журнала.
        guard.Failed += (_, failure) => log.Write(StudioLogLevel.Error, "Plugins", failure.Message);

        Feed(factory, "arxis.broken").Changed += (_, _) => throw new InvalidOperationException("обработчик сломан");

        log.Write(StudioLogLevel.Info, "Проверка", "запись");

        Assert.True(guard.IsFaulty("arxis.broken"), "обработчик, бросающий на каждой записи, не отключён");
        Assert.Equal(PluginGuard.FailureLimit + 1, log.Records.Count);
    }

    /// <summary>
    /// Расширение, ушедшее с подпиской, журнала больше не слышит, как и отписавшееся по правилам; чужие
    /// подписки остаются.
    /// </summary>
    [Fact]
    public void A_plugin_that_left_or_unsubscribed_hears_the_log_no_more()
    {
        var log = new StudioLog();
        var factory = Factory(log, new PluginGuard());
        var polite = Feed(factory, "arxis.polite");
        var heard = new List<string>();

        EventHandler leaving = (_, _) => heard.Add("arxis.polite");

        Feed(factory, "arxis.forgetful").Changed += (_, _) => heard.Add("arxis.forgetful");
        polite.Changed += leaving;
        Feed(factory, "arxis.reader").Changed += (_, _) => heard.Add("arxis.reader");

        polite.Changed -= leaving;
        factory.Forget("arxis.forgetful");
        log.Write(StudioLogLevel.Info, "Проверка", "запись");

        Assert.Equal(["arxis.reader"], heard);
    }

    /// <summary>Фабрика, которая раздаёт общий журнал, как главное окно, — над файлом настроек, которого нет.</summary>
    private static StudioContextFactory Factory(StudioLog log, PluginGuard guard) =>
        new(
            log,
            new StudioCommands(),
            null,
            new Dictionary<Type, object> { [typeof(IStudioLogFeed)] = log },
            settings: new PluginSettingsStore(userFile: Path.Combine(Path.GetTempPath(), $"arxis-log-feed-{Guid.NewGuid():N}.json")),
            guard: guard);

    /// <summary>Журнал, выданный расширению с этим идентификатором.</summary>
    private static IStudioLogFeed Feed(StudioContextFactory factory, string id)
    {
        var context = factory.Create(new InstalledPlugin(id, new PluginManifest { Id = id, Name = id }, null, IsEnabled: true));

        return Assert.IsType<IStudioLogFeed>(context.GetService<IStudioLogFeed>(), exactMatch: false);
    }
}
