import { test, expect } from '@playwright/test';

test('Search dedupes in-flight duplicate network requests (Desktop spam)', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const searchInput = page.getByRole('textbox', { name: /search/i });
    await expect(searchInput).toBeVisible();

    const isDesktopSearch = (url: string) =>
        url.includes('/api/assets/search') && url.includes('q=Desktop');

    let desktopCount = 0;
    page.on('request', req => {
        const url = req.url();
        if (isDesktopSearch(url)) desktopCount++;
    });

    // Trigger duplicates quickly
    await searchInput.fill('Desktop');

    // Kick off the first search and explicitly wait for it
    const firstReq = page.waitForRequest(req => isDesktopSearch(req.url()), { timeout: 5000 });
    await searchInput.press('Enter');
    await firstReq;

    // Spam more enters while first is in flight
    await Promise.all([
        searchInput.press('Enter'),
        searchInput.press('Enter'),
        searchInput.press('Enter'),
    ]);

    // Give our FE deduper/debouncer a moment to coalesce
    await page.waitForTimeout(1000);

    // Assert we only saw one Desktop search request
    expect(desktopCount).toBe(1);
});