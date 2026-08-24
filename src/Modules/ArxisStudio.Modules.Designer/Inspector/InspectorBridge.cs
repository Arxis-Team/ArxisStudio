using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Designer;

/// <summary>
/// Строка инспектора в том виде, в каком её видит плагин.
/// </summary>
/// <remarks>
/// Мост, а не копия: рисовальщик читает и пишет ту же строку, которую показывает
/// студия, поэтому правка из его контрола идёт обычным путём — через документ,
/// с проверкой и в общую историю.
/// </remarks>
/// <param name="row">Строка инспектора.</param>
/// <param name="commit">Чем записать новое значение.</param>
public sealed class RowPropertyContext(InspectorRow row, Func<InspectorRow, string?, Task> commit) : IPropertyContext
{
    /// <inheritdoc/>
    public string Name => row.Name;

    /// <inheritdoc/>
    public Type ValueType => row.ValueType ?? typeof(string);

    /// <inheritdoc/>
    public string? Value => row.Value;

    /// <inheritdoc/>
    public string? Effective => row.Value ?? row.Placeholder;

    /// <inheritdoc/>
    public bool IsSet => row.IsSet;

    /// <inheritdoc/>
    public event EventHandler? Changed
    {
        add => row.Refilled += value;
        remove => row.Refilled -= value;
    }

    /// <inheritdoc/>
    public void Set(string? value) => _ = commit(row, value);
}

/// <summary>Выделенный элемент в том виде, в каком его видит плагин.</summary>
/// <param name="Node">Узел дерева документа.</param>
/// <param name="Properties">Строки инспектора, обёрнутые для плагина.</param>
/// <param name="Studio">Что студия даёт плагину.</param>
public sealed record ElementInspectorContext(
    HierarchyNode Node,
    IReadOnlyList<IPropertyContext> Properties,
    IStudioContext Studio) : IInspectorContext
{
    /// <inheritdoc/>
    public string TypeName => Node.TypeName;

    /// <inheritdoc/>
    public string? ElementName => Node.Identity;
}
