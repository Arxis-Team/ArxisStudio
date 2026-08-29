using Arxis.Hello.Contracts;
using ArxisStudio.Sdk;

namespace Arxis.HelloPlugin;

/// <summary>
/// Реализация контракта приветствия.
/// </summary>
/// <remarks>
/// Реализация живёт в сборке плагина и выгружается вместе с ним; в общем
/// контексте остаётся только интерфейс. Слово берётся из настройки — той же,
/// какой пользуется команда <c>hello.greet</c>: у приветствия один источник.
/// </remarks>
/// <param name="context">Контекст плагина — ради настройки.</param>
public sealed class Greeter(IStudioContext context) : IGreeter
{
    /// <inheritdoc/>
    public string Greet(string name) =>
        $"{context.Settings.Get<string>("hello.greeting")} {name}";
}
