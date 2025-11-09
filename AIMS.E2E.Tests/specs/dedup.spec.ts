import { test, expect, Page, Locator } from '@playwright/test';
import { createAudit, uuid } from '../helpers/utils';
import { waitForAuditHub, waitForApiSeesExternalId } from '../helpers/realtime';

const FIND_TIMEOUT_MS = Number(process.env.DEDUP_FIND_TIMEOUT_MS ?? 15_000);

// ---- Helpers specific to this spec ------------------------------------

async function goToFirstPage(page: Page) {
    const prev = page.getByRole('button', { name: 'Previous page' });
    for (let i = 0; i < 20; i++) {
        if (await prev.isDisabled().catch(() => true)) break;
        await prev.click().catch(() => { });
        await page.waitForTimeout(100);
    }
}

async function findInPagedTable(page: Page, table: Locator, text: string): Promise<boolean> {
    await goToFirstPage(page);
    const next = page.getByRole('button', { name: 'Next page' });
    for (let i = 0; i < 20; i++) {
        if ((await table.filter({ hasText: text }).count()) > 0) return true;
        if (await next.isDisabled().catch(() => true)) return false;
        await next.click().catch(() => { });
        await page.waitForTimeout(100);
    }
    return false;
}

async function ensureNotInPagedTable(page: Page, table: Locator, text: string): Promise<boolean> {
    await goToFirstPage(page);
    const next = page.getByRole('button', { name: 'Next page' });
    for (let i = 0; i < 20; i++) {
        if ((await table.filter({ hasText: text }).count()) > 0) return false;
        if (await next.isDisabled().catch(() => true)) return true;
        await next.click().catch(() => { });
        await page.waitForTimeout(100);
    }
    return true;
}

// ---- Test -----------------------------------------------------

test('Dedup: same externalId updates, no duplicate', async ({ page, request, baseURL }) => {
    await page.goto('/AuditLog');
    await page.waitForLoadState('domcontentloaded');
    await page.waitForSelector('#auditTable', { timeout: 10_000 });

    // Make sure realtime is ready (best-effort)
    await waitForAuditHub(page);

    const ext = uuid();
    const desc1 = `Dedup1 ${Date.now()}`;
    const desc2 = `Dedup2 ${Date.now()}`;

    const table = page.locator('#auditTable');

    // 1) Seed first row and wait for server to acknowledge it, then UI find
    await createAudit(request, { action: 'Create', description: desc1, externalId: ext });
    expect(await waitForApiSeesExternalId(baseURL!, ext)).toBe(true);

    await expect
        .poll(async () => await findInPagedTable(page, table, desc1), { timeout: FIND_TIMEOUT_MS })
        .toBe(true);

    // 2) Upsert with same externalId to new description; wait for API to reflect update
    await createAudit(request, { action: 'Update', description: desc2, externalId: ext });
    expect(await waitForApiSeesExternalId(baseURL!, ext, desc2)).toBe(true);

    // Force a refresh so UI fetches latest state (if not live-updating this particular field)
    await page.reload();
    await page.waitForLoadState('domcontentloaded');
    await page.waitForSelector('#auditTable', { timeout: 10_000 });

    // 3) New description is present somewhere in the paged table
    await expect
        .poll(async () => await findInPagedTable(page, table, desc2), { timeout: FIND_TIMEOUT_MS })
        .toBe(true);

    // 4) Old description should be gone from all pages
    await expect
        .poll(async () => await ensureNotInPagedTable(page, table, desc1), { timeout: FIND_TIMEOUT_MS })
        .toBe(true);
});