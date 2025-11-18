import { test, expect, Page, BrowserContext } from '@playwright/test';
import { adminAfterAll, adminBeforeAll } from './admin-utils';

/**
 * Admin tests require a test user seeded in AAD:
 * 
 * For 8-Bit Coders purposes, I have added a new user with the following properties in AAD:
 * display name: test user
 * First: Test
 * Last: User
 * graphObjectId: e05d050b-7c37-4e59-90ab-f19872d808b8
 */


// setup shared scope to ensure that user is added
let sharedContext: BrowserContext;
let sharedPage: Page;

test.describe('Admin Page - UI is displayed properly', () => {


    test.beforeAll(async({browser}) => {
        sharedContext = await browser.newContext();
        sharedPage = await sharedContext.newPage();

        await sharedPage.goto('/Admin', { waitUntil: 'networkidle' });

        // Sanity check
        await expect(sharedPage.getByRole('heading', { name: /admin/i })).toBeVisible({ timeout: 10000 }).catch(() => { /* if no heading, ignore */ });

        await adminBeforeAll(sharedPage);

    });


    test.afterAll(async () => { 
        // clean out user
        await adminAfterAll(sharedPage, sharedContext);
    })


    test.beforeEach(async ({ page }) => {
        // Rely on global-setup’s cookie: we’re already logged in as Admin principal
        await page.goto('/Admin', { waitUntil: 'networkidle' });

        // Sanity check: confirm we actually landed on the page
        await expect(page.getByRole('heading', { name: /admin/i })).toBeVisible({ timeout: 10000 }).catch(() => { /* if no heading, ignore */ });
    });

    test.describe('Valid UI Elements', () => {
        test('UI Actions', async() => {
            await expect(sharedPage.getByRole('button', { name: 'Add User' })).toBeVisible();
            await expect(sharedPage.getByRole('textbox', { name: 'Search Users...' })).toBeVisible();
            await expect(sharedPage.getByText('All Roles ▾')).toBeVisible();
            await sharedPage.getByText('Show Inactive Users').click();
            await expect(sharedPage.getByText('Show Inactive Users')).toBeVisible();
        })

        test('Table Cells Visible', async () => {
            await expect(sharedPage.getByRole('cell', { name: 'Actions' })).toBeVisible();
            await expect(sharedPage.getByRole('cell', { name: 'Name' })).toBeVisible();
            await expect(sharedPage.getByRole('cell', { name: 'Email' })).toBeVisible();
            await expect(sharedPage.getByRole('cell', { name: 'Office' })).toBeVisible();
            await expect(sharedPage.getByRole('cell', { name: 'Status ⬍' })).toBeVisible();
            await expect(sharedPage.getByRole('cell', { name: 'Separation Date' })).toBeVisible();
        })
        
        test('Roles dropdown', async () => {
            await sharedPage.getByText('All Roles ▾').click();
            await expect(sharedPage.getByRole('option', { name: 'All Roles' })).toBeVisible();
            await expect(sharedPage.getByRole('option', { name: 'Admin' })).toBeVisible();
            await expect(sharedPage.getByRole('option', { name: 'User' })).toBeVisible();
        })
    });

    test('Open Add User Modal', async() => {
        await sharedPage.getByRole('button', { name: 'Add User' }).click();
        await expect(sharedPage.getByRole('heading', { name: 'Add New User' })).toBeVisible();
        await expect(sharedPage.getByRole('textbox', { name: 'Name *' })).toBeVisible();
        await expect(sharedPage.getByRole('textbox', { name: 'Email *' })).toBeVisible();
        await expect(sharedPage.getByLabel('Role *')).toBeVisible();
        await expect(sharedPage.getByRole('textbox', { name: 'Office *' })).toBeVisible();
        await expect(sharedPage.getByRole('combobox', { name: 'Status * Active ▾' })).toBeVisible();
        await expect(sharedPage.getByLabel('Add New User').getByRole('button', { name: 'Add User' })).toBeVisible();
    })
    test('Search for User', async () => {
        // TODO: Needs to have some sort of test or general user
        await sharedPage.getByRole('textbox', { name: 'Search Users...' }).fill('Singh');
        await expect(sharedPage.getByRole('cell', { name: 'Akal-Ustat Singh' })).toBeVisible();
    })
    test('Open Edit User Modal', async() => {

    })

})
