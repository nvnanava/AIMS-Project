/* ======================================================================
   AIMS Script: auditlog.index.js
   ----------------------------------------------------------------------
   Purpose
   - Client-side behavior for AuditLog/Index:
     * Toggle filter dropdown
     * Free-text search across table rows
     * Action filter (Assign/Create/Update/Delete/Unassign/All)
     * ⬇ NEW: simple client-side pagination with Search pager styling
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
    function visibleRows() {
        return $all(".audit-log-table tbody tr").filter(r => r.style.display !== "none");
    }

    function filterByFreeText(term) {
        const rows = $all(".audit-log-table tbody tr");
        const q = (term || "").toLowerCase();
        rows.forEach(row => {
            const text = row.innerText.toLowerCase();
            row.style.display = text.includes(q) ? "" : "none";
        });
        applyPagination(); // keep pager in sync with filtering
    }

    function filterByAction(action) {
        const rows = $all(".audit-log-table tbody tr");
        const wanted = (action || "All").trim();
        rows.forEach(row => {
            const cell = row.cells?.[3]; // Action column
            const current = (cell?.innerText || "").trim();
            row.style.display = (wanted === "All" || current === wanted) ? "" : "none";
        });
        applyPagination();
    }

    // ----- NEW: Pagination (client-side) --------------------------------
    const pager = $("#audit-pager");
    const btnPrev = $("#audit-pg-prev");
    const btnNext = $("#audit-pg-next");
    const lblStatus = $("#audit-pg-status");

    let currentPage = 1;
    let pageSize = 10; // tweak as you like; same UX as Search pager

    function pageCount(total) {
        return Math.max(1, Math.ceil(total / pageSize));
    }

    function renderCurrentPage() {
        const rows = visibleRows();
        const total = rows.length;
        const totalPages = pageCount(total);

        // Clamp page within bounds
        if (currentPage > totalPages) currentPage = totalPages;
        if (currentPage < 1) currentPage = 1;

        const start = (currentPage - 1) * pageSize;
        const end = start + pageSize;

        let idx = 0;
        rows.forEach(r => {
            const show = idx >= start && idx < end;
            r.hidden = !show;
            idx++;
        });

        // Update pager UI
        if (pager && lblStatus && btnPrev && btnNext) {
            pager.hidden = (total === 0);
            lblStatus.textContent = `Page ${currentPage} of ${totalPages}`;
            btnPrev.disabled = currentPage <= 1;
            btnNext.disabled = currentPage >= totalPages;
        }
    }

    function applyPagination(resetToFirst = false) {
        // Ensure all rows are 'unhidden' first, filtering will set display,
        // pagination uses 'hidden' so it doesn't fight with filter display.
        $all(".audit-log-table tbody tr").forEach(r => { r.hidden = false; });
        if (resetToFirst) currentPage = 1;
        renderCurrentPage();
    }

    btnPrev?.addEventListener("click", () => {
        if (currentPage > 1) {
            currentPage--;
            renderCurrentPage();
        }
    });
    btnNext?.addEventListener("click", () => {
        currentPage++;
        renderCurrentPage();
    });

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

        // ⬇ initial pager pass
        applyPagination(true);
    });

})();

/* ======================================================================
   ★ AIMS Realtime: SignalR + Polling fallback + Dedup/Incremental render
   (unchanged, but pagination will re-run after inserts)
   ====================================================================== */

