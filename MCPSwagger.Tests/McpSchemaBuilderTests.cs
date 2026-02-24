using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FluentAssertions;
using SwaggerMcp.Discovery;
using SwaggerMcp.Schema;
using Xunit;

namespace SwaggerMcp.Tests;

public sealed class McpSchemaBuilderTests
{
    private readonly McpSchemaBuilder _builder = new();

    [Fact]
    public void BuildSchema_RouteAndQueryParams_ProducesCorrectSchema()
    {
        var descriptor = new McpToolDescriptor
        {
            Name = "get_item",
            HttpMethod = "GET",
            RelativeUrl = "items/{id}",
            ActionDescriptor = null!,
            ApiDescription = null!,
            RouteParameters =
            [
                new McpParameterDescriptor { Name = "id", ParameterType = typeof(int), IsRequired = true }
            ],
            QueryParameters =
            [
                new McpParameterDescriptor { Name = "include_deleted", ParameterType = typeof(bool), IsRequired = false }
            ]
        };

        var json = _builder.BuildSchema(descriptor);
        var schema = JsonDocument.Parse(json).RootElement;

        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("properties").TryGetProperty("id", out var idProp).Should().BeTrue();
        idProp.GetProperty("type").GetString().Should().Be("integer");

        schema.GetProperty("properties").TryGetProperty("include_deleted", out _).Should().BeTrue();

        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        required.Should().Contain("id");
        required.Should().NotContain("include_deleted");
    }

    [Fact]
    public void BuildSchema_BodyType_ExpandsPropertiesInline()
    {
        var descriptor = new McpToolDescriptor
        {
            Name = "create_item",
            HttpMethod = "POST",
            RelativeUrl = "items",
            ActionDescriptor = null!,
            ApiDescription = null!,
            RouteParameters = [],
            QueryParameters = [],
            Body = new McpBodyDescriptor
            {
                BodyType = typeof(CreateItemRequest),
                ParameterName = "request"
            }
        };

        var json = _builder.BuildSchema(descriptor);
        var schema = JsonDocument.Parse(json).RootElement;
        var props = schema.GetProperty("properties");

        props.TryGetProperty("name", out _).Should().BeTrue();
        props.TryGetProperty("count", out var countProp).Should().BeTrue();
        countProp.GetProperty("type").GetString().Should().Be("integer");
    }

    [Fact]
    public void BuildSchema_NullableType_IncludesNullInTypeArray()
    {
        var descriptor = new McpToolDescriptor
        {
            Name = "search",
            HttpMethod = "GET",
            RelativeUrl = "search",
            ActionDescriptor = null!,
            ApiDescription = null!,
            RouteParameters = [],
            QueryParameters =
            [
                new McpParameterDescriptor { Name = "filter", ParameterType = typeof(string), IsRequired = false }
            ]
        };

        var json = _builder.BuildSchema(descriptor);
        json.Should().NotBeNullOrEmpty();
    }

    private sealed class CreateItemRequest
    {
        [Required] public string Name { get; set; } = default!;
        [Range(1, 100)] public int Count { get; set; }
    }
}
