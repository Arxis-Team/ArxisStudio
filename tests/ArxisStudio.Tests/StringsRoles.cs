using System.Diagnostics.CodeAnalysis;
using ArxisStudio.Sdk.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ArxisStudio.Tests;

/// <summary>
/// Роли словарей у входов сборки — так, как их открывает анализатору MSBuild.
/// </summary>
/// <remarks>
/// Таргет SDK и csproj модуля помечают словарь метаданными <c>AxStrings</c>, а компилятор отдаёт их
/// анализатору ключом <see cref="StringsFileAnalyzer.RoleKey"/>. В тесте сборки нет, и роли подаются
/// этим поставщиком — по пути файла.
/// </remarks>
internal sealed class StringsRoles(IReadOnlyDictionary<string, string> roles) : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions => Options.Empty;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
        roles.TryGetValue(textFile.Path, out var role) ? new Options(role) : Options.Empty;

    private sealed class Options(string? role) : AnalyzerConfigOptions
    {
        public static Options Empty { get; } = new(null);

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
        {
            value = key == StringsFileAnalyzer.RoleKey ? role : null;

            return value is not null;
        }
    }
}