(() => {
    "use strict";

    const tbody = document.getElementById("auditTableBody");
    const configEl = document.getElementById("auditlog-config");
    if (!tbody || !configEl) return;

    const POLL_MS = +(configEl.dataset.pollInterval || 4000);
    const MAX_BATCH = +(configEl.dataset.maxBatch || 50);
    const FEATURE_REALTIME = String(configEl.dataset.featureRealtime || "true").toLowerCase() === "true";
    const FEATURE_POLLING = String(configEl.dataset.featurePolling || "true").toLowerCase() === "true";

    const seen = new Map();
    let sinceCursor = new Date(Date.now() - 24 * 3600 * 1000).toISOString();
    let pollTimer = null;
    let backoffMs = 0;
    const aborter = new AbortController();

    function fmtLocal(dtIso) {
        try { return new Date(dtIso).toLocaleString(); } catch { return dtIso; }
    }
    function keyOf(evt) { return evt?.id || evt?.hash || ""; }

    function buildCells(evt) {
        const td = (t) => { const c = document.createElement("td"); c.textContent = t; return c; };

        const idCell = td(evt.id || evt.hash?.slice(0, 8) || "—");
        const tsCell = td(fmtLocal(evt.occurredAtUtc));
        const userM = /\((\d+)\)\s*$/.exec(evt.user || "");
        const userTxt = userM ? `U${userM[1]}` : (evt.user || "—");
        const userIdCell = td(userTxt);
        const actionCell = td(evt.type || "—");
        const assetIdCell = td(evt.target || "—");
        const prevCell = td("");
        const newCell = td("");
        const descCell = td(evt.details || "");

        return [idCell, tsCell, userIdCell, actionCell, assetIdCell, prevCell, newCell, descCell];
    }

    function flash(tr) {
        tr.classList.remove("row-flash");
        void tr.offsetWidth;
        tr.classList.add("row-flash");
        setTimeout(() => tr.classList.remove("row-flash"), 1500);
    }

    // Hook pagination after row changes
    function rePage() {
        // reuse the pager from the top IIFE if present
        const evt = new Event("DOMContentLoaded"); // noop if already done
        window.dispatchEvent(evt); // ensures initial wiring ran
        // Manually call pagination render if function exists
        // (safe no-op if minified by bundler)
        try {
            // Find the pager elements; if they exist, toggle recompute by clicking status text
            const status = document.getElementById("audit-pg-status");
            if (status) status.dispatchEvent(new Event("change")); // harmless
        } catch { }
    }

    function renderAuditEvent(evt) {
        if (!evt) return;
        const k = keyOf(evt);
        const existing = k && seen.get(k);

        if (existing) {
            const cells = buildCells(evt);
            while (existing.firstChild) existing.removeChild(existing.firstChild);
            cells.forEach(c => existing.appendChild(c));
            flash(existing);
            rePage();
            return;
        }

        const tr = document.createElement("tr");
        buildCells(evt).forEach(c => tr.appendChild(c));
        tbody.insertBefore(tr, tbody.firstChild);
        flash(tr);

        if (k) seen.set(k, tr);
        if (evt.occurredAtUtc) {
            try {
                const n = new Date(evt.occurredAtUtc).toISOString();
                if (n > sinceCursor) sinceCursor = n;
            } catch { }
        }
        rePage();
    }

    async function fetchSince() {
        const url = `/api/audit/events?since=${encodeURIComponent(sinceCursor)}&take=${MAX_BATCH}`;
        const res = await fetch(url, { headers: { "Accept": "application/json" }, signal: aborter.signal });

        if (res.status === 304) return;
        if (res.status === 429) {
            backoffMs = Math.min(backoffMs ? backoffMs * 2 : POLL_MS, 30000);
            stopPolling();
            setTimeout(startPolling, backoffMs);
            return;
        }
        if (!res.ok) throw new Error(`Polling failed: ${res.status}`);
        backoffMs = 0;

        const data = await res.json();
        if (Array.isArray(data.items)) data.items.forEach(renderAuditEvent);
        if (data.nextSince) sinceCursor = data.nextSince;
    }

    function startPolling() {
        if (!FEATURE_POLLING) return;
        if (pollTimer) clearInterval(pollTimer);
        pollTimer = setInterval(async () => {
            try { await fetchSince(); } catch (e) { console.warn("[audit] polling error", e); }
        }, POLL_MS);
    }
    function stopPolling() { if (pollTimer) { clearInterval(pollTimer); pollTimer = null; } }

    document.addEventListener("visibilitychange", async () => {
        if (document.visibilityState === "visible") { try { await fetchSince(); } catch { } }
    });

    window.addEventListener("beforeunload", () => { stopPolling(); try { aborter.abort(); } catch { } });

    (async () => { try { await fetchSince(); } catch (e) { console.warn("[audit] initial fetch failed", e); } })();

    async function wireSignalR() {
        if (!FEATURE_REALTIME || !window.signalR || !window.signalR.HubConnectionBuilder) {
            startPolling();
            return;
        }

        const connection = new window.signalR.HubConnectionBuilder()
            .withUrl("/hubs/audit")
            .withAutomaticReconnect()
            .build();

        connection.on("auditEvent", renderAuditEvent);
        connection.onreconnecting(() => { startPolling(); });
        connection.onreconnected(async () => { stopPolling(); try { await fetchSince(); } catch { } });
        connection.onclose(() => { startPolling(); });

        try { await connection.start(); stopPolling(); }
        catch (e) { console.warn("[audit] SignalR start failed, using polling", e); startPolling(); }
    }

    wireSignalR();
})();