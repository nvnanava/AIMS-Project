import { test, expect, Page, Locator, APIResponse } from '@playwright/test';
import { createAudit, uuid } from '../helpers/utils';
import { waitForAuditHub, waitForApiSeesExternalId } from '../helpers/realtime';

const FIND_TIMEOUT_MS = Number(process.env.DEDUP_FIND_TIMEOUT_MS ?? 30_000);
const TABLE_WAIT_MS = Number(process.env.DEDUP_TABLE_WAIT_MS ?? 20_000);

/* ---------------- Debug wiring (console/network) ---------------- */
function wireDiagnostics(page: Page) {
    page.on('console', msg => {
        // Forward browser errors/warnings to test output
        if (['error', 'warning'].includes(msg.type()))
            console.log(`[browser:${msg.type()}]`, msg.text());
    });
    page.on('pageerror', err => console.log('[pageerror]', err?.message || err));
    page.on('response', res => {
        const url = res.url();
        if (res.status() >= 400) console.log(`[resp ${res.status()}] ${url}`);
    });
}

/* ---------------- Auth/redirect sanity ---------------- */
async function failIfAuthRedirect(page: Page) {
    const url = page.url();
    if (/(login\.microsoftonline\.com|\/Account\/Login|\/signin-oidc)/i.test(url)) {
        throw new Error(`[AUTH] Redirected to login: ${url}. Load storageState or enable TestAuth.`);
    }
    const hasPassword = await page.locator('input[type="password"]').first().isVisible().catch(() => false);
    if (hasPassword) throw new Error(`[AUTH] Login form detected at ${url}. Provide storageState/TestAuth.`);
}

/* ---------------- Navigation that tries both routes ---------------- */
async function gotoAuditLog(page: Page) {
    // Prefer /AuditLog; fall back to /AuditLog/Index
    const paths = ['/AuditLog', '/AuditLog/Index'];

    for (const p of paths) {
        let nav: APIResponse | null = null;
        try {
            nav = await page.goto(p, { waitUntil: 'domcontentloaded' });
        } catch (e) {
            console.log(`[nav] Error navigating to ${p}:`, e);
        }

        const status = nav?.status();
        const okish = !status || (status >= 200 && status < 400);
        if (!okish) console.log(`[nav] ${p} returned status ${status ?? 'unknown'}`);

        const h1Has = await page
            .getByRole('heading', { name: /audit log/i })
            .first()
            .isVisible()
            .catch(() => false);

        if (h1Has) return;
    }

    const title = await page.title().catch(() => '');
    const bodyText = (await page.locator('body').innerText().catch(() => '')).slice(0, 1200);
    throw new Error(
        `[NAV] Could not reach an Audit Log page at /AuditLog or /AuditLog/Index. ` +
        `url=${page.url()} title="${title}"\nBody(first1.2k):\n${bodyText}`
    );
}

/* ---------------- Wait for any acceptable table ---------------- */
function tableLocator(page: Page) {
    // Prefer the styled audit table, then fall back to legacy id
    return page
        .locator('table.audit-log-table, table.admin-table-body, #auditTable')
        .first();
}

async function waitForAuditTable(page: Page): Promise<Locator> {
    await failIfAuthRedirect(page);

    const tbl = tableLocator(page);
    const attached = await tbl
        .waitFor({ state: 'attached', timeout: TABLE_WAIT_MS })
        .then(() => true)
        .catch(() => false);

    if (!attached) {
        const title = await page.title().catch(() => '');
        const bodyText = (await page.locator('body').innerText().catch(() => '')).slice(0, 1200);
        throw new Error(
            `[UI] audit table not found after ${TABLE_WAIT_MS}ms at ${page.url()} title="${title}"\nBody:\n${bodyText}`
        );
    }

    await page.waitForTimeout(200);
    return tbl;
}

/* ---------------- Pagination helpers (client-side pager) ---------------- */
async function goToFirstPage(page: Page) {
    const prev = page
        .getByRole('button', { name: /prev(ious)? page/i })
        .or(page.locator('#pg-prev,#audit-pg-prev'));

    for (let i = 0; i < 20; i++) {
        const disabled = await prev.isDisabled().catch(() => true);
        if (disabled) break;
        await prev.click().catch(() => { });
        await page.waitForTimeout(80);
    }
}

