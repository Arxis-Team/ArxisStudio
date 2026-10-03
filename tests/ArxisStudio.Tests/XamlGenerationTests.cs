using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Xaml;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Поколение типов проекта в службе XAML: выход проекта грузится в выгружаемый контекст, заменяется и
/// уходит из процесса — или не уходит, и тогда студию просят о перезапуске.
/// </summary>
/// <remarks>
/// <para>
/// Выход проекта — настоящая сборка на диске с контролом <c>Badge : Border</c>, а форма ставит его
/// элементом: так в поколении есть объект, тип которого держит контекст. Своё имя сборки у каждого
/// теста — чтобы поколение соседа, ещё не дособранное сборщиком мусора, не стало для этого предшественником,
/// которого ждут.
/// </para>
/// <para>
/// Объекты поколения берутся в отдельных методах без встраивания: локальная переменная асинхронного
/// метода живёт столько, сколько сам метод, и держала бы то, уход чего тест доказывает.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class XamlGenerationTests
{
    private const string BadgeSource = """
        namespace App;

        public class Badge : Avalonia.Controls.Border
        {
        }
        """;

    private const string Form = """
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:local="using:App">
          <local:Badge />
        </UserControl>
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Заменённое поколение уходит из процесса: тип контрола проекта, построенный им, собран, а показ
    /// стоит на типе нового поколения.
    /// </summary>
    [AvaloniaFact]
    public async Task A_replaced_generation_leaves_the_process()
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("MainView.axaml", Form);

        Emit(studio);

        await studio.OpenAsync();

        await using var handle = await studio.Documents.OpenAsync(path, Token);
        using var view = await handle.ShowAsync(null, Token);

        var before = BadgeType(view);

        Assert.True(before.IsAlive, "контрол проекта не построился из поколения");

        var report = await studio.Session.Host.SwapAsync("проверка выгрузки", Token);

        Assert.True(report.Reclaimed, report.ToString());
        Assert.False(before.IsAlive, "тип прежнего поколения остался в процессе");
        Assert.True(IsOfACollectibleGeneration(view), "новый показ стоит не на типе нового поколения");
        Assert.Equal(XamlDesignState.Live, studio.Design.State);
    }

    /// <summary>
    /// Контрол поколения, удержанный мимо замены, не даёт ему уйти — и служба просит студию о
    /// перезапуске от своего имени, а не меняет типы рядом с прежними.
    /// </summary>
    [AvaloniaFact]
    public async Task A_control_held_past_a_swap_asks_the_studio_for_a_restart()
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("MainView.axaml", Form);

        Emit(studio);

        await studio.OpenAsync();

        await using var handle = await studio.Documents.OpenAsync(path, Token);
        using var view = await handle.ShowAsync(null, Token);

        var held = Badge(view);
        var report = await studio.Session.Host.SwapAsync("проверка удержания", Token);

        Assert.False(report.Reclaimed, "поколение ушло, хотя его держали");
        Assert.Equal(XamlDesignState.RestartRequired, studio.Design.State);

        var (plugin, reason) = Assert.Single(studio.Restarts);

        Assert.Equal("arxis.xaml", plugin);
        Assert.Equal(reason, studio.Design.StateReason);

        GC.KeepAlive(held);
    }

    /// <summary>
    /// Отсрочка, взятая раньше, чем что-то открыто, держит замену типов, пересобранных потом, и
    /// отпущенная пускает её.
    /// </summary>
    [AvaloniaFact]
    public async Task A_deferral_taken_before_anything_is_open_holds_the_swap()
    {
        await using var studio = new XamlStudio();
        var path = studio.Write("MainView.axaml", Form);
        var output = Emit(studio);

        var deferral = studio.Design.Defer("тяга на холсте");

        await studio.OpenAsync();

        await using var handle = await studio.Documents.OpenAsync(path, Token);

        // Сборка переписала выход — время записи сдвигается, как сдвинул бы его компилятор.
        studio.Provider.Executed = request =>
        {
            if (request.Kind == ProjectOperationKind.Build)
                File.SetLastWriteTimeUtc(output.Value, File.GetLastWriteTimeUtc(output.Value).AddSeconds(2));

            return ProjectOperationResult.Succeeded();
        };

        var outcome = await studio.Design.RebuildAsync(Token);

        Assert.True(outcome.Succeeded);
        Assert.True(outcome.TypesChanged, "сборка не переписала то, из чего загружено поколение");
        Assert.Equal(outcome.Reason, studio.Design.LastBuild?.Reason);

        await XamlStudio.UntilAsync(() => studio.Design.State == XamlDesignState.SwapPending, "типы не встали в ожидание замены");

        Assert.Contains("тяга на холсте", studio.Design.StateReason, StringComparison.Ordinal);

        deferral.Dispose();

        await XamlStudio.UntilAsync(() => studio.Design.State == XamlDesignState.Live, "замена не пошла после отсрочки");
    }

    /// <summary>Кладёт выход проекта: сборку с контролом, позже разметки — поколению собирать нечего.</summary>
    private static CanonicalPath Emit(XamlStudio studio)
    {
        var name = "XamlProbe" + Guid.NewGuid().ToString("N")[..8];
        var output = Path.Combine(studio.ProjectFolder, "bin", name + ".dll");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        TestAssembly.EmitFile(output, name, BadgeSource);

        studio.AssemblyName = name;
        studio.Output = CanonicalPath.Create(output);

        return studio.Output.Value;
    }

    /// <summary>Тип показанного контрола проекта — слабой ссылкой.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference BadgeType(IXamlDesignView view) => new(Badge(view).GetType());

    /// <summary>Стоит ли показанный контрол проекта на типе выгружаемого контекста.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsOfACollectibleGeneration(IXamlDesignView view) =>
        AssemblyLoadContext.GetLoadContext(Badge(view).GetType().Assembly) is { IsCollectible: true };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object Badge(IXamlDesignView view) =>
        Assert.Single(view.GetDeclaredObjects(), declared => declared.GetType().Name == "Badge");
}
