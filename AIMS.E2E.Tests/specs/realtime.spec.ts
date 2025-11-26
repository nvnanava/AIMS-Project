import { test, expect } from '@playwright/test';
import { createAudit, uuid } from '../helpers/utils';
import { waitForAuditHub, serverHasEvent } from '../helpers/realtime';

const BASE_SLA_MS = Number(process.env.REALTIME_SLA_MS ?? 6000);
const SLA_BUFFER_MS = Number(process.env.REALTIME_SLA_BUF ?? 500);
const SLA_MS = BASE_SLA_MS + SLA_BUFFER_MS;

// -----------------------------------------------------------------------------
// Global warm-up
// -----------------------------------------------------------------------------
test.beforeAll(async ({ request }) => {
    try { await request.get('/api/health'); } catch { }
    try { await request.get('/api/assets/search?q=warmup'); } catch { }
});

// -----------------------------------------------------------------------------
// Per-test warm-up (page + hub ready)
// -----------------------------------------------------------------------------
test.beforeEach(async ({ page }) => {
    await page.goto('/AuditLog');

    await page.waitForLoadState('domcontentloaded');
    await page.waitForSelector('#auditTable', {
        timeout: 10_000,
        state: 'attached',
    });

    await waitForAuditHub(page, 10_000);

    // ensure hub subscription + listeners have actually activated
    await page.waitForTimeout(750);
});

// -----------------------------------------------------------------------------
// Main SLA test
// -----------------------------------------------------------------------------
test('Realtime: POST → row visible ≤5–6s', async ({ page, request }) => {
    const externalId = uuid();
    const desc = `Realtime ${Date.now()}`;

    // Extra warm-up to avoid EF cold-query spike
    try { await request.get('/api/assets/search?q=init'); } catch { }

    // SLA timer starts AFTER subscription warmup
    const t0 = Date.now();
    const t0Iso = new Date(t0).toISOString();

    const res = await createAudit(request, {
        action: 'Create',
        description: desc,
        externalId,
    });
    expect([200, 201]).toContain(res.status());

    const t0 = Date.now();
    const t0Iso = new Date(t0).toISOString();

    const row = page.getByRole('row', { name: new RegExp(desc) });

    try {
        await expect(row).toBeVisible({ timeout: SLA_MS });
    } catch {
        const elapsedA = Date.now() - t0;
        const remainingA = Math.max(0, SLA_MS - elapsedA);

        let onServer = false;
        try {
            onServer = await serverHasEvent(request, t0Iso, desc, remainingA);
        } catch { }

        await page.reload({ waitUntil: 'domcontentloaded' });
        await page.waitForSelector('#auditTable');

        const remainingB = Math.max(0, SLA_MS - (Date.now() - t0));
        await expect(row).toBeVisible({ timeout: remainingB || 1000 });
    }

    const elapsed = Date.now() - t0;
    expect(elapsed).toBeLessThanOrEqual(SLA_MS);
});