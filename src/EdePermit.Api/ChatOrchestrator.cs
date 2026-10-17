using System.Text;
using EdePermit.Core;

namespace EdePermit.Api;

public sealed class ChatOrchestrator
{
    public const string Checklist = """
I’m the EDE assistant for personal medicine import permits. Please send **everything below in one message** — type it or use the microphone. Attach documents in this chat if you already have them.

Checklist:
1. Applying for myself or someone else
2. Is the patient a UAE resident? Yes / No
3. Patient full name
4. Date of birth
5. Nationality
6. Emirates ID number (resident) or passport number (traveller)
7. Email and mobile
8. Each medicine: name (and strength if you know it), quantity, unit, duration in days
9. Prescription issue date
10. Upload the prescription (PDF/JPG/PNG). Add a medical report if a medicine is controlled, and a doctor declaration if it is injectable

I will extract and structure your reply, check medicines only against the EDE register (ALL_REG_PACKS.xlsx), and apply the official rules in code. I will only come back if something is missing, invalid, or unclear.
""";

    readonly LiteStore _store;
    readonly OpenAiExtractor _ai;
    readonly BusinessRulesEngine _rules;
    readonly ApplicantProfile _profile = new();
    readonly string _uploadRoot;
    readonly ILogger<ChatOrchestrator> _log;

    public ChatOrchestrator(LiteStore store, OpenAiExtractor ai, BusinessRulesEngine rules, IWebHostEnvironment env, ILogger<ChatOrchestrator> log)
    {
        _store = store;
        _ai = ai;
        _rules = rules;
        _log = log;
        _uploadRoot = Path.Combine(env.ContentRootPath, "data", "uploads");
        Directory.CreateDirectory(_uploadRoot);
    }

    public object Start()
    {
        var conv = new ConversationDoc();
        var req = new RequestDoc
        {
            ConversationId = conv.Id,
            ApplicationNumber = $"EDE-PMU-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}"
        };
        conv.RequestId = req.Id;
        _store.Conversations.Insert(conv);
        _store.Requests.Insert(req);
        var msg = AddAssistant(conv.Id, "Sign in with UAE PASS in the header to begin. After that I’ll collect the permit details in one step — not through a form.");
        _store.AuditEvent(conv.Id, req.Id, "applicant", "conversation.started", req.ApplicationNumber);
        return Snapshot(conv.Id, msg);
    }

    public object SignIn(string conversationId)
    {
        var conv = GetConv(conversationId);
        conv.UaePassSignedIn = true;
        conv.Extracted.Email ??= _profile.Email;
        conv.Extracted.Mobile ??= _profile.Mobile;
        conv.UpdatedUtc = DateTime.UtcNow;
        _store.Conversations.Update(conv);
        AddAssistant(conv.Id, $"UAE PASS verified as {_profile.UaePassName}. {Checklist}");
        conv.ChecklistSent = true;
        _store.Conversations.Update(conv);
        _store.AuditEvent(conv.Id, conv.RequestId, "applicant", "uaepass.signed_in", _profile.UaePassName);
        return Snapshot(conv.Id);
    }

