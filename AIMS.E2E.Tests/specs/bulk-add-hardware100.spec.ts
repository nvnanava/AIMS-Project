import { test, expect } from '@playwright/test';

test('bulk add 100 hardware assets with auto-generated tags', async ({ page }) => {
    // ---------- Step 1: Navigate to category ----------
    await page.goto('https://localhost:5119/');
    await page.addStyleTag({
        content: `.modal.fade, .fade { transition: none !important; animation: none !important; }`
    });
    await page.getByRole('link', { name: 'Desktops' }).click();

    // ---------- Step 2: Open Manage Asset -> Add Hardware ----------
    await page.getByRole('button', { name: 'Manage Asset' }).click();
    const addHardwareLink = page.locator('[data-bs-target="#addAssetModal"]');
    await addHardwareLink.waitFor({ state: 'visible', timeout: 5000 });
    await addHardwareLink.click();

    // ---------- Step 3: Fill out Phase 1 ----------
    await page.getByRole('spinbutton', { name: 'Number of Items:' }).fill('100');
    await page.getByLabel('Manufacturer').selectOption('Other');
    await page.getByLabel('Model').selectOption('Other');

    // Proceed to Phase 2
    await page.locator('#startPhase2btn').click();

    // ---------- Step 4: Generate all tags ----------
    const baseTag = 'MBC00000100';
    await page.getByRole('textbox', { name: 'Tag Number:' }).fill(baseTag);
    await page.getByRole('checkbox', { name: 'Generate All Tags' }).check();

    // Wait for preview list to populate
    const list = page.locator('#previewList li');
    await expect(list).toHaveCount(100, { timeout: 10000 });

    // ---------- Step 5: Edit each generated item ----------
    for (let i = 0; i < 100; i++) {
        const serial = `SN${String(i + 97).padStart(6, '0')}`;
        const tagIndex = (100 + i).toString().padStart(8, '0'); // continue from MBC00000100
        const tag = `MBC${tagIndex}`;

        // Find the correct list item
        const row = page.getByRole('listitem').filter({ hasText: `| ${tag}` });
        await row.waitFor({ state: 'visible', timeout: 5000 });

        // Click edit button
        await row.getByRole('button').click();

        // Wait for edit modal
        await page.locator('#editItemModal').waitFor({ state: 'visible', timeout: 5000 });

        // Fill serial number
        await page.locator('#editSerialNumber').fill(serial);


        // Save and wait for modal to close
        await Promise.all([
            page.waitForSelector('#editItemModal', { state: 'hidden', timeout: 10000 }),
            page.getByRole('button', { name: 'Save', exact: true }).click(),
        ]);

        // Confirm it updated in preview list
        await expect(page.getByRole('listitem').filter({ hasText: `${serial} | ${tag}` })).toBeVisible();
    }
    // click next button to proceed
    await page.getByLabel('Enter Item Details').getByRole('button', { name: 'Next' }).click();

    // ---------- Step 6: Submit all ----------
    const addAllBtn = page.getByRole('button', { name: 'Add All Assets' });
    await expect(addAllBtn).toBeVisible({ timeout: 10000 });
    await Promise.all([
        page.waitForResponse(r => r.url().includes('/api/hardware/add-bulk') && r.ok(), { timeout: 20000 }),
        addAllBtn.click(),
    ]);

    // ---------- Step 7: Verification (optional API check) ----------
    for (let i = 0; i < 100; i++) {
        const tagIndex = (12 + i).toString().padStart(7, '0');
        const tag = `MBC${tagIndex}`;
        const res = await page.request.get(`https://localhost:5119/api/assets/one?tag=${encodeURIComponent(tag)}`);
        expect(res.ok(), `DB lookup failed for tag ${tag}`).toBeTruthy();
    }

    console.log('Successfully added and verified 100 auto-generated hardware assets.');
});
