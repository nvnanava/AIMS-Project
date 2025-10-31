import { defineConfig, devices } from '@playwright/test';

const BASE_URL = process.env.BASE_URL || 'http://localhost:5119';
const USE_TEST_AUTH = (process.env.USE_TEST_AUTH || 'true').toLowerCase() === 'true';
const STORAGE_STATE = USE_TEST_AUTH ? undefined : './.auth/user.json';

export default defineConfig({
    testDir: './specs',
    fullyParallel: true,
    timeout: 60_000,
    expect: { timeout: 10_000 },
    reporter: [['list'], ['html', { open: 'never' }]],
    use: {
        baseURL: BASE_URL,
        headless: true,
        launchOptions: { slowMo: 0 },
        storageState: STORAGE_STATE,
        trace: 'on-first-retry',
        actionTimeout: 15_000,
        navigationTimeout: 15_000
    },
    projects: [
        {
            name: 'chromium',
            use: { ...devices['Desktop Chrome'] }
        }
    ]
});