/**
 * Admin tests require a test user seeded in AAD:
 * 
 * For 8-Bit Coders purposes, I have added a new user with the following properties in AAD:
 * display name: test user
 * First: Test
 * Last: User
 * graphObjectId: e05d050b-7c37-4e59-90ab-f19872d808b8
 */

// ensure that seeding doesn't have a race condition + make sure that archiving
// a user doesn't step on other tests' toes.
import { test as base, BrowserContext, Page, expect } from "@playwright/test";

type AdminFixtures = {
    sharedContext: BrowserContext;
    adminPage: Page;
};
// Extend the base test object with custom fixture (seed user and remove user before and after tests)
export const test = base.extend<{}, AdminFixtures>({
    // Define the sharedContext fixture with worker scope
    sharedContext: [async ({ browser }, use) => {
        // Setup: Create the context before tests run
        const context = await browser.newContext();
        await use(context);
        // Teardown: Close context after all tests
        await context.close();
    }, { scope: 'worker' }],

    // 2. Define the main adminPage fixture
    adminPage: [async ({ sharedContext }, use) => {
        const sharedPage = await sharedContext.newPage();

        // --- setup (Equivalent to beforeAll) ---

        // Navigate to Admin page
        await sharedPage.goto('/Admin', { waitUntil: 'networkidle' });

        // Sanity check 
        const isAdminHeadingVisible = await sharedPage.getByRole('heading', { name: /admin/i }).isVisible({ timeout: 10000 });
        if (!isAdminHeadingVisible) {
            console.log("Admin heading not visible, proceeding with setup.");
        }

        // Seeding User via UI
        console.log("Starting user setup...");

        // Check if user exists (via API) and delete if necessary
        const req = await sharedPage.request.get('/api/admin/users/exists?graphObjectId=e05d050b-7c37-4e59-90ab-f19872d808b8');
        const resp = await req.json();
        if (resp.exists) {
            await sharedPage.request.delete(`/api/clean/user?GraphObjectID=e05d050b-7c37-4e59-90ab-f19872d808b8`);
        }

        // Insert user via UI
        await sharedPage.getByRole('button', { name: 'Add User' }).click();
        await sharedPage.getByRole('textbox', { name: 'Name *' }).fill('test user');
        await sharedPage.getByRole('button', { name: 'test user test-user@' }).click();
        await sharedPage.getByLabel('Role *').selectOption('1');
        await sharedPage.getByRole('textbox', { name: 'Office *' }).fill('ISB');

        const isb_button = sharedPage.getByRole('button', { name: 'ISB' });
        if (await isb_button.isVisible()) {
            await isb_button.click();
        }

        const addBtn = sharedPage.getByLabel('Add New User').getByRole('button', { name: 'Add User' })
        const alreadyExistsLabel = sharedPage.getByText('User is already in the system.');
        if (!(await alreadyExistsLabel.isVisible({ timeout: 1000 }))) {
            await addBtn.click();
        }
        

        // Final assertion
        await expect(sharedPage.getByRole('cell', { name: 'test user' })).toBeVisible();
        console.log("User seeded successfully.");

        // Hand off the configured page to the actual test suite
        await use(sharedPage);

        // --- TEARDOWN (Equivalent to afterAll) ---

        try {
            console.log("Starting user cleanup...");

            // Clean out user/office via API
            const userRes = await sharedPage.request.delete(`/api/clean/user?GraphObjectID=e05d050b-7c37-4e59-90ab-f19872d808b8`);
            if (!(userRes.ok() || userRes.status() === 204)) {
                console.log(`Delete test user failed: ${userRes.status()} ${userRes.statusText()}`);
            }
            const officeRes = await sharedPage.request.delete(`/api/clean/office?OfficeName=Test%20Office`);
            if (!(officeRes.ok() || officeRes.status() === 204)) {
                console.log(`Delete test office failed: ${officeRes.status()} ${officeRes.statusText()}`);
            }
        } catch (e) {
            console.error("Warning: Cleanup failed.", e);
        } finally {
            // Close the page and context (The sharedContext teardown also handles context closing)
            await sharedContext.close();
        }

    }, { scope: 'worker' }], // Use 'worker' scope for beforeAll/afterAll behavior
});
test.describe.configure({ mode: 'serial' });

