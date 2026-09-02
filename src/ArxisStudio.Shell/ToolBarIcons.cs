using System.Collections.Frozen;
using System.Reflection;
using ArxisStudio.Controls;
using ArxisStudio.Icons;
using Avalonia.Media;

namespace ArxisStudio.Shell;

/// <summary>
/// Значки элементов полосы по записи из манифеста.
/// </summary>
/// <remarks>
/// Запись бывает двух видов, и оба читаются без загрузки сборки плагина:
/// <c>arxis:Play</c> берёт глиф из набора студии, всё остальное — свой контур в
/// той же сетке 16×16, в которой нарисован набор. Картинки файлом здесь нет
/// намеренно: набор контурный, одной обводки, и растр рядом с ним расслаивал
/// бы полосу по весу.
/// <para>
/// Имена набора берутся отражением по <see cref="AxIcons"/> один раз: новый
/// глиф, добавленный в набор, становится доступен плагинам без правки студии.
/// Сравнение строгое к регистру — имя копируется из кода как есть.
/// </para>
/// </remarks>
public static class ToolBarIcons
{
    /// <summary>Чем начинается ссылка на глиф набора.</summary>
    public const string Prefix = "arxis:";

    private static readonly FrozenDictionary<string, Geometry> Named = typeof(AxIcons)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(property => typeof(Geometry).IsAssignableFrom(property.PropertyType))
        .ToFrozenDictionary(
            property => property.Name,
            property => (Geometry)property.GetValue(null)!,
            StringComparer.Ordinal);

    /// <summary>
    /// Разбирает запись значка.
    /// </summary>
    /// <param name="icon">Запись из манифеста; пусто — значка нет.</param>
    /// <param name="problem">Почему значка не будет; null, если всё в порядке или его не просили.</param>
    /// <returns>Геометрия глифа или null.</returns>
    public static Geometry? Resolve(string? icon, out string? problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(icon))
            return null;

        if (icon.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = icon[Prefix.Length..].Trim();

            if (Named.TryGetValue(name, out var found))
                return found;

            problem = $"значка {icon} в наборе студии нет";
            return null;
        }

        try
        {
            var drawn = Geometry.Parse(icon);
            var bounds = drawn.Bounds;

            // Контур, который ничего не рисует, — та же ошибка, что и битый:
            // человек увидел бы пустую кнопку и не понял, почему.
            if (bounds.Width == 0 && bounds.Height == 0)
            {
                problem = $"контур значка «{icon}» пуст";
                return null;
            }

            return drawn;
        }

        // Строку принёс посторонний, и чем на неё ответит разборщик — его дело;
        // отказ процесса перехватывать нечем.
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            problem = $"контур значка «{icon}» не разобрался: {e.Message}";
            return null;
        }
    }
}
