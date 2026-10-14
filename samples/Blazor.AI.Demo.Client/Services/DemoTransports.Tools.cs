using System.Text.Json;

namespace Blazor.AI.Demo.Client.Services;

public static partial class DemoToolTransports
{
    /// <summary>
    /// Calls a tool, waits for the framework to run it, then answers using the result.
    /// </summary>
    /// <param name="toolName">The tool to call.</param>
    /// <param name="arguments">The arguments to send, as JSON.</param>
    /// <param name="answer">The answer to give once the tool has returned.</param>
    /// <returns>The transport.</returns>
    /// <remarks>
    /// The transport does not execute the tool. It asks for one, and on the next round trip it can
    /// read the result off the message it is given. That split is what lets an application decide
    /// where tools run without the transport knowing.
    /// </remarks>
    public static IAiTransport CallsTool(string toolName, string arguments, string answer) =>
        new ScriptedTransport((request, token) => DemoTransports.Stream(token, async yielder =>
        {
            var alreadyCalled = Script.HasToolResultThisTurn(request);

            await yielder(new MessageStartedEvent());

            if (!alreadyCalled)
            {
                await yielder(new StatusEvent("Deciding what to look up"));
                await Script.Pause(500, token);

                var callId = AiId.NewToolCallId();
                await yielder(new ToolCallStartedEvent(callId, toolName));

                foreach (var slice in Chunk(arguments, 6))
                {
                    await Script.Pause(40, token);
                    await yielder(new ToolCallDeltaEvent(callId, slice));
                }

                await yielder(new ToolCallCompletedEvent(callId));
                await yielder(new MessageCompletedEvent(FinishReason.ToolCalls));
                return;
            }

            await foreach (var evt in Script.Write(answer, 4, 12, token)) await yielder(evt);
            await yielder(Script.Usage(request, answer));
            await yielder(new MessageCompletedEvent());
        }));

    /// <summary>
    /// Calls two tools at once, which is where a serial tool UI falls apart.
    /// </summary>
    /// <returns>The transport.</returns>
    public static IAiTransport CallsTwoTools() =>
        new ScriptedTransport((request, token) => DemoTransports.Stream(token, async yielder =>
        {
            var alreadyCalled = Script.HasToolResultThisTurn(request);

            await yielder(new MessageStartedEvent());

            if (!alreadyCalled)
            {
                await Script.Pause(300, token);

                var first = AiId.NewToolCallId();
                var second = AiId.NewToolCallId();

                await yielder(new ToolCallStartedEvent(first, "get_weather")
                {
                    Arguments = Json("""{"city":"Oslo"}"""),
                });
                await Script.Pause(120, token);
                await yielder(new ToolCallStartedEvent(second, "get_weather")
                {
                    Arguments = Json("""{"city":"Lisbon"}"""),
                });

                await yielder(new MessageCompletedEvent(FinishReason.ToolCalls));
                return;
            }

            const string answer =
                "Oslo is cold and grey; Lisbon is mild and clear. If the trip is about the weather, " +
                "the decision makes itself.";

            await foreach (var evt in Script.Write(answer, 4, 12, token)) await yielder(evt);
            await yielder(new MessageCompletedEvent());
        }));

    private static IEnumerable<string> Chunk(string value, int size)
    {
        for (var i = 0; i < value.Length; i += size)
        {
            yield return value.Substring(i, Math.Min(size, value.Length - i));
        }
    }

    internal static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
