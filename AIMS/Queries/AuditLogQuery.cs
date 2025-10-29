// File: AIMS/Queries/AuditLogQuery.cs
/* ======================================================================
   AIMS Query: AuditLogQuery (Task 1.4 - paged search w/ cached totals)
   ----------------------------------------------------------------------
   Changes (surgical):
   - actor filter is now string-based (name contains) with optional int UserID match
   - Criteria, CacheKey, and SearchAsync signatures updated (actor: string?)
   - ApplyActor(...) updated to handle name or numeric id
   ====================================================================== */

using System.Linq.Expressions;
using System.Text;
using AIMS.Data;
using AIMS.Dtos.Audit;
using AIMS.Dtos.Common;
using AIMS.Models;
using AIMS.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AIMS.Queries
{
    #region DTOs (rows)

    /// <summary>Lightweight row for paged Audit Log table (read-only).</summary>
    public sealed class AuditLogRowDto
    {
        public int AuditLogID { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public int UserID { get; set; }
        public string Actor { get; set; } = string.Empty;
        public AuditLogAction Action { get; set; }
        public AssetKind AssetKind { get; set; }
        public int? HardwareID { get; set; }
        public int? SoftwareID { get; set; }
        public string Target { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    #endregion

    #region Functional helpers

    /// <summary>
    /// Tiny functional helper to chain query transforms without adding branches.
    /// </summary>
    public static class QueryableExtensions
    {
        public static IQueryable<T> Pipe<T>(this IQueryable<T> source, Func<IQueryable<T>, IQueryable<T>> transform)
            => transform(source);
    }

    #endregion

    #region Criteria + CacheKey (actor is string?)

    /// <summary>Normalized, immutable search criteria for Audit Log paging.</summary>
    public readonly struct Criteria
    {
        public int Page { get; }
        public int PageSize { get; }
        public string? Q { get; }
        public DateTime? FromUtc { get; }
        public DateTime? ToUtc { get; }
        public string? Actor { get; }     // string? (used for FullName contains; numeric allowed)
        public string? Action { get; }
        public AssetKind? Kind { get; }
        public int? HardwareId { get; }
        public int? SoftwareId { get; }

        private Criteria(
            int page, int pageSize, string? q,
            DateTime? fromUtc, DateTime? toUtc,
            string? actor, string? action,
            AssetKind? kind, int? hardwareId, int? softwareId)
        {
            Page = Math.Max(1, page);
            PageSize = Math.Clamp(pageSize, 5, 100);
            Q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
            FromUtc = fromUtc;
            ToUtc = toUtc;
            Actor = string.IsNullOrWhiteSpace(actor) ? null : actor.Trim();
            Action = string.IsNullOrWhiteSpace(action) ? null : action.Trim();
            Kind = kind;
            HardwareId = hardwareId;
            SoftwareId = softwareId;
        }

        /// <summary>Factory: normalize raw inputs (bounds-check, trim).</summary>
        public static Criteria Normalize(
            int page, int pageSize, string? q,
            DateTime? fromUtc, DateTime? toUtc,
            string? actor, string? action,
            AssetKind? kind, int? hardwareId, int? softwareId)
            => new(page, pageSize, q, fromUtc, toUtc, actor, action, kind, hardwareId, softwareId);
    }

    /// <summary>Stable cache key builder for Audit Log search (excludes page/size).</summary>
    public static class CacheKey
    {
        public static string ForSearch(Criteria c)
            => $"audit:search:q={c.Q ?? ""}|from={c.FromUtc?.ToString("O") ?? ""}|to={c.ToUtc?.ToString("O") ?? ""}|actor={c.Actor ?? ""}|action={c.Action ?? ""}|kind={(c.Kind.HasValue ? ((int)c.Kind.Value).ToString() : "")}|hid={c.HardwareId?.ToString() ?? ""}|sid={c.SoftwareId?.ToString() ?? ""}";
    }

    #endregion

    /// <summary>
    /// Audit Log read/write surface:
    /// - Paged search with cache (Paging.PageExactCachedAsync)
    /// - Detail retrieval
    /// - Upsert (create/update) + realtime broadcast
    /// </summary>
    public sealed class AuditLogQuery
    {
        private readonly AimsDbContext _db;
        private readonly IAuditEventBroadcaster _broadcaster;
        private readonly IMemoryCache _cache;

        public AuditLogQuery(AimsDbContext db, IAuditEventBroadcaster broadcaster, IMemoryCache cache)
        {
            _db = db;
            _broadcaster = broadcaster;
            _cache = cache;
        }

        #region Read Models

        /// <summary>All audit records (admin/diagnostics). Use paged search for UI.</summary>
        public async Task<List<GetAuditRecordDto>> GetAllAuditRecordsAsync(CancellationToken ct = default)
            => await _db.AuditLogs.AsNoTracking()
                .OrderByDescending(a => a.TimestampUtc)
                .Select(ProjectFullDto())
                .ToListAsync(ct);

        /// <summary>Single audit record by ID (includes child Changes).</summary>
        public async Task<GetAuditRecordDto?> GetAuditRecordAsync(int auditLogId, CancellationToken ct = default)
            => await _db.AuditLogs.AsNoTracking()
                .Where(a => a.AuditLogID == auditLogId)
                .Select(ProjectFullDto())
                .FirstOrDefaultAsync(ct);

        /// <summary>Recent audit records for a specific asset (preview modal).</summary>
        public async Task<List<GetAuditRecordDto>> GetRecentAuditRecordsAsync(string assetKind, int assetId, int take = 5, CancellationToken ct = default)
        {
            var kind = Enum.TryParse<AssetKind>(assetKind, true, out var parsedKind)
                ? parsedKind : AssetKind.Hardware;

            return await _db.AuditLogs.AsNoTracking()
                .Where(a =>
                    a.AssetKind == kind &&
                    ((kind == AssetKind.Hardware && a.HardwareID == assetId) ||
                     (kind == AssetKind.Software && a.SoftwareID == assetId)))
                .OrderByDescending(a => a.TimestampUtc)
                .Take(take)
                .Select(ProjectRecentDto())
                .ToListAsync(ct);
        }

        #endregion

        #region Search (Paged)

        /// <summary>
        /// Paged, filtered search over Audit Logs with deterministic ordering and cached totals.
        /// Mirrors Asset search (Paging.PageExactCachedAsync).
        /// </summary>
        public async Task<PagedResult<AuditLogRowDto>> SearchAsync(
            int page, int pageSize, string? q,
            DateTime? fromUtc, DateTime? toUtc,
            string? actor, string? action,                  // actor is string?
            AssetKind? kind, int? hardwareId, int? softwareId,
            CancellationToken ct = default)
        {
            var c = Criteria.Normalize(page, pageSize, q, fromUtc, toUtc, actor, action, kind, hardwareId, softwareId);

            var baseQ = _db.AuditLogs.AsNoTracking().Include(a => a.User);
            var filtered = ApplyFilters(baseQ, c);
            var ordered = OrderNewestFirst(filtered);
            var rows = ProjectRows(ordered);

            var cacheKey = CacheKey.ForSearch(c);
            return await Paging.PageExactCachedAsync(_cache, cacheKey, rows, c.Page, c.PageSize, ct);
        }

        #endregion

        #region Create / Update

        /// <summary>
        /// Upsert an audit record (ExternalId key when present): validates, persists, broadcasts.
        /// </summary>
        public async Task<int> CreateAuditRecordAsync(CreateAuditRecordDto data, CancellationToken ct = default)
        {
            GuardInput(data);
            await EnsureValidActorAsync(data.UserID, ct);
            await EnsureValidTargetAsync(data, ct);

            var log = await UpsertAsync(data, ct);
            await BroadcastAsync(log, ct);
            return log.AuditLogID;
        }

        #endregion

        #region DTO Projections

        private static Expression<Func<AuditLog, GetAuditRecordDto>> ProjectFullDto() => a => new GetAuditRecordDto
        {
            AuditLogID = a.AuditLogID,
            ExternalId = a.ExternalId,
            TimestampUtc = a.TimestampUtc,
            UserID = a.UserID,
            UserName = a.User.FullName,
            Action = a.Action,
            Description = a.Description,
            AssetKind = a.AssetKind,
            HardwareID = a.HardwareID,
            SoftwareID = a.SoftwareID,
            HardwareName = a.HardwareAsset != null ? a.HardwareAsset.AssetName : null,
            SoftwareName = a.SoftwareAsset != null ? a.SoftwareAsset.SoftwareName : null,
            Changes = a.Changes.OrderBy(c => c.AuditLogChangeID)
                .Select(c => new AuditLogChangeDto
                {
                    AuditLogChangeID = c.AuditLogChangeID,
                    Field = c.Field,
                    OldValue = c.OldValue,
                    NewValue = c.NewValue
                }).ToList()
        };

        private static Expression<Func<AuditLog, GetAuditRecordDto>> ProjectRecentDto() => a => new GetAuditRecordDto
        {
            AuditLogID = a.AuditLogID,
            ExternalId = a.ExternalId,
            TimestampUtc = a.TimestampUtc,
            UserID = a.UserID,
            UserName = a.User.FullName,
            Action = a.Action,
            Description = a.Description,
            AssetKind = a.AssetKind,
            HardwareID = a.HardwareID,
            SoftwareID = a.SoftwareID
        };

        private static IQueryable<AuditLogRowDto> ProjectRows(IQueryable<AuditLog> q)
            => q.Select(a => new AuditLogRowDto
            {
                AuditLogID = a.AuditLogID,
                OccurredAtUtc = a.TimestampUtc,
                UserID = a.UserID,
                Actor = a.User.FullName,
                Action = a.Action,
                AssetKind = a.AssetKind,
                HardwareID = a.HardwareID,
                SoftwareID = a.SoftwareID,
                Target = a.AssetKind == AssetKind.Hardware
                    ? (a.HardwareID.HasValue ? $"Hardware#{a.HardwareID}" : "Hardware")
                    : (a.SoftwareID.HasValue ? $"Software#{a.SoftwareID}" : "Software"),
                Details = a.Description
            });

        #endregion

        #region Filter + Cache Helpers

        /// <summary>Consistent ordering to keep paging stable.</summary>
        private static IQueryable<AuditLog> OrderNewestFirst(IQueryable<AuditLog> q)
            => q.OrderByDescending(a => a.TimestampUtc).ThenByDescending(a => a.AuditLogID);

        /// <summary>Apply all filters as a simple chain (low complexity).</summary>
        private static IQueryable<AuditLog> ApplyFilters(IQueryable<AuditLog> q, Criteria c)
            => q
                .Pipe(q1 => ApplyDateRange(q1, c.FromUtc, c.ToUtc))
                .Pipe(q2 => ApplyActor(q2, c.Actor))           // updated
                                                               //         .Pipe(q3 => ApplyAction(q3, c.Action))
                .Pipe(q4 => ApplyKind(q4, c.Kind))
                .Pipe(q5 => ApplyTargets(q5, c.HardwareId, c.SoftwareId))
                .Pipe(q6 => ApplySearchText(q6, c.Q));

        private static IQueryable<AuditLog> ApplyDateRange(IQueryable<AuditLog> q, DateTime? fromUtc, DateTime? toUtc)
        {
            if (fromUtc.HasValue) q = q.Where(a => a.TimestampUtc >= fromUtc.Value);
            if (toUtc.HasValue) q = q.Where(a => a.TimestampUtc <= toUtc.Value);
            return q;
        }

        // NEW: actor by name contains; if numeric, also match exact UserID
        private static IQueryable<AuditLog> ApplyActor(IQueryable<AuditLog> q, string? actor)
        {
            if (string.IsNullOrWhiteSpace(actor)) return q;

            if (int.TryParse(actor, out var id))
                return q.Where(a => a.UserID == id || a.User.FullName.Contains(actor));

            return q.Where(a => a.User.FullName.Contains(actor));
        }

        private static IQueryable<AuditLog> ApplyAction(IQueryable<AuditLog> q, AuditLogAction? action)
            => action is null ? q : q.Where(a => a.Action == action);

        private static IQueryable<AuditLog> ApplyKind(IQueryable<AuditLog> q, AssetKind? kind)
            => kind.HasValue ? q.Where(a => a.AssetKind == kind.Value) : q;

        private static IQueryable<AuditLog> ApplyTargets(IQueryable<AuditLog> q, int? hardwareId, int? softwareId)
        {
            if (hardwareId.HasValue) q = q.Where(a => a.HardwareID == hardwareId.Value);
            if (softwareId.HasValue) q = q.Where(a => a.SoftwareID == softwareId.Value);
            return q;
        }

        private static IQueryable<AuditLog> ApplySearchText(IQueryable<AuditLog> q, string? term)
            => string.IsNullOrWhiteSpace(term)
                ? q
                : q.Where(a =>
                    a.Description.Contains(term!) ||
                    // a.Action.Contains(term!) ||
                    a.User.FullName.Contains(term!));

        #endregion

        #region Validation (split into tiny helpers)

        /// <summary>Basic guard rails for required inputs.</summary>
        private static void GuardInput(CreateAuditRecordDto data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(data.Description)) throw new ArgumentException("Description is required.");
        }

        /// <summary>Ensure actor exists.</summary>
        private async Task EnsureValidActorAsync(int userId, CancellationToken ct)
        {
            var ok = await _db.Users.AsNoTracking().AnyAsync(u => u.UserID == userId, ct);
            if (!ok) throw new InvalidOperationException($"User with ID {userId} does not exist.");
        }

        /// <summary>Ensure target IDs are present/consistent with AssetKind.</summary>
        private async Task EnsureValidTargetAsync(CreateAuditRecordDto data, CancellationToken ct)
        {
            if (data.AssetKind == AssetKind.Hardware)
            {
                await EnsureHardwareTargetAsync(data.HardwareID, data.SoftwareID, ct);
                return;
            }
            if (data.AssetKind == AssetKind.Software)
            {
                await EnsureSoftwareTargetAsync(data.SoftwareID, data.HardwareID, ct);
                return;
            }
            throw new InvalidOperationException("Unknown AssetKind.");
        }

        private async Task EnsureHardwareTargetAsync(int? hardwareId, int? softwareId, CancellationToken ct)
        {
            if (hardwareId is null)
                throw new InvalidOperationException("For AssetKind=Hardware, HardwareID must be provided.");

            var exists = await _db.HardwareAssets.AsNoTracking()
                .AnyAsync(h => h.HardwareID == hardwareId, ct);
            if (!exists) throw new InvalidOperationException("Please specify a valid HardwareID.");

            if (softwareId is not null)
                throw new InvalidOperationException("Specify only HardwareID for AssetKind=Hardware.");
        }

        private async Task EnsureSoftwareTargetAsync(int? softwareId, int? hardwareId, CancellationToken ct)
        {
            if (softwareId is null)
                throw new InvalidOperationException("For AssetKind=Software, SoftwareID must be provided.");

            var exists = await _db.SoftwareAssets.AsNoTracking()
                .AnyAsync(s => s.SoftwareID == softwareId, ct);
            if (!exists) throw new InvalidOperationException("Please specify a valid SoftwareID.");

            if (hardwareId is not null)
                throw new InvalidOperationException("Specify only SoftwareID for AssetKind=Software.");
        }

        #endregion

        #region Upsert (split into very small steps)

        /// <summary>
        /// Upsert by ExternalId when present; otherwise insert.
        /// Split into small helpers to reduce complexity.
        /// </summary>
        private async Task<AuditLog> UpsertAsync(CreateAuditRecordDto data, CancellationToken ct)
        {
            var log = await FindExistingByExternalIdAsync(data.ExternalId, ct);
            if (log is not null)
            {
                UpdateCoreFields(log, data);
                SetPayloads(log, data.SnapshotJson);
                SetTargets(log, data);
                AppendChanges(log, data.Changes);
                await _db.SaveChangesAsync(ct);
                return log;
            }

            log = CreateNewEntity(data);
            AppendChanges(log, data.Changes);
            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync(ct);
            return log;
        }

        /// <summary>Find existing row by ExternalId (ignores Guid.Empty).</summary>
        private Task<AuditLog?> FindExistingByExternalIdAsync(Guid? externalId, CancellationToken ct)
        {
            if (!externalId.HasValue || externalId.Value == Guid.Empty)
                return Task.FromResult<AuditLog?>(null);

            return _db.AuditLogs.FirstOrDefaultAsync(a => a.ExternalId == externalId.Value, ct);
        }

        /// <summary>Update core scalar fields from DTO (no collections).</summary>
        private static void UpdateCoreFields(AuditLog log, CreateAuditRecordDto data)
        {
            log.TimestampUtc = DateTime.UtcNow;
            log.UserID = data.UserID;
            log.Action = data.Action;
            log.Description = data.Description;
            log.AssetKind = data.AssetKind;
        }

        /// <summary>Set binary payloads (AttachmentBytes unused; SnapshotBytes optional).</summary>
        private static void SetPayloads(AuditLog log, string? snapshotJson)
        {
            log.AttachmentBytes = null;
            log.SnapshotBytes = ToBytesOrNull(snapshotJson);
        }

        /// <summary>Set target IDs consistent with AssetKind.</summary>
        private static void SetTargets(AuditLog log, CreateAuditRecordDto data)
        {
            log.HardwareID = data.AssetKind == AssetKind.Hardware ? data.HardwareID : null;
            log.SoftwareID = data.AssetKind == AssetKind.Software ? data.SoftwareID : null;
        }

        /// <summary>Create a new entity from DTO (without changes collection).</summary>
        private static AuditLog CreateNewEntity(CreateAuditRecordDto data) => new()
        {
            ExternalId = data.ExternalId.HasValue && data.ExternalId.Value != Guid.Empty
                ? data.ExternalId.Value
                : Guid.Empty,
            TimestampUtc = DateTime.UtcNow,
            UserID = data.UserID,
            Action = data.Action,
            Description = data.Description,
            AttachmentBytes = null,
            SnapshotBytes = ToBytesOrNull(data.SnapshotJson),
            AssetKind = data.AssetKind,
            HardwareID = data.AssetKind == AssetKind.Hardware ? data.HardwareID : null,
            SoftwareID = data.AssetKind == AssetKind.Software ? data.SoftwareID : null
        };

        /// <summary>Append child changes (no-op on null/empty).</summary>
        private static void AppendChanges(AuditLog log, List<CreateAuditLogChangeDto>? changes)
        {
            if (changes is null || changes.Count == 0) return;
            foreach (var c in changes)
            {
                log.Changes.Add(new AuditLogChange
                {
                    Field = c.Field,
                    OldValue = c.OldValue,
                    NewValue = c.NewValue
                });
            }
        }

        private static byte[]? ToBytesOrNull(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : Encoding.UTF8.GetBytes(s);

        #endregion

        #region Broadcast (split for clarity)

        /// <summary>
        /// Broadcast a lightweight event to SignalR clients after persistence.
        /// </summary>
        private async Task BroadcastAsync(AuditLog log, CancellationToken ct)
        {
            var dto = await BuildEventDtoAsync(log, ct);
            await _broadcaster.BroadcastAsync(dto);
        }

        /// <summary>Build the event DTO (includes user lookup + stable hash).</summary>
        private async Task<AIMS.Contracts.AuditEventDto> BuildEventDtoAsync(AuditLog log, CancellationToken ct)
        {
            var userName = await _db.Users.AsNoTracking()
                .Where(u => u.UserID == log.UserID)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct);

            return new AIMS.Contracts.AuditEventDto
            {
                Id = (log.ExternalId != Guid.Empty ? log.ExternalId.ToString() : log.AuditLogID.ToString()),
                OccurredAtUtc = log.TimestampUtc,
                Type = log.Action.ToString(),
                User = $"{(userName ?? $"User#{log.UserID}")} ({log.UserID})",
                Target = log.AssetKind == AssetKind.Hardware
                    ? (log.HardwareID.HasValue ? $"Hardware#{log.HardwareID}" : "Hardware")
                    : (log.SoftwareID.HasValue ? $"Software#{log.SoftwareID}" : "Software"),
                Details = log.Description ?? "",
                Hash = ComputeEventHash(log)
            };
        }

        /// <summary>Stable hash fingerprint for dedup on the client.</summary>
        private static string ComputeEventHash(AuditLog log)
        {
            var raw =
                $"{log.AuditLogID}|{log.ExternalId}|{log.TimestampUtc:o}|{log.Action.ToString()}|{log.UserID}|{log.AssetKind}|{log.HardwareID}|{log.SoftwareID}|{log.Description}";
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        }

        #endregion
    }
}