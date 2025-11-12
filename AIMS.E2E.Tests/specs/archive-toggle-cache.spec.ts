import { test, expect } from '@playwright/test';

test('archives toggle uses aimsFetch cache (only 1 network hit for archived)', async ({ page }) => {
  await page.goto('http://localhost:5119/');
  await page.getByRole('link', { name: 'Desktops' }).click();

  // --- Track requests ---
  let archivedHits = 0;
  page.on('request', req => {
    if (req.method() !== 'GET') return;
    const url = req.url();
    if (!url.includes('/api/assets')) return;
    try {
      const val = new URL(url).searchParams.get('showArchived');
      if (val === 'true') archivedHits++;
    } catch { /* ignore */ }
  });

  // --- Open Filters ---
  await page.getByRole('button', { name: 'Filters' }).click();

  // --- Click ON once ---
  await page.getByRole('checkbox', { name: 'Show Archived' }).click();

  // Wait for the first archived response
  await page.waitForResponse(
    res => res.url().includes('/api/assets') &&
      res.ok() &&
      res.request().url().includes('showArchived=true'),
    { timeout: 2000 }
  ).catch(() => console.warn('Archived response not seen (possibly cached immediately)'));

  // --- Toggle 4–5 times with re-locating the element ---
  for (let i = 0; i < 5; i++) {
    const cb = page.getByRole('checkbox', { name: 'Show Archived' });
    await cb.click(); // off
    await page.waitForTimeout(300);
    await cb.click(); // on
    await page.waitForTimeout(300);
  }

  // Wait for any stray requests
  await page.waitForTimeout(1000);

  console.log(`Total archivedHits: ${archivedHits}`);
  expect(archivedHits).toBeLessThanOrEqual(1);
});
