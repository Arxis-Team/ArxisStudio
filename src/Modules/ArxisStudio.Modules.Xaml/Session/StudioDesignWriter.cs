using ArxisStudio.Markup;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;

namespace ArxisStudio.Modules.Xaml.Session;

/// <summary>
/// Запись документа хостом — службой файлов студии, а не потоком на диск (ADR 0032 ProjectSystem).
/// </summary>
/// <remarks>
/// <para>
/// Так сохранение дизайнера — сохранение студии: прежнее содержимое уходит в локальную историю
/// действием сохранения, слежение узнаёт запись по отпечатку, а файл, переписанный мимо документа, не
/// затирается — запись сверяется с тем, что документ считает содержимым файла.
/// </para>
/// <para>
/// Ожидаемое — сохранённый текст документа в его кодировке и с его меткой порядка байтов: так он
/// прочитан из файла. Файл с байтами, которых кодировка не выражает, сверку не пройдёт никогда — его
/// текст уже не тот, что на диске, — и сохранение откажет, а не перепишет его молча.
/// </para>
/// </remarks>
/// <param name="files">Служба файлов.</param>
/// <param name="expected">Что документ файла считает его содержимым; null — не сверять.</param>
/// <param name="words">Слова модуля: имя действия истории и отказ.</param>
internal sealed class StudioDesignWriter(
    IStudioFiles files,
    Func<CanonicalPath, ReadOnlyMemory<byte>?> expected,
    XamlWords words) : IProjectDesignWriter
{
    /// <inheritdoc/>
    public async ValueTask WriteAsync(CanonicalPath file, SourceText text, CancellationToken cancellationToken)
    {
        var write = new FileWrite(file, Encode(text)) { Expected = expected(file) };
        var result = await files.WriteAsync([write], words.Save(file.FileName), cancellationToken);

        if (result.Status != ProjectOperationStatus.Succeeded)
        {
            var reason = result.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == ProjectDiagnosticSeverity.Error)?.Message
                ?? result.Status.ToString();

            throw new IOException(words.SaveRefused(file.FileName, reason));
        }
    }

    /// <summary>Байты текста — так, как он записался бы на диск: метка порядка, если была, и кодировка.</summary>
    /// <param name="text">Текст.</param>
    public static byte[] Encode(SourceText text)
    {
        var preamble = text.HasByteOrderMark ? text.Encoding.GetPreamble() : [];
        var body = text.Encoding.GetBytes(text.ToString());

        return [.. preamble, .. body];
    }
}
