import { test, expect } from "@playwright/test";

test("Search caching: Desktop → Laptop → Desktop does not re-request Desktop", async ({ page }) => {
  const apiUrl = "/api/assets";
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

  // First Desktop search (should hit network)
  console.log("\n Search: Desktop");
  await searchInput.fill("Desktop");
  await searchInput.press("Enter");
  await page.waitForTimeout(600);

  // Second search: Laptop (new term → new call)
  console.log("\n Search: Laptop");
  await searchInput.fill("Laptop");
  await searchInput.press("Enter");
  await page.waitForTimeout(600);

  // Third search: Desktop again (cached → no new call)
  console.log("\n Repeat Search: Desktop (should be cached)");
  await searchInput.fill("Desktop");
  await searchInput.press("Enter");
  await page.waitForTimeout(600);

  console.log("\n===== NETWORK RESULTS =====");
  console.log("Total network calls:", requestCount);
  requestUrls.forEach((u, i) => console.log(` ${i + 1}. ${u}`));
  console.log("==========================\n");

  const searchRequestUrls = requestUrls.filter(u =>
    u.includes('/api/assets/search')
  );

  const desktopCalls = searchRequestUrls.filter(u => u.includes("q=Desktop"));
  const laptopCalls = searchRequestUrls.filter(u => u.includes("q=Laptop"));

  // Desktop only once (cached second time)
  expect(desktopCalls.length).toBe(1);

  // Laptop requested once
  expect(laptopCalls.length).toBe(1);

  // Total search network calls = 2 (unique queries only)
  expect(searchRequestUrls.length).toBe(2);
});
