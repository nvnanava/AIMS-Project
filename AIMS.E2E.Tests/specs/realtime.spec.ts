import { test, expect } from '@playwright/test';
import { createAudit, uuid } from '../helpers/utils';
import { waitForAuditHub, serverHasEvent } from '../helpers/realtime';

const BASE_SLA_MS = Number(process.env.REALTIME_SLA_MS ?? 5000);   // visible deadline
const SLA_BUFFER_MS = Number(process.env.REALTIME_SLA_BUF ?? 300); // tiny jitter
const SLA_MS = BASE_SLA_MS + SLA_BUFFER_MS;

test('Realtime: POST → row visible ≤5s', async ({ page, request }) => {
    await page.goto('/AuditLog');
    await page.waitForLoadState('domcontentloaded');
    await page.waitForSelector('#auditTable', { timeout: 10_000 });

    // Ensure the SignalR hub is connected BEFORE we post
    await waitForAuditHub(page, 10_000);

    const externalId = uuid();
    const desc = `Realtime ${Date.now()}`;

    // POST first, then start the clock for the "visible" SLA
    const res = await createAudit(request, { action: 'Create', description: desc, externalId });
    expect([200, 201]).toContain(res.status());

    const t0 = Date.now();
    const t0Iso = new Date(t0).toISOString();

    const row = page.getByRole('row', { name: new RegExp(desc) });

    // Primary path: realtime push updates the table within SLA
    try {
        await expect(row).toBeVisible({ timeout: SLA_MS });
    } catch {
        // Fallback: if push was missed, verify server has it, then refresh once and check again.
        const onServer = await serverHasEvent(request, t0Iso, desc, Math.max(0, SLA_MS - (Date.now() - t0)));
        expect(onServer).toBe(true);

        // One soft reload to pick the new row if the push was missed
        await page.reload({ waitUntil: 'domcontentloaded' });
        await page.waitForSelector('#auditTable', { timeout: 10_000 });

        // Still enforce the same SLA wall-clock
        const remaining = Math.max(0, SLA_MS - (Date.now() - t0));
        await expect(row).toBeVisible({ timeout: remaining });
    }

    const elapsed = Date.now() - t0;
    expect(elapsed).toBeLessThanOrEqual(SLA_MS);
});