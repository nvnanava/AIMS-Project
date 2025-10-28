/* ======================================================================
   AIMS Script: auditlog.index.js
   ----------------------------------------------------------------------
   Purpose
   - Client-side behavior for AuditLog/Index:
     * Toggle filter dropdown
     * Free-text search across table rows
     * Action filter (Assign/Create/Update/Delete/Unassign/All)

   How it works
   - Uses IDs/classes from AuditLog/Index.cshtml:
     #filter-button-toggle, #filterDropdown, #filterSearchInput,
     .dropdown-item, .audit-log-table.
   - No inline JS in the view; this module wires all events on DOMContentLoaded.

   Conventions
   - 4-space indentation; no tabs.
   - Defensive null checks for elements.
   - Accessibility: keeps aria-expanded/aria-hidden in sync.

   Public API
   - None (all private). Extend via new functions if server data binding is added.

   ====================================================================== */

(() => {
    "use strict";

    // ----- DOM helpers ---------------------------------------------------
    function $(sel) { return document.querySelector(sel); }
    function $all(sel) { return Array.from(document.querySelectorAll(sel)); }

    function setDropdownOpen(isOpen) {
        const btn = $("#filter-button-toggle");
        const dd = $("#filterDropdown");
        if (!btn || !dd) return;

        dd.classList.toggle("show", isOpen);
        dd.setAttribute("aria-hidden", String(!isOpen));
        btn.setAttribute("aria-expanded", String(isOpen));
    }

    function toggleDropdown() {
        const dd = $("#filterDropdown");
        const isOpen = dd?.classList.contains("show");
        setDropdownOpen(!isOpen);
    }

    function closeDropdownIfClickAway(evt) {
        const container = $("#filter-button-container");
        const dd = $("#filterDropdown");
        if (!container || !dd) return;
        if (!container.contains(evt.target) && dd.classList.contains("show")) {
            setDropdownOpen(false);
        }
    }

    // ----- Filters -------------------------------------------------------
    function filterByFreeText(term) {
        const rows = $all(".audit-log-table tbody tr");
        const q = (term || "").toLowerCase();
        rows.forEach(row => {
            const text = row.innerText.toLowerCase();
            row.style.display = text.includes(q) ? "" : "none";
        });
    }

    function filterByAction(action) {
        const rows = $all(".audit-log-table tbody tr");
        const wanted = (action || "All").trim();
        rows.forEach(row => {
            const cell = row.cells?.[3]; // Action column
            const current = (cell?.innerText || "").trim();
            row.style.display = (wanted === "All" || current === wanted) ? "" : "none";
        });
    }

    // ----- Event wiring --------------------------------------------------
    window.addEventListener("DOMContentLoaded", () => {
        const btnToggle = $("#filter-button-toggle");
        const dd = $("#filterDropdown");
        const txtSearch = $("#filterSearchInput");

        // Toggle dropdown
        btnToggle?.addEventListener("click", (e) => {
            e.preventDefault();
            toggleDropdown();
        });

        // Search-as-you-type across table
        txtSearch?.addEventListener("keyup", function () {
            filterByFreeText(this.value);
        });

        // Action filters (All / Assign / Create / Update / Delete / Unassign)
        $all(".dropdown-item").forEach(a => {
            a.addEventListener("click", (e) => {
                e.preventDefault();
                const picked = a.getAttribute("data-action");
                filterByAction(picked);
                setDropdownOpen(false);
            });
        });

        // Close on click-away
        document.addEventListener("click", closeDropdownIfClickAway);

        // Close on Escape
        document.addEventListener("keydown", (e) => {
            if (e.key === "Escape") setDropdownOpen(false);
        });

        // Initial ARIA state
        if (dd) setDropdownOpen(false);
    });

})();

