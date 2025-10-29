import { test, expect } from "@playwright/test";

test("Search dedupes in-flight duplicate network requests (Desktop spam)", async ({ page }) => {
    const apiUrl = "/api/assets";
    const desktopQuery = "q=Desktop";

    let requestCount = 0;
    const requestUrls: string[] = [];

    page.on("request", req => {
        if (req.url().includes(apiUrl)) {
            requestUrls.push(req.url());
            requestCount++;
            console.log(`Network Request #${requestCount}: ${req.url()}`);
        }
    });

    await page.goto("http://localhost:5119/");
    await page.waitForLoadState("networkidle");

    const searchInput = page.getByRole("textbox", { name: /search/i });
    await expect(searchInput).toBeVisible();

    console.log("\nDeduplication Test: Spam Desktop search before first finishes");

    // Fill once
    await searchInput.fill("Desktop");

    // Trigger the same request multiple times rapidly
    for (let i = 0; i < 4; i++) {
        await searchInput.press("Enter");
    }

    // Wait for results to appear
    await page.waitForTimeout(1500);

    console.log("\n===== NETWORK RESULTS =====");
    console.log("All request URLs:", requestUrls);
    console.log("==========================\n");

    // Only count Desktop search requests
    const desktopRequests = requestUrls.filter(u => u.includes(desktopQuery));

    console.log("Desktop Requests:", desktopRequests.length);

    //Deduping assertion (only one Desktop request should be sent to backend)
    expect(desktopRequests.length).toBe(1);
});
