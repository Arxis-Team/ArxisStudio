using System.Globalization;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Xaml;
using Avalonia.Controls;

namespace ArxisStudio.Modules.UiDesigner.Workbench;

/// <summary>Разметка, которую пишет вставка типа: фрагмент Markup с местом на его корне.</summary>
internal static class TypeMarkup
{
    /// <summary>
    /// Фрагмент типа в его пространстве — под приставкой, если она есть, — с местом, если родитель его
    /// читает.
    /// </summary>
    /// <param name="type">Тип и его разметка.</param>
    /// <param name="placement">Куда бросили; null — вставка без точки: палитра по Enter.</param>
    /// <remarks>
    /// <para>
    /// Пространство по умолчанию у фрагмента — то, в котором написана разметка типа, а у типа под
    /// приставкой — Avalonia: пространство по умолчанию там, где его вставят, переименовать нельзя
    /// (<c>AXM1043</c>), а приставку можно — вставка объявит её на корне формы. Разметка под приставкой —
    /// один элемент.
    /// </para>
    /// <para>
    /// Место пишется правкой корня фрагмента, а не текстом: у разметки с детьми последний <c>/&gt;</c>
    /// закрывает ребёнка. <c>Canvas</c> читает <c>Canvas.Left</c> и <c>Canvas.Top</c>, <c>Grid</c> —
    /// <c>Grid.Row</c> и <c>Grid.Column</c>, и только не нулевые: так их оставил бы человек. Прочие
    /// родители ставят ребёнка сами, и атрибут, которого раскладка не читает, был бы ложью о раскладке.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Разметка типа не начинается с его элемента.</exception>
    public static XamlFragment Fragment(XamlSnippet type, SurfaceDropPlacement? placement)
    {
        ArgumentNullException.ThrowIfNull(type);

        var markup = type.Markup ?? $"<{type.Name} />";
        var open = "<" + type.Name;

        if (!markup.StartsWith(open, StringComparison.Ordinal))
            throw new ArgumentException($"Разметка {type.Name} не начинается с его элемента.", nameof(type));

        var fragment = XamlFragment.Parse(type.Prefix is { } prefix
            ? $"<{prefix}:{type.Name} xmlns=\"{XamlSnippet.AvaloniaNamespace}\" xmlns:{prefix}=\"{type.XmlNamespace}\"{markup[open.Length..]}"
            : markup.Insert(open.Length, $" xmlns=\"{type.XmlNamespace}\""));

        if (placement is null || fragment.Root is not { } root)
            return fragment;

        var editor = fragment.Document.Edit();

        switch (placement)
        {
            case { Kind: SurfaceDropKind.Position, Parent: Canvas }:
                Set("Canvas.Left", placement.Position.X);
                Set("Canvas.Top", placement.Position.Y);
                break;

            case { Kind: SurfaceDropKind.Cell }:
                if (placement.Row > 0)
                    Set("Grid.Row", placement.Row);

                if (placement.Column > 0)
                    Set("Grid.Column", placement.Column);

                break;
        }

        return editor.HasChanges && editor.Apply().Root is { } placed ? XamlFragment.From(placed) : fragment;

        void Set(string attached, double value) =>
            editor.SetAttribute(
                root,
                editor.Qualify(root, XamlSnippet.AvaloniaNamespace, attached),
                Math.Round(value).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Тип контрола проекта — его разметкой, под приставкой библиотеки или <c>local</c>.</summary>
    /// <param name="control">Контрол проекта.</param>
    public static XamlSnippet Of(XamlPlaceable control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return new XamlSnippet(control.XmlNamespace, control.Name)
        {
            Prefix = control.SuggestedPrefix ?? "local",
            Placeable = control,
        };
    }
}
