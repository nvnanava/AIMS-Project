import { test, expect } from '@playwright/test';

test.describe('Admin Page - UI is displayed properly', () => { 

    // use the navbar to navigate to the admin page before each test
    test.beforeEach(async ({ page }) => {
        // go to home page
        await page.goto("localhost:5119/", { waitUntil: 'networkidle' });
        // find the admin link and click it
        await page.getByRole('link', { name: 'Admin' }).click();
    });

    test('Valid UI Elements', async ({ page }) => {
        await expect(page.getByRole('button', { name: 'Add User' })).toBeVisible();
        await expect(page.getByRole('textbox', { name: 'Search Users...' })).toBeVisible();
        await expect(page.getByText('All Roles ▾')).toBeVisible();
        await page.getByText('Show Inactive Users').click();
        await expect(page.getByText('Show Inactive Users')).toBeVisible();
        await expect(page.getByRole('cell', { name: 'Actions' })).toBeVisible();
        await expect(page.getByRole('cell', { name: 'Name' })).toBeVisible();
        await expect(page.getByRole('cell', { name: 'Email' })).toBeVisible();
        await expect(page.getByRole('cell', { name: 'Office' })).toBeVisible();
        await expect(page.getByRole('cell', { name: 'Status ⬍' })).toBeVisible();
        await expect(page.getByRole('cell', { name: 'Separation Date' })).toBeVisible();
    });

})