    public async Task<object> UserMessage(string conversationId, string text, string inputMode, CancellationToken ct)
    {
        var conv = GetConv(conversationId);
        if (!conv.UaePassSignedIn)
            return Snapshot(conv.Id, AddAssistant(conv.Id, "Please sign in with UAE PASS first. That’s the only step before we collect your information in chat."));

        text = (text ?? "").Trim();
        if (text.Length == 0) return Snapshot(conv.Id, AddAssistant(conv.Id, "I didn’t catch any text. Type your details or use the microphone."));

        _store.Messages.Insert(new MessageDoc { ConversationId = conv.Id, Role = "user", Text = text, InputMode = inputMode == "voice" ? "voice" : "text" });

        var extracted = await _ai.ExtractAsync(text, conv.Extracted, ct);
        ApplicationMerger.Merge(conv.Extracted, extracted.Data);
        conv.Extracted.UserConfirmedSummary =
            extracted.Data.UserConfirmedSummary == true || OpenAiExtractor.LooksLikeConfirm(text);
        if (string.Equals(conv.Extracted.ApplyingFor, "myself", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(conv.Extracted.PatientName))
        {
            conv.Extracted.PatientName = _profile.UaePassName;
            conv.Extracted.DateOfBirth ??= _profile.DateOfBirth;
            conv.Extracted.Nationality ??= _profile.Nationality;
            conv.Extracted.DocumentNumber ??= _profile.EmiratesId;
            conv.Extracted.DocumentType ??= "emirates-id";
            conv.Extracted.PatientResident ??= true;
        }

        var validation = Evaluate(conv);
        var reply = await BuildReply(conv, validation, extracted, ct);
        AddAssistant(conv.Id, reply);
        Persist(conv, validation);
        _store.AuditEvent(conv.Id, conv.RequestId, "applicant", "message.processed", $"mode={inputMode}; llm={extracted.UsedLlm}; status={conv.Status}");
        return Snapshot(conv.Id);
    }

    public async Task<object> Upload(string conversationId, string documentType, IFormFile file, CancellationToken ct)
    {
        var conv = GetConv(conversationId);
        if (file.Length > 40 * 1024 * 1024)
            return Snapshot(conv.Id, AddAssistant(conv.Id, "That file is over the 40 MB limit. Please upload a smaller PDF, JPG, or PNG."));
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not ".pdf" and not ".png" and not ".jpg" and not ".jpeg")
            return Snapshot(conv.Id, AddAssistant(conv.Id, "Please upload PDF, JPG, or PNG only."));

        var dir = Path.Combine(_uploadRoot, conv.Id);
        Directory.CreateDirectory(dir);
        var stored = $"{Guid.NewGuid():N}{ext}";
        var path = Path.Combine(dir, stored);
        await using (var fs = File.Create(path))
            await file.CopyToAsync(fs, ct);

        var type = string.IsNullOrWhiteSpace(documentType) ? "additional" : documentType;
        _store.Documents.Insert(new DocumentDoc
        {
            ConversationId = conv.Id,
            RequestId = conv.RequestId,
            DocumentType = type,
            FileName = Path.GetFileName(file.FileName),
            ContentType = file.ContentType,
            Size = file.Length,
            StoragePath = path
        });
        _store.AuditEvent(conv.Id, conv.RequestId, "applicant", "document.uploaded", $"{type}:{file.FileName}");

        var validation = Evaluate(conv);
        var reply = $"I’ve stored **{file.FileName}** as `{type}`.";
        if (validation.MissingDocuments.Count > 0)
            reply += " Still needed: " + string.Join(", ", validation.MissingDocuments) + ".";
        else if (validation.MissingFields.Count == 0)
            reply += "\n\n" + RenderSummary(conv, validation);
        AddAssistant(conv.Id, reply);
        Persist(conv, validation);
        return Snapshot(conv.Id);
    }

    public object Get(string conversationId) => Snapshot(conversationId);

    public object OfficerQueue() => _store.Requests.FindAll()
        .OrderByDescending(r => r.UpdatedUtc)
        .Select(OfficerListItem)
        .ToList();

    public object? OfficerCase(string requestId)
    {
        var req = _store.Requests.FindById(requestId);
        if (req is null) return null;
        var conv = _store.Conversations.FindById(req.ConversationId);
        var messages = _store.Messages.Find(m => m.ConversationId == req.ConversationId).OrderBy(m => m.CreatedUtc).ToList();
        var docs = _store.Documents.Find(d => d.ConversationId == req.ConversationId).ToList();
        var audit = _store.Audit.Find(a => a.RequestId == req.Id).OrderBy(a => a.CreatedUtc).ToList();
        return new { request = req, conversation = conv, messages, documents = docs.Select(d => new { d.Id, d.DocumentType, d.FileName, d.Size, d.UploadedUtc }), audit };
    }

