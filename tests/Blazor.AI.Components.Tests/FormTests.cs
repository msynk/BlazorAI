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

    [Fact]
    public void ANullableTypeListAndJunkKeywordsDoNotThrow()
    {
        var schema = JsonDocument.Parse("""
            {
              "title": 5,
              "type": "object",
              "required": [1, "nickname", null],
              "properties": {
                "nickname": { "type": ["string", "null"], "title": 7, "description": ["x"] },
                "age": { "type": ["null", "integer"] },
                "odd": { "type": 3 }
              }
            }
            """).RootElement;

        var definition = AiFormDefinition.FromJsonSchema(schema);

        Assert.Null(definition.Title);
        var nickname = definition.Fields.Single(f => f.Name == "nickname");
        Assert.Equal(AiFieldType.Text, nickname.Type);
        Assert.False(nickname.Required);
        Assert.Equal("Nickname", nickname.DisplayLabel);
        var age = definition.Fields.Single(f => f.Name == "age");
        Assert.Equal(AiFieldType.Number, age.Type);
        Assert.True(age.Integer);
        Assert.Equal(AiFieldType.Text, definition.Fields.Single(f => f.Name == "odd").Type);
    }

    [Fact]
    public void SchemaKeywordsMapOntoFieldConstraints()
    {
        var schema = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "qty": { "type": "integer", "exclusiveMinimum": 0, "maximum": 10, "multipleOf": 2, "default": 4 },
                "legacy": { "type": "number", "minimum": 1, "exclusiveMinimum": true },
                "code": { "type": "string", "minLength": 2, "maxLength": 4, "readOnly": true },
                "kind": { "const": "booking" },
                "plan": { "type": "string", "enum": ["free", "pro"], "enumNames": ["Free", "Professional"] },
                "size": { "oneOf": [ { "const": "s", "title": "Small" }, { "const": "l", "title": "Large" } ] },
                "when": { "type": "string", "format": "time" },
                "site": { "type": "string", "format": "uri" },
                "at": { "anyOf": [ { "type": "string", "format": "date-time" }, { "type": "null" } ] }
              },
              "required": ["at"]
            }
            """).RootElement;

        var fields = AiFormDefinition.FromJsonSchema(schema).Fields.ToDictionary(f => f.Name);

        Assert.Equal(0, fields["qty"].ExclusiveMinimum);
        Assert.Equal(10, fields["qty"].Maximum);
        Assert.Equal(2, fields["qty"].MultipleOf);
        Assert.Equal("4", fields["qty"].Value);
        Assert.Equal(1, fields["legacy"].ExclusiveMinimum);
        Assert.Null(fields["legacy"].Minimum);
        Assert.Equal(2, fields["code"].MinLength);
        Assert.Equal(4, fields["code"].MaxLength);
        Assert.True(fields["code"].ReadOnly);
        Assert.Equal("booking", fields["kind"].Value);
        Assert.True(fields["kind"].ReadOnly);
        Assert.Equal(["Free", "Professional"], fields["plan"].OptionLabels);
        Assert.Equal(AiFieldType.Select, fields["size"].Type);
        Assert.Equal(["s", "l"], fields["size"].Options);
        Assert.Equal(["Small", "Large"], fields["size"].OptionLabels);
        Assert.Equal(AiFieldType.Time, fields["when"].Type);
        Assert.Equal(AiFieldType.Url, fields["site"].Type);
        Assert.Equal(AiFieldType.DateTime, fields["at"].Type);
        Assert.False(fields["at"].Required);
    }

    [Fact]
    public void NestedObjectsArraysAndLocalReferencesAreFollowed()
    {
        var schema = JsonDocument.Parse("""
            {
              "type": "object",
              "$defs": { "address": { "type": "object", "properties": { "city": { "type": "string" } }, "required": ["city"] } },
              "definitions": { "tag": { "type": "string", "maxLength": 12 } },
              "properties": {
                "home": { "$ref": "#/$defs/address", "title": "Home address" },
                "tags": { "type": "array", "items": { "$ref": "#/definitions/tag" }, "minItems": 1, "maxItems": 3 },
                "guests": { "type": "array", "items": { "type": "object", "properties": { "name": { "type": "string" }, "vip": { "type": "boolean" } } } },
                "loop": { "$ref": "#/properties/loop" }
              }
            }
            """).RootElement;

        var fields = AiFormDefinition.FromJsonSchema(schema).Fields.ToDictionary(f => f.Name);

        var home = fields["home"];
        Assert.Equal(AiFieldType.Object, home.Type);
        Assert.Equal("Home address", home.Label);
        Assert.True(Assert.Single(home.Fields!).Required);

        var tags = fields["tags"];
        Assert.Equal(AiFieldType.Array, tags.Type);
        Assert.Equal(12, tags.Items!.MaxLength);
        Assert.Equal((1, 3), (tags.MinItems, tags.MaxItems));

        var guests = fields["guests"];
        Assert.Equal(AiFieldType.Object, guests.Items!.Type);
        Assert.Equal(["name", "vip"], guests.Items.Fields!.Select(f => f.Name));

        Assert.Equal(AiFieldType.Text, fields["loop"].Type);
    }

    [Fact]
    public void AnIntegerFieldRejectsFractions()
    {
        var definition = new AiFormDefinition
        {
            Fields = [new AiFormField("seats") { Type = AiFieldType.Number, Integer = true, MultipleOf = 2 }],
        };
        var form = Render<AiForm>(p => p.Add(c => c.Definition, definition));

        form.Find("input").Input("1.5");
        form.Find("button.bai-btn--primary").Click();
        Assert.Contains("whole number", form.Find(".bai-field__error").TextContent, StringComparison.Ordinal);

        form.Find("input").Input("3");
        form.Find("button.bai-btn--primary").Click();
        Assert.Contains("multiple of 2", form.Find(".bai-field__error").TextContent, StringComparison.Ordinal);

        form.Find("input").Input("4");
        form.Find("button.bai-btn--primary").Click();
        Assert.Empty(form.FindAll(".bai-field__error"));
    }

    [Fact]
    public void ControlsCarryTheAccessibilityAttributesTheirStateCallsFor()
    {
        var definition = new AiFormDefinition
        {
            Fields = [new AiFormField("guest name") { Required = true, Description = "As on the booking." }],
        };
        var form = Render<AiForm>(p => p.Add(c => c.Definition, definition));

        var input = form.Find("input");
        var id = input.Id;
        Assert.Matches("^[A-Za-z0-9_-]+$", id);
        Assert.Equal(id, form.Find("label").GetAttribute("for"));
        Assert.Equal("true", input.GetAttribute("aria-required"));
        Assert.True(input.HasAttribute("required"));
        Assert.Equal($"{id}-hint", input.GetAttribute("aria-describedby"));
        Assert.Equal($"{id}-hint", form.Find(".bai-field__hint").Id);
        Assert.False(input.HasAttribute("aria-invalid"));

        form.Find("button.bai-btn--primary").Click();

        input = form.Find("input");
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Equal($"{id}-hint {id}-error", input.GetAttribute("aria-describedby"));
        Assert.Equal($"{id}-error", form.Find("[role=alert]").Id);
    }

    [Fact]
    public void ADifferentSchemaWithTheSameFieldCountIsSeededAfresh()
    {
        var first = new AiFormDefinition { Fields = [new AiFormField("city") { Value = "Oslo" }, new AiFormField("notes")] };
        var form = Render<AiForm>(p => p.Add(c => c.Definition, first));
        form.FindAll("input")[1].Input("bring cake");
        Assert.Equal("Oslo", form.Instance.Values["city"]);

        var second = new AiFormDefinition { Fields = [new AiFormField("city") { Value = "Paris" }, new AiFormField("budget") { Type = AiFieldType.Number }] };
        form.Render(p => p.Add(c => c.Definition, second));

        Assert.Equal("Paris", form.Instance.Values["city"]);
        Assert.False(form.Instance.Values.ContainsKey("notes"));
        Assert.Equal("Paris", form.FindAll("input")[0].GetAttribute("value"));
    }

    [Fact]
    public void ARebuiltDefinitionOfTheSameShapeKeepsWhatWasTyped()
    {
        static AiFormDefinition Build() => AiFormDefinition.FromJsonSchema(JsonDocument.Parse("""
            { "type": "object", "properties": { "plan": { "type": "string", "enum": ["free", "pro"] }, "city": { "type": "string" } } }
            """).RootElement);

        var form = Render<AiForm>(p => p.Add(c => c.Definition, Build()));
        form.Find("select").Change("pro");
        form.Find("input").Input("Oslo");

        form.Render(p => p.Add(c => c.Definition, Build()));

        Assert.Equal("pro", form.Instance.Values["plan"]);
        Assert.Equal("Oslo", form.Instance.Values["city"]);
    }

    [Fact]
    public void SubmittingProducesJsonInTheShapeOfTheSchema()
    {
        var schema = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "guests": { "type": "integer" },
                "vip": { "type": "boolean", "default": true },
                "contact": { "type": "object", "properties": { "email": { "type": "string", "format": "email" }, "phone": { "type": "string" } } },
                "tags": { "type": "array", "items": { "type": "string" }, "default": ["window"] },
                "people": { "type": "array", "items": { "type": "object", "properties": { "name": { "type": "string" }, "age": { "type": "number" } } } }
              }
            }
            """).RootElement;
        var definition = AiFormDefinition.FromJsonSchema(schema);
        JsonElement? json = null;
        var form = Render<AiForm>(p => p
            .Add(c => c.Definition, definition)
            .Add(c => c.OnSubmitJson, j => json = j));

        form.Find("input[type=number]").Input("4");
        form.Find("input[type=email]").Input("ada@example.com");
        // Item inputs in document order: tags[0], tags[1], then people[0].name, people[0].age.
        // Re-queried after every event, because each one re-renders the tree.
        form.FindAll("button.bai-form__add")[0].Click();                 // tags[1]
        form.FindAll(".bai-form__item input")[1].Input("quiet");
        form.FindAll("button.bai-form__add")[1].Click();                 // people[0]
        form.FindAll(".bai-form__item input")[2].Input("Ada");
        form.FindAll(".bai-form__item input")[3].Input("36.5");
        form.Find("button.bai-btn--primary").Click();

        Assert.NotNull(json);
        var root = json!.Value;
        Assert.Equal(4, root.GetProperty("guests").GetInt32());
        Assert.True(root.GetProperty("vip").GetBoolean());
        Assert.Equal("ada@example.com", root.GetProperty("contact").GetProperty("email").GetString());
        Assert.False(root.GetProperty("contact").TryGetProperty("phone", out _));
        Assert.Equal(["window", "quiet"], root.GetProperty("tags").EnumerateArray().Select(e => e.GetString()));
        var people = root.GetProperty("people");
        Assert.Equal(1, people.GetArrayLength());
        Assert.Equal("Ada", people[0].GetProperty("name").GetString());
        Assert.Equal(36.5, people[0].GetProperty("age").GetDouble());
    }

    [Fact]
    public void RemovingAListEntryClosesTheGapAndItemBoundsAreEnforced()
    {
        var definition = new AiFormDefinition
        {
            Fields = [new AiFormField("tags") { Type = AiFieldType.Array, Items = new AiFormField("tag"), MinItems = 2, Value = """["a","b","c"]""" }],
        };
        var form = Render<AiForm>(p => p.Add(c => c.Definition, definition));
        Assert.Equal(3, form.FindAll(".bai-form__item").Count);

        form.FindAll("button.bai-form__remove")[1].Click();

        Assert.Equal(["a", "c"], form.FindAll(".bai-form__item input").Select(i => i.GetAttribute("value")));
        Assert.Equal("c", form.Instance.Values["tags[1]"]);
        Assert.False(form.Instance.Values.ContainsKey("tags[2]"));

        form.FindAll("button.bai-form__remove")[0].Click();
        form.Find("button.bai-btn--primary").Click();
        Assert.Contains("at least 2 items", form.Find(".bai-field__error").TextContent, StringComparison.Ordinal);
    }
}
