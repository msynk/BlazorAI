using Blazor.AI.Demo.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// The same registration the server uses. Nothing in the framework is host-specific, so a page can
// be prerendered on the server and then take over in the browser with identical services.
builder.Services.AddDemo();

await builder.Build().RunAsync();
