using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EdePermit.Core;

namespace EdePermit.Api;

public sealed class OpenAiExtractor
{
    readonly IHttpClientFactory _http;
    readonly IConfiguration _config;
    readonly ILogger<OpenAiExtractor> _log;
    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public OpenAiExtractor(IHttpClientFactory http, IConfiguration config, ILogger<OpenAiExtractor> log)
    {
        _http = http;
        _config = config;
        _log = log;
    }

    public string? ApiKey => _config["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    public string Model => _config["OpenAI:Model"] ?? "gpt-4o";

    public async Task<ExtractionEnvelope> ExtractAsync(string userText, ExtractedApplication current, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            try
            {
                var llm = await ExtractWithGpt4o(userText, current, ct);
                if (llm is not null)
                    return new ExtractionEnvelope { Data = llm, Model = Model, UsedLlm = true };
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "GPT-4o extraction failed; using local parser");
            }
        }
        return new ExtractionEnvelope { Data = HeuristicExtract(userText), Model = "local-parser", UsedLlm = false };
    }

    public async Task<string> PhraseAsync(string instruction, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) return instruction;
        try
        {
            var body = await CompleteAsync(
                "You are EDE's concise bilingual (English) permit assistant. Rewrite the instruction as a short, warm chat message. Do not add new questions. Do not mention models.",
                instruction, false, ct);
            return string.IsNullOrWhiteSpace(body) ? instruction : body.Trim();
        }
        catch
        {
            return instruction;
        }
    }

    async Task<ExtractedApplication?> ExtractWithGpt4o(string userText, ExtractedApplication current, CancellationToken ct)
    {
        var schemaHint = """
Return JSON with any of these fields you can confidently extract from the USER message only:
{
  "applyingFor": "myself" | "other",
  "patientResident": true | false,
  "patientName": string,
  "dateOfBirth": "YYYY-MM-DD",
  "nationality": string,
  "documentNumber": string,
  "documentType": "emirates-id" | "passport",
  "email": string,
  "mobile": string,
  "userConfirmedSummary": true | false,
  "medicines": [
    { "name": string, "activeIngredient": string, "quantity": number, "unit": string, "durationDays": number, "prescriptionDate": "YYYY-MM-DD", "productForm": string }
  ],
  "notes": string
}
Rules:
- Extract only what the user said. Use null/omit for unknown fields.
- Never invent dispensing mode, register match, strength, or eligibility.
- If the user confirms the summary, set userConfirmedSummary true.
- If the user corrects a value, return only the corrected fields plus medicine name when patching a medicine.
""";
        var payload = $"CURRENT_KNOWN_JSON:\n{JsonSerializer.Serialize(current, JsonOpts)}\n\nUSER_MESSAGE:\n{userText}";
        var raw = await CompleteAsync(schemaHint, payload, true, ct);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return JsonSerializer.Deserialize<ExtractedApplication>(raw, JsonOpts);
    }

    async Task<string> CompleteAsync(string system, string user, bool json, CancellationToken ct)
    {
        var client = _http.CreateClient("openai");
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        var body = new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["temperature"] = 0,
            ["messages"] = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };
        if (json) body["response_format"] = new { type = "json_object" };
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI HTTP {(int)resp.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    public async Task<string?> TranscribeAsync(Stream audio, string fileName, string contentType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) return null;
        var client = _http.CreateClient("openai");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("whisper-1"), "model");
        var sc = new StreamContent(audio);
        sc.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(sc, "file", string.IsNullOrWhiteSpace(fileName) ? "audio.webm" : fileName);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        req.Content = form;
        using var resp = await client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("text").GetString();
    }

    public static bool LooksLikeConfirm(string text)
    {
        var t = Regex.Replace((text ?? "").Trim().ToLowerInvariant(), @"[.!]+$", "");
        t = Regex.Replace(t, @"\s+", " ");
        string[] yes =
        [
            "confirm", "yes", "ok", "okay", "submit", "looks good", "that's correct", "thats correct",
            "confirm and submit", "yes confirm", "please submit", "i confirm", "confirmed", "go ahead"
        ];
        return yes.Contains(t);
    }

    public static ExtractedApplication HeuristicExtract(string text)
    {
        var data = new ExtractedApplication();
        var t = text.Trim();
        var lower = t.ToLowerInvariant();
        if (LooksLikeConfirm(t))
            data.UserConfirmedSummary = true;
        if (Regex.IsMatch(lower, @"someone else|for (a |my )?(relative|child|parent|spouse|guardian)|applying for other"))
            data.ApplyingFor = "other";
        else if (Regex.IsMatch(lower, @"\bmyself\b|for me\b|i am the patient|applying for (my ?)?self"))
            data.ApplyingFor = "myself";
        if (Regex.IsMatch(lower, @"not (a )?uae resident|resident\s*[:\-]?\s*no|\btravell?er\b|\bvisitor\b"))
            data.PatientResident = false;
        else if (Regex.IsMatch(lower, @"uae resident|resident\s*[:\-]?\s*yes|i am a resident"))
            data.PatientResident = true;

        var name = Regex.Match(t, @"(?:patient(?:'s| full)? name|full name|my name|name is)\s*[:\-]?\s*([A-Z][A-Za-z ]{2,80})", RegexOptions.IgnoreCase);
        if (name.Success) data.PatientName = name.Groups[1].Value.Trim().TrimEnd('.');
        var dob = Regex.Match(t, @"\b(19|20)\d{2}[-/.](0?[1-9]|1[0-2])[-/.](0?[1-9]|[12]\d|3[01])\b");
        if (!dob.Success) dob = Regex.Match(t, @"\b(0?[1-9]|[12]\d|3[01])[-/.](0?[1-9]|1[0-2])[-/.]((19|20)\d{2})\b");
        if (dob.Success && DateTime.TryParse(dob.Value, out var d)) data.DateOfBirth = d.ToString("yyyy-MM-dd");
        var eid = Regex.Match(t, @"\b784[- ]?\d{4}[- ]?\d{7}[- ]?\d\b");
        if (eid.Success) { data.DocumentNumber = eid.Value; data.DocumentType = "emirates-id"; }
        var pass = Regex.Match(t, @"\bpassport(?: number)?\s*[:\-]?\s*([A-Z0-9]{6,12})\b", RegexOptions.IgnoreCase);
        if (pass.Success) { data.DocumentNumber = pass.Groups[1].Value; data.DocumentType = "passport"; }
        var nat = Regex.Match(t, @"nationality\s*[:\-]?\s*([A-Za-z ]{3,40})", RegexOptions.IgnoreCase);
        if (nat.Success) data.Nationality = nat.Groups[1].Value.Trim().TrimEnd('.');
        var email = Regex.Match(t, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase);
        if (email.Success) data.Email = email.Value;
        var mobile = Regex.Match(t, @"\+971[\d\s]{8,14}");
        if (mobile.Success) data.Mobile = Regex.Replace(mobile.Value, @"\s+", " ").Trim();

        string? rxDate = null;
        var rx = Regex.Match(t, @"prescription(?: issue)? date\s*[:\-]?\s*([0-9]{4}[-/.][0-9]{1,2}[-/.][0-9]{1,2}|[0-9]{1,2}[-/.][0-9]{1,2}[-/.][0-9]{4})", RegexOptions.IgnoreCase);
        if (rx.Success && DateTime.TryParse(rx.Groups[1].Value, out var rxd))
            rxDate = rxd.ToString("yyyy-MM-dd");

        void addMed(string medName, string qty, string unit, string durationRaw, string durationUnit)
        {
            medName = Regex.Replace(medName, @"^(?:the|is|:|-)\s+", "", RegexOptions.IgnoreCase).Trim().TrimEnd(',', '.', ';');
            if (medName.Length < 3) return;
            var duration = int.Parse(durationRaw);
            if (durationUnit.StartsWith("month", StringComparison.OrdinalIgnoreCase)) duration *= 30;
            data.Medicines.Add(new ExtractedMedicine
            {
                Name = medName,
                Quantity = double.Parse(qty),
                Unit = unit,
                DurationDays = duration,
                PrescriptionDate = rxDate
            });
        }

        foreach (Match m in Regex.Matches(t,
                     @"(?:medicine|drug|rx)\s*[:\-]?\s*(.+?)\s+(\d+(?:\.\d+)?)\s*(tablets?|capsules?|pills?|vials?|ampoules?|ml|units?|packs?)\s*(?:for|over|x)?\s*(\d+)\s*(days?|months?)",
                     RegexOptions.IgnoreCase))
            addMed(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value);

        if (data.Medicines.Count == 0)
        {
            foreach (Match m in Regex.Matches(t,
                         @"\b([A-Z][A-Za-z0-9™®\-]{2,}(?:\s+[A-Za-z0-9™®\-][A-Za-z0-9™®\-]*){0,5})\s+(\d+(?:\.\d+)?)\s*(tablets?|capsules?|pills?|vials?|ampoules?|ml|units?|packs?)\s*(?:for|over|x)?\s*(\d+)\s*(days?|months?)",
                         RegexOptions.IgnoreCase))
                addMed(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value);
        }
        return data;
    }
}
