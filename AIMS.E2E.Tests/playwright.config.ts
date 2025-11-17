import { defineConfig, devices } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import dotenv from 'dotenv';

const envPath = path.resolve(__dirname, '.env.playwright');
if (fs.existsSync(envPath)) {
    dotenv.config({ path: envPath });
}

const BASE_URL = process.env.BASE_URL || 'http://localhost:5119';
const CACHE_PATH = path.resolve(__dirname, '.e2e-cache.json');
const STORAGE_STATE = path.resolve(__dirname, 'storageState.json');

// Read bearer token for API tests (written by global-setup)
let cachedHeaders: Record<string, string> | undefined;
if (fs.existsSync(CACHE_PATH)) {
    try {
        const cached = JSON.parse(fs.readFileSync(CACHE_PATH, 'utf8'));
        if (cached?.ACCESS_TOKEN) {
            cachedHeaders = {
                Authorization: `Bearer ${cached.ACCESS_TOKEN}`,
            };
        }
    } catch {
        // ignore parse errors; tests can still run UI-only
    }
}

export default defineConfig({
    testDir: './specs',
    fullyParallel: true,
    timeout: 60_000,
    expect: { timeout: 10_000 },
    reporter: [['list'], ['html', { open: 'never' }]],
    globalSetup: require.resolve('./global-setup'),
    use: {
        baseURL: BASE_URL,
        headless: true,
        launchOptions: { slowMo: 0 },
        storageState: STORAGE_STATE,
        trace: 'on-first-retry',
        actionTimeout: 15_000,
        navigationTimeout: 30_000,
        // Merge cached Authorization header (if any) with default Accept
        extraHTTPHeaders: {
            ...(cachedHeaders ?? {}),
            Accept: 'application/json',
        },
    },
    webServer: {
        // Run app in Playwright environment (non-prod branch in Program.cs)
        command:
            'ASPNETCORE_ENVIRONMENT=Playwright dotnet run --no-build --project ../AIMS',
        url: BASE_URL,
        reuseExistingServer: true,
        timeout: 180_000,
    },
    projects: [
        {
            name: 'chromium',
            use: {
                ...devices['Desktop Chrome'],
            },
        },
    ],
});