using System.Text;
using System.Text.Json;
using KestrelBooks.Api.Domain;

namespace KestrelBooks.Api.Services;

public record ExtractedReceipt(string? Vendor, DateOnly? Date, decimal? Net, decimal? Vat, decimal? Gross, string Notes);

public interface IReceiptExtractor
{
    Task<ExtractedReceipt> ExtractAsync(byte[] image, string contentType);
}

/// <summary>
/// Fallback when no vision API key is configured: the user keys the fields
/// in manually on the confirmation screen. The photo is still stored as the
/// source document for the audit trail.
/// </summary>
public class ManualReceiptExtractor : IReceiptExtractor
{
    public Task<ExtractedReceipt> ExtractAsync(byte[] image, string contentType) =>
        Task.FromResult(new ExtractedReceipt(null, null, null, null, null,
            "No extraction API configured — enter the details manually. Set Anthropic:ApiKey in appsettings.json to enable automatic extraction."));
}

/// <summary>
/// Extracts receipt fields with the Anthropic API (vision). Requires
/// Anthropic:ApiKey in configuration (get one at console.anthropic.com).
/// The image is sent to Anthropic for processing — one API call per scan.
/// </summary>
public class ClaudeReceiptExtractor : IReceiptExtractor
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    public ClaudeReceiptExtractor(IHttpClientFactory http, IConfiguration config)
    {
        _http = http; _config = config;
    }

    public async Task<ExtractedReceipt> ExtractAsync(byte[] image, string contentType)
    {
        var client = _http.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", _config["Anthropic:ApiKey"]);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var payload = new
        {
            model = _config["Anthropic:Model"] ?? "claude-sonnet-4-6",
            max_tokens = 500,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image",
                            source = new { type = "base64", media_type = contentType, data = Convert.ToBase64String(image) }
                        },
                        new
                        {
                            type = "text",
                            text = "Extract from this purchase receipt and respond with ONLY minified JSON, no markdown: " +
                                   "{\"vendor\":string|null,\"date\":\"yyyy-MM-dd\"|null,\"net\":number|null," +
                                   "\"vat\":number|null,\"gross\":number|null}. " +
                                   "Amounts in GBP as plain numbers. If VAT is not itemised, set net and vat to null and only fill gross."
                        }
                    }
                }
            }
        };

        var res = await client.PostAsync("https://api.anthropic.com/v1/messages",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            return new ExtractedReceipt(null, null, null, null, null, $"Extraction failed ({(int)res.StatusCode}) — enter details manually.");

        try
        {
            using var doc = JsonDocument.Parse(body);
            var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
            text = text.Replace("```json", "").Replace("```", "").Trim();
            using var json = JsonDocument.Parse(text);
            var r = json.RootElement;

            decimal? Num(string p) =>
                r.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;
            string? Str(string p) =>
                r.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            DateOnly? date = DateOnly.TryParse(Str("date"), out var d) ? d : null;
            var net = Num("net"); var vat = Num("vat"); var gross = Num("gross");
            // Fill in the third figure when two are present.
            if (gross is null && net is not null && vat is not null) gross = net + vat;
            if (net is null && gross is not null && vat is not null) net = gross - vat;

            return new ExtractedReceipt(Str("vendor"), date, net, vat, gross, "Extracted automatically — check before confirming.");
        }
        catch
        {
            return new ExtractedReceipt(null, null, null, null, null, "Could not read the extraction result — enter details manually.");
        }
    }
}
