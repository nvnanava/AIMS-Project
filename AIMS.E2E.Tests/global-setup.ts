import { request, FullConfig, chromium } from '@playwright/test';
import fs from 'fs';
import path from 'path';
import dotenv from 'dotenv';

const envPath = path.resolve(__dirname, '.env.playwright');
if (fs.existsSync(envPath)) dotenv.config({ path: envPath });

type Any = Record<string, any>;
const CACHE_PATH = path.resolve(__dirname, '.e2e-cache.json');

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
        : Array.isArray(data?.items) ? data.items
            : Array.isArray(data?.data) ? data.data
                : [];
    for (const r of rows) {
        const id = pickNumber(r, ...keys);
        if (id) return id;
    }
    return null;
}

function readClientSecret(): string | null {
    const fromEnv = process.env.CLIENT_SECRET?.trim();
    if (fromEnv) return fromEnv;
    const file = process.env.CLIENT_SECRET_FILE?.trim();
    if (file) {
        const p = path.isAbsolute(file) ? file : path.resolve(__dirname, file);
        if (fs.existsSync(p)) return fs.readFileSync(p, 'utf8').trim();
    }
    return null;
}

async function getAccessToken(): Promise<{ token: string; exp: number } | null> {
    const TENANT_ID = process.env.TENANT_ID;
    const CLIENT_ID = process.env.CLIENT_ID;
    const API_SCOPE = process.env.API_SCOPE;
    const secret = readClientSecret();

    if (!TENANT_ID || !CLIENT_ID || !API_SCOPE || !secret) return null;

    const tokenCtx = await request.newContext();
    const res = await tokenCtx.post(`https://login.microsoftonline.com/${TENANT_ID}/oauth2/v2.0/token`, {
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        form: {
            grant_type: 'client_credentials',
            client_id: CLIENT_ID,
            client_secret: secret,
            scope: API_SCOPE
        }
    });
    if (!res.ok()) {
        console.warn('[global-setup] token fetch failed:', res.status(), await res.text());
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

export default async function globalSetup(config: FullConfig) {
    const project = config.projects?.[0];
    const baseURL =
        (project?.use as any)?.baseURL ||
        process.env.BASE_URL ||
        'http://localhost:5119';

    const USE_TEST_AUTH = (process.env.USE_TEST_AUTH || 'true').toLowerCase() === 'true';

    if (USE_TEST_AUTH) {
        // Browser sign-in via TestAuth and persist storage
        const browser = await chromium.launch();
        const page = await browser.newPage({ baseURL });

        const user = process.env.TEST_USER || 'tburguillos@csus.edu';
        await page.goto(`/test/signin?as=${encodeURIComponent(user)}`);

        // 🔥 Pre-warm: compile views, init hubs, etc.
        await page.goto('/', { waitUntil: 'domcontentloaded' });
        await page.waitForLoadState('domcontentloaded');
        await page.waitForSelector('header, nav, main', { timeout: 10_000 }).catch(() => { });
        // Optional readiness ping (added in Program.cs below)
        await page.request.get('/_ready', { timeout: 20_000 }).catch(() => { });

        // Save cookies/localStorage so every spec starts authenticated
        await page.context().storageState({ path: 'storageState.json' });
        await browser.close();

        const cache = {
            REAL_USER_ID: 0,
            REAL_HARDWARE_ID: 0,
            BASE_URL: baseURL,
            ACCESS_TOKEN: '',
            TOKEN_SOURCE: 'testauth'
        };
        fs.writeFileSync(CACHE_PATH, JSON.stringify(cache, null, 2));
        return;
    }

    // === Bearer mode (API/client-credentials) ===
    const t = await getAccessToken();
    if (!t?.token) throw new Error('Failed to obtain AAD access token in global-setup.');
    const accessToken = t.token;

    const api = await request.newContext({
        baseURL,
        extraHTTPHeaders: { Authorization: `Bearer ${accessToken}` }
    });

    // Ensure app is up before discovery
    await api.get('/_ready', { timeout: 20_000 }).catch(() => { });

    const userId =
        await fetchAnyId(api, '/api/diag/users', ['UserID', 'userID', 'userId', 'id']);
    const hardwareId =
        await fetchAnyId(api, '/api/hardware/get-all', ['HardwareID', 'hardwareID', 'hardwareId', 'id']);

    const cache = {
        REAL_USER_ID: userId ?? 0,
        REAL_HARDWARE_ID: hardwareId ?? 0,
        BASE_URL: baseURL,
        ACCESS_TOKEN: accessToken,
        TOKEN_SOURCE: 'client_credentials'
    };
    fs.writeFileSync(CACHE_PATH, JSON.stringify(cache, null, 2));

    await api.dispose();
}