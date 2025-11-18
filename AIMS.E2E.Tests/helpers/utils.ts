import { APIRequestContext, expect } from '@playwright/test';
import type { APIResponse } from '@playwright/test';
import { randomUUID } from 'crypto';
import fs from 'fs';
import path from 'path';

//
// ---------- ID cache (from global-setup) + env fallbacks ----------
//

const CACHE_PATH = path.resolve(__dirname, '..', '.e2e-cache.json');
let cached: { REAL_USER_ID?: number; REAL_HARDWARE_ID?: number } = {};
try {
    if (fs.existsSync(CACHE_PATH)) {
        cached = JSON.parse(fs.readFileSync(CACHE_PATH, 'utf8'));
    }
} catch {
    // ignore cache errors
}

export const REAL_USER_ID = Number(process.env.REAL_USER_ID ?? cached.REAL_USER_ID ?? 0);
export const REAL_HARDWARE_ID = Number(process.env.REAL_HARDWARE_ID ?? cached.REAL_HARDWARE_ID ?? 0);

//
// ---------- Basic Utilities ----------
//

export const uuid = (): string => randomUUID();
export const nowIso = (): string => new Date().toISOString();
export const isoMinutesAgo = (mins: number): string =>
    new Date(Date.now() - mins * 60_000).toISOString();

//
// ---------- Internal helpers (ID discovery) with retries ----------
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

function rowsFrom(data: Any | null): Any[] {
    return Array.isArray(data)
        ? data
        : Array.isArray(data?.items)
            ? data!.items
            : Array.isArray(data?.data)
                ? data!.data
                : [];
}

function pickFirstNumberRowId(rows: Any[], ...keys: string[]): number | null {
    for (const r of rows) {
        const id = pickNumber(r, ...keys);
        if (id) return id;
    }
    return null;
}

async function sleep(ms: number) {
    return new Promise((r) => setTimeout(r, ms));
}

async function retryUntil<T>(
    op: () => Promise<T | null>,
    ok: (t: T | null) => boolean,
    attempts = 40,          // ~10s @ 250ms
    delayMs = 250,
    label = 'retryUntil'
): Promise<T> {
    for (let i = 1; i <= attempts; i++) {
        const val = await op();
        if (ok(val)) return val as T;
        if (i % 5 === 0) console.warn(`[${label}] attempt ${i}/${attempts}…`);
        await sleep(delayMs);
    }
    throw new Error(`[${label}] timed out after ${attempts * delayMs}ms`);
}

// Fetch a single valid UserID with retries + endpoint fallbacks.
async function getAnyUserId(request: APIRequestContext): Promise<number> {
    const endpoints = ['/api/diag/users', '/api/users', '/api/users/get-all'];

    const id = await retryUntil<number | null>(
        async () => {
            for (const ep of endpoints) {
                const data = await safeJson<any>(() =>
                    request.get(ep, { headers: { Accept: 'application/json' } })
                );
                const rows = rowsFrom(data);
                const id = pickFirstNumberRowId(rows, 'UserID', 'userID', 'userId', 'id');
                if (id) return id;
            }
            return null;
        },
        (v) => typeof v === 'number' && v > 0,
        40,
        250,
        'getAnyUserId'
    );

    return id!;
}

// Fetch a single valid HardwareID with retries + endpoint fallbacks.
async function getAnyHardwareId(request: APIRequestContext): Promise<number> {
    const endpoints = ['/api/hardware/get-all', '/api/diag/hardware', '/api/hardware'];

    const id = await retryUntil<number | null>(
        async () => {
            for (const ep of endpoints) {
                const data = await safeJson<any>(() =>
                    request.get(ep, { headers: { Accept: 'application/json' } })
                );
                const rows = rowsFrom(data);
                const id = pickFirstNumberRowId(rows, 'HardwareID', 'hardwareID', 'hardwareId', 'id');
                if (id) return id;
            }
            return null;
        },
        (v) => typeof v === 'number' && v > 0,
        40,
        250,
        'getAnyHardwareId'
    );

    return id!;
}

// Optional validators to avoid 400s
async function isValidUserId(request: APIRequestContext, id: number): Promise<boolean> {
    if (!id) return false;
    const res = await request.get('/api/diag/users', { headers: { Accept: 'application/json' } });
    const data = (await res.json().catch(() => null)) as Any | null;
    const rows: Any[] = rowsFrom(data);
    return rows.some((r) => pickNumber(r, 'UserID', 'userID', 'userId', 'id') === id);
}

async function isValidHardwareId(request: APIRequestContext, id: number): Promise<boolean> {
    if (!id) return false;
    const res = await request.get('/api/hardware/get-all', { headers: { Accept: 'application/json' } });
    const data = (await res.json().catch(() => null)) as Any | null;
    const rows: Any[] = rowsFrom(data);
    return rows.some((r) => pickNumber(r, 'HardwareID', 'hardwareID', 'hardwareId', 'id') === id);
}

//
// ---------- Public API helpers ----------
//

/**
 * Creates an audit entry via /api/audit/create.
 * Prefers explicit args → env/cache (REAL_* IDs if valid) → discovery.
 * On HTTP 400 for invalid IDs, retries once with discovered IDs.
 */
export async function createAudit(
    request: APIRequestContext,
    {
        userId,
        action = 'Create',
        description = `E2E seed @ ${nowIso()}`,
        assetKind = 1, // 1 = Hardware, 2 = Software
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
    // Prefer explicit param → env/cache (validated) → discovery (with retries)
    let resolvedUserId =
        userId ??
        (await (async () =>
            (await isValidUserId(request, REAL_USER_ID)) ? REAL_USER_ID : await getAnyUserId(request)
        )());

    let resolvedHardwareId: number | null = hardwareId ?? null;
    if (assetKind === 1) {
        if (!resolvedHardwareId) {
            resolvedHardwareId =
                (await isValidHardwareId(request, REAL_HARDWARE_ID))
                    ? REAL_HARDWARE_ID
                    : await getAnyHardwareId(request);
        }
        softwareId = null; // XOR
    } else if (assetKind === 2) {
        if (softwareId == null) {
            throw new Error(
                'assetKind=Software requires a softwareId. Auto-resolve only implemented for Hardware.'
            );
        }
        resolvedHardwareId = null;
    } else {
        throw new Error('assetKind must be 1 (Hardware) or 2 (Software).');
    }

    const send = async (uid: number, hid: number | null) =>
        request.post('/api/audit/create', {
            headers: { 'Content-Type': 'application/json' },
            data: {
                userId: uid,
                action,
                description,
                snapshotJson: null as string | null,
                assetKind,
                hardwareId: hid,
                softwareId,
                externalId,
            },
        });

    // First attempt
    let res = await send(resolvedUserId, resolvedHardwareId);
    if (res.status() === 400) {
        const body = (await res.text().catch(() => '')) || '';
        const badUser = /User with ID .* does not exist/i.test(body);
        const badHw = /Hardware with ID .* does not exist/i.test(body);

        if (badUser || badHw) {
            // Retry with discovered IDs
            if (badUser) resolvedUserId = await getAnyUserId(request);
            if (assetKind === 1 && (badHw || !resolvedHardwareId)) {
                resolvedHardwareId = await getAnyHardwareId(request);
            }
            res = await send(resolvedUserId, resolvedHardwareId);
        }
    }

    const status = res.status();
    if (status !== 200 && status !== 201) {
        const text = await res.text().catch(() => '');
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