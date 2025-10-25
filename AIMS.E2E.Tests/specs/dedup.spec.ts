import { test, expect } from '@playwright/test';
import { createAudit, uuid } from '../helpers/utils';

test('Dedup: same externalId updates, no duplicate', async ({ page, request }) => {
    await page.goto('/AuditLog');
    await page.waitForLoadState('networkidle');

    const ext = uuid();
    const desc1 = `Dedup1 ${Date.now()}`;
    const desc2 = `Dedup2 ${Date.now()}`;

    // 1) Seed first row
    await createAudit(request, { action: 'Create', description: desc1, externalId: ext });
    const row1 = page.getByRole('row', { name: new RegExp(desc1) });
    await expect(row1).toBeVisible({ timeout: 5_000 });

    // 2) Upsert with same externalId
    await createAudit(request, { action: 'Update', description: desc2, externalId: ext });
    const row2 = page.getByRole('row', { name: new RegExp(desc2) });
    await expect(row2).toBeVisible({ timeout: 5_000 });

    // 3) Old description should no longer be present
    await expect(page.getByText(desc1)).toHaveCount(0);
});