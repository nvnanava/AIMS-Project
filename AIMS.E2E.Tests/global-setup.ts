// global-setup.ts
import { request, FullConfig, chromium } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import dotenv from 'dotenv';

console.log('[global-setup] REALLY RUNNING');

// Load .env.playwright from the E2E project root
const envPath = path.resolve(__dirname, '.env.playwright');
if (fs.existsSync(envPath)) {
    dotenv.config({ path: envPath });
}

type Any = Record<string, any>;

const CACHE_PATH = path.resolve(__dirname, '.e2e-cache.json');
const STORAGE_STATE_PATH = path.resolve(__dirname, 'storageState.json');

// ---------- helpers ----------

function pickNumber(obj: Any, ...keys: string[]): number | null {
    for (const k of keys) {
        const v = obj?.[k];
        const n = typeof v === 'string' ? Number(v) : (v as number);
        if (Number.isFinite(n) && n > 0) return n;
    }
    return null;
}

async function fetchAnyId(api: any, url: string, keys: string[]) {
    const res = await api.get(url, { headers: { Accept: 'application/json' } });
    if (!res.ok()) return null;

    const data = await res.json().catch(() => null as any);
    const rows: Any[] = Array.isArray(data)
        ? data
        : Array.isArray(data?.items)
            ? data.items
            : Array.isArray(data?.data)
                ? data.data
                : [];

    for (const r of rows) {
        const id = pickNumber(r, ...keys);
        if (id) return id;
    }
    return null;
}

function readClientSecret(): string | null {
    const fromEnv =
        process.env.CLIENT_SECRET?.trim() ||
        process.env.AZUREAD_CLIENT_SECRET?.trim();
    if (fromEnv) return fromEnv;

    const file = process.env.CLIENT_SECRET_FILE?.trim();
    if (file) {
        const p = path.isAbsolute(file) ? file : path.resolve(__dirname, file);
        if (fs.existsSync(p)) return fs.readFileSync(p, 'utf8').trim();
    }
    return null;
}

async function getAccessToken(): Promise<{ token: string; exp: number } | null> {
    const TENANT_ID =
        process.env.TENANT_ID || process.env.AZUREAD_TENANT_ID || '';
    const CLIENT_ID =
        process.env.CLIENT_ID || process.env.AZUREAD_CLIENT_ID || '';
    const API_SCOPE =
        process.env.API_SCOPE || process.env.AZUREAD_API_SCOPE || '';
    const secret = readClientSecret();

    if (!TENANT_ID || !CLIENT_ID || !API_SCOPE || !secret) {
        console.warn(
            '[global-setup] Missing TENANT_ID / CLIENT_ID / API_SCOPE / CLIENT_SECRET env vars'
        );
        return null;
    }

    const tokenCtx = await request.newContext();
    const res = await tokenCtx.post(
        `https://login.microsoftonline.com/${TENANT_ID}/oauth2/v2.0/token`,
        {
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            form: {
                grant_type: 'client_credentials',
                client_id: CLIENT_ID,
                client_secret: secret,
                scope: API_SCOPE,
            },
        }
    );

    if (!res.ok()) {
        console.warn(
            '[global-setup] token fetch failed:',
            res.status(),
            await res.text()
        );
        await tokenCtx.dispose();
        return null;
    }

    const body = await res.json();
    await tokenCtx.dispose();

    const token = body?.access_token as string | undefined;
    const expiresIn = Number(body?.expires_in) || 3600;
    if (!token) return null;

    const exp = Math.floor(Date.now() / 1000) + expiresIn - 60;
    return { token, exp };
}

// ---------- global setup ----------

