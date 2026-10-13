namespace ArxisStudio.Xaml;

/// <summary>Что привязка может назвать на типе, к которому привёл её путь, — именами.</summary>
/// <param name="Type">Тип, чьи члены перечислены.</param>
/// <param name="Members">Члены по имени.</param>
/// <remarks>Имена, а не типы: тип — объект поколения. Появилось в контракте 1.4.</remarks>
public sealed record XamlBindableMembers(XamlTypeInfo Type, IReadOnlyList<XamlBindableMemberInfo> Members);

/// <summary>Член, который привязка может назвать на своём источнике.</summary>
/// <param name="Name">Имя, как его пишет путь привязки.</param>
/// <param name="TypeName">Тип, как его пишет читатель кода: <c>string</c>, <c>int?</c>, <c>ObservableCollection&lt;Customer&gt;</c>.</param>
/// <param name="CanWrite">Двусторонняя привязка может в него писать.</param>
/// <param name="IsCollection">Держит коллекцию — то, к чему привязывают источник элементов списка.</param>
/// <param name="IsCommand">Держит команду — то, к чему привязывают команду кнопки.</param>
/// <param name="HasMembers">Держит объект со своими членами: путь может шагнуть в него дальше.</param>
/// <param name="Fits">
/// Значение подходит члену, к которому привязывают: тип тот же, приводимый или число к числу; к тексту и к объекту
/// подходит всё.
/// </param>
/// <remarks>Появилось в контракте 1.4.</remarks>
public sealed record XamlBindableMemberInfo(
    string Name,
    string TypeName,
    bool CanWrite,
    bool IsCollection,
    bool IsCommand,
    bool HasMembers,
    bool Fits);
