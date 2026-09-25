
namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Сколько правок по букве отделяют одно имя от другого.
/// </summary>
/// <remarks>
/// Правка — вставка, удаление, замена буквы или перестановка двух соседних. Подсказку о похожем
/// имени дают два правила — значок набора у <c>ARX0011</c> и ключ темы у <c>ARX0009</c>, — и
/// считали они её двумя копиями одной таблицы.
/// </remarks>
internal static class EditDistance
{
    /// <summary>Число правок, превращающих одно имя в другое.</summary>
    /// <param name="first">Одно имя.</param>
    /// <param name="second">Другое.</param>
    public static int Between(string first, string second)
    {
        var table = new int[first.Length + 1, second.Length + 1];

        for (var row = 0; row <= first.Length; row++)
        {
            table[row, 0] = row;
        }

        for (var column = 0; column <= second.Length; column++)
        {
            table[0, column] = column;
        }

        for (var row = 1; row <= first.Length; row++)
        {
            for (var column = 1; column <= second.Length; column++)
            {
                var replaced = table[row - 1, column - 1] + (first[row - 1] == second[column - 1] ? 0 : 1);
                var best = Math.Min(replaced, Math.Min(table[row - 1, column], table[row, column - 1]) + 1);

                if (row > 1 && column > 1 && first[row - 1] == second[column - 2] && first[row - 2] == second[column - 1])
                {
                    best = Math.Min(best, table[row - 2, column - 2] + 1);
                }

                table[row, column] = best;
            }
        }

        return table[first.Length, second.Length];
    }
}
