using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Xaml;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Сессия формы — аренда документа и его показ: показы одного документа идут по одному.
/// </summary>
/// <remarks>
/// Службы проектов и XAML настоящие (<see cref="LiveFormStudio"/>); показ, который ещё идёт, держит шов
/// <c>FormShown</c>.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class FormSessionTests
{
    private const string Form = """
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     Width="200" Height="120">
          <Border x:Name="Frame" Width="160" Height="80" Background="Gray" />
        </UserControl>
        """;

    /// <summary>
    /// Показ, отпущенный, пока он ещё шёл, держит документ, пока не кончится: новый показ той же сессии ждёт его,
    /// а не натыкается на него, — форма встаёт, и сбоя нет.
    /// </summary>
    [AvaloniaFact]
    public async Task A_show_let_go_while_it_comes_is_waited_for_by_the_next()
    {
        var parking = false;
        var parked = new TaskCompletionSource();
        var park = new TaskCompletionSource();

        await using var studio = new LiveFormStudio(formShown: async (_, token) =>
        {
            if (!parking)
                return;

            parking = false;
            parked.TrySetResult();
            await park.Task.WaitAsync(token);
        });
        var path = studio.Xaml.Write("Badge.axaml", Form);

        await studio.Xaml.OpenAsync();

        var documents = Assert.IsAssignableFrom<IStudioXamlDocuments>(studio.Context.XamlDocuments());

        await using var session = new FormSession(studio.Context, documents, path, studio.Options, new Lender(), FormShowRank.Board);

        parking = true;

        var first = session.ShowAsync();

        await XamlStudio.UntilAsync(() => parked.Task.IsCompleted, "показ не встал на стоянку");

        // Показ уже занял документ: сессия отпускает его и тут же просит снова.
        session.Hide();

        var second = session.ShowAsync();

        park.TrySetResult();
        await first;
        await second;

        Assert.Null(session.Problem);
        Assert.NotNull(session.Shown?.Root);
    }

    /// <summary>Тот, кто одалживает корень на время записи сессии разметки: здесь корня не держит никто.</summary>
    private sealed class Lender : IXamlRootLender
    {
        public IDisposable Lend(object root) => new Nothing();

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
