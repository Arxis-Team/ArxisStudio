using System.ComponentModel;
using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Sample;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Корень разметки для проверки: важен только его тип и его сборка.
/// </summary>
/// <remarks>
/// Верхнего уровня, а не вложенный в тест: вложенный тип пишется в разметке
/// через плюс и разрешению по <c>using:</c> не поддаётся вовсе.
/// </remarks>
public class ProbeView : AxUserControl;

/// <summary>
/// Строки расширения в разметке: <c>{Text ключ}</c>.
/// </summary>
/// <remarks>
/// Разметка знает только свою сборку, а какой словарь ей отвечает — знает
/// хозяин, поднявший расширение. Проверяется здесь именно этот шов: без него
/// подпись в панели плагина осталась бы либо на языке автора, либо в коде.
/// </remarks>
public class StudioTextTests
{
    private const string Url = "https://github.com/Arxis-Team/ArxisStudio";
    private const string Avalonia = "https://github.com/avaloniaui";

    /// <summary>
    /// Подпись берётся из словаря той сборки, из которой пришла разметка, и
    /// переживает смену языка.
    /// </summary>
    [AvaloniaFact]
    public void A_caption_comes_from_the_dictionary_of_its_own_assembly()
    {
        var words = new Words();

        words.Set("panel.hint", "Подсказка");
        StudioText.Remember(typeof(ProbeView).Assembly, words);

        var view = AvaloniaRuntimeXamlLoader.Parse<ProbeView>(
            $$"""
              <ProbeView xmlns="using:ArxisStudio.Tests" xmlns:ax="{{Url}}" xmlns:a="{{Avalonia}}">
                <a:TextBlock Text="{ax:Text panel.hint}"/>
              </ProbeView>
              """);

        var text = Assert.IsType<TextBlock>(view.Content);

        Assert.Equal("Подсказка", text.Text);

        // Смена языка обязана перерисовать уже показанное — как в самой студии.
        words.Set("panel.hint", "Hint");

        Assert.Equal("Hint", text.Text);
    }

    /// <summary>
    /// Сборке без словаря отдаётся ключ, а не пустое место.
    /// </summary>
    /// <remarks>
    /// Так бывает в предпросмотре и в разметке, которую пишет не расширение.
    /// Пропуск виден и не притворяется текстом — ровно так же ведёт себя сам
    /// словарь для ключа без перевода.
    /// </remarks>
    [AvaloniaFact]
    public void An_assembly_without_a_dictionary_gets_the_key_back()
    {
        var view = AvaloniaRuntimeXamlLoader.Parse<AxUserControl>(
            $$"""
              <AxUserControl xmlns="{{Url}}" xmlns:a="{{Avalonia}}">
                <a:TextBlock Text="{Text panel.hint}"/>
              </AxUserControl>
              """);

        var text = Assert.IsType<TextBlock>(view.Content);

        Assert.Equal("!panel.hint!", text.Text);
    }

    /// <summary>
    /// Связь «сборка → словарь» кладёт хозяин, поднимая расширение.
    /// </summary>
    /// <remarks>
    /// Путь подъёма у плагина и у встроенного модуля один, поэтому и связь
    /// одна: панель модуля, написанная разметкой, берёт строки так же, как
    /// панель плагина.
    /// </remarks>
    [Fact]
    public void The_host_remembers_the_dictionary_when_it_raises_an_extension()
    {
        using var host = new PluginHost(
            new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

        var loaded = host.LoadBuiltIn(typeof(SampleModule).Assembly);

        Assert.True(loaded.IsLoaded, loaded.Error);
        Assert.NotNull(StudioText.Of(typeof(SampleModule).Assembly));
    }

    /// <summary>Словарь, который умеет менять текст под уже поставленной привязкой.</summary>
    private sealed class Words : IStudioStrings
    {
        private readonly Dictionary<string, Cell> _cells = new(StringComparer.Ordinal);

        public string this[string key] => Of(key).Value;

        public string Language => "ru";

        public BindingBase Text(string key) =>
            new Binding(nameof(Cell.Value)) { Source = Of(key), Mode = BindingMode.OneWay };

        public void Set(string key, string text) => Of(key).Value = text;

        private Cell Of(string key) =>
            _cells.TryGetValue(key, out var cell) ? cell : _cells[key] = new Cell();

        private sealed class Cell : INotifyPropertyChanged
        {
            private string _value = string.Empty;

            public event PropertyChangedEventHandler? PropertyChanged;

            public string Value
            {
                get => _value;
                set
                {
                    _value = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
                }
            }
        }
    }
}
