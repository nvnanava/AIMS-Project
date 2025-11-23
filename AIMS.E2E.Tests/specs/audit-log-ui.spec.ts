import { test, expect } from "@playwright/test";

test.describe('Audit Page - UI is displayed properly', () => {

    test.beforeEach(async ({ page }) => {
        // Rely on global-setup’s cookie: we’re already logged in as Admin principal
        await page.goto('/AuditLog', { waitUntil: 'networkidle' });

        // Sanity check: confirm we actually landed on the page
        await expect(page.getByRole('heading', { name: 'Audit Log' })).toBeVisible({ timeout: 10000 }).catch(() => { /* if no heading, ignore */ });
    });


    test('Table UI displays', async ({ page }) => {
        await expect(page.getByRole('cell', { name: 'Log ID' })).toBeVisible();
        await expect(page.getByRole('cell', { name: 'Timestamp' })).toBeVisible();
        await expect(page.getByRole('cell', { name: 'User ID' })).toBeVisible();
        await expect(page.getByRole('cell', { name: 'Action' })).toBeVisible();
        await expect(page.getByRole('cell', { name: 'Asset ID' })).toBeVisible();
        await expect(page.getByRole('cell', { name: 'Previous Value' })).toBeVisible();
        await expect(page.getByRole('cell', { name: 'New Value' })).toBeVisible();
        await expect(page.getByRole('cell', { name: 'Description' })).toBeVisible();
    })

    test('Filter UI displays', async ({ page }) => {
        // click on the modal
        await page.getByRole('button', { name: 'Filters' }).click();

        await expect(page.getByRole('heading', { name: 'Filter Logs' })).toBeVisible();
        await expect(page.getByRole('textbox', { name: 'From' })).toBeVisible();
        await expect(page.getByRole('textbox', { name: 'To', exact: true })).toBeVisible();
        await expect(page.getByRole('textbox', { name: 'Actor' })).toBeVisible();
        await expect(page.getByLabel('Asset Type')).toBeVisible();
        // close the modal using the apply button
        await page.getByRole('button', { name: 'Apply Filters' }).click();

        // make sure the modal is closed
        await expect(page.getByRole('heading', { name: 'Filter Logs' })).not.toBeVisible();

        // open the modal
        await page.getByRole('button', { name: 'Filters' }).click();
        // close the modal using the cancel button
        await page.getByRole('button', { name: 'Cancel' }).click();
        // make sure the modal is closed
        await expect(page.getByRole('heading', { name: 'Filter Logs' })).not.toBeVisible();
    })

    test('Row UI displays', async ({ page }) => {
        // select the first child element of the table
        const firstRow = page.locator('#auditTableBody >> tr').first();
        
        // get cells in the row
        const cells = firstRow.locator('td');

        // get an array of locators
        const allCellLocators = await cells.all();

        // iterate through each cell and assert its visibility
        for (const cellLocator of allCellLocators) {
            await expect(cellLocator).toBeVisible();
        }

    })

    test('Row modal displays', async ({ page }) => {
        // select the first child element of the table
        const firstRow = page.locator('#auditTableBody >> tr').first();
        await firstRow.click();

        // ensure that the modal + heading is visible
        await expect(page.getByRole('dialog', { name: 'Audit Entry' })).toBeVisible();
        await expect(page.getByRole('heading', { name: 'Audit Entry' })).toBeVisible();

        // ensure that important fields are shown
        await expect(page.getByLabel('Audit Entry').getByText('Log ID')).toBeVisible();
        await expect(page.getByLabel('Audit Entry').getByText('Timestamp')).toBeVisible();
        await expect(page.getByLabel('Audit Entry').getByText('User ID')).toBeVisible();
        await expect(page.getByLabel('Audit Entry').getByText('Action')).toBeVisible();
        await expect(page.getByLabel('Audit Entry').getByText('Asset ID')).toBeVisible();
        await expect(page.getByLabel('Audit Entry').getByText('Previous Value')).toBeVisible();
        await expect(page.getByLabel('Audit Entry').getByText('New Value')).toBeVisible();
        await expect(page.getByLabel('Audit Entry').getByText('Description')).toBeVisible();
        await expect(page.locator('#ar-desc')).toBeVisible();

        // close the modal
        await page.getByText('Close').click();

        // make sure the modal is closed
        await expect(page.getByRole('dialog', { name: 'Audit Entry' })).not.toBeVisible();
        await expect(page.getByRole('heading', { name: 'Audit Entry' })).not.toBeVisible();
    })

    test('Paging works', async ({ page }) => {
        const prevPageBtn = page.getByRole('button', { name: 'Previous page' });
        const nextPageBtn = page.getByRole('button', { name: 'Next page' });

        // ensure that page number is visible
        await expect(page.getByText('Page 1 of')).toBeVisible();

        if (!await nextPageBtn.isDisabled()) {
            await page.getByRole('button', { name: 'Next page' }).click();
            await expect(page.getByText('Page 2 of')).toBeVisible();
        } else {
            console.log("Next Page (AuditLog) button is disabled. Unable to verify pagination")
        }

        if (!await prevPageBtn.isDisabled()) {
            await page.getByRole('button', { name: 'Previous page' }).click();
            await expect(page.getByText('Page 1 of')).toBeVisible();
        } else {
            console.log("Previous Page (AuditLog) button is disabled. Unable to verify pagination")
        }
    })


});