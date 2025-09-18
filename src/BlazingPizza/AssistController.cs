using BlazingPizza.Server.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazingPizza;

[ApiController]
[Route("api/assist")]
[AllowAnonymous]
public sealed class AssistController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AssistController> _logger;

    public AssistController(IConfiguration config, IHttpClientFactory httpFactory, ILogger<AssistController> logger)
    {
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<SearchResult>>> Search([FromQuery] string q, [FromServices] PizzaStoreContext db)
    {
        q = (q ?? string.Empty).Trim();
        if (q.Length < 2) return Ok(Array.Empty<SearchResult>());

        var specials = await db.Specials
            .Where(s => EF.Functions.Like(s.Name, $"%{q}%") || EF.Functions.Like(s.Description, $"%{q}%"))
            .Select(s => new {
                s.Id, s.Name, s.Description, s.BasePrice
            })
            .AsNoTracking()
            .ToListAsync();
        var specialResults = specials.Select(s => new SearchResult(
            s.Id,
            s.Name,
            "special",
            s.Description,
            s.BasePrice,
            new Dictionary<int, decimal>
            {
                { Pizza.MinimumSize, (decimal)Pizza.MinimumSize / (decimal)Pizza.DefaultSize * s.BasePrice },
                { Pizza.DefaultSize, s.BasePrice },
                { Pizza.MaximumSize, (decimal)Pizza.MaximumSize / (decimal)Pizza.DefaultSize * s.BasePrice },
            }
        ));

        var toppingsEntities = await db.Toppings
            .Where(t => EF.Functions.Like(t.Name, $"%{q}%"))
            .AsNoTracking()
            .ToListAsync();
        var toppings = toppingsEntities
            .Select(t => new SearchResult(t.Id, t.Name, "topping", null, t.Price, null))
            .ToList();

        return Ok(specialResults.Concat(toppings));
    }

    [HttpPost("cart")]
    public async Task<ActionResult<CartPlan>> ToCart([FromBody] CartRequest req, [FromServices] PizzaStoreContext db)
    {
        var (baseUrl, apiKey, model) = GetLlmSettings();
        using var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        // Build menu context for the LLM so it can make accurate choices
        var specialsEntities = await db.Specials.AsNoTracking().ToListAsync();
        var specials = specialsEntities
            .Select(s => new { id = s.Id, name = s.Name, description = s.Description, basePrice = s.BasePrice })
            .ToList();
        var toppingsEntities = await db.Toppings.AsNoTracking().ToListAsync();
        var toppings = toppingsEntities
            .Select(t => new { id = t.Id, name = t.Name, price = t.Price })
            .ToList();
        var menu = new
        {
            sizes = new { min = Pizza.MinimumSize, max = Pizza.MaximumSize, @default = Pizza.DefaultSize },
            specials,
            toppings
        };
        var menuJson = JsonSerializer.Serialize(menu);

        var system = string.Join("\n", new[]
        {
            "You are a strict pizza cart planner.",
            "Only output a single JSON object matching this schema: {\"actions\": [{\"type\": \"add_pizza|clear_cart\", \"specialId\": number?, \"quantity\": number, \"size\": number, \"toppingIds\": number[]?}] }.",
            "Rules:",
            "- Choose specialId ONLY from the provided MENU JSON below.",
            "- Choose toppingIds ONLY from the provided MENU JSON below.",
            "- Select the special that best matches the user's request by name and description.",
            "- Map size mentions like 12, 12\" or 12-inch to the integer size field.",
            "- If size is not specified, use the default size.",
            "- Quantity defaults to 1 if not specified.",
            "- Do NOT invent items. Do NOT add unrelated toppings. Include toppings only if explicitly requested or clearly implied (e.g., 'extra cheese').",
            "- If the request is ambiguous between specials, prefer the one that explicitly contains the requested topping in its name/description, otherwise the most generic match.",
            $"- Valid size range is {Pizza.MinimumSize}-{Pizza.MaximumSize}. Clamp into range if needed.",
            "- If the user requests multiple pizzas (e.g., 'two pepperoni'), create one add_pizza action with quantity set accordingly.",
            "- Never include commentary or extra fields; only the JSON object is allowed.",
        });
        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "system", content = "MENU:\n" + menuJson },
                new { role = "user", content = req.Utterance }
            },
            response_format = new { type = "json_object" }
        };
        var requestJson = JsonSerializer.Serialize(payload);

        var url = baseUrl.TrimEnd('/') + "/chat/completions";

        try
        {
            using var resp = await client.PostAsync(url, new StringContent(requestJson, Encoding.UTF8, "application/json"));
            var respText = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError("LLM error {Status}: {Body}", (int)resp.StatusCode, respText);
                return BadRequest(new { error = "LLM request failed", status = (int)resp.StatusCode });
            }

            using var doc = JsonDocument.Parse(respText);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            CartPlan plan;
            try
            {
                plan = content is not null
                    ? (JsonSerializer.Deserialize<CartPlan>(content!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new CartPlan(Array.Empty<CartAction>()))
                    : new CartPlan(Array.Empty<CartAction>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse LLM JSON: {Content}", content);
                plan = new CartPlan(Array.Empty<CartAction>());
            }

            // Fallback: if the model left size at default, try to extract a size from the utterance
            // Supports patterns like 12, 12", 12-inch, 12in and words small/medium/large
            try
            {
                var extractedSize = TryExtractSize(req.Utterance);
                if (extractedSize is int s)
                {
                    var fixedActions = plan.Actions
                        .Select(a => a.Type == "add_pizza" && (a.Size <= 0 || a.Size == Pizza.DefaultSize)
                            ? new CartAction(a.Type, a.SpecialId, a.Quantity, s, a.ToppingIds)
                            : a)
                        .ToArray();
                    plan = new CartPlan(fixedActions);
                }
            }
            catch { }

            return Ok(plan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception calling LLM at {Url}", url);
            return StatusCode(500, new { error = "Exception calling LLM" });
        }
    }

    private (string baseUrl, string apiKey, string model) GetLlmSettings()
    {
        var section = _config.GetSection("LLM");
        var provider = section["Provider"]?.ToLowerInvariant() ?? "openai";
        var model = section["Model"] ?? "gpt-5-nano";
        if (provider == "ollama")
        {
            var url = section["Ollama:BaseUrl"];
            if (string.IsNullOrWhiteSpace(url)) url = "http://localhost:11434/v1";
            return (url, string.Empty, model);
        }
        else
        {
            var url = section["OpenAI:BaseUrl"];
            if (string.IsNullOrWhiteSpace(url)) url = "https://api.openai.com/v1";
            var key = section["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
            return (url, key, model);
        }
    }

    private static int? TryExtractSize(string? utterance)
    {
        if (string.IsNullOrWhiteSpace(utterance)) return null;
        var text = utterance.ToLowerInvariant();

        // Word sizes
        if (text.Contains("small")) return Math.Clamp(Pizza.MinimumSize, Pizza.MinimumSize, Pizza.MaximumSize);
        if (text.Contains("medium")) return Math.Clamp(Pizza.DefaultSize, Pizza.MinimumSize, Pizza.MaximumSize);
        if (text.Contains("large")) return Math.Clamp(Pizza.MaximumSize, Pizza.MinimumSize, Pizza.MaximumSize);

        // Numeric sizes (9-17), optionally with symbols/words
        var m = Regex.Match(text, "\\b(\\d{1,2})\\s*(\\\"|inch|in|inches)?\\b");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
        {
            if (n >= Pizza.MinimumSize && n <= Pizza.MaximumSize) return n;
        }

        return null;
    }
}


