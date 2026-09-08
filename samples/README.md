# The demo

Twenty scenarios, from a three-line chat to a five-hundred-message virtualized transcript.

```bash
dotnet run --project samples/Blazor.AI.Demo
```

No API key is needed. Every response is a scripted `IAiTransport` written in C#, which is the
clearest available demonstration of the framework's central claim: nothing downstream of the
transport knows or cares what produced the events.

## Layout

| Project | Contains |
| --- | --- |
| `Blazor.AI.Demo` | The host: routing, the app shell, the shared stylesheet |
| `Blazor.AI.Demo.Client` | Every page, transport, tool, and component |

The app uses **interactive auto**: pages are prerendered on the server, then take over in
WebAssembly. Both hosts call the same `AddDemo()` registration, because the framework has no
server-only or browser-only pieces.

## The tour

| # | Page | Shows |
| --- | --- | --- |
| 1 | Basic chat | The smallest working chat |
| 2 | Streaming | Progressive Markdown, code, and tables |
| 3 | Reasoning | Thinking shown separately from the answer |
| 4 | Multiple conversations | A sidebar over a store |
| 5 | Attachments | Drag, paste, and pick files |
| 6 | Citations | Inline markers anchored to claims |
| 7 | Artifacts | A document written and then revised |
| 8 | Structured output | A table that fills in row by row |
| 9 | Tool calling | A tool proposed, run, and answered |
| 10 | Tool approval | A destructive tool that has to be allowed |
| 11 | Custom tool UI | One tool rendered by your component |
| 12 | Parallel tools | Two calls in flight at once |
| 13 | Multi-step agent | A plan, executed and reported |
| 14 | Agent asks a question | A run that blocks on the user |
| 15 | Agent failure | A step that fails, legibly |
| 16 | Generative UI | The model renders your components |
| 17 | AI forms | A form proposed, checked, submitted |
| 18 | Models and usage | Picker, tokens, cost, context |
| 19 | Errors and retry | Failure that recovers |
| 20 | Long conversations | Five hundred messages, virtualized |

## Worth reading

| File | Why |
| --- | --- |
| `Services/ScriptedTransport.cs` | How little a transport has to be |
| `Services/DemoTransports*.cs` | One scripted scenario per capability |
| `Services/DemoCatalog.cs` | Tools and models, defined once |
| `Components/ForecastCard.razor` | A component model output may render |
| `Components/SearchResultCard.razor` | A custom renderer for one tool |
| `DemoServiceCollectionExtensions.cs` | The whole wiring, in forty lines |
