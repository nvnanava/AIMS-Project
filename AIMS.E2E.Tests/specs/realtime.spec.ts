import { test, expect } from '@playwright/test';
import { createAudit, uuid } from '../helpers/utils';

test('Realtime: POST → row visible ≤5s', async ({ page, request }) => {
    await page.goto('/AuditLog');
    await page.waitForLoadState('networkidle');

    const externalId = uuid();
    const desc = `Realtime ${Date.now()}`;

    const start = Date.now();
    const res = await createAudit(request, {
        action: 'Create',
        description: desc,
        externalId,
    });

    expect([200, 201]).toContain(res.status());

    const row = page.getByRole('row', { name: new RegExp(desc) });
    await expect(row).toBeVisible({ timeout: 5_000 });
    expect(Date.now() - start).toBeLessThanOrEqual(5000);
});