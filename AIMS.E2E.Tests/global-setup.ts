// global-setup.ts
import { request, FullConfig } from '@playwright/test';
import fs from 'fs';
import path from 'path';

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

async function globalSetup(config: FullConfig) {
    // Determine baseURL from config or env (fallback to local)
    const project = config.projects?.[0];
    const baseURL =
        (project?.use as any)?.baseURL ||
        process.env.BASE_URL ||
        'http://localhost:5119';

    const api = await request.newContext({ baseURL });

    // Try to fetch IDs
    const userId =
        await fetchAnyId(api, '/api/diag/users', ['UserID', 'userID', 'userId', 'id']);
    const hardwareId =
        await fetchAnyId(api, '/api/hardware/get-all', ['HardwareID', 'hardwareID', 'hardwareId', 'id']);

    // Write cache file (safe to be missing one of them; helpers will still fall back)
    const cache = {
        REAL_USER_ID: userId ?? 0,
        REAL_HARDWARE_ID: hardwareId ?? 0,
        BASE_URL: baseURL
    };
    fs.writeFileSync(CACHE_PATH, JSON.stringify(cache, null, 2));

    await api.dispose();
}

export default globalSetup;