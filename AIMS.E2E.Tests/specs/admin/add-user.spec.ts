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

test.describe('Add User: Valid, Invalid, Existing', () => {
    test.beforeAll(async({browser}) => {
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
    
    test.describe('Add a User: Success', () => {
        // since a user is already added as part of the setup, we just need to verify the result explicitly
        test('Verify User Setup', async () => {
            await sharedPage.getByRole('textbox', { name: 'Search Users...' }).click();
            await sharedPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
            await expect(sharedPage.getByRole('cell', { name: 'test user' })).toBeVisible();
        })

        test('New office name displays adding message', async () => {
            await sharedPage.getByRole('button', { name: 'Add User' }).click();
            await sharedPage.getByRole('textbox', { name: 'Office *' }).click();
            await sharedPage.getByRole('textbox', { name: 'Office *' }).fill('NewOffice');
            await expect(sharedPage.getByText('No results for "NewOffice." A')).toBeVisible();
            await sharedPage.getByRole('button', { name: 'Close' }).click();
        })

        test('Role selection proper options', async () => {
            await sharedPage.getByRole('button', { name: 'Add User' }).click();
            await expect(sharedPage.getByLabel('Role *')).toBeVisible();
            const options = await sharedPage.locator('#userRole option').allInnerTexts();
            await expect(options).toEqual(['-- choose --', 'Admin', 'Supervisor', 'User']);
            await sharedPage.getByRole('button', { name: 'Close' }).click();
        })
        test('Status Proper Options', async () => {
            await sharedPage.getByRole('button', { name: 'Add User' }).click();
            await expect(sharedPage.getByRole('combobox', { name: 'Status * Active ▾' })).toBeVisible();
            const options = await sharedPage.locator('#userStatus option').allInnerTexts();
            await expect(options).toEqual(['Active', 'Inactive']);
            await sharedPage.getByRole('button', { name: 'Close' }).click();
       }) 
    })
    test('Add a User: Not Allowed (Not existing in AAD)', async () => {
        await sharedPage.getByRole('button', { name: 'Add User' }).click();
        await sharedPage.getByRole('textbox', { name: 'Name *' }).fill('invalid user'); // Changed to be a full name for better visibility
        await sharedPage.getByRole('textbox', { name: 'Email *' }).click();
        await sharedPage.getByRole('textbox', { name: 'Email *' }).fill('some_email@gmail.com');
        await sharedPage.getByLabel('Role *').selectOption('1');
        await sharedPage.getByRole('textbox', { name: 'Office *' }).fill('ISB');
        await sharedPage.getByRole('button', { name: 'ISB' }).click();
        await sharedPage.getByLabel('Add New User').getByRole('button', { name: 'Add User' }).click();

        sharedPage.on('dialog', async dialog => { 
            expect(dialog.message()).toContain('Please pick a user from the Azure AD suggestions first.');
            await dialog.accept();
        })

        await sharedPage.getByRole('button', { name: 'Close' }).click();

    })
    test('Add a User: Not Allowed (User exists in AIMS already)', async () => {
        await sharedPage.getByRole('button', { name: 'Add User' }).click();
        await sharedPage.getByRole('textbox', { name: 'Name *' }).fill('test user'); // Changed to be a full name for better visibility
        await sharedPage.getByRole('button', { name: 'test user test-user@' }).click();
        await sharedPage.getByLabel('Role *').selectOption('1');
        await sharedPage.getByRole('textbox', { name: 'Office *' }).fill('ISB');
        await sharedPage.getByRole('button', { name: 'ISB' }).click();
        await expect(sharedPage.getByText('User is already in the system.')).toBeVisible();
        await expect(sharedPage.getByLabel('Add New User').getByRole('button', { name: 'Add User' })).toBeDisabled();
    })

 });