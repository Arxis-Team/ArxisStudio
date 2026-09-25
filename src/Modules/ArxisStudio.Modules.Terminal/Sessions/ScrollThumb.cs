namespace ArxisStudio.Modules.Terminal.Sessions;

/// <summary>
/// Бегунок полосы прокрутки терминала: где стоит, какой длины и сколько ему ходу.
/// </summary>
/// <remarks>
/// Одно место на рисунок, на попадание и на протяжку. Разойдясь, они дали бы полосу, которая берётся
/// не там, где нарисована, — и починить такое можно только вернув их в одно место. Своим типом, без
/// вида: геометрию проверяет тест, которому окно не нужно.
/// <para>
/// Мерится по <c>YBase</c> — самой нижней строке, на которую можно встать, — а не по длине списка
/// строк: список кольцевой, и длина у него бывает больше, чем есть куда вставать. Прежде на этой
/// разнице бегунок не доходил до низа дорожки: на живом сеансе <c>cmd</c> при двухстах строках
/// истории он кончался на одиннадцать точек выше её дна, и низ бегунка оказывался дорожкой —
/// нажатие туда листало страницу вниз вместо того, чтобы взять бегунок.
/// </para>
/// </remarks>
/// <param name="Top">Верх бегунка в координатах вида.</param>
/// <param name="Height">Длина бегунка.</param>
/// <param name="Travel">Сколько бегунку ходу от верха дорожки до низа.</param>
/// <param name="Max">Самая нижняя строка истории, на которую можно встать.</param>
public readonly record struct ScrollThumb(double Top, double Height, double Travel, int Max)
{
    /// <summary>Короче бегунок не бывает: на длинной истории он выродился бы в точку.</summary>
    public const double MinHeight = 20;

    /// <summary>Бегунок экрана; пусто — истории нет, и полосы тоже.</summary>
    /// <param name="inset">Отступ дорожки от верха и низа вида.</param>
    /// <param name="height">Высота вида.</param>
    /// <param name="rows">Строк на экране.</param>
    /// <param name="max">Самая нижняя строка, на которую можно встать, — <c>YBase</c>.</param>
    /// <param name="shown">Верхняя показанная строка — <c>YDisp</c>.</param>
    public static ScrollThumb? Of(double inset, double height, int rows, int max, int shown)
    {
        if (max <= 0)
            return null;

        var track = height - (2 * inset);
        var length = Math.Max(MinHeight, track * rows / (max + rows));
        var travel = Math.Max(0, track - length);

        return new ScrollThumb(inset + (travel * shown / max), length, travel, max);
    }

    /// <summary>Приходится ли точка на бегунок, а не на дорожку.</summary>
    /// <param name="y">Точка в координатах вида.</param>
    public bool Holds(double y) => y >= Top && y < Top + Height;

    /// <summary>
    /// Строка, на которую встать, когда бегунок тянут: обратный ход того же расчёта, которым он нарисован.
    /// </summary>
    /// <param name="y">Где указатель.</param>
    /// <param name="grab">Где бегунок взяли — от его верха.</param>
    /// <param name="inset">Отступ дорожки от верха вида.</param>
    /// <remarks>
    /// От точки захвата, а не от верха бегунка: взятый за середину, он и тянется за середину — иначе
    /// на первом же движении прыгнул бы под курсор.
    /// </remarks>
    public int LineAt(double y, double grab, double inset) =>
        Math.Clamp((int)Math.Round((y - grab - inset) * Max / Math.Max(1, Travel)), 0, Max);
}
