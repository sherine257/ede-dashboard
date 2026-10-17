using EdePermit.Core;
using LiteDB;

namespace EdePermit.Api;

public sealed class ConversationDoc
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public bool UaePassSignedIn { get; set; }
    public bool ChecklistSent { get; set; }
    public string Status { get; set; } = ApplicationStatuses.Collecting;
    public string RequestId { get; set; } = "";
    public ExtractedApplication Extracted { get; set; } = new();
}

public sealed class MessageDoc
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; set; } = "";
    public string Role { get; set; } = "assistant";
    public string Text { get; set; } = "";
    public string InputMode { get; set; } = "text";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RequestDoc
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; set; } = "";
    public string ApplicationNumber { get; set; } = "";
    public string Status { get; set; } = ApplicationStatuses.Collecting;
    public string ApplicantName { get; set; } = "";
    public string Classification { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public ExtractedApplication Snapshot { get; set; } = new();
    public ValidationResult? Validation { get; set; }
    public string OfficerNotes { get; set; } = "";
}

public sealed class DocumentDoc
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string DocumentType { get; set; } = "additional";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public string StoragePath { get; set; } = "";
    public DateTime UploadedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AuditDoc
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string Actor { get; set; } = "system";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class LiteStore : IDisposable
{
    readonly LiteDatabase _db;
    public ILiteCollection<ConversationDoc> Conversations { get; }
    public ILiteCollection<MessageDoc> Messages { get; }
    public ILiteCollection<RequestDoc> Requests { get; }
    public ILiteCollection<DocumentDoc> Documents { get; }
    public ILiteCollection<AuditDoc> Audit { get; }

    public LiteStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _db = new LiteDatabase(path);
        Conversations = _db.GetCollection<ConversationDoc>("conversations");
        Messages = _db.GetCollection<MessageDoc>("messages");
        Requests = _db.GetCollection<RequestDoc>("requests");
        Documents = _db.GetCollection<DocumentDoc>("documents");
        Audit = _db.GetCollection<AuditDoc>("audit");
        Conversations.EnsureIndex(x => x.Id, true);
        Messages.EnsureIndex(x => x.ConversationId);
        Requests.EnsureIndex(x => x.ConversationId);
        Requests.EnsureIndex(x => x.ApplicationNumber);
        Documents.EnsureIndex(x => x.ConversationId);
        Audit.EnsureIndex(x => x.RequestId);
    }

    public void AuditEvent(string conversationId, string requestId, string actor, string action, string detail)
    {
        Audit.Insert(new AuditDoc
        {
            ConversationId = conversationId,
            RequestId = requestId,
            Actor = actor,
            Action = action,
            Detail = detail
        });
    }

    public void Dispose() => _db.Dispose();
}
