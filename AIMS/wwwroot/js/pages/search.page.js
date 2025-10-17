/* ============================================================================
   AIMS: Search page
   ---------------------------------------------------------------------------
   Purpose
   - Paged asset search with sticky header, toolbar filters, “show archived”
     toggle, and admin/supervisor assign/unassign via modals.

   Conventions
   - Namespace: AIMS.Search (public surface kept minimal)
   - Reads server-bootstrapped flags on window:
       __CAN_ADMIN__, __IS_SUPERVISOR__, __ASSETS_VER__
   - Calls API: /api/assets/search?q=&page=&pageSize=&showArchived=&_v=

   Notes
   - Indentation: 4 spaces
   - No inline JS in the view beyond tiny bootstrap variables
   ============================================================================ */

(() => {
    // ----- Namespace ---------------------------------------------------------
    window.AIMS = window.AIMS || {};
    AIMS.Search = AIMS.Search || {};

    // ----- DOM ---------------------------------------------------------------
    const tbody = document.getElementById("table-body");
    const empty = document.getElementById("search-empty");

    const pager = document.getElementById("search-pager");
    const btnPrev = document.getElementById("pg-prev");
    const btnNext = document.getElementById("pg-next");
    const lblStatus = document.getElementById("pg-status");

    // Filters (toolbar)
    const inputName = document.querySelector('[name="Asset Name"]');
    const selectType = document.querySelector('[name="Type"]');
    const inputTag = document.querySelector('[name="Tag #"]');
    const inputAssn = document.querySelector('[name="Assignment"]');
    const selectStat = document.querySelector('[name="Status"]');

    // Role flags
    const CAN_ADMIN = (window.__CAN_ADMIN__ === true || window.__CAN_ADMIN__ === "true");
    const IS_SUPERVISOR = (window.__IS_SUPERVISOR__ === true || window.__IS_SUPERVISOR__ === "true");

    // ----- Paging state ------------------------------------------------------
    let currentPage = 1;
    let pageSize = 5; // keep in sync with default on server
    let lastResultMeta = null;  // { total, page, pageSize }
    let lastQuery = null;

    // ----- Show archived state ----------------------------------------------
    const showArchivedSwitch = document.getElementById("showArchivedSwitch");
    let showArchived = false;
    if (showArchivedSwitch) {
        showArchivedSwitch.addEventListener("change", async () => {
            showArchived = showArchivedSwitch.checked;
            await fetchInitial(activeQuery(), 1, pageSize);
        });
    }

    // ----- Scoped CSS attr (Blazor/razor isolation friendly) ----------------
    const scopeAttrName = (() => {
        const maybe = (host) => {
            if (!host) return null;
            for (const a of host.attributes) {
                if (/^b-[a-z0-9]+$/i.test(a.name)) return a.name;
            }
            return null;
        };
        return maybe(document.querySelector(".asset-table")) ||
            maybe(document.querySelector(".table-container")) ||
            null;
    })();
    const applyScope = el => { if (scopeAttrName) el.setAttribute(scopeAttrName, ""); };

    // ----- URL / initial query ----------------------------------------------
    const params = new URLSearchParams(window.location.search);
    const initialQ = (params.get("searchQuery") || "").trim();

    // ----- Helpers -----------------------------------------------------------
    const clearBody = () => { while (tbody.firstChild) tbody.removeChild(tbody.firstChild); };
    const setEmpty = (isEmpty) => { empty.hidden = !isEmpty; };

    // Zebra striping on visible rows
    function applyZebra() {
        if (!tbody) return;
        const rows = Array.from(tbody.querySelectorAll(":scope > tr"));
        let i = 0;
        rows.forEach(tr => {
            const hidden = tr.hidden || tr.style.display === "none";
            tr.classList.remove("zebra-even", "zebra-odd");
            if (hidden) return;
            tr.classList.add((i++ % 2 === 0) ? "zebra-even" : "zebra-odd");
        });
    }
    if (tbody) {
        const mo = new MutationObserver(applyZebra);
        mo.observe(tbody, { childList: true, subtree: false, attributes: true, attributeFilter: ["style", "hidden"] });
        window.addEventListener("load", applyZebra);
    }

    function ensureNoResultsRow() {
        let r = document.getElementById("no-results-row");
        if (!r) {
            r = document.createElement("tr");
            r.id = "no-results-row";
            applyScope(r);
            const td = document.createElement("td");
            applyScope(td);
            td.colSpan = 5;
            td.className = "no-results-row";
            td.textContent = "No matching results found";
            r.appendChild(td);
            tbody.appendChild(r);
        }
        r.style.display = "none";
        return r;
    }

    // Assignment cell (includes 👤 button for Admin/Supervisor)
    function renderAssignmentCell(row) {
        const displayName = (row.assignedEmployeeNumber && row.assignedTo)
            ? `${row.assignedTo} (${row.assignedEmployeeNumber})`
            : (row.assignedTo || "Unassigned");

        const currentUserId = (row.assignedUserId ?? row.assignedEmployeeNumber ?? "") + "";

        const span = document.createElement("span");
        span.className = "assigned-name";
        span.dataset.userId = currentUserId;
        span.textContent = displayName;
        applyScope(span);

        const canShowButton = CAN_ADMIN || IS_SUPERVISOR;
        if (!canShowButton) {
            const frag = document.createDocumentFragment();
            frag.appendChild(span);
            return frag;
        }

        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = "assign-btn";
        btn.setAttribute("data-testid", "assign-user-btn");

        // normalize API ids
        const swId = row.softwareID ?? row.SoftwareID ?? row.softwareId ?? null;
        const hwId = row.hardwareID ?? row.HardwareID ?? row.hardwareId ?? null;
        const numericId = swId ?? hwId;
        const kind = (swId != null) ? 2 : 1; // 1=hardware, 2=software

        btn.dataset.assetTag = (row.tag ?? "") + "";                     // UI targeting (Tag #)
        btn.dataset.assetNumericId = (numericId != null ? String(numericId) : "");
        btn.dataset.assetKind = String(kind);
        btn.dataset.currentUserId = currentUserId;

        btn.title = "Assign / change user";
        btn.setAttribute("aria-label", `Assign user to ${row.assetName || "asset"}`);
        btn.textContent = "👤";
        applyScope(btn);

        const wrapper = document.createDocumentFragment();
        wrapper.appendChild(span);
        wrapper.appendChild(document.createTextNode(" "));
        wrapper.appendChild(btn);
        return wrapper;
    }

    function renderRows(items) {
        clearBody();
        for (const row of (items || [])) {
            const tr = document.createElement("tr");
            tr.className = "result";

            // Deep-link to AssetDetails if admin/helpdesk
            const canDeepLink = !!CAN_ADMIN;
            if (canDeepLink) {
                tr.classList.add("row-clickable");
                tr.style.cursor = "pointer";
                tr.title = "View details";
            } else {
                tr.setAttribute("aria-disabled", "true");
                tr.style.cursor = "default";
            }

            tr.setAttribute("applied-filters", "0");

            // data-* for quick lookups
            tr.dataset.tag = row.tag || "";
            tr.dataset.type = row.type || "";
            const swId = row.softwareID ?? row.SoftwareID ?? row.softwareId ?? null;
            const hwId = row.hardwareID ?? row.HardwareID ?? row.hardwareId ?? null;
            tr.dataset.assetId = String(swId ?? hwId ?? "");
            tr.dataset.status = (row.status || "-");
            applyScope(tr);

            const tdName = document.createElement("td");
            tdName.className = "col-name";
            tdName.textContent = row.assetName || "-";
            applyScope(tdName);

            const tdType = document.createElement("td");
            tdType.className = "col-type";
            tdType.textContent = row.type || "-";
            applyScope(tdType);

            const tdTag = document.createElement("td");
            tdTag.className = "col-tag";
            tdTag.textContent = row.tag || "-";
            applyScope(tdTag);

            const tdAssn = document.createElement("td");
            tdAssn.className = "col-assignment";
            tdAssn.appendChild(renderAssignmentCell(row));
            applyScope(tdAssn);

            const tdStat = document.createElement("td");
            tdStat.className = "col-status";
            tdStat.textContent = row.status || "-";
            applyScope(tdStat);

            tr.append(tdName, tdType, tdTag, tdAssn, tdStat);
            tbody.appendChild(tr);
        }
        ensureNoResultsRow();
        applyZebra();
    }

    // Click handling (delegated)
    tbody.addEventListener("click", (e) => {
        // person icon → open assign/unassign workflow
        const btn = e.target.closest(".assign-btn");
        if (btn) {
            e.preventDefault();
            e.stopPropagation();

            const rowEl = btn.closest("tr");
            const tag = rowEl?.dataset.tag || ""; // Tag #
            const numericId = btn.dataset.assetNumericId || "";
            const assetKind = Number(btn.dataset.assetKind || "1");

            const currentUserId = btn.dataset.currentUserId || "";
            const nameEl = rowEl ? rowEl.querySelector(".assigned-name") : null;
            const currentDisplayName = (nameEl?.textContent || "Unassigned").trim();

            const isAssigned = (rowEl?.dataset.status || "").toLowerCase() === "assigned"
                || (currentUserId && currentUserId !== "");

            if (isAssigned) {
                window.dispatchEvent(new CustomEvent("unassign:open", {
                    detail: {
                        currentUserId: currentUserId || null,
                        assetTag: tag || null,
                        assetNumericId: numericId || null,
                        assetKind
                    }
                }));
                return;
            }

            window.dispatchEvent(new CustomEvent("assign:open", {
                detail: {
                    assetTag: tag,
                    assetNumericId: numericId,
                    assetKind,
                    currentUserId,
                    currentDisplayName
                }
            }));
            return;
        }

        // row deep-link (admins/helpdesk only)
        if (!CAN_ADMIN) return;
        const tr = e.target.closest("tr.result");
        if (!tr) return;

        const tag = tr.dataset.tag || "";
        const type = (tr.dataset.type || "").trim();
        if (!tag) return;

        const url = new URL("/AssetDetails/Index", window.location.origin);
        url.searchParams.set("category", type || "Laptop");
        url.searchParams.set("tag", tag);
        window.location.href = url.toString();
    });

    // Client-side filtering (header toolbar)
    const headerFlags = {
        ["Asset Name"]: 1 << 0,
        ["Type"]: 1 << 1,
        ["Tag #"]: 1 << 2,
        ["Assignment"]: 1 << 3,
        ["Status"]: 1 << 4
    };
    const selectorByHeader = {
        ["Asset Name"]: ".col-name",
        ["Type"]: ".col-type",
        ["Tag #"]: ".col-tag",
        ["Assignment"]: ".col-assignment",
        ["Status"]: ".col-status"
    };
    const cellMatches = (cell, value) => {
        const v = (value || "").trim().toLowerCase();
        if (!v) return true;
        return (cell?.textContent || "").toLowerCase().includes(v);
    };
    function applyFilterFor(header, value) {
        const rows = tbody.querySelectorAll("tr.result");
        const noRow = ensureNoResultsRow();
        let shown = 0;
        rows.forEach(row => {
            const selector = selectorByHeader[header];
            const cell = selector ? row.querySelector(selector) : null;
            const prev = parseInt(row.getAttribute("applied-filters") || "0", 10);
            const flag = headerFlags[header];
            const fails = !cellMatches(cell, value);
            const next = fails ? (prev | flag) : (prev & ~flag);
            if (next !== prev) row.setAttribute("applied-filters", String(next));
            row.style.display = next !== 0 ? "none" : "";
            if (next === 0) shown++;
        });
        noRow.style.display = shown === 0 ? "" : "none";
        applyZebra();
    }
    inputName?.addEventListener("input", e => applyFilterFor("Asset Name", e.target.value));
    selectType?.addEventListener("change", e => applyFilterFor("Type", e.target.value));
    inputTag?.addEventListener("input", e => applyFilterFor("Tag #", e.target.value));
    inputAssn?.addEventListener("input", e => applyFilterFor("Assignment", e.target.value));
    selectStat?.addEventListener("change", e => applyFilterFor("Status", e.target.value));

    // ----- Loading min time --------------------------------------------------
    const LOCAL_MIN_VISIBLE_MS = 500;
    let localShownAt = 0;

    function startLoading() {
        localShownAt = performance.now();
        clearBody();
        setEmpty(false);
        // Global spinner assumed available (loaded in base)
        if (window.GlobalSpinner?.show) GlobalSpinner.show();
    }

    function waitForMinimum() {
        const elapsed = performance.now() - localShownAt;
        const wait = Math.max(0, LOCAL_MIN_VISIBLE_MS - elapsed);
        return new Promise(resolve => setTimeout(resolve, wait));
    }

    // ----- Pager helpers -----------------------------------------------------
    function updatePager() {
        if (!pager || !btnPrev || !btnNext || !lblStatus) return;

        const total = lastResultMeta?.total ?? 0;
        const page = lastResultMeta?.page ?? currentPage;
        const size = lastResultMeta?.pageSize ?? pageSize;

        const totalPages = Math.max(1, Math.ceil(total / size));
        const hasPrev = page > 1;
        const hasNext = page < totalPages;

        btnPrev.disabled = !hasPrev;
        btnNext.disabled = !hasNext;
        lblStatus.textContent = `Page ${page} of ${totalPages}`;

        pager.hidden = (total === 0);
    }

    btnPrev?.addEventListener("click", () => {
        if (currentPage > 1) {
            fetchInitial(lastQuery ?? activeQuery(), currentPage - 1, pageSize);
        }
    });
    btnNext?.addEventListener("click", () => {
        fetchInitial(lastQuery ?? activeQuery(), currentPage + 1, pageSize);
    });

    function activeQuery() {
        const p = new URLSearchParams(window.location.search);
        return (p.get("searchQuery") || "").trim();
    }

    // ----- Fetch -------------------------------------------------------------
    async function fetchInitial(q, page = 1, pageSizeArg = 25) {
        const isBlank = !q || q.trim() === "";

        if (isBlank && !IS_SUPERVISOR) {
            clearBody();
            setEmpty(true);
            if (pager) pager.hidden = true;
            return;
        }

        lastQuery = q ?? "";

        const url = new URL("/api/assets/search", window.location.origin);
        if (!isBlank) url.searchParams.set("q", q.trim());
        url.searchParams.set("page", String(page));
        url.searchParams.set("pageSize", String(pageSizeArg));
        url.searchParams.set("showArchived", showArchived ? "true" : "false");

        // Cache-bust: use server-stamped version or timestamp fallback
        const ver = (window.__ASSETS_VER__ ? String(window.__ASSETS_VER__) : String(Date.now()));
        url.searchParams.set("_v", ver);

        startLoading();

        try {
            const res = await fetch(url.toString(), {
                cache: "no-store",
                headers: { "Cache-Control": "no-cache, no-store" }
            });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();

            await waitForMinimum();

            renderRows(data.items || []);
            if (!data.items || data.items.length === 0) setEmpty(true);

            lastResultMeta = {
                total: typeof data.total === "number" ? data.total : -1,
                page: typeof data.page === "number" ? data.page : page,
                pageSize: typeof data.pageSize === "number" ? data.pageSize : pageSizeArg
            };
            currentPage = lastResultMeta.page;
            pageSize = lastResultMeta.pageSize;

            updatePager();
        } catch (e) {
            console.error("Search fetch failed:", e);
            clearBody();
            setEmpty(true);
            if (pager) pager.hidden = true;
        } finally {
            if (window.GlobalSpinner?.hide) GlobalSpinner.hide();
        }
    }

    // ----- Public refresh (for assign/unassign modules) ----------------------
    AIMS.Search.refresh = function refresh() {
        const q = (lastQuery ?? activeQuery() ?? "").trim();
        fetchInitial(q, currentPage, pageSize);
    };
    // Back-compat alias
    window.refreshSearchTable = () => AIMS.Search.refresh();

    // ----- Init --------------------------------------------------------------
    (async function init() {
        if (IS_SUPERVISOR) {
            await fetchInitial("", 1, pageSize);
            applyZebra();
            return;
        }
        if (initialQ.length > 0) {
            await fetchInitial(initialQ, 1, pageSize);
        } else {
            clearBody();
            setEmpty(true);
            if (pager) pager.hidden = true;
            applyZebra();
        }
    })();

    // ----- Inline UI refresh after successful assignment --------------------
    window.addEventListener("assign:saved", (ev) => {
        const { assetId, userId, userDisplay } = ev.detail || {};
        if (!assetId) return;

        // Prefer Tag #; fallback to numeric
        let row = document.querySelector(`tr[data-tag="${CSS.escape(String(assetId))}"]`);
        if (!row) {
            row = Array.from(document.querySelectorAll("tr.result"))
                .find(r => (r.dataset.assetId || "") === String(assetId));
        }
        if (!row) return;

        // Assignment cell
        const nameSpan = row.querySelector(".assigned-name");
        if (nameSpan) {
            nameSpan.textContent = userDisplay || "Unassigned";
            nameSpan.dataset.userId = userId || "";
        }
        const iconBtn = row.querySelector(".assign-btn");
        if (iconBtn) {
            iconBtn.dataset.currentUserId = userId || "";
        }

        // Status → Assigned
        const statusCell = row.querySelector(".col-status");
        if (statusCell) statusCell.textContent = "Assigned";
        row.dataset.status = "Assigned";

        // If filtered by "Available", hide immediately
        if ((selectStat?.value || "").toLowerCase() === "available") {
            row.style.display = "none";
        }

        // Re-stripe
        applyZebra();

        // Light refresh to keep pager/total honest
        AIMS.Search.refresh();
    });

})();