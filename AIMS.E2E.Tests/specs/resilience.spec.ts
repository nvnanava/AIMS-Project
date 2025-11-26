import { test, expect, Page, Locator } from '@playwright/test';
import { createAudit, uuid } from '../helpers/utils';

async function goToFirstPage(page: Page) {
    const prev = page.getByRole('button', { name: 'Previous page' });
    for (let i = 0; i < 20; i++) {
        if (await prev.isDisabled()) break;
        await prev.click();
        await page.waitForLoadState('networkidle');
    }
}

// returns true if `text` is found on any page of #auditTable
async function findInPagedTable(page: Page, table: Locator, text: string): Promise<boolean> {
    await goToFirstPage(page);
    const next = page.getByRole('button', { name: 'Next page' });

    for (let i = 0; i < 20; i++) {
        if (await table.filter({ hasText: text }).count()) return true;
        if (await next.isDisabled()) return false;
        await next.click();
        await page.waitForLoadState('networkidle');
    }
    return false;
}

test('Resilience: offline → back online → missed events appear', async ({ page, request, context }) => {
    await page.goto('/AuditLog');
    await page.waitForLoadState('domcontentloaded');
    await page.waitForSelector('#auditTable', {
        timeout: 10_000,
        state: 'attached',
    });


    await context.setOffline(true);

    const extA = uuid();
    const extB = uuid();

    let res = await createAudit(request, { action: 'Create', description: `Offline ${extA}`, externalId: extA });
    expect([200, 201]).toContain(res.status());
    res = await createAudit(request, { action: 'Create', description: `Offline ${extB}`, externalId: extB });
    expect([200, 201]).toContain(res.status());

    // tiny settle for persistence
    await page.waitForTimeout(300);

    await context.setOffline(false);
    await page.reload();
    await page.waitForLoadState('domcontentloaded');
    await page.waitForSelector('#auditTable', {
        timeout: 10_000,
        state: 'attached',
    });

    const table = page.locator('#auditTable');

    // Search across all pages with polling (handles pagination + async render)
    await expect
        .poll(async () => await findInPagedTable(page, table, `Offline ${extA}`), { timeout: 10_000 })
        .toBe(true);
    await expect
        .poll(async () => await findInPagedTable(page, table, `Offline ${extB}`), { timeout: 10_000 })
        .toBe(true);
});