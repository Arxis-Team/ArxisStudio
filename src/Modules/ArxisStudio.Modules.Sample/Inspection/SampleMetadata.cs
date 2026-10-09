using ArxisStudio.Inspector;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Sample.Inspection;

/// <summary>
/// Таблица метаданных образца: раздел и подписи своих настроек и пояснение к чужому свойству.
/// </summary>
/// <remarks>
/// <para>
/// Образец того, как плагин описывает свойства, у которых своих атрибутов нет или которые нужно перевести. Таблица —
/// вклад: студия найдёт её сама, а инспектор спросит её один раз и запомнит до перемены набора. Её слово ложится
/// поверх атрибутов .NET источника и поверх таблиц модулей — по полю: что таблица не назвала, остаётся как было.
/// </para>
/// <para>
/// Свой тип таблица называет типом (<see cref="InspectorMetadataBuilder.For{T}"/>), тип проекта, которого у плагина
/// нет, — полным именем; помнит инспектор только имена, и выгрузку плагина таблица не держит.
/// </para>
/// </remarks>
public sealed class SampleMetadata : InspectorMetadata
{
    /// <inheritdoc/>
    public override void Describe(InspectorMetadataBuilder metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var strings = Context.Strings;

        metadata.Category(SampleSettings.Category).Title(strings["sample.category"]).Order(1);

        metadata.For<SampleSettings>()
            .Property(nameof(SampleSettings.Greeting), property => property.Title(strings["sample.greeting"]).Order(0))
            .Property(nameof(SampleSettings.Repeat), property => property.Title(strings["sample.repeat"]).Order(1).Step(1))
            .Property(nameof(SampleSettings.Tone), property => property.Title(strings["sample.tone"]).Order(2))
            .Property(nameof(SampleSettings.Loud), property => property.Title(strings["sample.loud"]).Order(3).Help(strings["sample.loud.help"]));

        // Чужое свойство: таблица Avalonia поставила тень в раздел вида, а образец дописывает пояснение.
        metadata.For<Border>()
            .Property(Border.BoxShadowProperty, property => property.Description(strings["sample.shadow.description"]));
    }
}
