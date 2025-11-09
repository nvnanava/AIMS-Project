import type { Page, APIRequestContext, request as pwRequestNS } from '@playwright/test';

/** Wait for the Audit SignalR hub websocket to be present (best-effort). */
export async function waitForAuditHub(page: Page, timeoutMs = 10_000) {
    // Try to detect an already-open audit hub websocket (Playwright keeps a list internally)
    // @ts-expect-error private context helper in PW; guarded use
    const existing = page.context()['__pw_webSockets']?.()
        ?.find?.((ws: any) => ws.url().includes('/hubs/audit') && !ws.isClosed?.());
    if (existing) return;

    // Otherwise wait for it to open
    await page.waitForEvent('websocket', {
        timeout: timeoutMs,
        predicate: (ws) => ws.url().includes('/hubs/audit')
    }).catch(() => { /* best-effort; don’t fail tests if already connected */ });
}

/** Polls GET /api/audit/events?since=… until an item with `desc` is observed (or timeout). */
export async function serverHasEvent(
    request: APIRequestContext,
    sinceIso: string,
    desc: string,
    timeoutMs = 4_000
) {
    const started = Date.now();
    while (Date.now() - started < timeoutMs) {
        const url = `/api/audit/events?since=${encodeURIComponent(sinceIso)}&take=200`;
        const res = await request.get(url);
        if (res.ok()) {
            const body = await res.json();
            const items = (body?.items ?? []) as Array<{ details?: string }>;
            if (items.some(i => (i.details ?? '').includes(desc))) return true;
        }
        await new Promise(r => setTimeout(r, 150));
    }
    return false;
}

/** Polls GET /api/audit/events/latest until `externalId` (and optional `expectUpdatedDesc`) is visible. */
export async function waitForApiSeesExternalId(
    baseURL: string,
    externalId: string,
    expectUpdatedDesc?: string,
    totalWaitMs = Number(process.env.DEDUP_API_WAIT_MS ?? 8_000),
    take = Number(process.env.DEDUP_LATEST_TAKE ?? 50),
    requestFactory: typeof pwRequestNS | undefined = undefined
): Promise<boolean> {
    const pwRequest = requestFactory ?? require('@playwright/test').request;
    const ctx = await pwRequest.newContext({ baseURL });
    const deadline = Date.now() + totalWaitMs;

    try {
        while (Date.now() < deadline) {
            const r = await ctx.get(`/api/audit/events/latest?take=${take}`);
            if (r.ok()) {
                const body = await r.json().catch(() => null as any);
                const items: any[] = Array.isArray(body?.items) ? body.items : [];
                const hit = items.find(
                    x =>
                        x?.id === externalId &&
                        (expectUpdatedDesc ? x?.details === expectUpdatedDesc : true)
                );
                if (hit) return true;
            }
            await new Promise(res => setTimeout(res, 200));
        }
        return false;
    } finally {
        await ctx.dispose();
    }
}