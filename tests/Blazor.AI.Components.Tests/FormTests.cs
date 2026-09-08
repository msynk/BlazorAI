using System.Text.Json;

namespace Blazor.AI.Components.Tests;

public sealed class FormTests : AiTestBase
{
    private static AiFormDefinition Definition => new()
    {
        Title = "Book a table",
        Fields =
        [
            new AiFormField("guests") { Type = AiFieldType.Number, Required = true, Minimum = 1, Maximum = 8 },
            new AiFormField("email") { Type = AiFieldType.Email },
            new AiFormField("notes") { Type = AiFieldType.MultilineText },
        ],
    };

    [Fact]
    public void EveryFieldGetsALabelBoundToItsControl()
    {
        var component = Render<AiForm>(p => p.Add(c => c.Definition, Definition));

        var label = component.Find("label[for]");
        var id = label.GetAttribute("for");

        Assert.False(string.IsNullOrEmpty(id));
        Assert.NotNull(component.Find("#" + id));
        Assert.Contains("Guests", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SubmittingWithAMissingRequiredFieldReportsItAndDoesNotSubmit()
    {
        IReadOnlyDictionary<string, string?>? submitted = null;
        var component = Render<AiForm>(p => p
            .Add(c => c.Definition, Definition)
            .Add(c => c.OnSubmit, values => submitted = values));

        component.FindAll("button").Last().Click();

        Assert.Null(submitted);
        Assert.Contains("is required", component.Markup, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AValidFormSubmitsItsValues()
    {
        IReadOnlyDictionary<string, string?>? submitted = null;
        var component = Render<AiForm>(p => p
            .Add(c => c.Definition, Definition)
            .Add(c => c.OnSubmit, values => submitted = values));

        component.Find("#" + component.Find("label[for]").GetAttribute("for")).Input("4");
        component.FindAll("button").Last().Click();

        Assert.NotNull(submitted);
        Assert.Equal("4", submitted!["guests"]);
    }

    [Fact]
    public void NumericBoundsAreEnforced()
    {
        var component = Render<AiForm>(p => p.Add(c => c.Definition, Definition));

        component.Find("#" + component.Find("label[for]").GetAttribute("for")).Input("20");
        component.FindAll("button").Last().Click();

        Assert.Contains("at most 8", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void FieldsStreamingInDoNotClearWhatTheUserHasTyped()
    {
        var component = Render<AiForm>(p => p.Add(c => c.Definition, Definition));
        component.Find("#" + component.Find("label[for]").GetAttribute("for")).Input("3");

        component.Render(p => p.Add(c => c.Definition, Definition with
        {
            Fields = [.. Definition.Fields, new AiFormField("seating")],
        }));

        Assert.Contains("Seating", component.Markup, StringComparison.Ordinal);
        Assert.Equal("3", component.Instance.Values["guests"]);
    }

    [Fact]
    public void AFormBuiltFromJsonSchemaPicksSensibleControls()
    {
        var schema = JsonDocument.Parse("""
            {
              "title": "Contact",
              "type": "object",
              "required": ["email"],
              "properties": {
                "email": { "type": "string", "format": "email" },
                "age": { "type": "integer", "minimum": 0 },
                "subscribed": { "type": "boolean" },
                "plan": { "type": "string", "enum": ["free", "pro"] }
              }
            }
            """).RootElement;

        var definition = AiFormDefinition.FromJsonSchema(schema);

        Assert.Equal("Contact", definition.Title);
        Assert.Equal(AiFieldType.Email, definition.Fields.Single(f => f.Name == "email").Type);
        Assert.Equal(AiFieldType.Number, definition.Fields.Single(f => f.Name == "age").Type);
        Assert.Equal(AiFieldType.Boolean, definition.Fields.Single(f => f.Name == "subscribed").Type);
        Assert.Equal(AiFieldType.Select, definition.Fields.Single(f => f.Name == "plan").Type);
        Assert.True(definition.Fields.Single(f => f.Name == "email").Required);
    }
}
