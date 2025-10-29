import { APIRequestContext, expect } from '@playwright/test';
import { REAL_USER_ID, REAL_HARDWARE_ID } from './ids';

type CreateAuditReq = {
    action: string;
    description: string;
    externalId: string;
    userId?: number;
    assetKind?: 'Hardware' | 'Software';
    hardwareId?: number;
    softwareId?: number;
};

export async function createAudit(
    api: APIRequestContext,
    req: CreateAuditReq
) {
    // provide server-required fields by default
    const payload = {
        userID: req.userId ?? REAL_USER_ID,
        action: req.action,
        description: req.description,
        assetKind: req.assetKind ?? 'Hardware',
        hardwareID: (req.assetKind ?? 'Hardware') === 'Hardware'
            ? (req.hardwareId ?? REAL_HARDWARE_ID)
            : undefined,
        softwareID: (req.assetKind ?? 'Hardware') === 'Software'
            ? req.softwareId
            : undefined,
        blobUri: null,
        snapshotJson: null,
        changes: [],
        externalId: req.externalId
    };

    const res = await api.post('/api/audit/create', { data: payload });
    expect([200, 201]).toContain(res.status()); // Created or OK
    return res;
}