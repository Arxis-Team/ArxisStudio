using ArxisStudio.Modules.Xaml.Session;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Что файл кода объявляет — текстом, без компилятора (запись 423): так служба XAML отвечает окну проекта, какие классы
/// в брошенном файле.
/// </summary>
public class CodeClassesTests
{
    /// <summary>Пространство — файловое и блочное, вложенные блоки складываются; записи — классы.</summary>
    [Fact]
    public void Namespaces_of_both_kinds_name_the_classes_and_a_record_is_a_class()
    {
        var file = CodeClasses.Read("""
            namespace App.ViewModels;

            public sealed partial class MainViewModel : ViewModelBase
            {
            }

            public record Customer(string Name);
            """);

        Assert.Equal(["App.ViewModels.MainViewModel", "App.ViewModels.Customer"], file.Select(static found => found.FullName));

        var blocks = CodeClasses.Read("""
            namespace App
            {
                namespace Models
                {
                    public class Order { }
                }

                public class Shell { }
            }
            """);

        Assert.Equal(["App.Models.Order", "App.Shell"], blocks.Select(static found => found.FullName));
    }

    /// <summary>
    /// Вложенный класс, закрытый класс, структура, интерфейс, перечисление и делегат — не то, что разметка создаст
    /// данными, и в ответе их нет.
    /// </summary>
    [Fact]
    public void Only_public_top_level_classes_are_read()
    {
        var found = CodeClasses.Read("""
            namespace App;

            public class Outer
            {
                public class Nested { }

                public void Make()
                {
                    var local = new { Value = 1 };
                }
            }

            internal class Hidden { }
            class Default { }
            public struct Point { }
            public record struct Size(int Width, int Height);
            public interface IService { }
            public enum Mode { One, Two }
            public delegate void Handler(object sender);
            public class Last { }
            """);

        Assert.Equal(["App.Outer", "App.Last"], found.Select(static item => item.FullName));
    }

    /// <summary>Статический, абстрактный и обобщённый класс помечены: экземпляра документ у них не создаст.</summary>
    [Fact]
    public void Static_abstract_and_generic_classes_are_marked()
    {
        var found = CodeClasses.Read("""
            namespace App;

            public static class Converters { }
            public abstract class ViewModelBase { }
            public class Box<T> where T : class { }
            public class Plain { }
            """).ToDictionary(static item => item.Name);

        Assert.True(found["Converters"].IsStatic, "статический класс не помечен");
        Assert.True(found["ViewModelBase"].IsAbstract, "абстрактный класс не помечен");
        Assert.True(found["Box"].IsGeneric, "обобщённый класс не помечен");
        Assert.False(found["Plain"].IsStatic || found["Plain"].IsAbstract || found["Plain"].IsGeneric, "обычный класс помечен лишним");
    }

    /// <summary>
    /// Скобки и слова в строках, символах, комментариях и директивах не открывают тел и не объявляют классов; код обеих
    /// ветвей <c>#if</c> читается.
    /// </summary>
    [Fact]
    public void Braces_in_strings_comments_and_directives_do_not_confuse_the_reader()
    {
        var found = CodeClasses.Read(""""
            namespace App;

            // public class InComment { }
            /* public class InBlock { } */

            public class Texts
            {
                private const string Brace = "}";
                private const string Verbatim = @"{ "" }";
                private const char Open = '{';
                private string Hole(int x) => $"{(x > 0 ? "{" : "}")} {{ }}";
                private const string Raw = """
                    public class InRaw { }
                    """;
            }

            #if DEBUG
            public class OnlyInDebug { }
            #else
            public class OnlyInRelease { }
            #endif

            public class After { }
            """");

        Assert.Equal(["App.Texts", "App.OnlyInDebug", "App.OnlyInRelease", "App.After"], found.Select(static item => item.FullName));
    }

    /// <summary>Класс, объявленный частями в одном файле, назван один раз; класс вне пространства — глобальный.</summary>
    [Fact]
    public void A_partial_class_is_named_once_and_a_class_outside_namespaces_is_global()
    {
        var found = CodeClasses.Read("""
            public partial class Shell { }
            public partial class Shell { }
            """);

        var shell = Assert.Single(found);

        Assert.Equal(string.Empty, shell.Namespace);
        Assert.Equal("Shell", shell.FullName);
    }
}
