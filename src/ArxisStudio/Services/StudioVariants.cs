#if DEBUG
using System.Globalization;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using AvaDevTools.Variants;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;

namespace ArxisStudio.Services;

/// <summary>
/// Оси, по которым инструменты разработчика перечитывают студию: так, как её меняет человек.
/// </summary>
/// <remarks>
/// Встроенные оси инструментов знают о приложении вообще: светлая тема, двойной текст,
/// узкое окно. Студия меняется своими дорогами — ступенью плотности, языком интерфейса и
/// шкалой кеглей темы, — и проверка «не обрежется ли это у человека» стоит чего-то, только
/// если прогоняет именно их.
/// <para>
/// Встроенный двойной текст растил наследуемый кегль у корня окна, и всякий текст, чей
/// кегль приходит из темы, для него был «прибит» — отсюда десятки находок «размер написан
/// на месте», хотя написан он ключом темы. Ось кеглей ниже растит сами ключи, и прибитым
/// остаётся только то, что действительно написано числом.
/// </para>
/// <para>
/// Каждая ось возвращает студию туда, где взяла: вариант записывает, что было, и
/// инструменты откатывают записанное. Выбранный человеком язык не угадывается по культуре
/// потока — студия её не трогает, — а берётся у словаря.
/// </para>
/// </remarks>
internal static class StudioVariants
{
    /// <summary>Оси студии для подключения к инструментам.</summary>
    public static IEnumerable<VariantAxis> Axes() => [new DensityAxis(), new LanguageAxis(), new TextSizeAxis()];

    /// <summary>Ступень плотности интерфейса.</summary>
    private sealed class DensityAxis : VariantAxis
    {
        public override string Id => "studio-density";

        public override string Name => "Density";

        public override string Varies => "the studio's interface density: row and control heights and the spacing scale";

        public override IReadOnlyList<VariantChoice> Suggested =>
        [
            new("compact", "Compact", () => new DensityVariant(StudioDensity.Compact)),
            new("comfortable", "Comfortable", () => new DensityVariant(StudioDensity.Comfortable)),
        ];

        public override IReadOnlyList<VariantChoice> Default => Suggested;
    }

    private sealed class DensityVariant(StudioDensity density) : Variant
    {
        public override string Axis => "Density";

        public override string Label => density.ToString();

        public override bool TryApply(IReadOnlyList<TopLevel> roots, RestoreLedger ledger, out string refusal)
        {
            var before = Current();

            StudioTheming.Apply(density);

            ledger.Record(() =>
            {
                if (before is { } was)
                {
                    StudioTheming.Apply(was);
                    return;
                }

                // До прогона ступени не было вовсе — так бывает в окне, открытом до запуска
                // этапа темы. Возвращать надо «ничего», а не «обычную».
                var merged = Application.Current!.Resources.MergedDictionaries;

                foreach (var tier in merged.OfType<ResourceInclude>().Where(IsDensity).ToList())
                    merged.Remove(tier);
            });

            refusal = string.Empty;

            return true;
        }

        private static StudioDensity? Current()
        {
            var include = Application.Current?.Resources.MergedDictionaries.OfType<ResourceInclude>().LastOrDefault(IsDensity);
            var name = include?.Source is { } source ? Path.GetFileNameWithoutExtension(source.OriginalString) : null;

            return Enum.TryParse<StudioDensity>(name, out var parsed) ? parsed : null;
        }

        private static bool IsDensity(ResourceInclude include) =>
            include.Source?.OriginalString.Contains("/Density/", StringComparison.Ordinal) == true;
    }

    /// <summary>Язык интерфейса — из тех, что студия сейчас умеет показать.</summary>
    private sealed class LanguageAxis : VariantAxis
    {
        public override string Id => "studio-language";

        public override string Name => "Language";

        public override string Varies => "the studio's interface language: every string it shows, and their lengths";

        public override IReadOnlyList<VariantChoice> Suggested =>
            [.. Localizer.Instance.Languages
                .Where(language => !string.Equals(language.Code, Localizer.Instance.Language, StringComparison.OrdinalIgnoreCase))
                .Select(language => new VariantChoice(language.Code, language.Name, () => new LanguageVariant(language.Code, language.Name)))];

        public override IReadOnlyList<VariantChoice> Default => Suggested;
    }

    private sealed class LanguageVariant(string code, string name) : Variant
    {
        public override string Axis => "Language";

        public override string Label => name;

        public override bool TryApply(IReadOnlyList<TopLevel> roots, RestoreLedger ledger, out string refusal)
        {
            var before = Localizer.Instance.Language;

            if (!Localizer.Instance.SetLanguage(code))
            {
                refusal = $"the studio has no dictionary for {code}";
                return false;
            }

            ledger.Record(() => Localizer.Instance.SetLanguage(before));
            refusal = string.Empty;

            return true;
        }
    }

    /// <summary>Шкала кеглей темы, выросшая целиком.</summary>
    private sealed class TextSizeAxis : VariantAxis
    {
        public override string Id => "studio-text-size";

        public override string Name => "Theme text size";

        public override string Varies => "every font size of the theme's type scale at once, the way a text size setting would grow them";

        public override IReadOnlyList<VariantChoice> Suggested =>
        [
            new("150", "150%", () => new TextSizeVariant(1.5)),
            new("200", "200%", () => new TextSizeVariant(2)),
        ];

        public override IReadOnlyList<VariantChoice> Default => [Suggested[1]];
    }

    private sealed class TextSizeVariant(double factor) : Variant
    {
        /// <summary>Кегли шкалы темы: всё, что текст студии берёт ключом.</summary>
        private static readonly string[] Keys =
        [
            "AxFontSize", "AxFontSizeSmall", "AxFontSizeCaption", "AxFontSizeLarge", "AxFontSizeTitle", "AxFontSizeDisplay",
        ];

        public override string Axis => "Theme text size";

        public override string Label => factor.ToString("P0", CultureInfo.InvariantCulture);

        public override bool ExpectsTextToScale => true;

        public override bool TryApply(IReadOnlyList<TopLevel> roots, RestoreLedger ledger, out string refusal)
        {
            var resources = Application.Current!.Resources;

            foreach (var key in Keys)
            {
                if (!Application.Current.TryGetResource(key, null, out var value) || value is not double size)
                    continue;

                var own = resources.TryGetValue(key, out var previous);

                resources[key] = size * factor;

                ledger.Record(() =>
                {
                    if (own)
                        resources[key] = previous;
                    else
                        resources.Remove(key);
                });
            }

            refusal = string.Empty;

            return true;
        }
    }
}
#endif
