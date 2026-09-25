using System.Collections.Frozen;
using System.Reflection;
using ArxisStudio.Icons;
using Avalonia.Media;

namespace ArxisStudio.Shell;

/// <summary>
/// Значки по записи из манифеста: у кнопки полосы, у панели и у команды.
/// </summary>
/// <remarks>
/// Запись бывает двух видов, и оба читаются без загрузки сборки плагина:
/// <c>arxis:Play</c> берёт глиф из набора студии, всё остальное — свой контур в
/// той же сетке 16×16, в которой нарисован набор. Картинки файлом здесь нет
/// намеренно: набор контурный, одной обводки, и растр рядом с ним расслаивал
/// бы полосу, вкладки и меню по весу.
/// <para>
/// Разборщик один на все три места, и это не экономия: запись, которая
/// разбирается у кнопки, обязана разбираться и у вкладки — иначе автор узнавал
/// бы правила записи по месту, где она не сработала.
/// </para>
/// <para>
/// Имена набора берутся отражением по <see cref="AxIcons"/> один раз: новый
/// глиф, добавленный в набор, становится доступен плагинам без правки студии.
/// Сравнение строгое к регистру — имя копируется из кода как есть.
/// </para>
/// <para>
/// По имени запоминается геттер, а не путь: набор разбирает путь при первом обращении,
/// и словарь, построенный вызовом всех геттеров, разобрал бы весь набор на старте ради
/// единиц значков, которые назвали манифесты.
/// </para>
/// </remarks>
public static class ManifestIcons
{
    /// <summary>Чем начинается ссылка на глиф набора.</summary>
    public const string Prefix = "arxis:";

    private static readonly FrozenDictionary<string, PropertyInfo> Named = typeof(AxIcons)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(property => typeof(Geometry).IsAssignableFrom(property.PropertyType))
        .ToFrozenDictionary(property => property.Name, StringComparer.Ordinal);

    /// <summary>
    /// Разбирает запись значка.
    /// </summary>
    /// <param name="icon">Запись из манифеста; пусто — значка нет.</param>
    /// <param name="problem">Почему значка не будет; null, если всё в порядке или его не просили.</param>
    /// <returns>Геометрия глифа или null.</returns>
    /// <remarks>
    /// Пробелы по краям записи снимаются: « arxis:Play» иначе не узнавалось бы по
    /// приставке и уходило бы разбираться контуром. Так же читает запись и
    /// <c>ARX0011</c> — правило при сборке не вправе пропускать то, чего студия
    /// не нарисует.
    /// </remarks>
    public static Geometry? Resolve(string? icon, out string? problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(icon))
            return null;

        icon = icon.Trim();

        if (icon.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = icon[Prefix.Length..].Trim();

            if (Named.TryGetValue(name, out var getter))
                return (Geometry)getter.GetValue(null)!;

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
        catch (Exception e) when (Faults.Survivable(e))
        {
            problem = $"контур значка «{icon}» не разобрался: {e.Message}";
            return null;
        }
    }
}
