using System.ComponentModel;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;

namespace ArxisStudio.ViewModels;

/// <summary>
/// Строка настройки плагина в экране настроек.
/// </summary>
/// <remarks>
/// Строится по манифесту, а не по коду плагина: студия читает манифесты, не
/// загружая сборок, и настройки должны быть видны и у плагина, который в этом
/// сеансе ни разу не поднимался.
/// <para>
/// Проектная настройка в этом экране только показывается: проекта здесь нет, и
/// записать её некуда. Прятать её при этом нельзя — человек искал бы её и не
/// нашёл, решив, что плагин её не объявляет.
/// </para>
/// </remarks>
/// <param name="pluginId">Чья настройка.</param>
/// <param name="pluginName">Как называется плагин.</param>
/// <param name="declared">Объявление из манифеста.</param>
/// <param name="store">Общее хранилище настроек.</param>
/// <param name="strings">Словари плагина: по ним переводится подпись.</param>
public sealed class PluginSettingRow(
    string pluginId,
    string pluginName,
    PluginSetting declared,
    PluginSettingsStore store,
    PluginStrings strings) : INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Как называется плагин, которому принадлежит настройка.</summary>
    public string PluginName => pluginName;

    /// <summary>Подпись настройки.</summary>
    public string Label => strings.Resolve(declared.Label);

    /// <summary>Ключ — его видно под подписью: по нему настройку правят в файле.</summary>
    public string Key => declared.Key;

    /// <summary>Настройка — переключатель.</summary>
    public bool IsToggle => declared.IsBool;

    /// <summary>Настройка — строка или число: показывается полем ввода.</summary>
    public bool IsText => !declared.IsBool;

    /// <summary>Настройка правится здесь.</summary>
    public bool IsEditable => !declared.IsProject;

    /// <summary>Пояснение к непрваимой строке; пусто у обычной.</summary>
    public string Note => declared.IsProject ? "проектная — правится при открытом проекте" : string.Empty;

    /// <summary>Значение переключателя.</summary>
    /// <remarks>
    /// Читается терпимо, потому что спрашивают его и о том, что переключателем
    /// не является. Привязка тумблера живёт в одном шаблоне с полем ввода и
    /// вычисляется у каждой строки — у скрытой тоже, — так что строковая
    /// настройка приходит сюда обычным путём. Без охраны студия писала бы на
    /// каждой такой строке ошибку привязки про тумблер, которого человек не
    /// видит.
    /// <para>
    /// Правило то же, что у <see cref="PluginSettings.Get{T}"/>: тип в файле
    /// правят руками, и разойтись с объявленным в манифесте он может всегда.
    /// </para>
    /// </remarks>
    public bool Flag
    {
        get => Read();
        set => Write(value);
    }

    /// <summary>Значение строкой — им же показывается число.</summary>
    public string Text
    {
        get => store.Read(pluginId, declared)?.ToString() ?? string.Empty;
        set => Write(declared.IsNumber && double.TryParse(value, out var number) ? number : value);
    }

    /// <summary>Флажок из хранилища; ложь, если там лежит не флажок.</summary>
    private bool Read()
    {
        var value = store.Read(pluginId, declared);

        if (value is null)
            return false;

        try
        {
            return value.GetValue<bool>();
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private void Write(object? value)
    {
        store.Write(pluginId, declared, value);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Flag)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
    }
}
