using System.Globalization;
using System.Runtime.CompilerServices;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Workbench;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Xaml;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>
/// Холст формы — цель перетаскивания студии: на него несут контролы из палитры и разметку контролов из
/// окна проекта.
/// </summary>
/// <remarks>
/// <para>
/// <b>Место решает дизайнер, пишет вкладка.</b> Пока несут, ядро называет родителя под курсором и место в
/// нём (<see cref="UiDesignerView.TryResolveDropPlacement"/>) — между соседями панели по правилу
/// перестановки, ячейку сетки, точку <c>Canvas</c>, пустую рамку — и рисует отметку; у курсора сказано,
/// что встанет и куда, или почему нельзя. Отпущенное — одна правка документа, Ctrl+Z отменяет её целиком,
/// а вставленное встаёт выбранным.
/// </para>
/// <para>
/// <b>Место — именами.</b> Ядро называет живые контролы, документ держит элементы: место становится путём
/// родителя и номером среди его содержимого ещё в обработчике броска. Контрол проекта, которого нет в
/// поколении, ждёт сборки и замены, а замена строит форму заново — держатель её прежних контролов держал
/// бы поколение.
/// </para>
/// </remarks>
internal sealed class FormDrops : IDisposable
{
    private readonly LiveFormDocument _form;
    private readonly UiDesignerView _sheet;
    private readonly IStudioXamlTypes? _types;
    private readonly IStudioStrings _strings;
    private readonly Action<string> _say;
    private IReadOnlyList<XamlPlaceable> _placeables = [];
    private int _listing;

    /// <summary>Объявляет холст целью.</summary>
    /// <param name="form">Вкладка формы.</param>
    /// <param name="sheet">Холст.</param>
    /// <param name="types">Служба типов; null — контролов проекта не ставят.</param>
    /// <param name="strings">Словарь модуля.</param>
    /// <param name="say">Строка состояния: что не встало и почему.</param>
    public FormDrops(LiveFormDocument form, UiDesignerView sheet, IStudioXamlTypes? types, IStudioStrings strings, Action<string> say)
    {
        _form = form;
        _sheet = sheet;
        _types = types;
        _strings = strings;
        _say = say;

        StudioDragDrop.SetAllowDrop(sheet, true);
        StudioDragDrop.AddDragEnterHandler(sheet, OnDragEnter);
        StudioDragDrop.AddDragOverHandler(sheet, OnDragOver);
        StudioDragDrop.AddDragLeaveHandler(sheet, OnDragLeave);
        StudioDragDrop.AddDropHandler(sheet, OnDrop);
    }

    /// <summary>Контролы проекта, которые служба перечислила последними, — тестам.</summary>
    internal IReadOnlyList<XamlPlaceable> Placeables => _placeables;

