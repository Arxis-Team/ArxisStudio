namespace ArxisStudio.LocalHistory;

/// <summary>
/// Сколько хранить и что: срок, предел файла, предел всего.
/// </summary>
/// <remarks>
/// Умолчания — те, с которыми живёт IntelliJ: пять дней. Предел файла отсекает то, что историей
/// быть не должно, — базы, архивы, выход, случайно оказавшийся в папке проекта; такой файл
/// записывается правкой без содержимого. Предел всего снимает самые старые дни, пока история в него
/// не уложится: сегодняшний день не снимается никогда.
/// </remarks>
public sealed record LocalHistoryOptions
{
    /// <summary>Умолчания.</summary>
    public static LocalHistoryOptions Default { get; } = new();

    /// <summary>Сколько дней хранить, сегодняшний включительно. Меньше одного — один.</summary>
    public int Days { get; init; } = 5;

    /// <summary>Самый большой файл, чьё содержимое история хранит, в байтах.</summary>
    public long MaxFileBytes { get; init; } = 5L * 1024 * 1024;

    /// <summary>Сколько история может занимать всего, в байтах.</summary>
    public long MaxTotalBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>Часы: тесту нужны свои.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;
}

/// <summary>Папку истории уже держит другой процесс.</summary>
/// <remarks>
/// Две студии над одной папкой истории спорили бы за журнал и за последнее известное состояние, и
/// выигрывала бы та, что закроется последней. Вторая поэтому историю не пишет, а говорит об этом.
/// </remarks>
public sealed class LocalHistoryBusyException : IOException
{
    /// <summary>Заводит исключение.</summary>
    public LocalHistoryBusyException()
    {
    }

    /// <summary>Заводит исключение с сообщением.</summary>
    /// <param name="message">Сообщение.</param>
    public LocalHistoryBusyException(string message)
        : base(message)
    {
    }

    /// <summary>Заводит исключение с сообщением и причиной.</summary>
    /// <param name="message">Сообщение.</param>
    /// <param name="inner">Причина.</param>
    public LocalHistoryBusyException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
