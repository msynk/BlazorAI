# The demo

Twenty-two scenarios, from a three-line chat to a five-hundred-message virtualized transcript.

```bash
dotnet run --project samples/Blazor.AI.Demo
```

No API key is needed. Every response is a scripted `IAiTransport` written in C#, which is the
clearest available demonstration of the framework's central claim: nothing downstream of the
transport knows or cares what produced the events.

## Layout

| Project | Contains |
| --- | --- |
| `Blazor.AI.Demo` | The host: the HTML shell, the error page, the shared stylesheet |
| `Blazor.AI.Demo.Client` | Routing, every page, transport, tool, and component |

The app uses **interactive auto**: pages are prerendered on the server, then take over in
WebAssembly. Both hosts call the same `AddDemo()` registration, because the framework has no
server-only or browser-only pieces. The router lives in the client project for the same reason:
once the WebAssembly runtime is cached, the browser has to be able to load it.

## The tour

The overview at `/` links to every page. Each page has previous and next links, a link to its
source, and an explanation beside the demo. A switcher at the bottom of the navigation sets the
colour scheme for the whole site.

| # | Page | Shows |
| --- | --- | --- |
| 1 | Basic chat | The smallest working chat |
| 2 | Streaming | Progressive Markdown, code, and tables |
| 3 | Reasoning | Thinking shown separately from the answer |
| 4 | Multiple conversations | A sidebar over a conversation store |
| 5 | Attachments | Drag, paste, and pick files |
| 6 | Commands and mentions | Slash commands and @-references in the composer |
| 7 | Citations | Inline markers anchored to claims |
| 8 | Artifacts | A document written and then revised |
| 9 | Structured output | A table that fills in row by row |
| 10 | Tool calling | A tool proposed, run, and answered |
| 11 | Tool approval | A destructive tool that has to be allowed |
| 12 | Custom tool UI | One tool rendered by your component |
| 13 | Parallel tools | Two calls in flight at once |
| 14 | Multi-step agent | A plan, executed and reported |
| 15 | Agent asks a question | A run that blocks on the user |
| 16 | Agent failure | A step that fails, legibly |
| 17 | Generative UI | The model renders your components |
| 18 | AI forms | A schema rendered as a form, checked, submitted |
| 19 | Models and usage | Picker, tokens, cost, context |
| 20 | Errors and retry | Failure that recovers |
| 21 | Long conversations | Five hundred messages, virtualized |
| 22 | Theming | Colour scheme, density, accent, and radius |

## Worth reading

| File | Why |
| --- | --- |
| `Services/ScriptedTransport.cs` | How little a transport has to be |
| `Services/DemoTransports*.cs` | One scripted scenario per capability |
| `Services/DemoCatalog.cs` | Tools and models, defined once |
| `Components/ForecastCard.razor` | A component model output may render |
| `Components/SearchResultCard.razor` | A custom renderer for one tool |
| `Components/BookingForm.razor` | A custom renderer for one structured-output schema |
| `DemoServiceCollectionExtensions.cs` | The whole wiring, in one method |