    public object OfficerDecide(string requestId, string decision, string notes, string actor)
    {
        var req = _store.Requests.FindById(requestId) ?? throw new InvalidOperationException("Request not found");
        var conv = GetConv(req.ConversationId);
        req.OfficerNotes = notes ?? "";
        req.UpdatedUtc = DateTime.UtcNow;
        decision = (decision ?? "").ToLowerInvariant();
        if (decision == "approve")
        {
            req.Status = ApplicationStatuses.Completed;
            conv.Status = ApplicationStatuses.Completed;
            AddAssistant(conv.Id, "An EDE officer has completed your application and issued the permit.");
        }
        else if (decision == "reject")
        {
            req.Status = ApplicationStatuses.Rejected;
            conv.Status = ApplicationStatuses.Rejected;
            AddAssistant(conv.Id, "An EDE officer has rejected this application. You can start a new chat if you need to apply again.");
        }
        else
        {
            req.Status = ApplicationStatuses.Correction;
            conv.Status = ApplicationStatuses.Correction;
            AddAssistant(conv.Id, "An officer asked for a correction: " + (string.IsNullOrWhiteSpace(notes) ? "please review your details in this chat." : notes));
        }
        _store.Requests.Update(req);
        _store.Conversations.Update(conv);
        _store.AuditEvent(conv.Id, req.Id, actor, "officer." + decision, notes ?? "");
        return OfficerCase(requestId)!;
    }

