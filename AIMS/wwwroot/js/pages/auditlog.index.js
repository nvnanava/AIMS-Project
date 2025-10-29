/* ======================================================================
   AIMS Script: auditlog.index.js
   ----------------------------------------------------------------------
   Purpose
   - Client-side behavior for AuditLog/Index:
     * Toggle filter dropdown
     * Free-text search across table rows
     * Action filter (Assign/Create/Update/Delete/Unassign/All)
     * Client-side pagination (Search pager styling)  ← kept
   - Realtime + Resilience
     * SignalR with polling fallback
     * 30-day sinceCursor seed
     * First-paint fallback to /api/audit/events/latest
     * Exponential backoff on 5xx; never clears existing rows
     * Uses the SAME spinner as Search: window.GlobalSpinner
   - UX
     * Only ellipsed cells get native tooltips (no “help” cursor)
     * Row click opens read-only modal with scrollable Description
   ====================================================================== */

(() => {
    "use strict";

    // ----- DOM helpers ---------------------------------------------------
    const $ = (sel) => document.querySelector(sel);
    const $all = (sel) => Array.from(document.querySelectorAll(sel));

    // ----- Filter dropdown ------------------------------------------------
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
        if (!container.contains(evt.target) && dd.classList.contains("show")) setDropdownOpen(false);
    }

    // ----- Filtering ------------------------------------------------------
    function visibleRows() {
        // support both legacy .audit-log-table and admin chrome .admin-table-body
        return $all(".audit-log-table tbody tr, .admin-table-body tbody tr").filter(r => r.style.display !== "none");
    }
    function filterByFreeText(term) {
        const rows = $all(".audit-log-table tbody tr, .admin-table-body tbody tr");
        const q = (term || "").toLowerCase();
        rows.forEach(row => {
            const text = row.innerText.toLowerCase();
            row.style.display = text.includes(q) ? "" : "none";
        });
        // filtering should reset to first page
        window.AuditPager.applyPagination(true);
    }
    function filterByAction(action) {
        const rows = $all(".audit-log-table tbody tr, .admin-table-body tbody tr");
        const wanted = (action || "All").trim();
        rows.forEach(row => {
            const cell = row.cells?.[3]; // Action column
            const current = (cell?.innerText || "").trim();
            row.style.display = (wanted === "All" || current === wanted) ? "" : "none";
        });
        // filtering should reset to first page
        window.AuditPager.applyPagination(true);
    }

    // ----- Pagination (client-side) --------------------------------------
    const pager = document.getElementById("audit-pager");
    const btnPrev = document.getElementById("pg-prev") || document.getElementById("audit-pg-prev");
    const btnNext = document.getElementById("pg-next") || document.getElementById("audit-pg-next");
    const lblStatus = document.getElementById("pg-status") || document.getElementById("audit-pg-status");

    let currentPage = 1;
    let pageSize = 10;

    function pageCount(total) { return Math.max(1, Math.ceil(total / pageSize)); }

    // Helper: trigger flash if a row *just became visible* and requested to flash
    function triggerDeferredFlashIfNeeded(tr) {
        if (tr.classList.contains("pending-flash")) {
            tr.classList.remove("pending-flash");
            tr.classList.remove("row-flash");
            void tr.offsetWidth; // reflow
            tr.classList.add("row-flash");
            setTimeout(() => tr.classList.remove("row-flash"), 1500);
        }
    }

    function renderCurrentPage() {
        const rows = visibleRows();
        const total = rows.length;
        const totalPages = pageCount(total);

        if (currentPage > totalPages) currentPage = totalPages;
        if (currentPage < 1) currentPage = 1;

        const start = (currentPage - 1) * pageSize;
        const end = start + pageSize;

        let idx = 0;
        rows.forEach(r => {
            const shouldShow = idx >= start && idx < end;
            r.hidden = !shouldShow;
            if (shouldShow) triggerDeferredFlashIfNeeded(r);
            idx++;
        });

        if (pager && lblStatus && btnPrev && btnNext) {
            pager.hidden = (total === 0);
            lblStatus.textContent = `Page ${currentPage} of ${totalPages}`;
            btnPrev.disabled = currentPage <= 1;
            btnNext.disabled = currentPage >= totalPages;
        }
    }

    function applyPagination(resetToFirst = false) {
        $all(".audit-log-table tbody tr, .admin-table-body tbody tr").forEach(r => { r.hidden = false; });
        if (resetToFirst) currentPage = 1;
        renderCurrentPage();
    }

    // Expose a tiny pager API so realtime can re-page WITHOUT resetting to page 1
    window.AuditPager = {
        get currentPage() { return currentPage; },
        set currentPage(v) { currentPage = Math.max(1, +v || 1); renderCurrentPage(); },
        setPageSize(n) { pageSize = Math.max(1, +n || 10); renderCurrentPage(); },
        renderCurrentPage,
        applyPagination
    };

    btnPrev?.addEventListener("click", () => { if (currentPage > 1) { currentPage--; renderCurrentPage(); } });
    btnNext?.addEventListener("click", () => { currentPage++; renderCurrentPage(); });

    // ----- Tooltips only for ellipsed cells ------------------------------
    function setSmartTooltip(td) {
        if (!td) return;
        const hasOverflow = td.scrollWidth > td.clientWidth;
        if (hasOverflow) td.title = td.textContent.trim();
        else td.removeAttribute("title");
    }
    function refreshVisibleTooltips() {
        const rows = $all(".admin-table-body tbody tr:not([hidden])");
        rows.forEach(tr => {
            Array.from(tr.cells).forEach(setSmartTooltip);
        });
    }
    window.addEventListener("resize", () => { refreshVisibleTooltips(); });

    // ----- Row details modal (read-only) ---------------------------------
    function openRowModalFromTR(tr) {
        if (!tr) return;
        const cells = Array.from(tr.cells).map(td => (td.textContent || "").trim());

        document.getElementById("ar-id").textContent = cells[0] || "—";
        document.getElementById("ar-ts").textContent = cells[1] || "—";
        document.getElementById("ar-user").textContent = cells[2] || "—";
        document.getElementById("ar-action").textContent = cells[3] || "—";
        document.getElementById("ar-asset").textContent = cells[4] || "—";
        document.getElementById("ar-prev").textContent = cells[5] || "—";
        document.getElementById("ar-new").textContent = cells[6] || "—";
        document.getElementById("ar-desc").value = cells[7] || "";

        if (window.bootstrap?.Modal) {
            const m = bootstrap.Modal.getOrCreateInstance(document.getElementById("auditRowModal"));
            m.show();
        }
    }

    document.addEventListener("DOMContentLoaded", () => {
        const tbody = document.getElementById("auditTableBody");
        if (tbody) {
            tbody.addEventListener("click", (e) => {
                const tr = e.target?.closest("tr");
                if (!tr) return;
                openRowModalFromTR(tr);
            });
        }
    });

    // ----- Wire filter UI -------------------------------------------------
    window.addEventListener("DOMContentLoaded", () => {
        const btnToggle = document.getElementById("filter-button-toggle");
        const dd = document.getElementById("filterDropdown");
        const txtSearch = document.getElementById("filterSearchInput");

        btnToggle?.addEventListener("click", (e) => { e.preventDefault(); toggleDropdown(); });
        txtSearch?.addEventListener("keyup", function () { filterByFreeText(this.value); });

        $all(".dropdown-item").forEach(a => {
            a.addEventListener("click", (e) => {
                e.preventDefault();
                const picked = a.getAttribute("data-action");
                filterByAction(picked);
                setDropdownOpen(false);
            });
        });

        document.addEventListener("click", closeDropdownIfClickAway);
        document.addEventListener("keydown", (e) => { if (e.key === "Escape") setDropdownOpen(false); });
        if (dd) setDropdownOpen(false);

        applyPagination(true);
    });

    // Expose for realtime to refresh tooltips after re-render
    window.__Audit_RefreshVisibleTooltips = refreshVisibleTooltips;
})();