async function findInPagedTable(page: Page, table: Locator, text: string): Promise<boolean> {
    await goToFirstPage(page);
    const next = page
        .getByRole('button', { name: /next page/i })
        .or(page.locator('#pg-next,#audit-pg-next'));

    for (let i = 0; i < 20; i++) {
        if ((await table.filter({ hasText: text }).count()) > 0) return true;

        const disabled = await next.isDisabled().catch(() => true);
        if (disabled) return false;

        await next.click().catch(() => { });
        await page.waitForTimeout(80);
    }
    return false;
}

async function ensureNotInPagedTable(page: Page, table: Locator, text: string): Promise<boolean> {
    await goToFirstPage(page);
    const next = page
        .getByRole('button', { name: /next page/i })
        .or(page.locator('#pg-next,#audit-pg-next'));

    for (let i = 0; i < 20; i++) {
        if ((await table.filter({ hasText: text }).count()) > 0) return false;

        const disabled = await next.isDisabled().catch(() => true);
        if (disabled) return true;

        await next.click().catch(() => { });
        await page.waitForTimeout(80);
    }
    return true;
}

/* ---------------- Test ---------------- */
test('Dedup: same externalId updates, no duplicate', async ({ page, request, baseURL }) => {
    wireDiagnostics(page);

    await gotoAuditLog(page);
    let table = await waitForAuditTable(page);

    // Ensure realtime hub is up (best-effort)
    await waitForAuditHub(page);

    const ext = uuid();
    const desc1 = `Dedup1 ${Date.now()}`;
    const desc2 = `Dedup2 ${Date.now()}`;

    // 1) Create → confirm server sees it → confirm UI has it
    await createAudit(request, { action: 'Create', description: desc1, externalId: ext });
    expect(await waitForApiSeesExternalId(baseURL!, ext)).toBe(true);

    try {
        await expect
            .poll(async () => await findInPagedTable(page, table, desc1), { timeout: FIND_TIMEOUT_MS })
            .toBe(true);
    } catch {
        console.warn('[dedup] Initial UI poll for desc1 timed out; reloading once and retrying.');
        await page.reload({ waitUntil: 'domcontentloaded' });
        table = await waitForAuditTable(page);

        await expect
            .poll(async () => await findInPagedTable(page, table, desc1), {
                timeout: Math.floor(FIND_TIMEOUT_MS / 2),
            })
            .toBe(true);
    }

    // 2) Update same externalId → confirm server sees new description
    await createAudit(request, { action: 'Update', description: desc2, externalId: ext });
    expect(await waitForApiSeesExternalId(baseURL!, ext, desc2)).toBe(true);

    // 3) Reload once to ensure UI picks up the updated description if not live-pushed
    await page.reload({ waitUntil: 'domcontentloaded' });
    table = await waitForAuditTable(page);

    try {
        await expect
            .poll(async () => await findInPagedTable(page, table, desc2), { timeout: FIND_TIMEOUT_MS })
            .toBe(true);
    } catch {
        console.warn('[dedup] UI did not show desc2 after first poll; reloading once more.');
        await page.reload({ waitUntil: 'domcontentloaded' });
        table = await waitForAuditTable(page);

        await expect
            .poll(async () => await findInPagedTable(page, table, desc2), {
                timeout: Math.floor(FIND_TIMEOUT_MS / 2),
            })
            .toBe(true);
    }

    // 4) Old description must not exist on any page
    try {
        await expect
            .poll(async () => await ensureNotInPagedTable(page, table, desc1), { timeout: FIND_TIMEOUT_MS })
            .toBe(true);
    } catch {
        console.warn('[dedup] ensureNotInPagedTable(desc1) timed out; reloading once and retrying.');
        await page.reload({ waitUntil: 'domcontentloaded' });
        table = await waitForAuditTable(page);

        await expect
            .poll(async () => await ensureNotInPagedTable(page, table, desc1), {
                timeout: Math.floor(FIND_TIMEOUT_MS / 2),
            })
            .toBe(true);
    }
});