    public object Analytics()
    {
        var reqs = _store.Requests.FindAll().ToList();
        var msgs = _store.Messages.FindAll().ToList();
        var userMsgs = msgs.Where(m => m.Role == "user").ToList();
        var medicines = reqs.SelectMany(r => r.Validation?.Medicines ?? new List<ValidatedMedicine>()).ToList();
        var byStatus = reqs.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());
        var trend = reqs.GroupBy(r => r.CreatedUtc.ToString("yyyy-MM-dd"))
            .OrderBy(g => g.Key)
            .Select(g => new { date = g.Key, count = g.Count() })
            .ToList();
        return new
        {
            totalRequests = reqs.Count,
            byStatus,
            trend,
            medicineValidation = new
            {
                total = medicines.Count,
                matched = medicines.Count(m => m.Matched),
                unmatched = medicines.Count(m => !m.Matched),
                controlled = medicines.Count(m => m.Controlled),
                injectable = medicines.Count(m => m.Injectable)
            },
            inputModes = new
            {
                text = userMsgs.Count(m => m.InputMode != "voice"),
                voice = userMsgs.Count(m => m.InputMode == "voice")
            },
            outcomes = new
            {
                autoApproved = reqs.Count(r => r.Status == ApplicationStatuses.AutoApproved),
                mixed = reqs.Count(r => r.Status == ApplicationStatuses.Mixed),
                officerReview = reqs.Count(r => r.Status == ApplicationStatuses.OfficerReview),
                completed = reqs.Count(r => r.Status == ApplicationStatuses.Completed)
            }
        };
    }

    public DocumentDoc? FindDocument(string id) => _store.Documents.FindById(id);

    ValidationResult Evaluate(ConversationDoc conv)
    {
        var docs = _store.Documents.Find(d => d.ConversationId == conv.Id).Select(d => d.DocumentType).ToList();
        var applicantKey = TextNorm.Normalize(conv.Extracted.DocumentNumber ?? _profile.EmiratesId);
        var others = _store.Requests.FindAll().Where(r => r.Id != conv.RequestId).ToList();
        var active = others.Any(r =>
            r.Status is ApplicationStatuses.Submitted or ApplicationStatuses.OfficerReview or ApplicationStatuses.Mixed or ApplicationStatuses.FollowUp or ApplicationStatuses.Collecting
            && TextNorm.Normalize(r.Snapshot.DocumentNumber) == applicantKey
            && !string.IsNullOrWhiteSpace(applicantKey));
        var repeat = others.Any(r => r.CreatedUtc > DateTime.UtcNow.AddDays(-30)
            && TextNorm.Normalize(r.Snapshot.DocumentNumber) == applicantKey
            && !string.IsNullOrWhiteSpace(applicantKey));

        var result = _rules.Evaluate(new RulesContext
        {
            Application = conv.Extracted,
            SignedIn = _profile,
            UploadedDocumentTypes = docs,
            HasActiveApplication = active,
            RepeatWithin30Days = repeat,
            SubmissionUtc = DateTime.UtcNow
        });
        result.RequiredDocuments = BusinessRulesEngine.DocumentsForValidated(result, conv.Extracted);
        result.MissingDocuments = result.RequiredDocuments
            .Where(d => !docs.Contains(d, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (!result.Blocked && (result.MissingFields.Count > 0 || result.MissingDocuments.Count > 0))
            result.ApplicationOutcome = ApplicationStatuses.FollowUp;
        return result;
    }

    async Task<string> BuildReply(ConversationDoc conv, ValidationResult validation, ExtractionEnvelope extraction, CancellationToken ct)
    {
        if (validation.Blocked)
        {
            conv.Status = ApplicationStatuses.Blocked;
            return validation.BlockReason;
        }

        if (conv.Extracted.UserConfirmedSummary == true
            && validation.MissingFields.Count == 0
            && validation.MissingDocuments.Count == 0
            && validation.Medicines.Count > 0)
        {
            return Submit(conv, validation);
        }

        if (validation.MissingFields.Count > 0 || validation.MissingDocuments.Count > 0)
        {
            conv.Status = ApplicationStatuses.FollowUp;
            var sb = new StringBuilder();
            sb.AppendLine("I captured what you already provided and did not ask those items again.");
            if (extraction.UsedLlm) sb.AppendLine($"Extracted with {extraction.Model}.");
            if (validation.MissingFields.Count > 0)
            {
                sb.AppendLine("I still need:");
                foreach (var f in validation.MissingFields.Take(8)) sb.AppendLine("- " + f);
            }
            if (validation.MissingDocuments.Count > 0)
            {
                sb.AppendLine("Please upload in this chat:");
                foreach (var d in validation.MissingDocuments) sb.AppendLine("- " + d);
            }
            var unmatched = validation.Medicines.Where(m => !m.Matched).ToList();
            foreach (var m in unmatched)
                sb.AppendLine($"- {m.Name}: {m.FailedRules.FirstOrDefault() ?? "not in ALL_REG_PACKS.xlsx"}");
            return await _ai.PhraseAsync(sb.ToString(), ct);
        }

        conv.Status = ApplicationStatuses.ReadyForSummary;
        return RenderSummary(conv, validation);
    }

    string Submit(ConversationDoc conv, ValidationResult validation)
    {
        var req = _store.Requests.FindById(conv.RequestId)!;
        var outcome = validation.ApplicationOutcome;
        if (outcome == ApplicationStatuses.AutoApproved)
            conv.Status = ApplicationStatuses.AutoApproved;
        else if (outcome == ApplicationStatuses.Mixed)
            conv.Status = ApplicationStatuses.Mixed;
        else
            conv.Status = ApplicationStatuses.OfficerReview;
        req.Status = conv.Status;
        req.ApplicantName = conv.Extracted.PatientName ?? "";
        req.Classification = validation.Classification;
        req.Snapshot = conv.Extracted;
        req.Validation = validation;
        req.UpdatedUtc = DateTime.UtcNow;
        _store.Requests.Update(req);
        _store.Conversations.Update(conv);
        _store.AuditEvent(conv.Id, req.Id, "applicant", "application.submitted", req.Status);

        var sb = new StringBuilder();
        sb.AppendLine($"Submitted as **{req.ApplicationNumber}**.");
        sb.AppendLine($"Application outcome: **{req.Status}** ({validation.Classification}).");
        foreach (var m in validation.Medicines)
            sb.AppendLine($"- {m.Name}: {(m.AutoEligible ? "auto-approved" : "officer review")} · {m.DispensingMode} · {(m.Matched ? "matched" : "unmatched")}");
        if (req.Status != ApplicationStatuses.AutoApproved)
            sb.AppendLine("An EDE officer can review the chat, documents, register matches, and rule results in the officer portal.");
        else
            sb.AppendLine("No officer action is required. Keep this application number for your permit.");
        return sb.ToString();
    }

    string RenderSummary(ConversationDoc conv, ValidationResult validation)
    {
        var e = conv.Extracted;
        var sb = new StringBuilder();
        sb.AppendLine("Here is the consolidated summary. Reply **confirm** to submit, or tell me what to change.");
        sb.AppendLine();
        sb.AppendLine($"- Applying for: {e.ApplyingFor}");
        sb.AppendLine($"- Classification: {validation.Classification}");
        sb.AppendLine($"- Patient: {e.PatientName}, DOB {e.DateOfBirth}, {e.Nationality}");
        sb.AppendLine($"- Identity: {e.DocumentType} {e.DocumentNumber}");
        sb.AppendLine($"- Contact: {e.Email} · {e.Mobile}");
        foreach (var m in validation.Medicines)
        {
            sb.AppendLine($"- Medicine: {m.Name} ({m.ActiveIngredient}) · {m.Quantity} {m.Unit} · {m.DurationDays} days");
            sb.AppendLine($"  Register: {(m.Matched ? m.RegistryId + " · " + m.DispensingMode : "No record found → N/A")} · {(m.AutoEligible ? "auto-eligible" : "officer review")}");
            foreach (var f in m.FailedRules.Take(3)) sb.AppendLine("  · " + f);
        }
        return sb.ToString();
    }

    void Persist(ConversationDoc conv, ValidationResult validation)
    {
        var req = _store.Requests.FindById(conv.RequestId);
        if (req is not null)
        {
            req.Status = conv.Status;
            req.ApplicantName = conv.Extracted.PatientName ?? req.ApplicantName;
            req.Classification = validation.Classification;
            req.Snapshot = conv.Extracted;
            req.Validation = validation;
            req.UpdatedUtc = DateTime.UtcNow;
            _store.Requests.Update(req);
        }
        conv.UpdatedUtc = DateTime.UtcNow;
        _store.Conversations.Update(conv);
    }

    ConversationDoc GetConv(string id) =>
        _store.Conversations.FindById(id) ?? throw new InvalidOperationException("Conversation not found");

    MessageDoc AddAssistant(string conversationId, string text)
    {
        var msg = new MessageDoc { ConversationId = conversationId, Role = "assistant", Text = text, InputMode = "text" };
        _store.Messages.Insert(msg);
        return msg;
    }

    object Snapshot(string conversationId, MessageDoc? _ = null)
    {
        var conv = GetConv(conversationId);
        var req = _store.Requests.FindById(conv.RequestId);
        var messages = _store.Messages.Find(m => m.ConversationId == conversationId).OrderBy(m => m.CreatedUtc)
            .Select(m => new { m.Id, m.Role, m.Text, m.InputMode, m.CreatedUtc });
        var docs = _store.Documents.Find(d => d.ConversationId == conversationId)
            .Select(d => new { d.Id, d.DocumentType, d.FileName, d.Size });
        return new
        {
            conversationId = conv.Id,
            requestId = conv.RequestId,
            applicationNumber = req?.ApplicationNumber,
            status = conv.Status,
            uaePassSignedIn = conv.UaePassSignedIn,
            extracted = conv.Extracted,
            validation = req?.Validation,
            documents = docs,
            messages
        };
    }

    static object OfficerListItem(RequestDoc r) => new
    {
        r.Id,
        r.ApplicationNumber,
        r.Status,
        r.ApplicantName,
        r.Classification,
        r.CreatedUtc,
        r.UpdatedUtc,
        medicineCount = r.Validation?.Medicines.Count ?? r.Snapshot.Medicines.Count,
        unmatched = r.Validation?.Medicines.Count(m => !m.Matched) ?? 0
    };
}
