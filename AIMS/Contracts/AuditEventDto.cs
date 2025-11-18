using System;

namespace AIMS.Contracts;

// Lightweight payload for streaming/polling audit events to the client.
public sealed class AuditEventDto
{
    public string Id { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public string Type { get; set; } = "";
    public string User { get; set; } = "";
    public string Target { get; set; } = "";
    public string Details { get; set; } = "";
    public string Hash { get; set; } = "";
    public string? ChangeField { get; set; }
    public string? PrevValue { get; set; }
    public string? NewValue { get; set; }
}
