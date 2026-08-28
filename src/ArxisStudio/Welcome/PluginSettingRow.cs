using System.ComponentModel;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;

namespace ArxisStudio.Welcome;

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
public sealed class PluginSettingRow(
    string pluginId,
    string pluginName,
    PluginSetting declared,
    PluginSettingsStore store) : INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Как называется плагин, которому принадлежит настройка.</summary>
    public string PluginName => pluginName;

    /// <summary>Подпись настройки.</summary>
    public string Label => declared.Label;

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
    public bool Flag
    {
        get => store.Read(pluginId, declared)?.GetValue<bool>() ?? false;
        set => Write(value);
    }

    /// <summary>Значение строкой — им же показывается число.</summary>
    public string Text
    {
        get => store.Read(pluginId, declared)?.ToString() ?? string.Empty;
        set => Write(declared.IsNumber && double.TryParse(value, out var number) ? number : value);
    }

    private void Write(object? value)
    {
        store.Write(pluginId, declared, value);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Flag)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
    }
}
