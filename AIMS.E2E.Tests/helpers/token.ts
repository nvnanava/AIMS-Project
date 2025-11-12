import * as fs from 'fs';
import * as path from 'path';

function readSecret(): string {
    const file = process.env.CLIENT_SECRET_FILE;
    if (file && fs.existsSync(file)) {
        return fs.readFileSync(path.resolve(file), 'utf8').trim();
    }
    const env = process.env.CLIENT_SECRET;
    if (env && env.trim()) return env.trim();
    throw new Error('No CLIENT_SECRET_FILE or CLIENT_SECRET available');
}

export async function getClientCredentialsToken(): Promise<string> {
    const tenant = process.env.TENANT_ID!;
    const clientId = process.env.CLIENT_ID!;
    const scope = process.env.API_SCOPE!;
    const clientSecret = readSecret();

    const body = new URLSearchParams({
        grant_type: 'client_credentials',
        client_id: clientId,
        client_secret: clientSecret,
        scope
    });

    const resp = await fetch(`https://login.microsoftonline.com/${tenant}/oauth2/v2.0/token`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body
    });

    if (!resp.ok) {
        const text = await resp.text();
        throw new Error(`Token request failed: ${resp.status} ${resp.statusText}\n${text}`);
    }
    const json = await resp.json() as { access_token?: string };
    if (!json.access_token) throw new Error('No access_token in token response');
    return json.access_token;
}