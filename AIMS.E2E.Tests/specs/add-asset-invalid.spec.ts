import { test, expect } from '@playwright/test';

test.describe('Add Asset – Invalid Input Scenarios', () => {

  // Reusable test item for duplicate tests
  let testItem = {
    serialNumber: 'SN_DUP_TEST1',
    tagNumber: 'MBC_DUPLICATE_01',
    category: 'Charging Cable'
  };

  const BASE_URL = 'http://localhost:5119';

  //
  async function ensureSeeded(page: any, item: { serialNumber: string; tagNumber: string; category: string }) {
    // Check if item already exists
    const exists = await page.request.get(`${BASE_URL}/api/assets/one?tag=${encodeURIComponent(item.tagNumber)}`);
    if (exists.ok()) {
      console.log(`Test item with tag ${item.tagNumber} already exists.`);
      return;
    }

    // Add the item via API
    await page.request.post(`${BASE_URL}/api/hardware/add-bulk`, {
      data: {
        dtos: [{
          serialNumber: item.serialNumber,
          manufacturer: 'Other',
          model: 'Other',
          assetTag: item.tagNumber,
          assetType: item.category,
          status: 'Available',
        }]
      },
    });

    // Fallback: seed via UI
    await page.goto(BASE_URL);
    await page.getByRole('link', { name: 'Charging Cables' }).click();
    await page.getByRole('button', { name: 'Manage Asset' }).click();
    await page.getByRole('link', { name: '➕ Add Hardware' }).click();
    await page.getByLabel('Manufacturer').selectOption('Other');
    await page.getByLabel('Model').selectOption('Other');
    await page.getByRole('spinbutton', { name: 'Number of Items:' }).fill('1');
    await page.getByLabel('Add New Assets').getByRole('button', { name: 'Next' }).click();
    await page.locator('#serialNumber').fill(item.serialNumber);
    await page.locator('#tagNumber').fill(item.tagNumber);
    await page.getByLabel('Enter Item Details').getByRole('button', { name: 'Next' }).click();
    await page.getByRole('button', { name: 'Add All Assets' }).click();

  }

  test.beforeAll(async ({ browser }) => {
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await ctx.newPage();
    await ensureSeeded(page, testItem);
    await ctx.close();
  });

  test.afterAll(async ({ browser }) => {
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await ctx.newPage();
    await ctx.close();
  });


  // ---------- Shared setup ----------
  test.beforeEach(async ({ page }) => {
    await page.goto(BASE_URL);
    await page.getByRole('link', { name: 'Charging Cables' }).click();
    await page.getByRole('button', { name: 'Manage Asset' }).click();
    await page.getByRole('link', { name: '➕ Add Hardware' }).click();

    // Phase 1 setup
    await page.getByLabel('Manufacturer').selectOption('Other');
    await page.getByLabel('Model').selectOption('Other');
    await page.getByRole('spinbutton', { name: 'Number of Items:' }).fill('1');
    await page.getByLabel('Add New Assets').getByRole('button', { name: 'Next' }).click();
  });

  // ===============================================================
  // TEST 1: Both fields blank
  // ===============================================================
  test('should prevent submission when serial and tag are blank', async ({ page }) => {
    const serialInput = page.locator('#serialNumber');
    const tagInput = page.locator('#tagNumber');

    // Attempt to submit with blanks
    await page.getByLabel('Enter Item Details').getByRole('button', { name: 'Next' }).click();
    await page.waitForTimeout(500);

    // Expect Bootstrap invalid classes
    await expect(serialInput).toHaveClass(/is-invalid/);
    await expect(tagInput).toHaveClass(/is-invalid/);


    // Backend should not accept blank tag
    const response = await page.request.get(`http://localhost:5119/api/assets/one?tag=`);
    expect(response.status(), 'Blank tag should not be accepted').not.toBe(200);
  });

  // ===============================================================
  // TEST 2: Over-length tag number
  // ===============================================================
  test('should reject tag numbers longer than 16 characters', async ({ page }) => {
    const tagInput = page.locator('#tagNumber');
    await tagInput.fill('MBC' + '9'.repeat(30)); // try 33 chars
    const value = await tagInput.inputValue();
    expect(value.length).toBeLessThanOrEqual(16);
  });

  // ===============================================================
  // TEST 3: Duplicate tag number
  // ===============================================================
  test('should reject duplicate tag numbers', async ({ page }) => {
    // Prepare a known valid tag first
    const serialInput = page.getByRole('textbox', { name: 'Serial Number:' });
    const tagInput = page.getByRole('textbox', { name: 'Tag Number:' });

    // Try using a tag that already exists in the DB
    await serialInput.fill('SN_DUPLICATE_CHECK');
    await tagInput.fill(testItem.tagNumber);
    await page.getByLabel('Enter Item Details').getByRole('button', { name: 'Next' }).click();

    // Check for invalid indicator or backend error popup
    //await expect(tagInput).toHaveClass(/is-invalid/);

    // Validate backend API also rejects duplicates
    const res = await page.request.post('http://localhost:5119/api/hardware/add-bulk', {
      data: [{ serialNumber: 'SN000999', tagNumber: testItem.tagNumber, category: 'Charging Cable' }],
    });
    expect(res.status()).not.toBe(200);
  });

  // ===============================================================
  // TEST 4: Tag valid, Serial blank
  // ===============================================================
  test('should prevent submission when serial is blank but tag is valid', async ({ page }) => {
    const serialInput = page.getByRole('textbox', { name: 'Serial Number:' });
    const tagInput = page.getByRole('textbox', { name: 'Tag Number:' });

    await tagInput.fill('MBC0000600');
    await serialInput.fill('');
    await page.getByLabel('Enter Item Details').getByRole('button', { name: 'Next' }).click();

    await expect(serialInput).toHaveClass(/is-invalid/);
    await expect(tagInput).not.toHaveClass(/is-invalid/);
  });

  // ===============================================================
  // TEST 5: Serial valid, Tag blank
  // ===============================================================
  test('should prevent submission when tag is blank but serial is valid', async ({ page }) => {
    const serialInput = page.getByRole('textbox', { name: 'Serial Number:' });
    const tagInput = page.getByRole('textbox', { name: 'Tag Number:' });

    await serialInput.fill('SN123456');
    await tagInput.fill('');
    await page.getByLabel('Enter Item Details').getByRole('button', { name: 'Next' }).click();

    await expect(tagInput).toHaveClass(/is-invalid/);
    await expect(serialInput).not.toHaveClass(/is-invalid/);
  });

  // ===============================================================
  // TEST 6: Duplicate Serial number
  // ===============================================================
  test('should reject duplicate serial numbers', async ({ page }) => {
    // Prepare a known valid tag first
    const serialInput = page.getByRole('textbox', { name: 'Serial Number:' });
    const tagInput = page.getByRole('textbox', { name: 'Tag Number:' });
    const submitBtn = page.getByRole('button', { name: 'Add All Assets' })

    // Try using a tag that already exists in the DB
    await serialInput.fill(testItem.serialNumber);
    await tagInput.fill('TEST_SERIAL_DUP');
    await page.getByLabel('Enter Item Details').getByRole('button', { name: 'Next' }).click();

    //check for validation errors once submit all button is clicked
    await submitBtn.click();
    await page.waitForTimeout(500);

    const duplicateAlert = page.locator('text=Duplicate serial number');
    await page.waitForTimeout(500);
    await expect(duplicateAlert).toBeVisible({ timeout: 3000 });

    //check backend as well
    const res = await page.request.post('http://localhost:5119/api/hardware/add-bulk', {
      data: [{ serialNumber: testItem.serialNumber, tagNumber: 'TEST_SERIAL_DUP', category: 'Charging Cable' }],
    });
    expect(res.status()).not.toBe(200);
  });

});
