import { test, expect } from '@playwright/test';

test('bulk add 100 hardware assets with auto-generated tags', async ({ page }) => {
    const BASE_URL = 'http://localhost:5119';

    // ---------- Step 1: Navigate to category ----------
    await page.goto(BASE_URL + '/');
    await page.addStyleTag({
        content:
            `.modal.fade, .fade { transition: none !important; animation: none !important; }`,
    });
    await page.getByRole('link', { name: 'Desktops' }).click();

    // ---------- Step 2: Open Manage Asset -> Add Hardware ----------
    await page.locator('#manageAssetDropdown').click();
    const addHardwareLink = page.locator('[data-bs-target="#addAssetModal"]');
    await addHardwareLink.waitFor({ state: 'visible', timeout: 5000 });
    await addHardwareLink.click();

    // ---------- Step 3: Fill out Phase 1 ----------
    await page.getByRole('spinbutton', { name: 'Number of Items:' }).fill('100');
    await page.getByLabel('Manufacturer').fill('Other');
    await page.getByLabel('Model').fill('Other');
    await page.locator('#startPhase2btn').click();

    // ---------- Step 4: Generate all tags ----------
    const baseTag = 'MBC00000100';
    const tagInput = page.locator('#tagNumber');
    await tagInput.waitFor({ state: 'visible', timeout: 5000 });
    await tagInput.fill(baseTag);

    await page.getByRole('checkbox', { name: 'Generate All Tags' }).check();

    // Wait for preview list to populate
    const list = page.locator('#previewList li');
    await expect(list).toHaveCount(100, { timeout: 10000 });

    // ---------- Step 5: Edit each generated item ----------
    for (let i = 0; i < 100; i++) {
        const serial = `SN${String(i + 97).padStart(6, '0')}`;
        const tagIndex = (100 + i).toString().padStart(8, '0');
        const tag = `MBC${tagIndex}`;

        const row = page.getByRole('listitem').filter({ hasText: `| ${tag}` });
        await row.waitFor({ state: 'visible', timeout: 5000 });

        await row.getByRole('button').click();
        await page.locator('#editItemModal').waitFor({
            state: 'visible',
            timeout: 5000,
        });

        await page.locator('#editSerialNumber').fill(serial);

        await Promise.all([
            page.waitForSelector('#editItemModal', {
                state: 'hidden',
                timeout: 10000,
            }),
            page.getByRole('button', { name: 'Save', exact: true }).click(),
        ]);

        await expect(
            page.getByRole('listitem').filter({ hasText: `${serial} | ${tag}` })
        ).toBeVisible();
    }

    await page
        .getByLabel('Enter Item Details')
        .getByRole('button', { name: 'Next' })
        .click();

    const addAllBtn = page.getByRole('button', { name: 'Add All Assets' });
    await expect(addAllBtn).toBeVisible({ timeout: 10000 });

    await addAllBtn.click();

    await page.waitForTimeout(3000);
    await expect(page.locator('#addAssetModal')).toBeHidden();

    console.log(
        'Submitted bulk-add of 100 auto-generated hardware assets (UI flow verified).'
    );
});