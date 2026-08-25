using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Basalt.Core.Services;

namespace Basalt.Workspace.Ai;

/// <summary>
/// A conversation with an assistant, with the project's context.
///
/// The history is kept here rather than in the panel: an assistant answers
/// better for having seen what was already said, and the panel should not have
/// to know that.
/// </summary>
public sealed class AiConversation
{
    /// <summary>
    /// How many turns are carried forward.
    ///
    /// A whole conversation would eventually exceed what the assistant reads,
    /// and the oldest turns are the least relevant.
    /// </summary>
    private const int RememberedTurns = 20;

    private readonly List<ChatMessage> _history = [];

    /// <summary>The assistants the IDE can talk to.</summary>
    public static IReadOnlyList<IAiProvider> Providers { get; } =
    [
        new ClaudeProvider(),
        new OpenAiProvider(),
        new GeminiProvider()
    ];

    public static IAiProvider ProviderFor(AiVendor vendor) =>
        Providers.First(p => p.Vendor == vendor);

    public IReadOnlyList<ChatMessage> History => _history;

    public void Clear() => _history.Clear();

    /// <summary>
    /// Asks the assistant, and yields the reply as it arrives.
    ///
    /// The context is attached to the question rather than kept in the
    /// history: the file changes as the user edits, and a stale copy from
    /// three turns ago would mislead.
    /// </summary>
    public async IAsyncEnumerable<string> AskAsync(
        IChatClient client,
        string question,
        string? context = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var prompt = context is { Length: > 0 }
            ? $"{context}\n\n{question}"
            : question;

        var messages = new List<ChatMessage> { new(ChatRole.System, SystemPrompt) };

        messages.AddRange(_history.TakeLast(RememberedTurns));
        messages.Add(new ChatMessage(ChatRole.User, prompt));

        var reply = new System.Text.StringBuilder();

        await foreach (var update in client
            .GetStreamingResponseAsync(messages, cancellationToken: ct)
            .ConfigureAwait(false))
        {
            if (update.Text is not { Length: > 0 } text) continue;

            reply.Append(text);
            yield return text;
        }

        // The question is recorded without its context, which is what keeps
        // the history small and current.
        _history.Add(new ChatMessage(ChatRole.User, question));
        _history.Add(new ChatMessage(ChatRole.Assistant, reply.ToString()));
    }

    /// <summary>
    /// What the assistant is told about its job.
    ///
    /// It is told the IDE is for Visual Basic because otherwise it answers in
    /// C#, which is what nearly every example on the internet is written in.
    /// </summary>
    public const string SystemPrompt =
        "You are helping inside Basalt, an IDE for Visual Basic .NET and the Basic "
      + "dialects, running on macOS, Windows and Linux. Answer in Visual Basic "
      + "unless the user's code is in another language. Be brief. When you show "
      + "code, put it in a fenced block so it can be applied to the file.";

    /// <summary>
    /// Turns a chosen action into the instruction it stands for.
    ///
    /// The user picks "Explain" and types nothing; the assistant needs a
    /// sentence.
    /// </summary>
    public static string InstructionFor(string action, string question) => action switch
    {
        "Explain" => Combine(
            "Explain what this code does, briefly.", question),

        "Fix the error" => Combine(
            "Fix the errors listed above. Show the corrected code.", question),

        "Generate tests" => Combine(
            "Write xunit tests in Visual Basic for this code.", question),

        "Document" => Combine(
            "Write XML documentation comments for this code, saying why rather "
          + "than restating what it does.", question),

        _ => question
    };

    private static string Combine(string instruction, string question) =>
        question.Length == 0 ? instruction : $"{instruction}\n\n{question}";
}
