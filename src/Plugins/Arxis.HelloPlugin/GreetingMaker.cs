using System.Text;
using ArxisStudio.Sdk;

namespace Arxis.HelloPlugin;

/// <summary>
/// Код пункта «Добавить ▸ Образцы ▸ Приветствие»: файл с приветствием из настройки плагина.
/// </summary>
/// <remarks>
/// Шаблона здесь мало: слово приветствия — настройка, и меняет его человек, а не автор, — поэтому
/// пункт объявлен видом <c>code</c>, и файл собирает этот класс. Спящий плагин будит событие
/// <c>onNewItem:hello.greeting</c>; переменные подставляет тот же <see cref="NewItemTemplate"/>, что и
/// у шаблонов манифеста.
/// </remarks>
[NewItem("hello.greeting")]
public sealed class GreetingMaker : NewItemMaker
{
    /// <inheritdoc/>
    public override Task<NewItemResult> MakeAsync(NewItemRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = NewItemTemplate.Expand(
            $"{Context.Settings.Get<string>("hello.greeting")}\n\n$project$ · $year$\n",
            request);

        return Task.FromResult(NewItemResult.Made(
            [new NewItemFile(request.Name) { Content = Encoding.UTF8.GetBytes(text), Open = true }]));
    }
}