/* ======================================================================
   ★ AIMS Realtime: SignalR + Polling fallback + Dedup/Incremental render
   ----------------------------------------------------------------------
   Goal
   - Render new rows within ≤5s via SignalR; fallback to polling.
   - Deduplicate by Id or Hash (update in place if seen).

   Endpoints
   - SignalR hub: /hubs/audit  -> event "auditEvent"
   - Polling API: /api/audit/events?since=<ISO>&take=<N>

   Config
   - Read from hidden #auditlog-config (data-* attributes):
     poll-interval, max-batch, feature flags.

   DOM
   - Target tbody#auditTableBody inside table#auditTable.

   ====================================================================== */

(() => {
    "use strict";

    // ---- Quick guards (don’t break page if element missing) -------------
    const tbody = document.getElementById("auditTableBody");
    const configEl = document.getElementById("auditlog-config");
    if (!tbody || !configEl) return;

    // ---- Read config passed from Razor ----------------------------------
    const POLL_MS = +(configEl.dataset.pollInterval || 4000);
    const MAX_BATCH = +(configEl.dataset.maxBatch || 50);
    const FEATURE_REALTIME = String(configEl.dataset.featureRealtime || "true").toLowerCase() === "true";
    const FEATURE_POLLING = String(configEl.dataset.featurePolling || "true").toLowerCase() === "true";

    // ---- Local state -----------------------------------------------------
    const seen = new Map(); // key: id||hash -> <tr>
    let sinceCursor = new Date(Date.now() - 24 * 3600 * 1000).toISOString(); // start: last 24h
    let pollTimer = null;
    let backoffMs = 0;
    const aborter = new AbortController();  // cancel in-flight requests on unload

    // ---- Helpers ---------------------------------------------------------
    function fmtLocal(dtIso) {
        try {
            const d = new Date(dtIso);
            return d.toLocaleString();
        } catch { return dtIso; }
    }

    function keyOf(evt) {
        return evt?.id || evt?.hash || "";
    }

    // Build table cells for an audit event
    function buildCells(evt) {
        // Columns: Log ID, Timestamp, User ID, Action, Asset ID, Previous, New, Description
        // For realtime events we don’t have LogID (DB int). Show id/hash short as a stand-in until list refresh.
        const idCell = document.createElement("td");
        idCell.textContent = evt.id || evt.hash?.slice(0, 8) || "—";

        const tsCell = document.createElement("td");
        tsCell.textContent = fmtLocal(evt.occurredAtUtc);

        const userIdCell = document.createElement("td");
        // evt.user like "Name (9)" -> try to extract trailing (id)
        const m = /\((\d+)\)\s*$/.exec(evt.user || "");
        userIdCell.textContent = m ? `U${m[1]}` : (evt.user || "—");

        const actionCell = document.createElement("td");
        actionCell.textContent = evt.type || "—";

        const assetIdCell = document.createElement("td");
        assetIdCell.textContent = evt.target || "—";

        const prevCell = document.createElement("td");
        prevCell.textContent = ""; // unknown in realtime; filled by polling list if provided

        const newCell = document.createElement("td");
        newCell.textContent = ""; // unknown in realtime; filled by polling list if provided

        const descCell = document.createElement("td");
        descCell.textContent = evt.details || "";

        return [idCell, tsCell, userIdCell, actionCell, assetIdCell, prevCell, newCell, descCell];
    }

    function renderAuditEvent(evt) {
        if (!evt) return;
        const k = keyOf(evt);
        const existing = k && seen.get(k);

        if (existing) {
            // update in place
            const cells = buildCells(evt);
            // replace cells except keep same <tr> to preserve references
            while (existing.firstChild) existing.removeChild(existing.firstChild);
            cells.forEach(c => existing.appendChild(c));
            flash(existing);
            return;
        }

        // create new row at top
        const tr = document.createElement("tr");
        const cells = buildCells(evt);
        cells.forEach(c => tr.appendChild(c));

        tbody.insertBefore(tr, tbody.firstChild);
        flash(tr);

        if (k) seen.set(k, tr);
        // advance cursor to newest timestamp
        if (evt.occurredAtUtc) {
            try {
                const n = new Date(evt.occurredAtUtc).toISOString();
                if (n > sinceCursor) sinceCursor = n;
            } catch { /* ignore */ }
        }
    }

    function flash(tr) {
        tr.classList.remove("row-flash");
        // force reflow so re-adding the class retriggers animation
        void tr.offsetWidth;
        tr.classList.add("row-flash");
        setTimeout(() => tr.classList.remove("row-flash"), 1500);
    }

    async function fetchSince() {
        const url = `/api/audit/events?since=${encodeURIComponent(sinceCursor)}&take=${MAX_BATCH}`;
        const res = await fetch(url, {
            headers: { "Accept": "application/json" },
            signal: aborter.signal
        });

        if (res.status === 304) return; // ETag hit

        // Handle throttling (429)
        if (res.status === 429) {
            backoffMs = Math.min(backoffMs ? backoffMs * 2 : POLL_MS, 30000); // cap at 30s
            // eslint-disable-next-line no-console
            console.warn(`[audit] throttled; backing off ${backoffMs}ms`);
            stopPolling();
            setTimeout(startPolling, backoffMs);
            return;
        }

        if (!res.ok) throw new Error(`Polling failed: ${res.status}`);
        backoffMs = 0; // reset on success

        const data = await res.json();
        if (Array.isArray(data.items)) {
            // newest first; render newest first to preserve desc order
            data.items.forEach(renderAuditEvent);
        }
        if (data.nextSince) sinceCursor = data.nextSince;
    }

    function startPolling() {
        if (!FEATURE_POLLING) return;
        if (pollTimer) clearInterval(pollTimer);
        pollTimer = setInterval(async () => {
            try {
                await fetchSince();
            } catch (e) {
                // simple backoff is handled inside fetchSince for 429; other errors just log
                // eslint-disable-next-line no-console
                console.warn("[audit] polling error", e);
            }
        }, POLL_MS);
    }

    function stopPolling() {
        if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
    }

    // Resync on tab becoming visible again
    document.addEventListener("visibilitychange", async () => {
        if (document.visibilityState === "visible") {
            try { await fetchSince(); } catch { /* ignore */ }
        }
    });

    // Cancel timers + in-flight fetches on page unload
    window.addEventListener("beforeunload", () => {
        stopPolling();
        try { aborter.abort(); } catch { /* ignore */ }
    });

    // ---- Initial sync (get baseline recent items) -----------------------
    (async () => {
        try {
            await fetchSince();
        } catch (e) {
            // eslint-disable-next-line no-console
            console.warn("[audit] initial fetch failed", e);
        }
    })();

    // ---- SignalR wiring with automatic reconnect ------------------------
    async function wireSignalR() {
        if (!FEATURE_REALTIME || !window.signalR || !window.signalR.HubConnectionBuilder) {
            startPolling(); // fallback if no library or disabled
            return;
        }

        const connection = new window.signalR.HubConnectionBuilder()
            .withUrl("/hubs/audit")
            .withAutomaticReconnect()
            .build();

        connection.on("auditEvent", (evt) => {
            renderAuditEvent(evt);
        });

        connection.onreconnecting(() => {
            // eslint-disable-next-line no-console
            console.log("[audit] reconnecting…");
            // keep polling while reconnecting
            startPolling();
        });

        connection.onreconnected(async () => {
            // eslint-disable-next-line no-console
            console.log("[audit] reconnected");
            // stop polling once signalR is healthy again
            stopPolling();
            // fill any gaps
            try { await fetchSince(); } catch { /* ignore */ }
        });

        connection.onclose(() => {
            // eslint-disable-next-line no-console
            console.log("[audit] connection closed");
            startPolling();
        });

        try {
            await connection.start();
            // eslint-disable-next-line no-console
            console.log("[audit] SignalR connected");
            stopPolling(); // prefer realtime when connected
        } catch (e) {
            // eslint-disable-next-line no-console
            console.warn("[audit] SignalR start failed, using polling", e);
            startPolling();
        }
    }

    wireSignalR();
})();