export default async function globalSetup(config: FullConfig) {
    const project = config.projects?.[0];
    const baseURL =
        (project?.use as any)?.baseURL ||
        process.env.BASE_URL ||
        'http://localhost:5119';

    // Clean old storage; each run re-establishes a fresh session
    try {
        if (fs.existsSync(STORAGE_STATE_PATH)) fs.unlinkSync(STORAGE_STATE_PATH);
    } catch { }
    try {
        if (fs.existsSync(CACHE_PATH)) fs.unlinkSync(CACHE_PATH);
    } catch { }

    // 1) Acquire AAD access token (client credentials)
    const t = await getAccessToken();
    if (!t?.token)
        throw new Error('Failed to obtain AAD access token in global-setup.');
    const accessToken = t.token;

    // 2) Use token for API calls to gather some IDs + cache metadata
    const api = await request.newContext({
        baseURL,
        extraHTTPHeaders: { Authorization: `Bearer ${accessToken}` },
    });

    // Soft health checks
    await api.get('/e2e/app-ready', { timeout: 20_000 }).catch(() => { });
    await api.get('/e2e/auth-ready', { timeout: 20_000 }).catch(() => { });

    const userId = await fetchAnyId(api, '/api/diag/users', [
        'UserID',
        'userID',
        'userId',
        'id',
    ]);
    const hardwareId = await fetchAnyId(api, '/api/hardware/get-all', [
        'HardwareID',
        'hardwareID',
        'hardwareId',
        'id',
    ]);

    fs.writeFileSync(
        CACHE_PATH,
        JSON.stringify(
            {
                REAL_USER_ID: userId ?? 0,
                REAL_HARDWARE_ID: hardwareId ?? 0,
                BASE_URL: baseURL,
                ACCESS_TOKEN: accessToken,
                TOKEN_SOURCE: 'client_credentials',
            },
            null,
            2
        )
    );
    await api.dispose();

    // 3) Perform the bearer→cookie bridge via /e2e/ensure-cookie-admin
    const browser = await chromium.launch();
    const page = await browser.newPage({ baseURL });

    // Health checks (anonymous OK)
    await page.request.get('/e2e/app-ready', { timeout: 20_000 }).catch(() => { });
    await page.request.get('/e2e/auth-ready', { timeout: 20_000 }).catch(() => { });

    await page.goto(
        `/e2e/ensure-cookie-admin?token=${encodeURIComponent(accessToken)}`,
        {
            waitUntil: 'networkidle',
        }
    );

    // 4) Verify auth & roles
    const who = await page.request.get('/e2e/whoami').catch(() => null);
    if (!who || !who.ok()) {
        throw new Error('E2E auth failed: /e2e/whoami not reachable.');
    }

    const ct = (who.headers()['content-type'] || '').toLowerCase();
    if (!ct.includes('application/json')) {
        const body = await who.text().catch(() => '');
        console.error(
            '[global-setup] /e2e/whoami returned non-JSON',
            ct,
            body.slice(0, 400)
        );
        throw new Error('E2E auth failed: /e2e/whoami did not return JSON.');
    }

    const whoJson = await who.json();
    if (!whoJson.isAuthenticated) {
        console.error(
            '[global-setup] /e2e/whoami says isAuthenticated = false:',
            whoJson
        );
        throw new Error(
            'E2E auth failed: not authenticated after /e2e/ensure-cookie-admin'
        );
    }
    console.log('[global-setup] whoami:', JSON.stringify(whoJson, null, 2));
    
    // Roles debug (helpful when fighting 401s)
    const rolesDebug = await page.request
        .get('/e2e/roles-debug')
        .catch(() => null);
    if (rolesDebug && rolesDebug.ok()) {
        console.log('[global-setup] roles-debug:', await rolesDebug.text());
    }

    // 5) Warm up key pages (Dashboard + AuditLog)
    await page.goto('/', { waitUntil: 'domcontentloaded' }).catch(() => { });
    await page.goto('/AuditLog', { waitUntil: 'domcontentloaded' }).catch(() => { });
    await page
        .waitForSelector('#auditTable', { state: 'attached', timeout: 10_000 })
        .catch(() => {
            // Non-fatal; tests will still assert more precisely
            console.warn(
                '[global-setup] #auditTable not attached during warmup (will rely on tests to wait)'
            );
        });

    // 6) Save storage state (cookie-based admin session)
    await page.context().storageState({ path: STORAGE_STATE_PATH });
    await browser.close();
}