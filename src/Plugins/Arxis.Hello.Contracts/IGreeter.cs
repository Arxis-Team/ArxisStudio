namespace Arxis.Hello.Contracts;

/// <summary>
/// Приветствие — то, что Arxis.HelloPlugin отдаёт соседям типизированно.
/// </summary>
/// <remarks>
/// Интерфейс живёт в контрактной сборке, а не в самом плагине: всё публичное
/// в сборке плагина иначе становилось бы его API — сослался сосед, и
/// переименование ломает чужой код. Что в контракте — обещано, что не в нём
/// — не обещано.
/// </remarks>
public interface IGreeter
{
    /// <summary>Здоровается с тем, кого назвали.</summary>
    /// <param name="name">Кого поприветствовать.</param>
    /// <returns>Готовая фраза приветствия.</returns>
    string Greet(string name);
}
