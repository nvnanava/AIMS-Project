import { test, expect } from '@playwright/test';
import { createAudit, uuid } from '../helpers/utils';

test('Resilience: offline → back online → missed events appear', async ({ page, request, context }) => {
    await page.goto('/AuditLog');
    await page.waitForLoadState('networkidle');

    await context.setOffline(true);
    const extA = uuid();
    const extB = uuid();

    let res = await createAudit(request, { action: 'Create', description: `Offline ${extA}`, externalId: extA });
    expect([200, 201]).toContain(res.status());

    res = await createAudit(request, { action: 'Create', description: `Offline ${extB}`, externalId: extB });
    expect([200, 201]).toContain(res.status());

    await page.waitForTimeout(5000);
    await context.setOffline(false);

    await expect(page.getByText(`Offline ${extA}`)).toBeVisible({ timeout: 10000 });
    await expect(page.getByText(`Offline ${extB}`)).toBeVisible({ timeout: 10000 });
});