    /// <summary>Перечисляет контролы проекта заново: сменились типы или файлы.</summary>
    public async Task ListAsync()
    {
        if (_types is null)
            return;

        var listing = ++_listing;
        IReadOnlyList<XamlPlaceable> placeables;

        try
        {
            placeables = await _types.GetPlaceableAsync(_form.Path);
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
        {
            return;
        }

        if (listing == _listing)
            _placeables = placeables;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        StudioDragDrop.SetAllowDrop(_sheet, false);
        StudioDragDrop.RemoveDragEnterHandler(_sheet, OnDragEnter);
        StudioDragDrop.RemoveDragOverHandler(_sheet, OnDragOver);
        StudioDragDrop.RemoveDragLeaveHandler(_sheet, OnDragLeave);
        StudioDragDrop.RemoveDropHandler(_sheet, OnDrop);
        _sheet.HideDropIndicator();
    }

    private void OnDragEnter(object? sender, StudioDragEventArgs e)
    {
        // Файл, написанный только что, мог ещё не попасть в перечень: пока курсор над холстом, он обновится.
        if (e.Data.Files.Count > 0)
            _ = ListAsync();
    }

    private void OnDragOver(object? sender, StudioDragEventArgs e)
    {
        e.Handled = true;

        if (Carried(e) is not { } carried)
        {
            _sheet.HideDropIndicator();
            return;
        }

        if (carried.Refusal is { } refusal)
        {
            _sheet.HideDropIndicator();
            e.Hint = refusal;
            return;
        }

        var type = carried.Type!;

        if (Place(e) is not { } placement || Intent(type, placement) is not { } intent)
        {
            _sheet.HideDropIndicator();
            e.Hint = Format("form.drop.nowhere", type.Name);
            return;
        }

        var effect = (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy
            : (e.AllowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link
            : DragDropEffects.None;

        if (effect == DragDropEffects.None)
            return;

        _sheet.ShowDropIndicator(placement);
        e.Effect = effect;
        e.Hint = string.Format(
            CultureInfo.CurrentCulture,
            _strings[type.Placeable is { IsBuilt: false } ? "form.drop.build" : "form.drop.add"],
            type.Name,
            intent.ParentName);
    }

    private void OnDragLeave(object? sender, StudioDragEventArgs e) => _sheet.HideDropIndicator();

    /// <remarks>
    /// Клавиатура переходит к холсту: жест кончился на нём, и следующий Ctrl+Z должен отменить бросок, а
    /// стрелки — сдвигать брошенное. Выбор холста её не берёт сам — его ставит и иерархия, и код.
    /// </remarks>
    private void OnDrop(object? sender, StudioDragEventArgs e)
    {
        e.Handled = true;
        _sheet.HideDropIndicator();

        if (Carried(e) is not { Type: { } type } || Place(e) is not { } placement || Intent(type, placement) is not { } intent)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        _sheet.Focus();
        Write(type, intent);
    }

    /// <summary>
    /// Пишет брошенное — своим методом, куда приходят только имена: лямбды одного метода делят замыкание, и
    /// долгая, ждущая сборки, держала бы вместе с размещением ядра живые контролы прежнего поколения.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Write(XamlSnippet type, DropIntent intent) => _ = WriteAsync(type, intent);

    /// <summary>Вставляет тип туда, куда его бросили; контрол проекта без класса — после сборки.</summary>
    internal async Task<bool> WriteAsync(XamlSnippet type, DropIntent intent)
    {
        if (_form.Edits is not { } edits)
            return false;

        if (type.Placeable is { IsBuilt: false } control)
        {
            if (_types is null)
                return false;

            _say(Format("form.drop.building", type.Name));

            bool built;

            try
            {
                built = await _types.EnsureBuiltAsync(control);
            }
            catch (ObjectDisposedException)
            {
                return false;
            }

            if (!built)
            {
                _say(string.Format(CultureInfo.CurrentCulture, _strings["form.drop.unbuilt"], type.Name, control.ClassName));
                return false;
            }
        }
        else if (_types is not null && type.Placeable is null && !await _types.ResolvesAsync(_form.Path, type))
        {
            // Отказ — до правки, а не после: форма, взявшая контрол, которого ничто не разрешит, показала бы
            // пустое место, и бросок выглядел бы так, будто не сделал ничего.
            _say(type.Package is { Length: > 0 } package
                ? string.Format(CultureInfo.CurrentCulture, _strings["form.drop.package"], type.Name, package)
                : Format("form.drop.unresolved", type.Name));

            return false;
        }

        return await edits.InsertAsync(intent.Parent, intent.Index, intent.Fragment, type.Name);
    }

    /// <summary>Что несут: тип, разметку контрола проекта — или отказ с причиной; null — несут чужое.</summary>
    private Carried? Carried(StudioDragEventArgs e)
    {
        if (e.Data.TryGet(XamlDataFormats.Snippet, out var carried))
            return new Carried(carried, null);

        if (e.Data.Files is not [var first, ..])
            return null;

        var file = CanonicalPath.Create(first);

        if (file == _form.Path)
            return new Carried(null, _strings["form.drop.self"]);

        if (_placeables.FirstOrDefault(control => control.Document == file) is { } control)
            return new Carried(TypeMarkup.Of(control), null);

        return new Carried(null, Format("form.drop.notControl", file.FileName));
    }

    /// <summary>Место под курсором, которое назвал дизайнер.</summary>
    private SurfaceDropPlacement? Place(StudioDragEventArgs e) =>
        _sheet.TryResolveDropPlacement(e.GetPosition(_sheet), out var placement) ? placement : null;

    /// <summary>Место именами: путь родителя, номер среди его содержимого и разметка с местом на корне.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private DropIntent? Intent(XamlSnippet type, SurfaceDropPlacement placement)
    {
        if (_form.Shown is not { } shown || _form.Document is not { } document)
            return null;

        if (shown.PathOf(placement.Parent) is not { } parent || parent.Resolve(document.Syntax) is not { } element)
            return null;

        var index = placement switch
        {
            { Kind: SurfaceDropKind.Content } => 0,
            { Anchor: { } anchor } when shown.PathOf(anchor)?.Resolve(document.Syntax) is { IndexInContent: >= 0 } next => next.IndexInContent,
            _ => element.ContentElements.Count(),
        };

        XamlFragment fragment;

        try
        {
            fragment = TypeMarkup.Fragment(type, placement);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            // Разметка, которую принёс плагин, не прочлась фрагментом: ставить нечего.
            return null;
        }

        var name = element.Identity is { Length: > 0 } identity ? $"{element.Name.LocalName} «{identity}»" : element.Name.LocalName;

        return new DropIntent(parent, index, fragment, name);
    }

    private string Format(string key, string value) => string.Format(CultureInfo.CurrentCulture, _strings[key], value);
}

/// <summary>Что несут над холстом: тип или отказ с причиной.</summary>
/// <param name="Type">Тип; null — отказ.</param>
/// <param name="Refusal">Почему нельзя.</param>
internal sealed record Carried(XamlSnippet? Type, string? Refusal);

/// <summary>Куда встанет брошенное — именами, которые замена поколения не отнимет.</summary>
/// <param name="Parent">Путь родителя.</param>
/// <param name="Index">Место среди элементов его содержимого.</param>
/// <param name="Fragment">Разметка с местом на корне.</param>
/// <param name="ParentName">Как назвать родителя у курсора.</param>
internal sealed record DropIntent(XamlElementPath Parent, int Index, XamlFragment Fragment, string ParentName);