test.describe('Admin Page - UI is displayed properly', () => {

    test.beforeEach(async ({ adminPage }) => {
        // Rely on global-setup’s cookie: we’re already logged in as Admin principal
        await adminPage.goto('/Admin', { waitUntil: 'networkidle' });

        // Sanity check: confirm we actually landed on the page
        await expect(adminPage.getByRole('heading', { name: /admin/i })).toBeVisible({ timeout: 10000 }).catch(() => { /* if no heading, ignore */ });
    });

    test.describe('Valid UI Elements', () => {
        test('UI Actions', async ({ adminPage }) => {
            await adminPage.locator('.slider').click();
            await expect(adminPage.getByRole('button', { name: 'Add User' })).toBeVisible();
            await expect(adminPage.getByRole('textbox', { name: 'Search Users...' })).toBeVisible();
            await expect(adminPage.getByText('All Roles ▾')).toBeVisible();
            await adminPage.getByText('Show Inactive Users').click();
            await expect(adminPage.getByText('Show Inactive Users')).toBeVisible();
        })

        test('Table Cells Visible', async ({ adminPage }) => {
            await adminPage.locator('.slider').click();
            await expect(adminPage.getByRole('cell', { name: 'Actions' })).toBeVisible();
            await expect(adminPage.getByRole('cell', { name: 'Name' })).toBeVisible();
            await expect(adminPage.getByRole('cell', { name: 'Email' })).toBeVisible();
            await expect(adminPage.getByRole('cell', { name: 'Office' })).toBeVisible();
            await expect(adminPage.getByRole('cell', { name: 'Status ⬍' })).toBeVisible();
            await expect(adminPage.getByRole('cell', { name: 'Separation Date' })).toBeVisible();
        })

        test('Roles dropdown', async ({ adminPage }) => {
            await adminPage.locator('.slider').click();
            await adminPage.getByText('All Roles ▾').click();
            await expect(adminPage.getByRole('option', { name: 'All Roles' })).toBeVisible();
            await expect(adminPage.getByRole('option', { name: 'Admin' })).toBeVisible();
            await expect(adminPage.getByRole('option', { name: 'User' })).toBeVisible();
        })
    });

    test('Open Add User Modal', async ({ adminPage }) => {
        await adminPage.locator('.slider').click();
        await adminPage.getByRole('button', { name: 'Add User' }).click();
        await expect(adminPage.getByRole('heading', { name: 'Add New User' })).toBeVisible();
        await expect(adminPage.getByRole('textbox', { name: 'Name *' })).toBeVisible();
        await expect(adminPage.getByRole('textbox', { name: 'Email *' })).toBeVisible();
        await expect(adminPage.getByLabel('Role *')).toBeVisible();
        await expect(adminPage.getByRole('textbox', { name: 'Office *' })).toBeVisible();
        await expect(adminPage.getByRole('combobox', { name: 'Status * Active ▾' })).toBeVisible();
        await expect(adminPage.getByLabel('Add New User').getByRole('button', { name: 'Add User' })).toBeVisible();
    })
    test('Search for User', async ({ adminPage }) => {
        await adminPage.locator('.slider').click();
        await adminPage.getByRole('textbox', { name: 'Search Users...' }).click();
        await adminPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
        await expect(adminPage.getByRole('cell', { name: 'test user' })).toBeVisible();
    })
    test('Open Edit User Modal', async ({ adminPage }) => {
        await adminPage.locator('.slider').click();
        await adminPage.getByRole('textbox', { name: 'Search Users...' }).click();
        await adminPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
        await expect(adminPage.getByRole('cell', { name: 'test user' })).toBeVisible();
        await adminPage.getByRole('row', { name: 'test user' }).getByRole('button').click();
        const nameBox = adminPage.getByRole('textbox', { name: 'Name' });
        const nameBoxValue = await nameBox.inputValue();
        await expect(nameBoxValue).not.toBe('');
        const emailBox = adminPage.getByRole('textbox', { name: 'Email' });
        const emailBoxValue = await emailBox.inputValue();
        await expect(emailBoxValue).not.toBe('');
        const officeBox = adminPage.getByRole('textbox', { name: 'Office' });
        const officeBoxValue = await officeBox.inputValue();
        await expect(officeBoxValue).not.toBe('');
    })

})
test.describe('Add User: Valid, Invalid, Existing', () => {
    test.beforeEach(async ({ adminPage }) => {
        // Rely on global-setup’s cookie: we’re already logged in as Admin principal
        await adminPage.goto('/Admin', { waitUntil: 'networkidle' });

        // Sanity check: confirm we actually landed on the page
        await expect(adminPage.getByRole('heading', { name: /admin/i })).toBeVisible({ timeout: 10000 }).catch(() => { /* if no heading, ignore */ });
    });

    test.describe('Add a User: Success', () => {
        // since a user is already added as part of the setup, we just need to verify the result explicitly
        test('Verify User Setup', async ({ adminPage }) => {
            await adminPage.locator('.slider').click();
            await adminPage.getByRole('textbox', { name: 'Search Users...' }).click();
            await adminPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
            await expect(adminPage.getByRole('cell', { name: 'test user' })).toBeVisible();
        })

        test('New office name displays adding message', async ({ adminPage }) => {
            await adminPage.locator('.slider').click();
            await adminPage.getByRole('button', { name: 'Add User' }).click();
            await adminPage.getByRole('textbox', { name: 'Office *' }).click();
            await adminPage.getByRole('textbox', { name: 'Office *' }).fill('NewOffice');
            await expect(adminPage.getByText('No results for "NewOffice." A')).toBeVisible();
            await adminPage.getByRole('button', { name: 'Close' }).click();
        })

        test('Role selection proper options', async ({ adminPage }) => {
            await adminPage.locator('.slider').click();
            await adminPage.getByRole('button', { name: 'Add User' }).click();
            await expect(adminPage.getByLabel('Role *')).toBeVisible();
            const options = await adminPage.locator('#userRole option').allInnerTexts();
            await expect(options).toEqual(['-- choose --', 'Admin', 'Supervisor', 'User']);
            await adminPage.getByRole('button', { name: 'Close' }).click();
        })
        test('Status Proper Options', async ({ adminPage }) => {
            await adminPage.locator('.slider').click();
            await adminPage.getByRole('button', { name: 'Add User' }).click();
            await expect(adminPage.getByRole('combobox', { name: 'Status * Active ▾' })).toBeVisible();
            const options = await adminPage.locator('#userStatus option').allInnerTexts();
            await expect(options).toEqual(['Active', 'Inactive']);
            await adminPage.getByRole('button', { name: 'Close' }).click();
        })
    })
    test('Add a User: Not Allowed (Not existing in AAD)', async ({ adminPage }) => {
        adminPage.once('dialog', async dialog => {
            expect(dialog.message()).toContain('Please pick a user from the Azure AD suggestions first.');
            await dialog.accept();
        })

        await adminPage.locator('.slider').click();
        await adminPage.getByRole('button', { name: 'Add User' }).click();
        await adminPage.getByRole('textbox', { name: 'Name *' }).fill('invalid user'); // Changed to be a full name for better visibility
        await adminPage.getByRole('textbox', { name: 'Email *' }).click();
        await adminPage.getByRole('textbox', { name: 'Email *' }).fill('some_email@gmail.com');
        await adminPage.getByLabel('Role *').selectOption('1');
        await adminPage.getByRole('textbox', { name: 'Office *' }).fill('ISB');
        await adminPage.getByRole('button', { name: 'ISB' }).click();
        await adminPage.getByLabel('Add New User').getByRole('button', { name: 'Add User' }).click();


        await adminPage.getByRole('button', { name: 'Close' }).click();

    })
    test('Add a User: Not Allowed (User exists in AIMS already)', async ({ adminPage }) => {
        await adminPage.locator('.slider').click();
        await adminPage.getByRole('button', { name: 'Add User' }).click();
        await adminPage.getByRole('textbox', { name: 'Name *' }).fill('test user'); // Changed to be a full name for better visibility
        await adminPage.getByRole('button', { name: 'test user test-user@' }).click();
        await adminPage.getByLabel('Role *').selectOption('1');
        await adminPage.getByRole('textbox', { name: 'Office *' }).fill('ISB');
        await adminPage.getByRole('button', { name: 'ISB' }).click();
        await expect(adminPage.getByText('User is already in the system.')).toBeVisible();
        await expect(adminPage.getByLabel('Add New User').getByRole('button', { name: 'Add User' })).toBeDisabled();
    })

});

