using System.ComponentModel;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Tests;

/// <summary>
/// Проект с данными для тестов раздела данных (запись 423): модели, конвертер и класс формы — сборкой выхода, а их
/// объявления — файлами кода в проекте.
/// </summary>
/// <remarks>
/// Сборка собрана без компилятора XAML: у класса формы нет своей разметки, и живой текст заполняет экземпляр после
/// конструктора. Файлы кода — только текст: каталог поколения узнаёт собранное по сборке, а служба читает файл.
/// </remarks>
internal static class DataProject
{
    /// <summary>Модели, конвертер и контрол проекта — то, что собрано.</summary>
    public const string Source = """
        using System;
        using System.ComponentModel;
        using System.Globalization;
        using Avalonia.Controls;
        using Avalonia.Data.Converters;

        namespace App.ViewModels
        {
            public class MainViewModel : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged
                {
                    add { }
                    remove { }
                }

                public string Title => "Главное окно";

                public Customer Customer { get; } = new();
            }

            public class Customer
            {
                public string Name => "Анна";
            }

            public class Shout : IValueConverter
            {
                public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value;

                public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
            }
        }

        namespace App.Views
        {
            public class SelfView : UserControl
            {
                public SelfView() => DataContext = this;

                public string Greeting => "Привет";
            }
        }
        """;

    /// <summary>Файл одной модели — тот, что бросают на поле данных.</summary>
    public const string Model = """
        using System.ComponentModel;

        namespace App.ViewModels;

        public class MainViewModel : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;

            public string Title => "Главное окно";
        }
        """;

    /// <summary>Файл класса формы — контрола, а не данных.</summary>
    public const string View = """
        using Avalonia.Controls;

        namespace App.Views;

        public class SelfView : UserControl
        {
            public SelfView() => DataContext = this;
        }
        """;

    /// <summary>Класс, который файл кода объявляет, а сборка ещё не собрала.</summary>
    public const string Draft = """
        namespace App.ViewModels;

        public class DraftViewModel
        {
        }
        """;

    /// <summary>Кладёт выход проекта: сборку с моделями, конвертером и классом формы.</summary>
    /// <param name="studio">Студия теста.</param>
    public static void Emit(XamlStudio studio)
    {
        ArgumentNullException.ThrowIfNull(studio);

        // Сборка, которую проба компилирует, видна ей, только если уже загружена.
        _ = typeof(INotifyPropertyChanged);

        var name = "DataProject" + Guid.NewGuid().ToString("N")[..8];
        var output = Path.Combine(studio.ProjectFolder, "bin", name + ".dll");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        TestAssembly.EmitFile(output, name, Source);

        studio.AssemblyName = name;
        studio.Output = CanonicalPath.Create(output);
    }
}
