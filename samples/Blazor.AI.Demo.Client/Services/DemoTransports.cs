using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Blazor.AI.Demo.Client.Services;

/// <summary>
/// The scripted responses behind each demo page.
/// </summary>
public static class DemoTransports
{
    private const string ModelId = "demo-model-1";

    /// <summary>Echoes the prompt, the smallest useful transport.</summary>
    /// <returns>The transport.</returns>
    public static IAiTransport Echo() => new ScriptedTransport((request, token) => Stream(token, async yielder =>
    {
        var reply = $"You said: {Script.LastUserText(request)}";

        await yielder(new MessageStartedEvent());
        await foreach (var evt in Script.Write(reply, 3, 12, token)) await yielder(evt);
        await yielder(Script.Usage(request, reply));
        await yielder(new MessageCompletedEvent());
    }));

    /// <summary>A full Markdown answer: headings, lists, a table, and code.</summary>
    /// <returns>The transport.</returns>
    public static IAiTransport Markdown() => new ScriptedTransport((request, token) => Stream(token, async yielder =>
    {
        await yielder(new MessageStartedEvent());
        await yielder(new MetadataEvent(new GenerationMetadata { ModelId = ModelId, ContextWindow = 128_000 }));

        const string answer = """
            Blazor renders in two places, and the difference matters.

            ## Where the code runs

            | Mode | Runs on | First paint | Best for |
            | --- | --- | :---: | --- |
            | Server | The server, over a circuit | Fast | Data-heavy internal apps |
            | WebAssembly | The browser | Slower | Offline and low-latency work |

            ### What that means for streaming

            Every update on **Server** is a diff sent down a network connection, so the cost of
            rendering is measured in *messages*, not milliseconds. That is why this framework
            coalesces renders instead of drawing every token:

            ```csharp
            var scheduler = new RenderScheduler(
                () => InvokeAsync(StateHasChanged),
                TimeSpan.FromMilliseconds(50));

            foreach (var token in tokens)
            {
                part.Append(token);
                scheduler.Request();
            }
            ```

            - On WebAssembly this saves CPU.
            - On Server it saves bandwidth, which matters more.
            - Either way the user sees the same thing.

            > Rendering more often than the eye can follow is not smoother. It is just louder.
            """;

        await foreach (var evt in Script.Write(answer, 5, 10, token)) await yielder(evt);
        await yielder(Script.Usage(request, answer));
        await yielder(new MessageCompletedEvent());
    }));

    /// <summary>Visible reasoning before the answer.</summary>
    /// <returns>The transport.</returns>
    public static IAiTransport Reasoning() => new ScriptedTransport((request, token) => Stream(token, async yielder =>
    {
        await yielder(new MessageStartedEvent());

        const string thinking =
            "The user is asking about the cost of a round trip. I should separate the fixed cost of " +
            "the connection from the per-token cost, and give a number rather than an adjective.";

        await foreach (var evt in Script.Think(thinking, 12, token)) await yielder(evt);
        await Script.Pause(250, token);

        const string answer = """
            Two costs are worth separating.

            **Connection setup** is roughly constant: one TLS handshake, one request, and whatever
            the provider spends before the first token. Call it 200 to 600 milliseconds.

            **Generation** scales with output length at the model throughput, so a 600 token answer
            at 80 tokens a second is about 7.5 seconds regardless of how fast your network is.

            Optimising the first without measuring the second is the usual mistake.
            """;

        await foreach (var evt in Script.Write(answer, 4, 12, token)) await yielder(evt);
        await yielder(Script.Usage(request, answer));
        await yielder(new MessageCompletedEvent(FinishReason.Stop));
    }));

    /// <summary>
    /// Turns a sequential script into an event stream, so a scenario reads top to bottom.
    /// </summary>
    /// <param name="token">Cancels the script.</param>
    /// <param name="body">The script, which yields events by calling the supplied delegate.</param>
    /// <returns>The event stream.</returns>
    internal static async IAsyncEnumerable<AiEvent> Stream(
        [EnumeratorCancellation] CancellationToken token,
        Func<Func<AiEvent, Task>, Task> body)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<AiEvent>();

        var producer = Task.Run(async () =>
        {
            try
            {
                await body(evt => channel.Writer.WriteAsync(evt, token).AsTask());
                channel.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                channel.Writer.TryComplete(exception);
            }
        }, token);

        await foreach (var evt in channel.Reader.ReadAllAsync(token)) yield return evt;
        await producer;
    }
}
