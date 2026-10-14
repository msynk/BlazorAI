using System.Text.Json;

namespace Blazor.AI.Tests;

public sealed class PartialJsonTests
{
    [Fact]
    public void CompleteJsonParsesUnchanged()
    {
        var value = PartialJson.TryParse("""{"city":"Seattle","days":3}""");

        Assert.NotNull(value);
        Assert.Equal("Seattle", value!.Value.GetProperty("city").GetString());
        Assert.Equal(3, value.Value.GetProperty("days").GetInt32());
    }

    [Fact]
    public void OpenObjectIsClosed()
    {
        var value = PartialJson.TryParse("""{"city":"Seattle","days":3""");

        Assert.NotNull(value);
        Assert.Equal(3, value!.Value.GetProperty("days").GetInt32());
    }

    [Fact]
    public void PartialStringValueIsKept()
    {
        var value = PartialJson.TryParse("""{"city":"Sea""");

        Assert.NotNull(value);
        Assert.Equal("Sea", value!.Value.GetProperty("city").GetString());
    }

    [Fact]
    public void PartialKeyIsDropped()
    {
        var value = PartialJson.TryParse("""{"city":"Seattle","da""");

        Assert.NotNull(value);
        Assert.Equal("Seattle", value!.Value.GetProperty("city").GetString());
        Assert.False(value.Value.TryGetProperty("da", out _));
    }

    [Fact]
    public void KeyWithoutValueIsDropped()
    {
        var value = PartialJson.TryParse("""{"city":"Seattle","days":""");

        Assert.NotNull(value);
        Assert.Single(value!.Value.EnumerateObject());
    }

    [Fact]
    public void TrailingCommaIsRemoved()
    {
        var value = PartialJson.TryParse("""{"a":1,""");

        Assert.NotNull(value);
        Assert.Equal(1, value!.Value.GetProperty("a").GetInt32());
    }

    [Fact]
    public void NestedContainersAreClosedInOrder()
    {
        var value = PartialJson.TryParse("""{"rows":[{"id":1},{"id":2""");

        Assert.NotNull(value);
        var rows = value!.Value.GetProperty("rows");
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal(2, rows[1].GetProperty("id").GetInt32());
    }

    [Fact]
    public void ArrayOfPartialStringsIsUsable()
    {
        var value = PartialJson.TryParse("""["alpha","be""");

        Assert.NotNull(value);
        Assert.Equal(2, value!.Value.GetArrayLength());
        Assert.Equal("be", value.Value[1].GetString());
    }

    [Fact]
    public void IncompleteLiteralIsDropped()
    {
        var value = PartialJson.TryParse("""{"ok":tru""");

        Assert.NotNull(value);
        Assert.False(value!.Value.TryGetProperty("ok", out _));
    }

    [Fact]
    public void CompleteNumberAtEndIsKept()
    {
        var value = PartialJson.TryParse("""{"count":42""");

        Assert.NotNull(value);
        Assert.Equal(42, value!.Value.GetProperty("count").GetInt32());
    }

    [Fact]
    public void EscapedQuoteInsideStringDoesNotTerminateIt()
    {
        var value = PartialJson.TryParse("""{"text":"he said \"hi""");

        Assert.NotNull(value);
        Assert.Equal("he said \"hi", value!.Value.GetProperty("text").GetString());
    }

    [Fact]
    public void DanglingEscapeIsTrimmed()
    {
        var value = PartialJson.TryParse("""{"text":"line\""");

        Assert.NotNull(value);
        Assert.Equal("line", value!.Value.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyInputYieldsNothing(string? input) => Assert.Null(PartialJson.TryParse(input));

    [Fact]
    public void GrowingPayloadNeverThrowsAndEndsCorrect()
    {
        const string complete = """{"items":[{"name":"a","score":1.5},{"name":"b","score":2}],"done":true}""";

        for (var length = 1; length < complete.Length; length++)
        {
            var partial = PartialJson.TryParse(complete[..length]);
            if (partial is { } value) Assert.NotEqual(JsonValueKind.Undefined, value.ValueKind);
        }

        var final = PartialJson.TryParse(complete);
        Assert.NotNull(final);
        Assert.True(final!.Value.GetProperty("done").GetBoolean());
    }

    [Theory]
    [InlineData("""{"a":"x","b":1.""")]
    [InlineData("""{"a":"x","b":-""")]
    [InlineData("""{"a":"x","b":1e""")]
    [InlineData("""{"a":"x","b":1e+""")]
    [InlineData("""{"a":"x","b":NaN""")]
    public void AnUnfinishedNumberIsDroppedWithoutLosingTheRestOfTheDocument(string input)
    {
        // double.TryParse accepts "1." and "NaN", which used to be kept and made the repair invalid.
        var value = PartialJson.TryParse(input);

        Assert.NotNull(value);
        Assert.Equal("x", value!.Value.GetProperty("a").GetString());
        Assert.False(value.Value.TryGetProperty("b", out _));
    }

    [Fact]
    public void AnEscapedBackslashFollowedByUIsNotMistakenForAUnicodeEscape()
    {
        var value = PartialJson.TryParse("""{"t":"a\\u12""");

        Assert.NotNull(value);
        Assert.Equal("a\\u12", value!.Value.GetProperty("t").GetString());
    }

    [Theory]
    [InlineData("""{"t":"x\uD83D""")]
    [InlineData("""{"t":"x\uD83D\uDE""")]
    public void HalfAnEscapedSurrogatePairIsTrimmedSoTheStringStaysReadable(string input)
    {
        var value = PartialJson.TryParse(input);

        Assert.NotNull(value);
        Assert.Equal("x", value!.Value.GetProperty("t").GetString());
    }

    [Fact]
    public void HalfARawSurrogatePairIsTrimmedRatherThanThrown()
    {
        // Kept out of InlineData: attribute strings are stored as UTF-8 and cannot hold a lone surrogate.
        var value = PartialJson.TryParse("{\"t\":\"x\uD83D");

        Assert.NotNull(value);
        Assert.Equal("x", value!.Value.GetProperty("t").GetString());
    }

    [Fact]
    public void TextThatCannotBeTranscodedIsReportedAsInvalidRatherThanThrown()
    {
        Assert.Null(PartialJson.TryParseComplete("{\"t\":\"x\uD83D\"}"));
    }

    [Fact]
    public void GrowingEscapedEmojiNeverYieldsAnUnreadableString()
    {
        const string complete = """{"t":"ok 😀 \\u done"}""";

        for (var length = 1; length <= complete.Length; length++)
        {
            if (PartialJson.TryParse(complete[..length]) is { ValueKind: JsonValueKind.Object } value
                && value.TryGetProperty("t", out var text))
            {
                _ = text.GetString();
            }
        }
    }
}
