import { APIRequestContext, expect } from '@playwright/test';
import type { APIResponse } from '@playwright/test';
import { randomUUID } from 'crypto';

//
// ---------- Basic Utilities ----------
//

// Generates a UUID v4 using Node’s built-in crypto API.
export const uuid = (): string => randomUUID();

//Returns the current UTC timestamp in ISO 8601 format.
export const nowIso = (): string => new Date().toISOString();

// Returns an ISO timestamp N minutes ago.
export const isoMinutesAgo = (mins: number): string =>
    new Date(Date.now() - mins * 60_000).toISOString();

//
// ---------- Internal helpers (ID discovery) ----------
//

type Any = Record<string, any>;

function coerceNumber(v: unknown): number | null {
    const n = typeof v === 'string' ? Number(v) : (v as number);
    return Number.isFinite(n) && n > 0 ? n : null;
}

function pickNumber(obj: Any, ...keys: string[]): number | null {
    for (const k of keys) {
        const n = coerceNumber(obj?.[k]);
        if (n) return n;
    }
    return null;
}

async function safeJson<T = any>(call: () => Promise<APIResponse>): Promise<T | null> {
    try {
        const res = await call();
        if (!res.ok()) return null;
        return (await res.json()) as T;
    } catch {
        return null;
    }
}

// Fetch a single valid UserID from /api/diag/users.
async function getAnyUserId(request: APIRequestContext): Promise<number> {
    const data = await safeJson<any>(() =>
        request.get('/api/diag/users', { headers: { Accept: 'application/json' } })
    );

    const rows: Any[] =
        Array.isArray(data) ? data :
            Array.isArray((data as Any)?.items) ? (data as Any).items :
                Array.isArray((data as Any)?.data) ? (data as Any).data : [];

    for (const r of rows) {
        // Try common shapes
        const id = pickNumber(r, 'UserID', 'userID', 'userId', 'id');
        if (id) return id;
    }

    throw new Error(
        'E2E setup error: could not find a valid UserID from /api/diag/users. ' +
        'Ensure that endpoint returns at least one user with a numeric ID.'
    );
}

// Fetch a single valid HardwareID from /api/hardware/get-all (required for AssetKind=Hardware).
async function getAnyHardwareId(request: APIRequestContext): Promise<number> {
    const data = await safeJson<any>(() =>
        request.get('/api/hardware/get-all', { headers: { Accept: 'application/json' } })
    );

    const rows: Any[] =
        Array.isArray(data) ? data :
            Array.isArray((data as Any)?.items) ? (data as Any).items :
                Array.isArray((data as Any)?.data) ? (data as Any).data : [];

    for (const r of rows) {
        const id = pickNumber(r, 'HardwareID', 'hardwareID', 'hardwareId', 'id');
        if (id) return id;
    }

    throw new Error(
        'E2E setup error: could not find a valid HardwareID from /api/hardware/get-all. ' +
        'Ensure that endpoint returns at least one hardware asset with a numeric ID.'
    );
}

//
// ---------- Public API helpers ----------
//

/**
 * Creates an audit entry through the API (/api/audit/create).
 * If userId/hardwareId are not provided, they are **resolved** strictly via:
 *   - /api/diag/users  (UserID)
 *   - /api/hardware/get-all (HardwareID)
 */
export async function createAudit(
    request: APIRequestContext,
    {
        userId,
        action = 'Create',
        description = `E2E seed @ ${nowIso()}`,
        assetKind = 1,         // 1 = Hardware, 2 = Software (we only auto-resolve Hardware in this helper)
        hardwareId,
        softwareId = null,
        externalId,
    }: Partial<{
        userId: number;
        action: string;
        description: string;
        assetKind: 1 | 2;
        hardwareId: number | null;
        softwareId: number | null;
        externalId?: string;
    }> = {}
): Promise<APIResponse> {
    // Resolve only from our required endpoints
    const resolvedUserId = userId ?? (await getAnyUserId(request));

    let resolvedHardwareId: number | null = hardwareId ?? null;
    if (assetKind === 1) {
        resolvedHardwareId = resolvedHardwareId ?? (await getAnyHardwareId(request));
        softwareId = null; // XOR rule
    } else if (assetKind === 2) {
        // If we later need software support, add a /api/software/... resolver here.
        if (softwareId == null) {
            throw new Error(
                'E2E setup error: assetKind=Software requires a softwareId. ' +
                'This helper only auto-resolves Hardware via /api/hardware/get-all.'
            );
        }
        resolvedHardwareId = null;
    } else {
        throw new Error('assetKind must be 1 (Hardware) or 2 (Software).');
    }

    const payload = {
        userId: resolvedUserId,
        action,
        description,
        snapshotJson: null as string | null,
        assetKind,
        hardwareId: resolvedHardwareId,
        softwareId,
        externalId,
    };

    const res = await request.post('/api/audit/create', {
        headers: { 'Content-Type': 'application/json' },
        data: payload,
    });

    const status = res.status();
    const text = await res.text().catch(() => '');
    if (status !== 200 && status !== 201) {
        throw new Error(`createAudit failed: HTTP ${status} — ${text}`);
    }
    return res;
}

//
// ---------- Polling + Events ----------
//

export type EventsResponse = {
    items: Array<{
        id: string;
        occurredAtUtc: string;
        type: string;
        user: string;
        target: string;
        details: string;
        hash: string;
    }>;
    nextSince: string;
};

// Fetch events with optional ETag support.
export async function getEvents(
    request: APIRequestContext,
    { since, take = 50, etag = '' }: { since: string; take?: number; etag?: string }
): Promise<{ status: number; etag?: string; json?: EventsResponse }> {
    const res = await request.get('/api/audit/events', {
        params: { since, take: String(take) },
        headers: { Accept: 'application/json', 'If-None-Match': etag },
    });

    const status = res.status();
    const newEtag = res.headers()['etag'];
    if (status === 304) return { status, etag: newEtag };

    const json = (await res.json().catch(() => undefined)) as EventsResponse | undefined;
    return { status, etag: newEtag, json };
}

// Poll until a given event ID appears in /api/audit/events.
export async function waitForEventById(
    request: APIRequestContext,
    {
        id,
        startSince,
        timeoutMs = 10_000,
        pollMs = 1_000,
    }: { id: string; startSince: string; timeoutMs?: number; pollMs?: number }
): Promise<{ seenAt: number; payload?: EventsResponse['items'][number] }> {
    const start = performance.now();
    let since = startSince;
    let etag = '';

    while (performance.now() - start < timeoutMs) {
        const { status, etag: e, json } = await getEvents(request, { since, etag });
        if (status === 200 && json) {
            etag = e ?? etag;
            since = json.nextSince ?? since;

            const hit = json.items.find((i) => i.id === id);
            if (hit) return { seenAt: performance.now() - start, payload: hit };
        }
        await new Promise((r) => setTimeout(r, pollMs));
    }

    throw new Error(`Timeout waiting for event id=${id} within ${timeoutMs}ms`);
}

// Validate items are sorted by occurredAtUtc (desc).
export function expectDescSorted(items: EventsResponse['items']) {
    const ts = items.map((i) => Date.parse(i.occurredAtUtc));
    const sorted = [...ts].sort((a, b) => b - a);
    expect(ts).toEqual(sorted);
}