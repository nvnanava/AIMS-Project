// File: AIMS.Contracts/AuditEventDto.cs
using System;

namespace AIMS.Contracts
{
    public sealed class AuditEventDto
    {
        /// <summary>
        /// Stable identifier for the event used on the client for dedup.
        /// Prefer ExternalId when available, otherwise AuditLogID.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        public DateTime OccurredAtUtc { get; set; }

        /// <summary>High-level type of event, e.g. "Assign", "Unassign", "Security".</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Display name of the actor, e.g. "Adam Lopez (12)".</summary>
        public string User { get; set; } = string.Empty;

        /// <summary>Target string, e.g. "Hardware#23" or "Software#5".</summary>
        public string Target { get; set; } = string.Empty;

        /// <summary>Human-readable description shown in the audit table.</summary>
        public string Details { get; set; } = string.Empty;

        /// <summary>Last changed field (if any) for quick diff context.</summary>
        public string? ChangeField { get; set; }

        public string? PrevValue { get; set; }
        public string? NewValue { get; set; }

        /// <summary>Stable SHA-256 fingerprint for client-side dedup.</summary>
        public string Hash { get; set; } = string.Empty;

        /// <summary>
        /// Optional assignment ID (used by the UI to build the Preview Agreement URL).
        /// </summary>
        public int? AssignmentID { get; set; }
    }
}
