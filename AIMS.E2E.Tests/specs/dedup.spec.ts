import { test, expect, Page, Locator } from '@playwright/test';
import { createAudit, uuid } from '../helpers/utils';

async function goToFirstPage(page: Page) {
    const prev = page.getByRole('button', { name: 'Previous page' });
    // Click "Prev" until it's disabled (or a small cap to be safe)
    for (let i = 0; i < 20; i++) {
        if (await prev.isDisabled()) break;
        await prev.click();
        await page.waitForLoadState('networkidle');
    }
}

async function findInPagedTable(page: Page, table: Locator, text: string): Promise<boolean> {
    await goToFirstPage(page);
    const next = page.getByRole('button', { name: 'Next page' });

    for (let i = 0; i < 20; i++) {
        // Check current page
        if (await table.filter({ hasText: text }).count()) return true;

        // If no more pages, bail
        if (await next.isDisabled()) return false;

        // Next page
        await next.click();
        await page.waitForLoadState('networkidle');
    }
    return false;
}

async function ensureNotInPagedTable(page: Page, table: Locator, text: string): Promise<boolean> {
    await goToFirstPage(page);
    const next = page.getByRole('button', { name: 'Next page' });

    for (let i = 0; i < 20; i++) {
        if (await table.filter({ hasText: text }).count()) return false;
        if (await next.isDisabled()) return true;
        await next.click();
        await page.waitForLoadState('networkidle');
    }
    return true;
}

test('Dedup: same externalId updates, no duplicate', async ({ page, request }) => {
    await page.goto('/AuditLog');
    await page.waitForLoadState('networkidle');

    const ext = uuid();
    const desc1 = `Dedup1 ${Date.now()}`;
    const desc2 = `Dedup2 ${Date.now()}`;

    // Concrete data table to avoid strict-mode violations
    const table = page.locator('#auditTable');

    // 1) Seed first row and confirm it appears (on any page)
    await createAudit(request, { action: 'Create', description: desc1, externalId: ext });

    await expect
        .poll(async () => await findInPagedTable(page, table, desc1), { timeout: 10_000 })
        .toBe(true);

    // 2) Upsert with same externalId
    await createAudit(request, { action: 'Update', description: desc2, externalId: ext });

    // Force a backfill so UI fetches latest state (if not live-updating)
    await page.reload();
    await page.waitForLoadState('networkidle');

    // 3) New description is present somewhere in the paged table
    await expect
        .poll(async () => await findInPagedTable(page, table, desc2), { timeout: 10_000 })
        .toBe(true);

    // 4) Old description should be gone from all pages
    await expect
        .poll(async () => await ensureNotInPagedTable(page, table, desc1), { timeout: 10_000 })
        .toBe(true);
});