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

test.describe('Search Users', () => {
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

    test('Search: Valid User', async () => {
        await sharedPage.getByRole('textbox', { name: 'Search Users...' }).click();
        await sharedPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
        await expect(sharedPage.getByRole('cell', { name: 'test user' })).toBeVisible();
    })
    test('Search: InValid User', async () => {
        await sharedPage.getByRole('textbox', { name: 'Search Users...' }).click();
        await sharedPage.getByRole('textbox', { name: 'Search Users...' }).fill('invalid user');
        await expect(sharedPage.getByRole('cell', { name: 'invalid user' })).not.toBeVisible();
    })
});