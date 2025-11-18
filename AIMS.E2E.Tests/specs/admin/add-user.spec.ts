import { test, expect } from '@playwright/test';

test.describe('Add User – Invalid Input Scenarios', () => {
    const BASE_URL = 'http://localhost:5119';


    // use the navbar to navigate to the admin page before each test
    test.beforeEach(async ({ page }) => {
        // go to home page
        page.goto(BASE_URL);

        // find the admin link and click it
        await page.getByRole('link', { name: 'Admin' }).click();
    })

 });