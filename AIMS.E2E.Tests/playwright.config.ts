import { defineConfig, devices } from '@playwright/test';
import fs from 'fs';
import path from 'path';
import dotenv from 'dotenv';

const envPath = path.resolve(__dirname, '.env.playwright');
if (fs.existsSync(envPath)) dotenv.config({ path: envPath });

const BASE_URL = process.env.BASE_URL || 'http://localhost:5119';
const USE_TEST_AUTH = (process.env.USE_TEST_AUTH || 'true').toLowerCase() === 'true';

// Always use storageState if it exists (bearer-login will create it in global-setup)
const STORAGE_STATE_PATH = 'storageState.json';
const STORAGE_STATE = fs.existsSync(STORAGE_STATE_PATH) ? STORAGE_STATE_PATH : (USE_TEST_AUTH ? STORAGE_STATE_PATH : undefined);

// Read cache from global-setup (only used for bearer-mode API calls)
const CACHE_PATH = path.resolve(__dirname, '.e2e-cache.json');
let extraHTTPHeaders: Record<string, string> | undefined;
if (!USE_TEST_AUTH && fs.existsSync(CACHE_PATH)) {
    try {
        const cached = JSON.parse(fs.readFileSync(CACHE_PATH, 'utf8'));
        if (cached?.ACCESS_TOKEN) {
            extraHTTPHeaders = {
                Authorization: `Bearer ${cached.ACCESS_TOKEN}`,
                Accept: 'application/json,text/plain;q=0.9,*/*;q=0.8'
            };
        }
    } catch { /* ignore */ }
}

export default defineConfig({
    testDir: './specs',
    fullyParallel: true,
    timeout: 60_000,
    expect: { timeout: 10_000 },
    reporter: [['list'], ['html', { open: 'never' }]],
    globalSetup: './global-setup.ts',
    use: {
        baseURL: BASE_URL,
        headless: true,
        launchOptions: { slowMo: 0 },
        storageState: STORAGE_STATE,
        trace: 'on-first-retry',
        actionTimeout: 15_000,
        navigationTimeout: 30_000,
        extraHTTPHeaders
    },
    // Make the app and tests agree on auth mode by passing UseTestAuth to the server.
    webServer: USE_TEST_AUTH ? {
        command: 'ASPNETCORE_ENVIRONMENT=Playwright UseTestAuth=true dotnet run --no-build --project ../AIMS',
        url: BASE_URL,
        reuseExistingServer: true,
        timeout: 180_000
    } : {
        command: 'ASPNETCORE_ENVIRONMENT=Playwright UseTestAuth=false dotnet run --no-build --project ../AIMS',
        url: BASE_URL,
        reuseExistingServer: true,
        timeout: 180_000
    },
    projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }]
});