using System.Globalization;
using ArxisStudio.Controls;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>Руки теста для вопросов окна проекта: найти диалог, ответить и дождаться итога.</summary>
internal static class ProjectWindowDialogs
{
    /// <summary>Диалог, который окно показало, — один.</summary>
    public static T Dialog<T>(ProjectWindowStudio studio) where T : Window
    {
        Dispatcher.UIThread.RunJobs();

        return Assert.Single(studio.Window.OwnedWindows.OfType<T>());
    }

    /// <summary>
    /// Ждёт, пока окно дойдёт до ожидаемого: правка идёт через службу, постройку дерева вне потока
    /// интерфейса и возврат в него — несколько переходов, а не один.
    /// </summary>
    public static async Task Settled(ProjectWindowStudio studio, Func<bool> reached)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (true)
        {
            Dispatcher.UIThread.RunJobs();

            if (reached())
                return;

            Assert.True(DateTime.UtcNow < deadline, "окно не дошло до ожидаемого за десять секунд");

            await studio.Model.Settled.WaitAsync(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }
    }

    /// <summary>Enter в форме диалога.</summary>
    public static void Enter(AxDialog dialog) =>
        Part<StackPanel>(dialog, "Form").RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

    /// <summary>Названная часть разметки диалога — и в содержимом, и среди кнопок.</summary>
    public static T Part<T>(AxDialog dialog, string name) where T : Control =>
        Assert.IsType<T>(dialog.GetLogicalDescendants()
            .Concat(dialog.Buttons is Control buttons ? buttons.GetSelfAndLogicalDescendants() : [])
            .OfType<Control>()
            .Single(control => control.Name == name));

    /// <summary>Строка словаря окна с подставленными значениями.</summary>
    public static string Format(ProjectWindowStudio studio, string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, studio.Strings[key], values);
}
