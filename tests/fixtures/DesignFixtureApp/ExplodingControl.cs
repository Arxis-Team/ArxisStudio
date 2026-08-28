using System;
using Avalonia.Controls;

namespace DesignFixtureApp;

/// <summary>
/// Контрол, который не строится.
/// </summary>
/// <remarks>
/// Нужен тестам отката: правка разметки, доведённая до живых объектов, должна
/// упасть где-то посередине — между записью в документ и построением дерева, —
/// и это единственное место, где такое падение устраивается честно.
/// </remarks>
public sealed class ExplodingControl : Control
{
    /// <summary>Падает при создании.</summary>
    public ExplodingControl() => throw new InvalidOperationException("контрол не строится");
}
