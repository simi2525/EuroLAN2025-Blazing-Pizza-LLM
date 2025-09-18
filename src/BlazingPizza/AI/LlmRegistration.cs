using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace BlazingPizza.Server.AI;

public static class LlmRegistration
{
    public static void AddLlmFromConfig(this IHostApplicationBuilder builder, string sectionName = "LLM")
    {
        var cfg = builder.Configuration.GetSection(sectionName);
        var prov = cfg["Provider"]?.ToLowerInvariant() ?? "openai";
        var model = cfg["Model"] ?? (prov == "ollama" ? "llama3.1" : "gpt-5-nano");

        ChatClient chat;
        if (prov == "ollama")
        {
            var baseUrl = cfg["Ollama:BaseUrl"] ?? "http://localhost:11434/v1";
            chat = new ChatClient(model, new ApiKeyCredential("ollama"), new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });
        }
        else
        {
            var key = (cfg["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"))!;
            var baseUrl = cfg["OpenAI:BaseUrl"];
            var opts = new OpenAIClientOptions();
            if (!string.IsNullOrWhiteSpace(baseUrl)) opts.Endpoint = new Uri(baseUrl);
            chat = new ChatClient(model, new ApiKeyCredential(key), opts);
        }

        builder.Services.AddSingleton(chat);
    }
}


