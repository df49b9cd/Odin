using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;

namespace Odin.Core.Tests;

public class JsonOptionsTests
{
    private enum SampleStatus
    {
        Pending,
        InProgress
    }

    private sealed record SamplePayload(string WorkflowId, SampleStatus Status, string? OptionalValue);

    [Fact]
    public void DefaultOptions_AreConfiguredForWeb()
    {
        var options = JsonOptions.Default;

        options.PropertyNamingPolicy.ShouldBe(JsonNamingPolicy.CamelCase);
        options.DefaultIgnoreCondition.ShouldBe(JsonIgnoreCondition.WhenWritingNull);
        options.PropertyNameCaseInsensitive.ShouldBeTrue();
        options.WriteIndented.ShouldBeFalse();
        options.Converters.ShouldContain(converter => converter is JsonStringEnumConverter);
    }

    [Fact]
    public void PrettyOptions_InheritDefaultsWithIndentation()
    {
        var pretty = JsonOptions.Pretty;
        var defaults = JsonOptions.Default;

        pretty.ShouldNotBeSameAs(defaults);
        pretty.PropertyNamingPolicy.ShouldBe(defaults.PropertyNamingPolicy);
        pretty.DefaultIgnoreCondition.ShouldBe(defaults.DefaultIgnoreCondition);
        pretty.PropertyNameCaseInsensitive.ShouldBe(defaults.PropertyNameCaseInsensitive);
        pretty.WriteIndented.ShouldBeTrue();
    }

    [Fact]
    public void Serialize_UsesCamelCaseAndOmitsNulls()
    {
        var payload = new SamplePayload("Workflow-123", SampleStatus.InProgress, null);

        var json = JsonOptions.Serialize(payload);

        json.ShouldBe("{\"workflowId\":\"Workflow-123\",\"status\":\"inProgress\"}");
    }

    [Fact]
    public void SerializePretty_ProducesIndentedJson()
    {
        var payload = new SamplePayload("Workflow-123", SampleStatus.Pending, null);

        var json = JsonOptions.SerializePretty(payload);

        json.ShouldContain(Environment.NewLine);
        json.ShouldContain("\"workflowId\"");
        json.ShouldContain("\"status\"");
        json.ShouldContain("Workflow-123");
    }

    [Fact]
    public void Deserialize_HandlesCaseInsensitiveProperties()
    {
        const string json = """
        {
          "WORKFLOWID": "workflow-xyz",
          "STATUS": "inProgress",
          "optionalValue": "set"
        }
        """;

        var payload = JsonOptions.Deserialize<SamplePayload>(json);

        payload.ShouldNotBeNull();
        payload.WorkflowId.ShouldBe("workflow-xyz");
        payload.Status.ShouldBe(SampleStatus.InProgress);
        payload.OptionalValue.ShouldBe("set");
    }
}
