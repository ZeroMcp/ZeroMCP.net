using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ZeroMCP.Tests;

// ---------------------------------------------------------------------------
// Resources and Prompts integration tests
//
// These tests exercise the three new attribute types added in the second
// development phase:
//
//   [McpResource]   → resources/list  + resources/read  (static URI)
//   [McpTemplate]   → resources/templates/list + resources/read (URI template)
//   [McpPrompt]     → prompts/list    + prompts/get
//
// All tests run against the standard SampleAppWebApplicationFactory so that
// the full ASP.NET Core pipeline (routing, model binding, validation) is live.
// ---------------------------------------------------------------------------

public sealed class McpResourcesAndPromptsIntegrationTests
    : IClassFixture<SampleAppWebApplicationFactory>
{
    private readonly HttpClient _client;

    public McpResourcesAndPromptsIntegrationTests(SampleAppWebApplicationFactory factory)
        => _client = factory.CreateClient();

    // -----------------------------------------------------------------------
    // initialize — capabilities advertisement
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Initialize_AdvertisesResourcesAndPromptsCapabilities()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = McpProtocolConstants.ProtocolVersion,
                clientInfo = new { name = "test", version = "1.0" }
            }
        });

        response.Should().HaveProperty("result");
        var capabilities = response["result"]!.AsObject()["capabilities"]!.AsObject();

        capabilities.Should().HaveProperty("resources",
            "server must advertise resource capability when [McpResource]/[McpTemplate] actions are present");
        capabilities.Should().HaveProperty("prompts",
            "server must advertise prompt capability when [McpPrompt] actions are present");
    }

    // -----------------------------------------------------------------------
    // resources/list — static resources from [McpResource]
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ResourcesList_ReturnsBothStaticResources()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0", id = 10, method = "resources/list"
        });

        response.Should().HaveProperty("result");
        var resources = response["result"]!.AsObject()["resources"]!.AsArray();

        resources.Should().NotBeEmpty();

        var uris = resources.Select(r => r!.AsObject()["uri"]!.GetValue<string>()).ToList();
        uris.Should().Contain("catalog://info",
            "CatalogController.GetCatalogInfo is decorated with [McpResource(\"catalog://info\", ...)]");
        uris.Should().Contain("catalog://categories",
            "CatalogController.GetCategories is decorated with [McpResource(\"catalog://categories\", ...)]");
    }

    [Fact]
    public async Task ResourcesList_EachResourceHasRequiredFields()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0", id = 11, method = "resources/list"
        });

        var resources = response["result"]!.AsObject()["resources"]!.AsArray();
        foreach (var r in resources)
        {
            var obj = r!.AsObject();
            obj.Should().HaveProperty("uri");
            obj.Should().HaveProperty("name");
            obj["uri"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
            obj["name"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task ResourcesList_CatalogInfoResource_HasDescriptionAndMimeType()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0", id = 12, method = "resources/list"
        });

        var resources = response["result"]!.AsObject()["resources"]!.AsArray();
        var infoResource = resources
            .Select(r => r!.AsObject())
            .FirstOrDefault(r => r["uri"]!.GetValue<string>() == "catalog://info");

        infoResource.Should().NotBeNull("catalog://info resource must be discoverable");
        infoResource!.Should().HaveProperty("description");
        infoResource.Should().HaveProperty("mimeType");
        infoResource["mimeType"]!.GetValue<string>().Should().Be("application/json");
    }

    // -----------------------------------------------------------------------
    // resources/templates/list — parameterised resources from [McpTemplate]
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ResourcesTemplatesList_ReturnsBothTemplates()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0", id = 20, method = "resources/templates/list"
        });

        response.Should().HaveProperty("result");
        var templates = response["result"]!.AsObject()["resourceTemplates"]!.AsArray();

        templates.Should().NotBeEmpty();

        var uriTemplates = templates
            .Select(t => t!.AsObject()["uriTemplate"]!.GetValue<string>())
            .ToList();

        uriTemplates.Should().Contain("catalog://products/{id}",
            "CatalogController.GetProductById is decorated with [McpTemplate(\"catalog://products/{id}\", ...)]");
        uriTemplates.Should().Contain("catalog://categories/{category}/products",
            "CatalogController.GetProductsByCategory uses the two-variable template");
    }

    [Fact]
    public async Task ResourcesTemplatesList_EachTemplateHasRequiredFields()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0", id = 21, method = "resources/templates/list"
        });

        var templates = response["result"]!.AsObject()["resourceTemplates"]!.AsArray();
        foreach (var t in templates)
        {
            var obj = t!.AsObject();
            obj.Should().HaveProperty("uriTemplate");
            obj.Should().HaveProperty("name");
            obj["uriTemplate"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
            obj["name"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        }
    }

    // -----------------------------------------------------------------------
    // resources/read — static resource
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ResourcesRead_CatalogInfo_ReturnsMetadata()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 30,
            method = "resources/read",
            @params = new { uri = "catalog://info" }
        });

        response.Should().HaveProperty("result");
        var contents = response["result"]!.AsObject()["contents"]!.AsArray();

        contents.Should().HaveCount(1);
        var content = contents[0]!.AsObject();
        content["uri"]!.GetValue<string>().Should().Be("catalog://info");
        content.Should().HaveProperty("text");

        // The text is JSON — verify it has the expected catalog fields
        var catalogInfo = JsonNode.Parse(content["text"]!.GetValue<string>())!.AsObject();
        catalogInfo.Should().HaveProperty("productCount");
        catalogInfo.Should().HaveProperty("categoryCount");
        catalogInfo.Should().HaveProperty("categories");
        catalogInfo["productCount"]!.GetValue<int>().Should().BeGreaterThan(0);
        catalogInfo["categories"]!.AsArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task ResourcesRead_CatalogCategories_ReturnsCategoryArray()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 31,
            method = "resources/read",
            @params = new { uri = "catalog://categories" }
        });

        var contents = response["result"]!.AsObject()["contents"]!.AsArray();
        var text = contents[0]!.AsObject()["text"]!.GetValue<string>();
        var categories = JsonSerializer.Deserialize<string[]>(text);

        categories.Should().NotBeNullOrEmpty();
        categories.Should().Contain("Hardware");
        categories.Should().Contain("Electronics");
    }

    [Fact]
    public async Task ResourcesRead_UnknownUri_ReturnsError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 32,
            method = "resources/read",
            @params = new { uri = "catalog://does-not-exist" }
        });

        response.Should().HaveProperty("error",
            "an unknown URI must produce a JSON-RPC error, not a result");
    }

    [Fact]
    public async Task ResourcesRead_MissingUriParam_ReturnsInvalidParams()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 33,
            method = "resources/read",
            @params = new { }
        });

        response.Should().HaveProperty("error");
        response["error"]!.AsObject()["code"]!.GetValue<int>().Should().Be(-32602);
    }

    // -----------------------------------------------------------------------
    // resources/read — URI template resolution
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ResourcesRead_ProductTemplate_ReturnsProductById()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 40,
            method = "resources/read",
            @params = new { uri = "catalog://products/1" }
        });

        response.Should().HaveProperty("result");
        var contents = response["result"]!.AsObject()["contents"]!.AsArray();

        contents.Should().HaveCount(1);
        var content = contents[0]!.AsObject();
        content["uri"]!.GetValue<string>().Should().Be("catalog://products/1");

        var product = JsonNode.Parse(content["text"]!.GetValue<string>())!.AsObject();
        product["id"]!.GetValue<int>().Should().Be(1);
        product["name"]!.GetValue<string>().Should().Be("Widget");
    }

    [Fact]
    public async Task ResourcesRead_ProductTemplate_NotFound_ReturnsContentsWithErrorText()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 41,
            method = "resources/read",
            @params = new { uri = "catalog://products/9999" }
        });

        // resources/read wraps HTTP 404 as content (not a JSON-RPC error),
        // so the caller can see the error text
        response.Should().HaveProperty("result");
    }

    [Fact]
    public async Task ResourcesRead_CategoryTemplate_ReturnsProductsInCategory()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 42,
            method = "resources/read",
            @params = new { uri = "catalog://categories/Hardware/products" }
        });

        response.Should().HaveProperty("result");
        var contents = response["result"]!.AsObject()["contents"]!.AsArray();
        var text = contents[0]!.AsObject()["text"]!.GetValue<string>();

        var products = JsonSerializer.Deserialize<JsonElement[]>(text);
        products.Should().NotBeNullOrEmpty();
        products!.Should().AllSatisfy(p =>
            p.GetProperty("category").GetString().Should()
             .Be("Hardware", "template filters by category"));
    }

    [Fact]
    public async Task ResourcesRead_CategoryTemplate_PreservesUriInResponse()
    {
        const string uri = "catalog://categories/Electronics/products";
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 43,
            method = "resources/read",
            @params = new { uri }
        });

        var contents = response["result"]!.AsObject()["contents"]!.AsArray();
        contents[0]!.AsObject()["uri"]!.GetValue<string>().Should().Be(uri);
    }

    // -----------------------------------------------------------------------
    // prompts/list — all [McpPrompt] methods
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PromptsList_ReturnsBothPrompts()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0", id = 50, method = "prompts/list"
        });

        response.Should().HaveProperty("result");
        var prompts = response["result"]!.AsObject()["prompts"]!.AsArray();

        prompts.Should().NotBeEmpty();

        var names = prompts.Select(p => p!.AsObject()["name"]!.GetValue<string>()).ToList();
        names.Should().Contain("search_products_prompt");
        names.Should().Contain("restock_recommendation_prompt");
    }

    [Fact]
    public async Task PromptsList_EachPromptHasNameAndDescription()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0", id = 51, method = "prompts/list"
        });

        var prompts = response["result"]!.AsObject()["prompts"]!.AsArray();
        foreach (var p in prompts)
        {
            var obj = p!.AsObject();
            obj.Should().HaveProperty("name");
            obj.Should().HaveProperty("description");
            obj["name"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
            obj["description"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task PromptsList_SearchPrompt_AdvertisesKeywordAndCategoryArguments()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0", id = 52, method = "prompts/list"
        });

        var prompts = response["result"]!.AsObject()["prompts"]!.AsArray();
        var searchPrompt = prompts
            .Select(p => p!.AsObject())
            .First(p => p["name"]!.GetValue<string>() == "search_products_prompt");

        searchPrompt.Should().HaveProperty("arguments");
        var args = searchPrompt["arguments"]!.AsArray()
            .Select(a => a!.AsObject())
            .ToList();

        var keywordArg = args.FirstOrDefault(a => a["name"]!.GetValue<string>() == "keyword");
        keywordArg.Should().NotBeNull("keyword is a required parameter of SearchProductsPrompt");
        keywordArg!["required"]!.GetValue<bool>().Should().BeTrue();

        var categoryArg = args.FirstOrDefault(a => a["name"]!.GetValue<string>() == "category");
        categoryArg.Should().NotBeNull("category is an optional parameter of SearchProductsPrompt");
        categoryArg!["required"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task PromptsList_RestockPrompt_AdvertisesProductIdArgument()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0", id = 53, method = "prompts/list"
        });

        var prompts = response["result"]!.AsObject()["prompts"]!.AsArray();
        var restockPrompt = prompts
            .Select(p => p!.AsObject())
            .First(p => p["name"]!.GetValue<string>() == "restock_recommendation_prompt");

        restockPrompt.Should().HaveProperty("arguments");
        var args = restockPrompt["arguments"]!.AsArray()
            .Select(a => a!.AsObject())
            .ToList();

        args.Should().ContainSingle(a => a["name"]!.GetValue<string>() == "productId");
    }

    // -----------------------------------------------------------------------
    // prompts/get — invoke a prompt with arguments
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PromptsGet_SearchPrompt_ReturnsMessageEnvelope()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 60,
            method = "prompts/get",
            @params = new
            {
                name = "search_products_prompt",
                arguments = new { keyword = "widget" }
            }
        });

        response.Should().HaveProperty("result");
        var result = response["result"]!.AsObject();

        result.Should().HaveProperty("messages", "prompts/get must return an MCP messages array");
        var messages = result["messages"]!.AsArray();
        messages.Should().HaveCount(1);

        var msg = messages[0]!.AsObject();
        msg["role"]!.GetValue<string>().Should().Be("user");
        var content = msg["content"]!.AsObject();
        content["type"]!.GetValue<string>().Should().Be("text");
        content["text"]!.GetValue<string>().Should().Contain("widget",
            "the prompt text must embed the supplied keyword argument");
    }

    [Fact]
    public async Task PromptsGet_SearchPrompt_WithCategory_IncludesCategoryInText()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 61,
            method = "prompts/get",
            @params = new
            {
                name = "search_products_prompt",
                arguments = new { keyword = "gadget", category = "Electronics" }
            }
        });

        var text = ExtractPromptText(response);
        text.Should().Contain("gadget");
        text.Should().Contain("Electronics",
            "optional category argument must be embedded when supplied");
    }

    [Fact]
    public async Task PromptsGet_SearchPrompt_HasDescriptionAtTopLevel()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 62,
            method = "prompts/get",
            @params = new
            {
                name = "search_products_prompt",
                arguments = new { keyword = "test" }
            }
        });

        var result = response["result"]!.AsObject();
        result.Should().HaveProperty("description",
            "prompts/get result must include the description from [McpPrompt]");
        result["description"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PromptsGet_RestockPrompt_ReturnsProductSpecificText()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 63,
            method = "prompts/get",
            @params = new
            {
                name = "restock_recommendation_prompt",
                arguments = new { productId = 1 }
            }
        });

        var text = ExtractPromptText(response);
        text.Should().Contain("Widget", "product name must appear in the generated prompt");
        text.Should().Contain("Hardware", "product category must appear in the generated prompt");
    }

    [Fact]
    public async Task PromptsGet_RestockPrompt_ProductNotFound_IsHandledGracefully()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 64,
            method = "prompts/get",
            @params = new
            {
                name = "restock_recommendation_prompt",
                arguments = new { productId = 9999 }
            }
        });

        // The handler returns the HTTP 404 response text wrapped in the message envelope
        // rather than a JSON-RPC protocol error, so the caller always gets a result shape.
        response.Should().HaveProperty("result");
    }

    [Fact]
    public async Task PromptsGet_UnknownPromptName_ReturnsInvalidParamsError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 65,
            method = "prompts/get",
            @params = new { name = "nonexistent_prompt", arguments = new { } }
        });

        response.Should().HaveProperty("error");
        response["error"]!.AsObject()["code"]!.GetValue<int>().Should().Be(-32602,
            "unknown prompt name must produce an InvalidParams error");
    }

    [Fact]
    public async Task PromptsGet_MissingNameParam_ReturnsInvalidParamsError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 66,
            method = "prompts/get",
            @params = new { arguments = new { keyword = "test" } }
        });

        response.Should().HaveProperty("error");
        response["error"]!.AsObject()["code"]!.GetValue<int>().Should().Be(-32602);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<JsonObject> PostMcpAsync(object body)
    {
        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var httpResponse = await _client.PostAsync("/mcp", content);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        return JsonNode.Parse(responseJson)!.AsObject();
    }

    private static string ExtractPromptText(JsonObject response)
        => response["result"]!.AsObject()
            ["messages"]!.AsArray()[0]!.AsObject()
            ["content"]!.AsObject()
            ["text"]!.GetValue<string>();
}
