using System.Runtime.CompilerServices;
using ArxisStudio.Extensibility;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Привязка к строкам интерфейса. Смена языка должна перерисовывать уже
/// показанный текст — иначе переключатель в настройках меняет только настройку.
/// </summary>
/// <remarks>
/// Привязки здесь ставятся из отдельного метода, и он не встраивается. Это не
/// придирка: привязка Avalonia смотрит на свой источник слабо, и строку,
/// оставшуюся в кадре теста, держал бы сам тест. Проверка тогда доказывала бы
/// только то, что у неё есть локальная переменная, — так и вышло однажды:
/// набор был зелёным, а язык в работающей студии не переключался.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class LocalizationBindingTests : IDisposable
{
    public void Dispose()
    {
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);
        GC.SuppressFinalize(this);
    }

    /// <summary>Смена языка перерисовывает показанный текст студии.</summary>
    [AvaloniaFact]
    public void Switching_language_updates_text_already_on_screen()
    {
        Localizer.Instance.SetLanguage("ru");

        var text = Shown(Studio);

        Assert.Equal("Открыть", text.Text);

        Localizer.Instance.SetLanguage("en");

        Assert.Equal("Open", text.Text);
    }

    /// <summary>
    /// Сборка мусора между привязкой и сменой языка ничего не ломает.
    /// </summary>
    /// <remarks>
    /// Ровно этот случай и был багом: строку не держал никто — ни привязка,
    /// ни студия, — и первая же сборка мусора замораживала показанный текст
    /// навсегда. Возню с плагинами человек замечал как «язык не меняется»,
    /// потому что пересборка словарей выделяет достаточно, чтобы сборка
    /// случилась.
    /// </remarks>
    [AvaloniaFact]
    public void A_collection_between_binding_and_switch_changes_nothing()
    {
        Localizer.Instance.SetLanguage("ru");

        var text = Shown(Studio);

        Assert.Equal("Открыть", text.Text);

        Collect();

        Localizer.Instance.SetLanguage("en");

        Assert.Equal("Open", text.Text);
    }

    /// <summary>Строки плагина переживают сборку мусора так же.</summary>
    /// <remarks>
    /// У них свой источник и свой хозяин, и дорога к обновлению та же самая:
    /// заголовок панели плагина обязан менять язык вместе со студией.
    /// </remarks>
    [AvaloniaFact]
    public void Plugin_strings_survive_a_collection_too()
    {
        var strings = PluginStrings.Studio;

        Localizer.Instance.SetLanguage("ru");

        var text = Shown(() => strings.Text("projects.open"));

        Assert.Equal("Открыть", text.Text);

        Collect();

        Localizer.Instance.SetLanguage("en");

        Assert.Equal("Open", text.Text);
    }

    /// <summary>На один ключ заводится одна строка, а не по одной на привязку.</summary>
    /// <remarks>
    /// Иначе память росла бы с числом построенных контролов: карточки
    /// менеджера пересобираются на каждое включение плагина.
    /// </remarks>
    [AvaloniaFact]
    public void One_key_means_one_tracked_string()
    {
        Assert.Same(Localizer.Instance.Track("projects.open"), Localizer.Instance.Track("projects.open"));
    }

    private static Avalonia.Data.BindingBase Studio() =>
        new LocExtension("projects.open").ProvideValue(null!);

    /// <summary>
    /// Ставит привязку и показывает её — как это делает загрузчик разметки:
    /// ни расширения, ни привязки в кадре вызывающего не остаётся.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TextBlock Shown(Func<Avalonia.Data.BindingBase> binding)
    {
        var text = new TextBlock();

        text.Bind(TextBlock.TextProperty, binding());

        new Window { Content = text }.Show();

        return text;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
