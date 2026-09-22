using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocOps.Application.Intelligence;
using DocOps.Domain.Documents;
using DocOps.Domain.Intelligence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocOps.Infrastructure.Intelligence;

/// <summary>
/// Configuration of the optional language-model provider. Every value comes from configuration —
/// in practice from environment variables (<c>Ai__Provider</c>, <c>Ai__Endpoint</c>, <c>Ai__ApiKey</c>,
/// <c>Ai__Model</c>) — and never from source control.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";
    public const string DemoProvider = "Demo";
    public const string OpenAiCompatibleProvider = "OpenAiCompatible";

    /// <summary>"Demo" (default) or "OpenAiCompatible".</summary>
    public string Provider { get; set; } = DemoProvider;

    /// <summary>
    /// Base URL of the API: <c>https://api.openai.com/v1</c>, or for Azure OpenAI
    /// <c>https://{resource}.openai.azure.com/openai/deployments/{deployment}</c>.
    /// </summary>
    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }
    public string? Model { get; set; }

    /// <summary>Azure OpenAI only; ignored by other endpoints.</summary>
    public string ApiVersion { get; set; } = "2024-10-21";

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Text beyond this is not sent: cost and latency grow with it, and so does the risk.</summary>
    public int MaxDocumentCharacters { get; set; } = 12_000;

    public bool UsesModel => string.Equals(Provider, OpenAiCompatibleProvider, StringComparison.OrdinalIgnoreCase);

    public bool IsAzure => Endpoint?.Contains(".openai.azure.com", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>
/// A language model reached through the OpenAI chat-completions API, or Azure OpenAI.
///
/// A model is trusted less than the demo rules, not more. It is asked, for every value, for the exact
/// words it read the value from; those words are then looked up in the document. A value whose words
/// are not there is discarded along with its citation — a model can invent a value as easily as a
/// quote, and a value nobody can point to is not a suggestion worth reviewing.
/// </summary>
public sealed class OpenAiCompatibleProvider(
    HttpClient httpClient,
    IOptions<AiOptions> options,
    ILogger<OpenAiCompatibleProvider> logger) : IAiDocumentService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AiOptions _options = options.Value;

    public AiProviderInfo Provider => new(
        AiSource.Model,
        $"Modelo de linguagem ({_options.Model})",
        "Modelo de linguagem configurado pela organização. Cada valor sugerido é confirmado contra o " +
        "texto do documento; o que não for encontrado no documento é descartado.");

    public async Task<ClassificationResult> ClassifyAsync(
        DocumentText content, IReadOnlyList<DocumentTypeProfile> candidates, CancellationToken cancellationToken = default)
    {
        if (content.IsEmpty || candidates.Count == 0)
        {
            return new ClassificationResult([]);
        }

        string catalogue = string.Join('\n', candidates.Select(c => $"- {c.Code}: {c.Name}"));

        ClassificationReply? reply = await AskAsync<ClassificationReply>(
            "You classify business documents into one of the listed types. Answer only with JSON of the form " +
            "{\"candidates\":[{\"typeCode\":string,\"confidence\":number between 0 and 1,\"quote\":string}]}. " +
            "\"quote\" must be words copied exactly from the document that show the type. Use only the listed codes.",
            $"Document types:\n{catalogue}\n\nDocument:\n{Excerpt(content)}",
            cancellationToken);

        List<ClassificationCandidate> result = [];

        foreach (ClassificationItem item in reply?.Candidates ?? [])
        {
            if (candidates.All(c => !string.Equals(c.Code, item.TypeCode, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            TextReference? evidence = content.Locate(item.Quote);

            result.Add(new ClassificationCandidate(
                candidates.First(c => string.Equals(c.Code, item.TypeCode, StringComparison.OrdinalIgnoreCase)).Code,
                Clamp(item.Confidence),
                evidence is null ? [] : [evidence]));
        }

        return new ClassificationResult([.. result.OrderByDescending(c => c.Confidence)]);
    }

    public async Task<ExtractionResult> ExtractAsync(
        DocumentText content, DocumentTypeProfile schema, CancellationToken cancellationToken = default)
    {
        if (content.IsEmpty)
        {
            return new ExtractionResult([.. schema.Fields.Select(f => new ExtractedField(f.Code, null, 0m, null))]);
        }

        string fields = string.Join('\n', schema.Fields.Select(f =>
            $"- {f.Code} ({f.Label}; {f.FieldType}{(f.Options.Count > 0 ? "; one of: " + string.Join(", ", f.Options) : string.Empty)})"));

        ExtractionReply? reply = await AskAsync<ExtractionReply>(
            "You extract fields from a business document. Answer only with JSON of the form " +
            "{\"fields\":[{\"code\":string,\"value\":string or null,\"confidence\":number between 0 and 1,\"quote\":string or null}]}. " +
            "Dates as yyyy-MM-dd, amounts as plain numbers with a dot for decimals. " +
            "\"quote\" must be the words copied exactly from the document the value was read from. " +
            "If a field is not in the document, return null for it — never guess.",
            $"Document type: {schema.Name}\nFields:\n{fields}\n\nDocument:\n{Excerpt(content)}",
            cancellationToken);

        List<ExtractedField> result = [];

        foreach (FieldProfile field in schema.Fields)
        {
            FieldItem? item = reply?.Fields?.FirstOrDefault(f => string.Equals(f.Code, field.Code, StringComparison.OrdinalIgnoreCase));
            string? value = item?.Value is { Length: > 0 } raw ? Normalise(field, raw) : null;
            TextReference? evidence = value is null ? null : content.Locate(item!.Quote);

            // Two checks, because a model can fail either way: the quote must be in the document,
            // and the value must be readable from the quote. A true quote with a different number
            // beside it is still an invented value.
            if (value is not null && (evidence is null || !ValueMatchesQuote(field, value, item!.Quote!)))
            {
                logger.LogWarning(
                    "Model value for {Field} is not supported by the document text and was discarded", field.Code);
                value = null;
                evidence = null;
            }

            result.Add(new ExtractedField(field.Code, value, value is null ? 0m : Clamp(item!.Confidence), evidence));
        }

        return new ExtractionResult(result);
    }

    public async Task<DocumentAnswer> AnswerAsync(
        DocumentText content, DocumentTypeProfile? schema, string question, CancellationToken cancellationToken = default)
    {
        if (content.IsEmpty)
        {
            return new DocumentAnswer(false, "Não encontrei esta informação no documento.", 0m, []);
        }

        AnswerReply? reply = await AskAsync<AnswerReply>(
            "You answer questions about one business document, using only that document. Answer only with JSON of the form " +
            "{\"answered\":boolean,\"answer\":string,\"quotes\":[string]}. Every quote must be copied exactly from the document. " +
            "If the document does not answer the question, set answered to false. Answer in the language of the question.",
            $"Question: {question}\n\nDocument:\n{Excerpt(content)}",
            cancellationToken);

        if (reply is null || !reply.Answered || string.IsNullOrWhiteSpace(reply.Answer))
        {
            return new DocumentAnswer(false, "Não encontrei esta informação no documento.", 0m, []);
        }

        // Quotes are located rather than trusted; the service then shows only the ones that verify.
        List<TextReference> citations = [.. (reply.Quotes ?? []).Select(content.Locate).OfType<TextReference>()];

        return new DocumentAnswer(true, reply.Answer.Trim(), citations.Count > 0 ? 0.7m : 0.3m, citations);
    }

    // -----------------------------------------------------------------------------------------

    private async Task<T?> AskAsync<T>(string system, string user, CancellationToken cancellationToken) where T : class
    {
        string path = _options.IsAzure
            ? $"{_options.Endpoint!.TrimEnd('/')}/chat/completions?api-version={Uri.EscapeDataString(_options.ApiVersion)}"
            : $"{_options.Endpoint!.TrimEnd('/')}/chat/completions";

        using HttpRequestMessage request = new(HttpMethod.Post, path);

        // The key travels in a header and is never logged.
        if (_options.IsAzure)
        {
            request.Headers.Add("api-key", _options.ApiKey);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        request.Content = JsonContent.Create(new ChatRequest(
            _options.Model!,
            [new ChatMessage("system", system), new ChatMessage("user", user)],
            new ResponseFormat("json_object"),
            Temperature: 0), options: Json);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The body may echo the prompt, which contains document text: only the status is logged.
            logger.LogError("AI model request failed with HTTP {Status}", (int)response.StatusCode);
            throw new HttpRequestException($"O modelo respondeu com o estado {(int)response.StatusCode}.");
        }

        ChatResponse? chat = await response.Content.ReadFromJsonAsync<ChatResponse>(Json, cancellationToken);
        string? payload = chat?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, Json);
        }
        catch (JsonException)
        {
            logger.LogWarning("AI model returned content that is not the requested JSON");
            return null;
        }
    }

    private string Excerpt(DocumentText content)
    {
        string text = content.Raw;
        return text.Length <= _options.MaxDocumentCharacters ? text : text[.._options.MaxDocumentCharacters];
    }

    /// <summary>A model's value is held to the same format a person's is before it can be suggested.</summary>
    private static string? Normalise(FieldProfile field, string raw) => field.FieldType switch
    {
        MetadataFieldType.Date => TextValueParser.ParseDate(raw),
        MetadataFieldType.Currency or MetadataFieldType.Number =>
            TextValueParser.TryParseAmount(raw, out decimal amount)
                ? amount.ToString(field.FieldType == MetadataFieldType.Currency ? "0.00" : "0.##", CultureInfo.InvariantCulture)
                : null,
        MetadataFieldType.Select => field.Options.FirstOrDefault(o => string.Equals(o, raw.Trim(), StringComparison.OrdinalIgnoreCase)),
        MetadataFieldType.Boolean => bool.TryParse(raw, out bool flag) ? flag.ToString() : null,
        _ => raw.Trim() is { Length: > 0 } text ? text[..Math.Min(text.Length, 1000)] : null
    };

    /// <summary>Reads the quote with the field's own parser and compares; text must simply appear in it.</summary>
    private static bool ValueMatchesQuote(FieldProfile field, string value, string quote) => field.FieldType switch
    {
        MetadataFieldType.Date => TextValueParser.ParseDate(quote) == value,
        MetadataFieldType.Currency => TextValueParser.ParseAmount(quote) == value
            || TextValueParser.ParseAmount(quote + " EUR") == value,
        MetadataFieldType.Number => TextValueParser.ParseInteger(quote) == value,
        _ => DocumentText.Normalise(quote).Contains(DocumentText.Normalise(value), StringComparison.Ordinal)
            || field.FieldType == MetadataFieldType.Select
    };

    private static decimal Clamp(decimal? value) => Math.Round(Math.Clamp(value ?? 0.5m, 0m, 1m), 2);

    // Wire formats -------------------------------------------------------------------------------

    private sealed record ChatRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("response_format")] ResponseFormat ResponseFormat,
        decimal Temperature);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ResponseFormat(string Type);

    private sealed record ChatResponse(IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(ChatMessage? Message);

    private sealed record ClassificationReply(IReadOnlyList<ClassificationItem>? Candidates);

    private sealed record ClassificationItem(string? TypeCode, decimal? Confidence, string? Quote);

    private sealed record ExtractionReply(IReadOnlyList<FieldItem>? Fields);

    private sealed record FieldItem(string? Code, string? Value, decimal? Confidence, string? Quote);

    private sealed record AnswerReply(bool Answered, string? Answer, IReadOnlyList<string>? Quotes);
}
