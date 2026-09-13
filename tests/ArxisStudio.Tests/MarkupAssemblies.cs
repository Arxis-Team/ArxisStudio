using System.Reflection;
using System.Runtime.CompilerServices;
using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Icons;
using ArxisStudio.Sdk;
using ArxisStudio.Shell;

namespace ArxisStudio.Tests;

/// <summary>
/// Называет сборки студии прежде, чем что-нибудь разберёт разметку на ходу.
/// </summary>
/// <remarks>
/// Разбор разметки на ходу складывает словарь адреса
/// <c>https://github.com/Arxis-Team/ArxisStudio</c> из сборок, которые в этот миг уже загружены в
/// процесс, — и держит сложенное до конца прогона. А грузится сборка лениво: не тогда, когда на
/// неё сослался проект, а тогда, когда её тип назвал метод, дошедший до компиляции.
/// <para>
/// Отсюда и беда. <c>{Text}</c> живёт в <c>ArxisStudio.Sdk</c>, тот же адрес объявляют и контролы,
/// и оболочка, и движок докинга. Если первым разберёт разметку тест, не назвавший ни одного типа
/// SDK, адрес разрешится — его объявили соседи, — а типа <c>Text</c> в нём не окажется, и упадёт
/// не он один, а всё, что до конца прогона попросит <c>{Text}</c>. Виден был такой прогон как
/// <c>XamlX.XamlTransformException: Unable to resolve type Text</c> сразу у пары тестов
/// <see cref="TextExtensionTests"/>, а мигал он оттого, что порядок тестов меняют соседи по
/// параллельному прогону: в одиночку класс начинал с теста, который SDK называл, и беды не знал.
/// </para>
/// <para>
/// Поэтому сборки называются здесь, а не в тесте: инициализатор модуля выполняется один раз и
/// раньше всего в сборке тестов, и обойти его порядком нельзя. Avalonia при этом не трогается —
/// приложения в этот миг ещё нет; берутся только сборки.
/// </para>
/// </remarks>
internal static class MarkupAssemblies
{
    /// <summary>Сборки студии, объявляющие адрес разметки.</summary>
    internal static IReadOnlyList<Assembly> Declaring { get; } =
    [
        typeof(TextExtension).Assembly,
        typeof(AxButton).Assembly,
        typeof(AxIcon).Assembly,
        typeof(StudioShell).Assembly,
        typeof(DockView).Assembly,
    ];

    /// <summary>Загружает их до первого разбора разметки.</summary>
    [ModuleInitializer]
    internal static void Load() => _ = Declaring.Count;
}
