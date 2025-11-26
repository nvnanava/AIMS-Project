import { test, expect } from '@playwright/test';

test('Search caching: Desktop → Laptop → Desktop does not explode network traffic', async ({ page }) => {
  const apiUrl = '/api/assets';
  let requestCount = 0;
  const requestUrls: string[] = [];

  page.on('request', req => {
    if (req.url().includes(apiUrl)) {
      requestUrls.push(req.url());
      requestCount++;
      console.log(`Network Request #${requestCount}: ${req.url()}`);
    }
  });

  await page.goto('/', { waitUntil: 'domcontentloaded' });
  await page.waitForLoadState('networkidle').catch(() => { });

  const searchInput = page.getByRole('textbox', { name: /search/i });
  await expect(searchInput).toBeVisible();

  // First Desktop search
  console.log('\n Search: Desktop');
  await searchInput.fill('Desktop');
  await searchInput.press('Enter');
  await page.waitForTimeout(700);

  // Laptop
  console.log('\n Search: Laptop');
  await searchInput.fill('Laptop');
  await searchInput.press('Enter');
  await page.waitForTimeout(700);

  // Desktop again – behavior should not explode beyond pagination
  console.log('\n Repeat Search: Desktop (pagination expected)');
  await searchInput.fill('Desktop');
  await searchInput.press('Enter');
  await page.waitForTimeout(700);

  console.log('\n===== NETWORK RESULTS =====');
  console.log('Total network calls:', requestCount);
  requestUrls.forEach((u, i) => console.log(` ${i + 1}. ${u}`));
  console.log('==========================\n');

  const normalize = (raw: string) => {
    const u = new URL(raw);
    u.searchParams.delete('_v');
    return u.toString();
  };

  const searchRequestUrls = requestUrls.filter(u =>
    u.includes('/api/assets/search')
  );
  const normalized = searchRequestUrls.map(normalize);

  const desktopCalls = normalized.filter(u => u.includes('q=Desktop'));
  const laptopCalls = normalized.filter(u => u.includes('q=Laptop'));

  // Sanity checks
  expect(desktopCalls.length).toBeGreaterThan(0);
  expect(laptopCalls.length).toBeGreaterThan(0);

  // ---- REALISTIC limits based on actual pagination behavior ----
  // Desktop searches paginate (5 pages max) → allow up to 6 calls
  expect(desktopCalls.length).toBeLessThanOrEqual(6);

  // Laptop often fits on one page -> allow up to 3
  expect(laptopCalls.length).toBeLessThanOrEqual(3);
});