/* ======================================================================
   ★ AIMS Realtime: resilient polling + quiet status banner
   - Quiet period after success to avoid banner flicker
   - Show banner only if: table empty OR prolonged failures
   - Re-page only when rows actually changed
   - Only flash rows for events AFTER initial seed
   - After each render/update, recompute "smart tooltips"
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

    // Spinner parity with Search (initial only)
    const MIN_SPINNER_MS = 500;
    let spinnerShownAt = 0;
    function showSpinner() { if (window.GlobalSpinner?.show) { spinnerShownAt = performance.now(); GlobalSpinner.show(); } }
    async function hideSpinner() {
        if (window.GlobalSpinner?.hide) {
            const elapsed = performance.now() - spinnerShownAt;
            const wait = Math.max(0, MIN_SPINNER_MS - elapsed);
            if (wait) await new Promise(r => setTimeout(r, wait));
            GlobalSpinner.hide();
        }
    }

    const seen = new Map();
    let sinceCursor = new Date(Date.now() - 30 * 24 * 3600 * 1000).toISOString(); // 30 days
    let seeded = false;                 // ← key: initial backfill done?
    let pollTimeout = null;
    let backoff = POLL_MS;
    const MAX_BACKOFF = 30000;

    // throttled banner
    const QUIET_MS = 15000;
    const FAILS_BEFORE_BANNER = 2;
    let lastSuccessAt = 0;
    let consecutiveFailures = 0;

    const aborter = new AbortController();
    const now = () => Date.now();                               // ← keep single definition
    const haveRows = () => tbody.querySelector("tr") !== null;  // ← keep single definition

    // ---------- Banner ----------
    function ensureBanner() {
        let el = document.getElementById("audit-status-banner");
        if (!el) {
            el = document.createElement("div");
            el.id = "audit-status-banner";
            el.style.cssText = "margin:8px 0;padding:6px 10px;border:1px solid #f2c97d;background:#fff7e6;font-size:.9rem;border-radius:6px;display:none;";
            const container = document.querySelector(".audit-log-table")?.parentElement
                || document.querySelector(".admin-table-chrome")
                || document.body;
            container.insertBefore(el, container.firstChild);
        }
        return el;
    }
    function shouldShowStatus() {
        const quiet = (now() - lastSuccessAt) < QUIET_MS;
        if (!haveRows()) return true;
        if (quiet) return false;
        return consecutiveFailures >= FAILS_BEFORE_BANNER;
    }
    function showStatus(msg) {
        if (!shouldShowStatus()) return;
        const el = ensureBanner();
        el.textContent = msg || "";
        el.style.display = msg ? "" : "none";
    }
    function hideStatus() {
        const el = document.getElementById("audit-status-banner");
        if (el) el.style.display = "none";
    }

    // ---------- Robust Time Formatting (handles naive + microseconds) ----------
    function fmtLocal(dt) {
        try {
            let s = String(dt ?? "").trim();
            if (!s) return "—";

            const hasT = s.includes("T");
            const hasTZ = /[Zz]$|[+\-]\d{2}:?\d{2}$/.test(s);

            if (hasT && !hasTZ) {
                s = s
                    .replace(/(\.\d{3})\d+$/, "$1")
                    .replace(/\.([\d]{1,2})$/, (m, g1) => "." + g1.padEnd(3, "0"))
                    .replace(/(T\d{2}:\d{2}:\d{2})(?!\.)/, "$1.000")
                    + "Z";
            }

            const d = new Date(s);
            if (isNaN(d)) return String(dt);
            return new Intl.DateTimeFormat("en-US", {
                dateStyle: "medium",
                timeStyle: "short",
                timeZone: "America/Los_Angeles"
            }).format(d);
        } catch {
            return String(dt ?? "");
        }
    }

    function keyOf(evt) { return evt?.id || evt?.hash || ""; }

    // Create <td> helper (no title by default; tooltips set later if ellipsed)
    function makeTD(text) {
        const c = document.createElement("td");
        c.textContent = (text == null) ? "" : String(text);
        return c;
    }

    // Recompute tooltip eligibility for a given row
    function refreshTooltipsForRow(tr) {
        if (!tr) return;
        Array.from(tr.cells).forEach(td => {
            const hasOverflow = td.scrollWidth > td.clientWidth;
            if (hasOverflow) td.title = td.textContent.trim();
            else td.removeAttribute("title");
        });
    }

    // returns true if row content changed (to trigger re-page)
    // opts: { seed:boolean } — when seeding initial data, do NOT flash
    function renderAuditEvent(evt, opts = {}) {
        if (!evt) return false;
        const seed = !!opts.seed;
        const k = keyOf(evt);
        const existing = k && seen.get(k);

        const userM = /\((\d+)\)\s*$/.exec(evt.user || "");
        const userTxt = userM ? `U${userM[1]}` : (evt.user || "—");
        const cells = () => ([
            makeTD(evt.id || evt.hash?.slice(0, 8) || "—"),
            makeTD(fmtLocal(evt.occurredAtUtc)),
            makeTD(userTxt),
            makeTD(evt.type || "—"),
            makeTD(evt.target || "—"),
            makeTD(""), // prev
            makeTD(""), // new
            makeTD(evt.details || "")
        ]);

        if (existing) {
            while (existing.firstChild) existing.removeChild(existing.firstChild);
            cells().forEach(c => existing.appendChild(c));
            refreshTooltipsForRow(existing); // tooltips for overflow only

            if (!seed) {
                if (!existing.hidden) {
                    existing.classList.remove("row-flash"); void existing.offsetWidth; existing.classList.add("row-flash");
                    setTimeout(() => existing.classList.remove("row-flash"), 1500);
                } else {
                    existing.classList.add("pending-flash");
                }
            }
            return true;
        }

        const tr = document.createElement("tr");
        cells().forEach(c => tr.appendChild(c));
        tbody.insertBefore(tr, tbody.firstChild);

        refreshTooltipsForRow(tr);      // tooltips only for ellipsed cells
        if (!seed) tr.classList.add("pending-flash");

        if (k) seen.set(k, tr);
        if (evt.occurredAtUtc) {
            try {
                const n = new Date(evt.occurredAtUtc).toISOString();
                if (n > sinceCursor) sinceCursor = n;
            } catch { /* ignore */ }
        }
        return true;
    }

    // Re-page *without* resetting to page 1
    function rePageIfChanged(changed) {
        if (!changed) return;
        if (window.AuditPager?.renderCurrentPage) {
            window.AuditPager.renderCurrentPage();
            window.__Audit_RefreshVisibleTooltips?.(); // recompute visible tooltips
        }
    }

    async function fetchLatestPageFallback(seedMode = false) {
        try {
            const res = await fetch(`/api/audit/events/latest?take=${MAX_BATCH}`, {
                headers: { "Accept": "application/json" },
                signal: aborter.signal
            });
            if (!res.ok) return false;
            const data = await res.json();
            if (!data || !Array.isArray(data.items)) return false;

            let anyChanged = false;
            data.items.forEach(evt => { anyChanged = renderAuditEvent(evt, { seed: seedMode }) || anyChanged; });
            rePageIfChanged(anyChanged);

            if (data.nextSince) sinceCursor = data.nextSince;
            if (tbody.querySelector("tr")) { onSuccess(); }
            return true;
        } catch {
            return false;
        }
    }

    async function fetchEventsSince(seedMode = false) {
        const url = `/api/audit/events?since=${encodeURIComponent(sinceCursor)}&take=${MAX_BATCH}`;
        const res = await fetch(url, { headers: { "Accept": "application/json" }, signal: aborter.signal });

        if (res.status === 304) { onSuccess(); return { ok: true, changed: false }; }
        if (res.status === 429) return { ok: false, retry: "rate" };
        if (res.status >= 500) return { ok: false, retry: "server" };
        if (!res.ok) return { ok: false, retry: "client", code: res.status };

        const data = await res.json();
        let anyChanged = false;
        if (Array.isArray(data.items) && data.items.length > 0) {
            data.items.forEach(evt => { anyChanged = renderAuditEvent(evt, { seed: seedMode }) || anyChanged; });
            rePageIfChanged(anyChanged);
        }
        if (data.nextSince) sinceCursor = data.nextSince;
        onSuccess();
        return { ok: true, changed: anyChanged };
    }

    function onSuccess() {
        lastSuccessAt = now();
        consecutiveFailures = 0;
        hideStatus();
        backoff = POLL_MS;
    }

    function scheduleNext(success) {
        if (pollTimeout) clearTimeout(pollTimeout);
        const delay = success ? POLL_MS : Math.min(backoff = Math.min((backoff || POLL_MS) * 2, MAX_BACKOFF), MAX_BACKOFF);
        pollTimeout = setTimeout(() => pollCycle(), delay);
    }

    async function pollCycle() {
        try {
            const res = await fetchEventsSince(false); // normal mode after seed
            if (res.ok) { scheduleNext(true); return; }

            consecutiveFailures++;

            if (!seeded) {
                showStatus("Server warming up; loading latest audit entries…");
                const seededOk = await fetchLatestPageFallback(true /* seed mode: no flashing */);
                if (seededOk) { seeded = true; scheduleNext(true); return; }
            } else {
                if (shouldShowStatus()) {
                    if (res.retry === "rate") showStatus("Rate limited; retrying…");
                    else if (res.retry === "server") showStatus("Server warming up; retrying…");
                    else showStatus(`Error ${res.code || ""}; retrying…`.trim());
                }
            }
            scheduleNext(false);
        } catch {
            consecutiveFailures++;
            if (!seeded) {
                showStatus("Reconnecting to audit log…");
                const ok = await fetchLatestPageFallback(true /* seed mode */);
                if (ok) { seeded = true; scheduleNext(true); return; }
            } else {
                if (shouldShowStatus()) showStatus("Network error; retrying…");
            }
            scheduleNext(false);
        }
    }

    // Visibility nudge
    document.addEventListener("visibilitychange", async () => {
        if (document.visibilityState === "visible") {
            try { const res = await fetchEventsSince(false); if (res.ok) hideStatus(); } catch { /* ignore */ }
        }
    });

    window.addEventListener("beforeunload", () => { if (pollTimeout) clearTimeout(pollTimeout); try { aborter.abort(); } catch { } });

    // Initial load (spinner just for first paint) + SignalR wiring
    (async () => {
        showSpinner();
        try {
            const res = await fetchEventsSince(true); // seed mode: NO flashing
            if (!res.ok) {
                const ok = await fetchLatestPageFallback(true);
                if (!ok && !tbody.querySelector("tr")) showStatus("Unable to load audit log. Retrying…");
            }
            seeded = true; // from here on, new items may flash
        } catch {
            if (!tbody.querySelector("tr")) showStatus("Unable to load audit log. Retrying…");
        } finally {
            await hideSpinner();
        }

        if (FEATURE_REALTIME && window.signalR && window.signalR.HubConnectionBuilder) {
            try {
                const connection = new window.signalR.HubConnectionBuilder()
                    .withUrl("/hubs/audit", {
                        transport: window.signalR.HttpTransportType.WebSockets | window.signalR.HttpTransportType.LongPolling,
                        withCredentials: true
                    })
                    .withAutomaticReconnect({
                        nextRetryDelayInMilliseconds: retryContext => {
                            const seq = [0, 2000, 5000, 10000, 10000, 10000];
                            return seq[Math.min(retryContext.previousRetryCount + 1, seq.length - 1)];
                        }
                    })
                    .build();

                connection.on("auditEvent", evt => {
                    const changed = renderAuditEvent(evt, { seed: false });
                    rePageIfChanged(changed);
                    hideStatus();
                });

                connection.onreconnected(async () => {
                    try { const r = await fetchEventsSince(false); if (r.ok) hideStatus(); } catch { }
                });

                await connection.start();
            } catch (e) {
                console.warn("[audit] SignalR start failed; continuing with polling.", e);
            }
        }

        scheduleNext(true);
    })();
})();