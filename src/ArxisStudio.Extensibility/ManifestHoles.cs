using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArxisStudio.Sdk.Plugins;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Ищет в разобранном манифесте пустое место там, где модель обещает значение.
/// </summary>
/// <remarks>
/// Явный <c>null</c> вместо списка или записи, <c>null</c> в списке и запись без обязательного поля —
/// законный JSON, и разбор отдаёт такой манифест без слова: свойство, обещавшее список, остаётся пустым
/// местом. Дыра доезжала до студии и роняла её там, где манифеста уже не видно, — сбор полосы падал на
/// чужом <c>"toolBar": null</c> раньше, чем поднимался хоть один модуль (запись 382).
/// <para>
/// Обещание читается из самой модели — из аннотаций обнуляемости SDK, — а не списком полей: новое поле
/// манифеста проверяется само, и забыть его здесь нечем. Флаг разбора
/// <see cref="JsonSerializerOptions.RespectNullableAnnotations"/> этого не закрывает: элементов списка
/// он не смотрит, а параметр конструктора записи, которого в JSON нет, оставляет пустым молча.
/// </para>
/// </remarks>
internal static class ManifestHoles
{
    private static readonly ConcurrentDictionary<Type, Slot[]> Slots = new();

    /// <summary>Бросает, если в манифесте пусто там, где модель обещает значение.</summary>
    /// <param name="manifest">Разобранный манифест.</param>
    /// <exception cref="JsonException">Найдена дыра; сообщение называет её место.</exception>
    public static void Check(PluginManifest manifest) => Walk(manifest, path: null);

    private static void Walk(object node, string? path)
    {
        foreach (var slot in Slots.GetOrAdd(node.GetType(), Describe))
        {
            var where = path is null ? slot.Name : $"{path}.{slot.Name}";

            switch (slot.Property.GetValue(node))
            {
                case null when slot.Required:
                    throw Hole(where);
                case string or null:
                    break;
                case IEnumerable items:
                    var index = 0;

                    foreach (var item in items)
                    {
                        var at = $"{where}[{index++}]";

                        if (item is null && slot.ItemsRequired)
                            throw Hole(at);

                        if (item is not null && IsModel(item.GetType()))
                            Walk(item, at);
                    }

                    break;
                case var value when IsModel(value.GetType()):
                    Walk(value, where);
                    break;
            }
        }
    }

    private static Slot[] Describe(Type type)
    {
        // Контекст не потокобезопасен, а описание типа строится один раз — свой контекст на тип дешевле
        // замка вокруг общего.
        var nullability = new NullabilityInfoContext();

        return
        [
            .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.SetMethod is { IsPublic: true }
                                   && property.GetIndexParameters().Length == 0
                                   && !property.IsDefined(typeof(JsonIgnoreAttribute)))
                .Select(property =>
                {
                    var info = nullability.Create(property);

                    return new Slot(
                        property,
                        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                        ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                        info.ReadState == NullabilityState.NotNull,
                        (info.ElementType ?? info.GenericTypeArguments.FirstOrDefault())?.ReadState
                        == NullabilityState.NotNull);
                }),
        ];
    }

    private static bool IsModel(Type type) =>
        type.IsClass && type.Assembly == typeof(PluginManifest).Assembly && type.Namespace == typeof(PluginManifest).Namespace;

    private static JsonException Hole(string where) => new($"Пусто на месте {where}: манифест ждёт там значение");

    /// <summary>Поле записи манифеста и что оно обещает.</summary>
    /// <param name="Property">Свойство модели.</param>
    /// <param name="Name">Имя поля, как его пишут в манифесте.</param>
    /// <param name="Required">Пустым поле быть не может.</param>
    /// <param name="ItemsRequired">Пустых элементов в списке быть не может.</param>
    private sealed record Slot(PropertyInfo Property, string Name, bool Required, bool ItemsRequired);
}
