import { test, expect, request as pwRequest } from '@playwright/test';
import { uuid, createAudit, isoMinutesAgo, getEvents, waitForEventById, expectDescSorted } from '../helpers/utils';

test('seed audit and see it via polling within poll budget', async ({ playwright, baseURL }) => {
    const api = await pwRequest.newContext({ baseURL });

    const externalId = uuid();
    const t0 = performance.now();

    await createAudit(api, {
        action: 'Create',
        description: `E2E Create @ ${new Date().toISOString()}`,
        externalId,
    });

    // Rewind 10 min to be safe; then poll until we see externalId as 'id'
    const since = isoMinutesAgo(10);
    const { seenAt, payload } = await waitForEventById(api, {
        id: externalId,
        startSince: since,
        timeoutMs: 7_000, // we want <= pollInterval+1s; bump if our poll is >6s
        pollMs: 1_000,
    });

    // Assert latency budget (fallback polling budget)
    expect(seenAt).toBeLessThanOrEqual(6_000);

    // Basic shape checks
    expect(payload?.id).toBe(externalId);

    // And the list is ordered / capped
    const { status, json } = await getEvents(api, { since, take: 50 });
    expect(status).toBe(200);
    expect(json).toBeDefined();
    expect(Array.isArray(json!.items)).toBeTruthy();
    expectDescSorted(json!.items);
    expect(json!.items.length).toBeLessThanOrEqual(50);

    const elapsed = performance.now() - t0;
    console.log(`Seen in ${Math.round(seenAt)}ms, total test ${Math.round(elapsed)}ms`);
});