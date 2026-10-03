namespace ArxisStudio.Modules.Xaml;

/// <summary>
/// Шов службы XAML: из чего её собрать, если не из продуктовых частей.
/// </summary>
/// <remarks>
/// Модуль поднимает студия конструктором без аргументов, поэтому подменить части можно только через
/// контекст: служба спрашивает этот тип у <c>IStudioContext.GetService</c>, а тест кладёт его в словарь
/// служб. Тип внутренний: плагин его не назовёт, и в продукте ответ всегда null — берутся умолчания.
/// </remarks>
internal sealed class XamlServiceOptions
{
    /// <summary>Продуктовая сборка службы.</summary>
    public static XamlServiceOptions Default { get; } = new();

    /// <summary>
    /// Куда девать исключение чужого кода — подписчика аренды, участника замены; null — бросить заново
    /// в потоке интерфейса.
    /// </summary>
    /// <remarks>
    /// В продукте исключение уходит студии необработанным, и она приписывает его тому, чей код бросил.
    /// Тесту нужен сам сбой, а не упавший поток.
    /// </remarks>
    public Action<Exception>? SubscriberFailed { get; init; }

    /// <summary>
    /// Сколько поколение и профиль дизайна живут после того, как отпущен последний документ решения;
    /// <see cref="Timeout.InfiniteTimeSpan"/> — до закрытия решения.
    /// </summary>
    /// <remarks>
    /// Не сразу: вкладку закрыли и открыли снова — поколение то же, без новой оценки, сборки и
    /// загрузки. И не вечно: профиль — вторая оценка всего решения, а поколение — типы проекта в памяти,
    /// и держать их, когда дизайнер давно закрыт, незачем.
    /// </remarks>
    public TimeSpan IdleRelease { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Часы паузы перед сборкой дизайна и простоя.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
