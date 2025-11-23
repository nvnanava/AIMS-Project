import { test as base, BrowserContext, Page, expect } from "@playwright/test";

type ReportsFixtures = {
    sharedContext: BrowserContext;
    reportsPage: Page;
};
// use a custom fixture to ensure that teardown (deleting reports is handled correctly)
const test = base.extend<{}, ReportsFixtures>({
    // Define the sharedContext fixture with worker scope
    sharedContext: [async ({ browser }, use) => {
        // Setup: Create the context before tests run
        const context = await browser.newContext();
        await use(context);
        // Teardown: Close context after all tests
        await context.close();
    }, { scope: 'worker' }],
    reportsPage: [async ({ sharedContext }, use) => {
        const sharedPage = await sharedContext.newPage();

        // --- setup (Equivalent to beforeAll) ---

        // Navigate to Admin page
        await sharedPage.goto('/Reports', { waitUntil: 'networkidle' });

        // Sanity check 
        await sharedPage.getByRole('heading', { name: "Reports" }).isVisible({ timeout: 10000 });

        // seed a test office
        const officeSeedRes = await sharedPage.request.post('api/debug/seed-offices');
        if (!(officeSeedRes.ok() || officeSeedRes.status() === 204)) {
            const errorBody = await officeSeedRes.text();
            console.log(`Seed test office failed: ${officeSeedRes.status()} ${officeSeedRes.statusText()}`);
            console.log(errorBody);
        }

        // Hand off the configured page to the actual test suite
        await use(sharedPage);

        // --- TEARDOWN (Equivalent to afterAll) ---

        try {
            console.log("Starting report cleanup...");

            // Clean out user/office via API
            const reportsRes = await sharedPage.request.delete(`/api/clean/reports`);
            if (!(reportsRes.ok() || reportsRes.status() === 204)) {
                console.log(`Delete test reports failed: ${reportsRes.status()} ${reportsRes.statusText()}`);
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

    }, { scope: 'worker' }]
});

// ensure that seeding/deleting doesn't step on other tests'/workers' toes.
test.describe.configure({ mode: 'serial' });

test.describe('Reports Page - UI is displayed properly', () => {

    test('Table displays correctly', async ({ reportsPage }) => {
        await expect(reportsPage.getByText('Report Name', { exact: true })).toBeVisible();
        await expect(reportsPage.getByRole('columnheader', { name: 'Type' })).toBeVisible();
        await expect(reportsPage.getByRole('columnheader', { name: 'Description' })).toBeVisible();
        await expect(reportsPage.getByRole('columnheader', { name: 'Office' })).toBeVisible();
        await expect(reportsPage.getByText('Date Created')).toBeVisible();
        await expect(reportsPage.getByText('Created By')).toBeVisible();
    })

    test('New assignment report modal displays', async ({ reportsPage }) => {
        await reportsPage.getByRole('button', { name: 'New Report' }).click();
        await reportsPage.getByRole('button', { name: 'Assignment Report' }).click();
        await expect(reportsPage.getByRole('heading', { name: 'Generate an Assignment Report' })).toBeVisible();
        await expect(reportsPage.getByLabel('Generate an Assignment Report').getByText('Report Name *')).toBeVisible();
        await expect(reportsPage.getByLabel('Generate an Assignment Report').getByText('Start Date *')).toBeVisible();
        await expect(reportsPage.getByLabel('Generate an Assignment Report').getByText('End Date (optional)')).toBeVisible();
        await expect(reportsPage.getByLabel('Generate an Assignment Report').getByText('Description (optional)')).toBeVisible();
        await expect(reportsPage.getByRole('button', { name: 'Generate Report' })).toBeVisible();
        await expect(reportsPage.getByRole('button', { name: 'Cancel' })).toBeVisible();

        await reportsPage.getByRole('button', { name: 'Cancel' }).click();
        await expect(reportsPage.getByRole('heading', { name: 'Generate an Assignment Report' })).not.toBeVisible();
    })
    test('New office report modal displays', async ({ reportsPage }) => {
        // open modal
        await reportsPage.getByRole('button', { name: 'New Report' }).click();
        await reportsPage.getByRole('button', { name: 'Office Report' }).click();

        // ensure visibility
        await expect(reportsPage.getByRole('heading', { name: 'Generate an Office Report' })).toBeVisible();

        // ensure correct fields
        await expect(reportsPage.getByLabel('Generate an Office Report').getByText('Report Name *')).toBeVisible();
        await expect(reportsPage.getByText('Office Name *')).toBeVisible();
        await expect(reportsPage.getByLabel('Generate an Office Report').getByText('Start Date *')).toBeVisible();
        await expect(reportsPage.getByLabel('Generate an Office Report').getByText('End Date (optional)')).toBeVisible();
        await expect(reportsPage.getByLabel('Generate an Office Report').getByText('Description (optional)')).toBeVisible();
        await expect(reportsPage.getByRole('button', { name: 'Generate Report' })).toBeVisible();
        await expect(reportsPage.getByRole('button', { name: 'Cancel' })).toBeVisible();

        // close modal
        await reportsPage.getByRole('button', { name: 'Cancel' }).click();

        // ensure closure
        await expect(reportsPage.getByRole('heading', { name: 'Generate an Office Report' })).not.toBeVisible();
    })

    test('New custom report modal displays', async ({ reportsPage }) => {
        // open
        await reportsPage.getByRole('button', { name: 'New Report' }).click();
        await reportsPage.getByRole('button', { name: 'Custom Report' }).click();

        // visibility
        await expect(reportsPage.getByRole('heading', { name: 'Generate a Custom Report' })).toBeVisible();
        await expect(reportsPage.getByLabel('Generate a Custom Report').getByText('Report Name *')).toBeVisible();
        await reportsPage.getByLabel('Generate a Custom Report').getByText('Start Date *').click();
        await expect(reportsPage.getByLabel('Generate a Custom Report').getByText('Start Date *')).toBeVisible();
        await expect(reportsPage.getByLabel('Generate a Custom Report').getByText('End Date (optional)')).toBeVisible();
        await expect(reportsPage.getByLabel('Generate a Custom Report').getByText('Description (optional)')).toBeVisible();
        await expect(reportsPage.getByText('See Hardware')).toBeVisible();
        await expect(reportsPage.getByText('See Software')).toBeVisible();
        await expect(reportsPage.getByText('See Users')).toBeVisible();
        await expect(reportsPage.getByText('See Office')).toBeVisible();
        await expect(reportsPage.getByText('See when software and/or')).toBeVisible();
        await expect(reportsPage.getByText('See what requires maintenance')).toBeVisible();
        await expect(reportsPage.getByRole('button', { name: 'Generate Report' })).toBeVisible();
        await expect(reportsPage.getByRole('button', { name: 'Cancel' })).toBeVisible();

        // closure
        await reportsPage.getByRole('button', { name: 'Cancel' }).click();
        await expect(reportsPage.getByRole('heading', { name: 'Generate a Custom Report' })).not.toBeVisible();
    })
});

test.describe('Create Assignment Report', () => {
    test.describe('Success Case', () => {
        test('Name and start only', async ({ reportsPage }) => {
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Assignment Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).press('ControlOrMeta+a');
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-assignment-1');
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).click();
            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();
            await reportsPage.getByText('e2e-test-assignment-1').click();
            await reportsPage.getByRole('button', { name: 'Close' }).click();
        })
        test('Name, Start, and End', async ({ reportsPage }) => {
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Assignment Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-assignment-2');
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'End Date (optional)' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'End Date (optional)' }).press('ArrowUp');
            await reportsPage.getByRole('textbox', { name: 'End Date (optional)' }).fill('2025-11-23');
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).click();
            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();
            await reportsPage.getByText('e2e-test-assignment-2').click();
            await reportsPage.getByRole('button', { name: 'Close' }).click();
        })
        test('Name, Start, End, and Description', async ({ reportsPage }) => {
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Assignment Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('reportsPage');
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).press('ControlOrMeta+z');
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-assignment-3');
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'End Date (optional)' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).click();
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).fill('e2e-test');
            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();
            await expect(reportsPage.getByText('e2e-test-assignment-3')).toBeVisible();
            await reportsPage.getByRole('cell', { name: 'e2e-test-assignment-3' }).click();
            await reportsPage.getByRole('button', { name: 'Close' }).click();
        })
    })

    test.describe('Fail Case', () => {
        test('No name', async ({ reportsPage }) => {
            reportsPage.once('dialog', async dialog => {
                await expect(dialog.message()).toContain('Please enter a report name.');
                await dialog.accept();
            });
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Assignment Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');


            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();

            await reportsPage.waitForTimeout(500);

            await reportsPage.getByRole('button', { name: 'Cancel' }).click();
        })
        test('No start date', async ({ reportsPage }) => {
            reportsPage.once('dialog', async dialog => {
                await expect(dialog.message()).toContain('Please select a start date.');
                await dialog.accept();
            });
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Assignment Report' }).click();

            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-assignment-4');


            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();

            await reportsPage.waitForTimeout(500);

            await reportsPage.getByRole('button', { name: 'Cancel' }).click();

        })
    })
})
test.describe('Create Office Report', () => {
    test.describe('Success Case', () => {
        test('Name, Office, Start', async ({ reportsPage }) => {
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Office Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('reportsPage');
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-office-1');
            await reportsPage.getByLabel('Office Name *').selectOption('Test Office');
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');
            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();
            await expect(reportsPage.getByText('e2e-test-office-1')).toBeVisible();
            await reportsPage.getByRole('cell', { name: 'e2e-test-office-1' }).click();
            await reportsPage.getByRole('button', { name: 'Close' }).click();
        })
        test('Name, Office, Start, and End', async ({ reportsPage }) => {
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Office Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('reportsPage');
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-office-2');
            await reportsPage.getByLabel('Office Name *').selectOption('Test Office');
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'End Date (optional)' }).fill('2025-11-22');
            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();
            await expect(reportsPage.getByText('e2e-test-office-2')).toBeVisible();
            await reportsPage.getByRole('cell', { name: 'e2e-test-office-2' }).click();
            await reportsPage.getByRole('button', { name: 'Close' }).click();
        })
        test('Name, Office, Start, End, and Description', async ({ reportsPage }) => {
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Office Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('reportsPage');
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-office-3');
            await reportsPage.getByLabel('Office Name *').selectOption('Test Office');
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'End Date (optional)' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).click();
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).fill('e2e-test');
            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();
            await expect(reportsPage.getByText('e2e-test-office-3')).toBeVisible();
            await reportsPage.getByRole('cell', { name: 'e2e-test-office-3' }).click();
            await reportsPage.getByRole('button', { name: 'Close' }).click();
        })
    })
    test.describe('Fail Case', () => {
        test('No Name', async ({ reportsPage }) => {
            reportsPage.once('dialog', async dialog => {
                await expect(dialog.message()).toContain('Please enter a report name.');
                await dialog.accept();
            });
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Office Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');
            await reportsPage.getByLabel('Office Name *').selectOption('Test Office');

            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();

            await reportsPage.waitForTimeout(500);

            await reportsPage.getByRole('button', { name: 'Cancel' }).click();
        })
        test('No Start', async ({ reportsPage }) => {
            reportsPage.once('dialog', async dialog => {
                await expect(dialog.message()).toContain('Please select a start date.');
                await dialog.accept();
            });
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Office Report' }).click();

            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-assignment-4');
            await reportsPage.getByLabel('Office Name *').selectOption('Test Office');

            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();

            await reportsPage.waitForTimeout(500);

            await reportsPage.getByRole('button', { name: 'Cancel' }).click();
        })
        test('No Office', async ({ reportsPage }) => {
            reportsPage.once('dialog', async dialog => {
                await expect(dialog.message()).toContain('Please select an office number.');
                await dialog.accept();
            });
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Office Report' }).click();

            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-assignment-4');


            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();

            await reportsPage.waitForTimeout(500);

            await reportsPage.getByRole('button', { name: 'Cancel' }).click();
        })
    })
})
test.describe('Create Custom Report', () => {
    test.describe('Success Case', () => {
        test('Name, Start, Default Options', async ({ reportsPage }) => { 
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Custom Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('reportsPage');
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-custom-1');
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');
            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();
            await expect(reportsPage.getByText('e2e-test-custom-1')).toBeVisible();
            await reportsPage.getByRole('cell', { name: 'e2e-test-custom-1' }).click();
            await reportsPage.getByRole('button', { name: 'Close' }).click();
        })
        test('Name, Start, End, Default Options', async ({ reportsPage }) => {
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Custom Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('reportsPage');
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-custom-2');
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'End Date (optional)' }).fill('2025-11-22');
            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();
            await expect(reportsPage.getByText('e2e-test-custom-2')).toBeVisible();
            await reportsPage.getByRole('cell', { name: 'e2e-test-custom-2' }).click();
            await reportsPage.getByRole('button', { name: 'Close' }).click();
         })
        test('Name, Start, End, Description, Default Options', async ({ reportsPage }) => {
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Custom Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('reportsPage');
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-custom-3');
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'End Date (optional)' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).click();
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).fill('e2e-test');
            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();
            await expect(reportsPage.getByText('e2e-test-custom-3')).toBeVisible();
            await reportsPage.getByRole('cell', { name: 'e2e-test-custom-3' }).click();
            await reportsPage.getByRole('button', { name: 'Close' }).click();
         })
        test('Name, Start, End, Description, Hardware Only', async ({ reportsPage }) => { 
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Custom Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('reportsPage');
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-custom-4');
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'End Date (optional)' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).click();
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).fill('e2e-test');
            await reportsPage.getByRole('checkbox', { name: 'See Software' }).uncheck();
            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();
            await expect(reportsPage.getByText('e2e-test-custom-4')).toBeVisible();
            await reportsPage.getByRole('cell', { name: 'e2e-test-custom-4' }).click();
            await reportsPage.getByRole('button', { name: 'Close' }).click();
        })
        test('Name, Start, End, Description, Software Only', async ({ reportsPage }) => { 
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Custom Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('reportsPage');
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-custom-5');
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'End Date (optional)' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).click();
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).fill('e2e-test');
            await reportsPage.getByRole('checkbox', { name: 'See Hardware' }).uncheck();
            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();
            await expect(reportsPage.getByText('e2e-test-custom-5')).toBeVisible();
            await reportsPage.getByRole('cell', { name: 'e2e-test-custom-5' }).click();
            await reportsPage.getByRole('button', { name: 'Close' }).click();
        })
        test('Name, Start, End, Description, See Expiration', async ({ reportsPage }) => { 
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Custom Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('reportsPage');
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-custom-6');
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'End Date (optional)' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).click();
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).fill('e2e-test');
            await reportsPage.getByRole('checkbox', { name: 'See when software and/or' }).check();
            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();
            await expect(reportsPage.getByText('e2e-test-custom-6')).toBeVisible();
            await reportsPage.getByRole('cell', { name: 'e2e-test-custom-6' }).click();
            await reportsPage.getByRole('button', { name: 'Close' }).click();
        })
        test('Name, Start, End, Description, See Assets requiring maintenence', async ({ reportsPage }) => { 
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Custom Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('reportsPage');
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-custom-7');
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'End Date (optional)' }).fill('2025-11-22');
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).click();
            await reportsPage.getByRole('textbox', { name: 'Description (optional)' }).fill('e2e-test');
            await reportsPage.getByRole('checkbox', { name: 'See what requires maintenance' }).check();
            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();
            await expect(reportsPage.getByText('e2e-test-custom-7')).toBeVisible();
            await reportsPage.getByRole('cell', { name: 'e2e-test-custom-7' }).click();
            await reportsPage.getByRole('button', { name: 'Close' }).click();
        })
    })
    test.describe('Fail Case', () => {
        test('No Name', async ({ reportsPage }) => {
            reportsPage.once('dialog', async dialog => {
                await expect(dialog.message()).toContain('Please enter a report name.');
                await dialog.accept();
            });
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Custom Report' }).click();
            await reportsPage.getByRole('textbox', { name: 'Start Date *' }).fill('2025-11-22');


            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();

            await reportsPage.waitForTimeout(500);

            await reportsPage.getByRole('button', { name: 'Cancel' }).click();
        })
        test('No Start', async ({ reportsPage }) => {
            reportsPage.once('dialog', async dialog => {
                await expect(dialog.message()).toContain('Please select a start date.');
                await dialog.accept();
            });
            await reportsPage.reload();
            await reportsPage.getByRole('button', { name: 'New Report' }).click();
            await reportsPage.getByRole('button', { name: 'Custom Report' }).click();

            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).click();
            await reportsPage.getByRole('textbox', { name: 'Report Name *' }).fill('e2e-test-assignment-4');


            await reportsPage.getByRole('button', { name: 'Generate Report' }).click();

            await reportsPage.waitForTimeout(500);

            await reportsPage.getByRole('button', { name: 'Cancel' }).click();
        })
    })
})
