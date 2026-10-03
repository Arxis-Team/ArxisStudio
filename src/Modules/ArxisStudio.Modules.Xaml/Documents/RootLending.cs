using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.Xaml.Documents;

/// <summary>
/// Возврат корня на время записи — тому, кто его сейчас показывает.
/// </summary>
/// <remarks>
/// Хост спрашивает возврат при открытии документа, а показывают документ потом и разные: вкладка
/// закрылась, открылась снова, рамка сменилась. Поэтому документу отдаётся этот посредник, а кто
/// одалживает части корня, он узнаёт у показа (<see cref="Lender"/>). Пока документ никто не
/// показывает, одалживать нечего, и возврат пуст.
/// </remarks>
internal sealed class RootLending : IXamlRootAccess
{
    /// <summary>Тот, кто одалживает части корня, — показ документа; null — не показан.</summary>
    public IXamlRootLender? Lender { get; set; }

    /// <inheritdoc/>
    public IDisposable Lend(object root) => Lender?.Lend(root) ?? Nothing.Instance;

    /// <summary>Аренда корня, которого никто не одалживал.</summary>
    private sealed class Nothing : IDisposable
    {
        public static Nothing Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
