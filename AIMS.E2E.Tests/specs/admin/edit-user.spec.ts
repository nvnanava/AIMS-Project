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

// ensure that seeding doesn't have a race condition
test.describe.configure({ mode: 'serial' });

test.describe('Edit User: Valid, Invalid', () => {
    test.beforeAll(async ({ browser }) => {
        sharedContext = await browser.newContext();
        sharedPage = await sharedContext.newPage();
   
        await sharedPage.goto('/Admin', { waitUntil: 'networkidle' });
   
        // Sanity check
        await expect(sharedPage.getByRole('heading', { name: /admin/i })).toBeVisible({ timeout: 10000 }).catch(() => { /* if no heading, ignore */ });
   
        // Since we add a test user on test start, we have already tested the success case.
        await adminBeforeAll(sharedPage);
   
    });
   
   
    test.afterAll(async () => {
        // clean out user
        await adminAfterAll(sharedPage, sharedContext);
    })
   
   
    test.beforeEach(async () => {
        // Rely on global-setup’s cookie: we’re already logged in as Admin principal
        await sharedPage.goto('/Admin', { waitUntil: 'networkidle' });
   
        // Sanity check: confirm we actually landed on the page
        await expect(sharedPage.getByRole('heading', { name: /admin/i })).toBeVisible({ timeout: 10000 }).catch(() => { /* if no heading, ignore */ });
    });


    test.describe('Sucessful Edits', () => {
        // since we pull user data from Entra ID, only Office, Status, and Separation Edits should work
        test('Change Office', async () => { 
            await sharedPage.getByRole('textbox', { name: 'Search Users...' }).click();
            await sharedPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
            await expect(sharedPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await sharedPage.getByRole('row', { name: 'test user' }).getByRole('button').click();
            await sharedPage.getByRole('textbox', { name: 'Office' }).click();
            await sharedPage.getByRole('textbox', { name: 'Office' }).fill('Test Office');
            await sharedPage.getByRole('button', { name: 'Save Changes' }).click();

            await sharedPage.getByRole('row', { name: 'test user' }).getByRole('button').click();
            const officeBox = sharedPage.getByRole('textbox', { name: 'Office' });
            const officeBoxValue = await officeBox.inputValue();
            await expect(officeBoxValue).toBe('Test Office');
        });
        test('Change Archive Status', async () => {
            await sharedPage.getByRole('textbox', { name: 'Search Users...' }).click();
            await sharedPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
            await expect(sharedPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await sharedPage.getByRole('row', { name: 'test user' }).getByRole('button').click();
            await sharedPage.getByRole('combobox', { name: 'Archive Status Active (Not' }).click();
            await sharedPage.getByRole('option', { name: 'Archived', exact: true }).click();
            await sharedPage.getByRole('button', { name: 'Save Changes' }).click();
            await sharedPage.locator('.slider').click();

            await expect(sharedPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await sharedPage.getByRole('row', { name: 'test user' }).getByRole('button').click();

            await expect(sharedPage.getByRole('combobox', { name: 'Archive Status Archived ▾' })).toBeVisible();
            await sharedPage.getByRole('combobox', { name: 'Archive Status Archived ▾' }).click();
            await sharedPage.getByRole('option', { name: 'Active (Not Archived)', exact: true }).click();
            await sharedPage.getByRole('button', { name: 'Save Changes' }).click();
         });
        test('Change Separation Date', async () => {
            await sharedPage.getByRole('textbox', { name: 'Search Users...' }).click();
            await sharedPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
            await expect(sharedPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await sharedPage.getByRole('row', { name: 'test user' }).getByRole('button').click();
            await sharedPage.getByRole('combobox', { name: 'Archive Status Active (Not' }).click();
            await sharedPage.getByRole('option', { name: 'Archived', exact: true }).click();
            await sharedPage.getByRole('button', { name: 'Save Changes' }).click();
            
            await sharedPage.locator('.slider').click();

            await expect(sharedPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await sharedPage.getByRole('row', { name: 'test user' }).getByRole('button').click();

            const separationBox = sharedPage.getByRole('textbox', { name: 'Separation Date' });
            const separationBoxContents = await separationBox.inputValue();

            const today = new Date();
            const formattedDate = new Intl.DateTimeFormat('en-US').format(today);

            await expect(separationBoxContents).toBe(formattedDate);

            await sharedPage.getByRole('combobox', { name: 'Archive Status Archived ▾' }).click();
            await sharedPage.getByRole('option', { name: 'Active (Not Archived)', exact: true }).click();
            await sharedPage.getByRole('button', { name: 'Save Changes' }).click();


         });
    })

    test.describe('Failed Edits', () => {
        test('Separation should not save without archival', async () => {
            await sharedPage.getByRole('textbox', { name: 'Search Users...' }).click();
            await sharedPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
            await expect(sharedPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await sharedPage.getByRole('row', { name: 'test user' }).getByRole('button').click();

            await sharedPage.getByRole('textbox', { name: 'Separation Date' }).fill('11/05/2025');
            await sharedPage.getByRole('button', { name: 'Save Changes' }).click();
            
            await expect(sharedPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await sharedPage.getByRole('row', { name: 'test user' }).getByRole('button').click();
            const separationBox = sharedPage.getByRole('textbox', { name: 'Separation Date' });
            const separationBoxContents = await separationBox.inputValue();
            await expect(separationBoxContents).toBe('');
        })
        test('Name should not update', async () => {
            await sharedPage.getByRole('textbox', { name: 'Search Users...' }).click();
            await sharedPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
            await expect(sharedPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await sharedPage.getByRole('row', { name: 'test user' }).getByRole('button').click();
            await sharedPage.getByRole('textbox', { name: 'Name' }).fill('Best User');
            await sharedPage.getByRole('button', { name: 'Save Changes' }).click();

            // re-open panel and validate user name
            await sharedPage.getByRole('row', { name: 'test user' }).getByRole('button').click();
            const nameBox = sharedPage.getByRole('textbox', { name: 'Name' });
            const nameBoxValue = await nameBox.inputValue();
            await expect(nameBoxValue).toBe('test user');
        })
    })
});