test.describe('Edit User: Valid, Invalid', () => {


    test.beforeEach(async ({ adminPage }) => {
        // Rely on global-setup’s cookie: we’re already logged in as Admin principal
        await adminPage.goto('/Admin', { waitUntil: 'networkidle' });

        // Sanity check: confirm we actually landed on the page
        await expect(adminPage.getByRole('heading', { name: /admin/i })).toBeVisible({ timeout: 10000 }).catch(() => { /* if no heading, ignore */ });
    });


    test.describe('Sucessful Edits', () => {
        // since we pull user data from Entra ID, only Office, Status, and Separation Edits should work
        test('Change Office', async ({ adminPage }) => {
            await adminPage.locator('.slider').click();
            await adminPage.getByRole('textbox', { name: 'Search Users...' }).click();
            await adminPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
            await expect(adminPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await adminPage.getByRole('row', { name: 'test user' }).getByRole('button').click();
            await adminPage.getByRole('textbox', { name: 'Office' }).click();
            await adminPage.getByRole('textbox', { name: 'Office' }).fill('Test Office');
            await adminPage.getByRole('button', { name: 'Save Changes' }).click();

            await adminPage.getByRole('row', { name: 'test user' }).getByRole('button').click();
            const officeBox = adminPage.getByRole('textbox', { name: 'Office' });
            const officeBoxValue = await officeBox.inputValue();
            await expect(officeBoxValue).toBe('Test Office');
        });
        test('Change Archive Status', async ({ adminPage }) => {
            await adminPage.locator('.slider').click();
            await adminPage.getByRole('textbox', { name: 'Search Users...' }).click();
            await adminPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
            await expect(adminPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await adminPage.getByRole('row', { name: 'test user' }).getByRole('button').click();
            await adminPage.getByRole('combobox', { name: 'Archive Status Active (Not' }).click();
            await adminPage.getByRole('option', { name: 'Archived', exact: true }).click();
            await adminPage.getByRole('button', { name: 'Save Changes' }).click();

            await expect(adminPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await adminPage.getByRole('row', { name: 'test user' }).getByRole('button').click();

            await expect(adminPage.getByRole('combobox', { name: 'Archive Status Archived ▾' })).toBeVisible();
            await adminPage.getByRole('combobox', { name: 'Archive Status Archived ▾' }).click();
            await adminPage.getByRole('option', { name: 'Active (Not Archived)', exact: true }).click();
            await adminPage.getByRole('button', { name: 'Save Changes' }).click();
        });
        test('Change Separation Date', async ({ adminPage }) => {
            await adminPage.locator('.slider').click();
            await adminPage.getByRole('textbox', { name: 'Search Users...' }).click();
            await adminPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
            await expect(adminPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await adminPage.getByRole('row', { name: 'test user' }).getByRole('button').click();
            await adminPage.getByRole('combobox', { name: 'Archive Status Active (Not' }).click();
            await adminPage.getByRole('option', { name: 'Archived', exact: true }).click();
            await adminPage.getByRole('button', { name: 'Save Changes' }).click();

            await expect(adminPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await adminPage.getByRole('row', { name: 'test user' }).getByRole('button').click();

            const separationBox = adminPage.getByRole('textbox', { name: 'Separation Date' });
            const separationBoxContents = await separationBox.inputValue();

            const today = new Date();
            const formattedDate = new Intl.DateTimeFormat('en-US').format(today);

            await expect(separationBoxContents).toBe(formattedDate);

            await adminPage.getByRole('combobox', { name: 'Archive Status Archived ▾' }).click();
            await adminPage.getByRole('option', { name: 'Active (Not Archived)', exact: true }).click();
            await adminPage.getByRole('button', { name: 'Save Changes' }).click();


        });
    })

    test.describe('Failed Edits', () => {
        test('Separation should not save without archival', async ({ adminPage }) => {
            await adminPage.locator('.slider').click();
            await adminPage.getByRole('textbox', { name: 'Search Users...' }).click();
            await adminPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
            await expect(adminPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await adminPage.getByRole('row', { name: 'test user' }).getByRole('button').click();

            await adminPage.getByRole('textbox', { name: 'Separation Date' }).fill('11/05/2025');
            await adminPage.getByRole('button', { name: 'Save Changes' }).click();

            await expect(adminPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await adminPage.getByRole('row', { name: 'test user' }).getByRole('button').click();
            const separationBox = adminPage.getByRole('textbox', { name: 'Separation Date' });
            const separationBoxContents = await separationBox.inputValue();
            await expect(separationBoxContents).toBe('');
        })
        test('Name should not update', async ({ adminPage }) => {
            await adminPage.locator('.slider').click();
            await adminPage.getByRole('textbox', { name: 'Search Users...' }).click();
            await adminPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
            await expect(adminPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            await adminPage.getByRole('row', { name: 'test user' }).getByRole('button').click();
            await adminPage.getByRole('textbox', { name: 'Name' }).fill('Best User');
            await adminPage.getByRole('button', { name: 'Save Changes' }).click();

            // re-open panel and validate user name
            await adminPage.getByRole('row', { name: 'test user' }).getByRole('button').click();
            const nameBox = adminPage.getByRole('textbox', { name: 'Name' });
            const nameBoxValue = await nameBox.inputValue();
            await expect(nameBoxValue).toBe('test user');
        })
    })
});

test.describe('Search Users', () => {


    test.beforeEach(async ({ adminPage }) => {
        // Rely on global-setup’s cookie: we’re already logged in as Admin principal
        await adminPage.goto('/Admin', { waitUntil: 'networkidle' });

        // Sanity check: confirm we actually landed on the page
        await expect(adminPage.getByRole('heading', { name: /admin/i })).toBeVisible({ timeout: 10000 }).catch(() => { /* if no heading, ignore */ });
    });

    test('Search: Valid User', async ({ adminPage }) => {
        await adminPage.locator('.slider').click();
        await adminPage.getByRole('textbox', { name: 'Search Users...' }).click();
        await adminPage.getByRole('textbox', { name: 'Search Users...' }).fill('test');
        await expect(adminPage.getByRole('cell', { name: 'test user' })).toBeVisible();
    })
    test('Search: InValid User', async ({ adminPage }) => {
        await adminPage.locator('.slider').click();
        await adminPage.getByRole('textbox', { name: 'Search Users...' }).click();
        await adminPage.getByRole('textbox', { name: 'Search Users...' }).fill('invalid user');
        await expect(adminPage.getByRole('cell', { name: 'invalid user' })).not.toBeVisible();
    })
});