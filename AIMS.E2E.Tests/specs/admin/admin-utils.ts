import { expect } from "@playwright/test";


export const adminBeforeAll = async (sharedPage: any) => {
    // check if test user already exists in the application
            const req = await sharedPage.request.get('/api/admin/users/exists?graphObjectId=e05d050b-7c37-4e59-90ab-f19872d808b8');
    const resp = await req.json();
    console.log(resp)
            if (!resp.exists) {
                console.log("Seeding User...");
                // Insert user via UI
                await sharedPage.getByRole('button', { name: 'Add User' }).click();
                await sharedPage.getByRole('textbox', { name: 'Name *' }).fill('test user'); // Changed to be a full name for better visibility
                await sharedPage.getByRole('button', { name: 'test user test-user@' }).click();
                await sharedPage.getByLabel('Role *').selectOption('1');
                await sharedPage.getByRole('textbox', { name: 'Office *' }).fill('ISB');
                const isb_button = sharedPage.getByRole('button', { name: 'ISB' });
                if (await isb_button.isVisible()) {
                    await isb_button.click();
                }
                await sharedPage.getByLabel('Add New User').getByRole('button', { name: 'Add User' }).click();
    
                // Final assertion
                await expect(sharedPage.getByRole('cell', { name: 'test user' })).toBeVisible();
            }
    
}


export const adminAfterAll = async (sharedPage:any, sharedContext: any) => {
    const userRes = await sharedPage.request.delete(`/api/clean/user?GraphObjectID=e05d050b-7c37-4e59-90ab-f19872d808b8`);
    if (!(userRes.ok() || userRes.status() === 204)) {
        console.log(`Delete test user failed: ${userRes.status()} ${userRes.statusText()}`);
    }
    const officeRes = await sharedPage.request.delete(`/api/clean/office?OfficeName=Test%20Office`);
    if (!(officeRes.ok() || officeRes.status() === 204)) {
        console.log(`Delete test office failed: ${officeRes.status()} ${officeRes.statusText()}`);
    }
    await sharedContext.close();
}