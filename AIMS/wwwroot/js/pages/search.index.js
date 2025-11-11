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

    const headerTable = document.getElementById("search-header");
    const toolbarGrid = document.getElementById("toolbar-grid");
    const toolbar = document.getElementById("search-toolbar");
    const adminWrapper = document.querySelector(".admin-table-wrapper");

    const filterFab = document.getElementById("filter-fab");
    const hiddenFilterAnchor = document.getElementById("searchFilters");

    // Filters (toolbar)
    const inputName = document.querySelector('[name="Asset Name"]');
    const selectType = document.querySelector('[name="Type"]');
    const inputTag = document.querySelector('[name="Tag #"]');
    const inputAssn = document.querySelector('[name="Assignment"]');
    const selectStat = document.querySelector('[name="Status"]');

    const archivedToggle = document.querySelector('[data-role="show-archived-toggle"]');

    // Role flags
    const asBool = v => String(v).toLowerCase() === "true" || String(v) === "1";
    const CAN_ADMIN = asBool(window.__CAN_ADMIN__);
    const IS_SUPERVISOR = asBool(window.__IS_SUPERVISOR__);

    // ---- Normalizers ------------------------------------------------------------
    const getTag = (row) => String(row.assetTag ?? row.AssetTag ?? row.tag ?? row.Tag ?? "")
    
    // ----- Paging / cache state ---------------------------------------------
    let currentPage = 1;
    let pageSize = 5;           // keep in sync with default on server
    let lastResultMeta = null;  // { total, page, pageSize }
    let lastQuery = null;

    // ----- Show archived state ----------------------------------------------
    // Always start FALSE; user must explicitly opt-in each session.
    let showArchived = false;
    // During boot we ignore archive toggle notifications so we never prefetch archived.
    let _bootArchiveGuard = true;

    // ----- Client cache for cross-page filtering ----------------------------
    const MAX_CLIENT_CACHE = 5000;
    // Global registry: key = `${q}|${archived?1:0}` → { items, loadedPages, pageSize, ts }
    const CACHE = new Map();
    let cacheKey = null;       // currently active key
    let cacheAllItems = [];    // active working set for current view
    let cacheLoadedPages = new Set();
    let filteredItems = [];    // result after client filters
    let activeFilters = { name: "", type: "", tag: "", assignment: "", status: "" };

    // ----- Persistence (reloads only) -------------------------------------------
    const STORAGE_TOGGLE_KEY = "aims.search.showArchived";
    const STORAGE_CACHE_KEY = "aims.search.cache";

    // nav type: 'reload' vs 'navigate' (modern)
    const _navType = (performance.getEntriesByType?.("navigation")?.[0]?.type) || "navigate";
    // legacy fallback for some Safari/old browsers
    if (_navType === "navigate" && performance.navigation?.type === 1) { /* reload */ }

    // Restore toggle only on reloads; clear on navigations from other pages
    (function primeShowArchivedFromStorage() {
        try {
            if (_navType === "reload") {
                const saved = localStorage.getItem(STORAGE_TOGGLE_KEY);
                if (saved != null) showArchived = (saved === "1");
            } else {
                // coming from another page → start clean per requirement
                localStorage.removeItem(STORAGE_TOGGLE_KEY);
            }
        } catch { }
    })();

    // Persist the whole CACHE Map into sessionStorage (scoped to this tab)
    function persistCacheToSession() {
        try {
            const payload = {
                ver: String(window.__ASSETS_VER__ ?? ""),
                entries: Object.fromEntries(
                    Array.from(CACHE.entries()).map(([k, v]) => [
                        k,
                        {
                            items: v.items,
                            loadedPages: Array.from(v.loadedPages || []),
                            pageSize: v.pageSize,
                            ts: v.ts
                        }
                    ])
                )
            };
            sessionStorage.setItem(STORAGE_CACHE_KEY, JSON.stringify(payload));
        } catch (e) { /* non-fatal */ }
    }

    // Hydrate CACHE from sessionStorage on load (same tab, survives reload)
    function hydrateCacheFromSession() {
        try {
            const raw = sessionStorage.getItem(STORAGE_CACHE_KEY);
            if (!raw) return;
            const obj = JSON.parse(raw);
            const verOk = String(obj?.ver ?? "") === String(window.__ASSETS_VER__ ?? "");
            if (!verOk) return;
            const entries = obj?.entries || {};
            Object.entries(entries).forEach(([k, v]) => {
                CACHE.set(k, {
                    items: v.items || [],
                    loadedPages: new Set(v.loadedPages || []),
                    pageSize: v.pageSize || pageSize,
                    ts: v.ts || Date.now()
                });
            });
        } catch (e) { /* ignore */ }
    }

    // --- TTL so cached views don't live forever ---
    const CACHE_TTL_MS = 15 * 60_000; // Cache expires every 15 minutes so view isn't stale
    const getEntry = (key) => {
        const e = CACHE.get(key);
        if (!e) return null;
        if (e.ts && (Date.now() - e.ts) > CACHE_TTL_MS) {
            CACHE.delete(key);
            return null;
        }
        return e;
    };
    const setEntry = (key, entry) => {
        CACHE.set(key, entry);
        // persist on every write so reload is instant
        persistCacheToSession();
    };

    const debounce = (fn, ms = 250) => { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); }; };
    const debouncedApply = debounce(() => applyFiltersAndRender({ page: 1 }), 250);

    const makeCacheKey = (q, archivedFlag) =>
         `${(q || "").trim().toLowerCase()}|${archivedFlag ? "1" : "0"}`;

    // ----- Helpers: detect archived on any DTO shape -----
    const isArchivedRow = (r) => {
        const flag = (r?.isArchived ?? r?.IsArchived ?? r?.archived ?? r?.Archived) === true;
        const statusText = String(r?.status ?? r?.Status ?? "").trim().toLowerCase();
        return flag || statusText === "archived";
    };

    // ---------- Overlay positioning helpers (no accumulated transforms) ----------
    function placeOverlayBelowAnchor(anchorEl, overlayEl, offsetY = 8) {
        if (!anchorEl || !overlayEl) return;
        // Ensure overlay has display so measurements work
        const prevDisplay = overlayEl.style.display;
        if (getComputedStyle(overlayEl).display === "none") overlayEl.style.display = "block";

        const a = anchorEl.getBoundingClientRect();
        const pad = 12;

        // size first so clamping is correct
        const w = overlayEl.offsetWidth || 0;
        const desiredLeft = a.left;
        const desiredTop = Math.round(a.bottom + offsetY);

        const maxLeft = Math.max(pad, window.innerWidth - pad - w);
        const left = Math.min(Math.max(desiredLeft, pad), maxLeft);

        overlayEl.style.position = "fixed";
        overlayEl.style.left = `${Math.round(left)}px`;
        overlayEl.style.top = `${desiredTop}px`;
        overlayEl.style.transform = "none"; // never accumulate deltas

        // restore display if we changed it and caller wants it hidden
        if (prevDisplay === "none") overlayEl.style.display = "none";
    }

    // Track & continuously reposition while open; returns a cleanup() fn.
    function trackOverlay(anchorEl, overlayEl, offsetY = 8) {
        const reposition = () => placeOverlayBelowAnchor(anchorEl, overlayEl, offsetY);
        const onScroll = () => reposition();
        const onResize = () => reposition();
        const ro = (window.ResizeObserver)
            ? new ResizeObserver(() => reposition())
            : null;

        window.addEventListener("scroll", onScroll, { passive: true });
        window.addEventListener("resize", onResize, { passive: true });
        ro?.observe(anchorEl);

        // initial place
        reposition();

        return () => {
            window.removeEventListener("scroll", onScroll);
            window.removeEventListener("resize", onResize);
            ro?.disconnect?.();
        };
    }

    // ----- Loading min time ----------------------------------------------------
    const LOCAL_MIN_VISIBLE_MS = 500;
    let localShownAt = 0;

    function startLoading() {
        localShownAt = performance.now();
        clearBody();
        setEmpty(false);
        if (adminWrapper) adminWrapper.classList.remove("table-has-rows");
        if (window.GlobalSpinner?.show) GlobalSpinner.show();
    }

    function waitForMinimum() {
        const elapsed = performance.now() - localShownAt;
        const wait = Math.max(0, LOCAL_MIN_VISIBLE_MS - elapsed);
        return new Promise(resolve => setTimeout(resolve, wait));
    }

    // Initialize global filter icon logic (archive-filter.js from _Layout)
    try {
        AIMSFilterIcon.init("searchFilters", {
            onChange: ({ showArchived: newVal }) => {
                // Ignore any initial notify while booting; we will do our own first fetch.
                if (_bootArchiveGuard) return;
                showArchived = newVal;
                localStorage.setItem(STORAGE_TOGGLE_KEY, newVal ? "1" : "0");
                const q = (lastQuery ?? activeQuery() ?? "").trim();
                // Try to reuse cached results for this q+flag; otherwise fetch.
                const key = makeCacheKey(q, showArchived);
                const hit = getEntry(key);
                if (hit) {
                    cacheKey = key;
                    cacheAllItems = hit.items.slice(0, MAX_CLIENT_CACHE);
                    cacheLoadedPages = new Set(hit.loadedPages || []);
                    pageSize = hit.pageSize || pageSize;
                    activeFilters = { name: "", type: "", tag: "", assignment: "", status: "" };
                    applyFiltersAndRender({ page: 1 });
                } else {
                    // If turning OFF archived and we don't have an active-only cache yet,
                    // derive it from the current items immediately to keep UI snappy.
                    if (!newVal && cacheAllItems.length) {
                        const derived = cacheAllItems.filter(r => !isArchivedRow(r)).slice(0, MAX_CLIENT_CACHE);
                        const k = makeCacheKey(q, /*archived*/ false);
                        setEntry(k, { items: derived, loadedPages: [], pageSize, ts: Date.now() });
                        cacheKey = k;
                        cacheAllItems = derived;
                        cacheLoadedPages = new Set();
                        activeFilters = { name: "", type: "", tag: "", assignment: "", status: "" };
                        applyFiltersAndRender({ page: 1 });
                    } else {
                        fetchInitial(q, 1, pageSize);
                    }     
                }
            }
        });
    } catch (e) {
        console.warn("AIMSFilterIcon not initialized:", e);
    }

    // ----- Page safety: ensure nothing overlays the navbar --------------------
    function ensureNavbarClickable() {
        [toolbar, document.querySelector(".table-wrapper")].forEach(el => {
            if (el) {
                el.style.position = "relative";
                el.style.zIndex = "1";
            }
        });
        const killers = [
            ".search-loading-overlay",
            ".aims-global-overlay",
            ".modal-backdrop:not(.show)"
        ];
        killers.forEach(sel => {
            document.querySelectorAll(sel).forEach(el => {
                const cs = getComputedStyle(el);
                if (cs.display === "none" || cs.visibility === "hidden" || cs.opacity === "0") {
                    el.style.pointerEvents = "none";
                }
            });
        });
    }

    /* ---- FAB → popover toggle (Show archived) -------------------------------- */
    function wireFilterFab() {
        if (!filterFab || !hiddenFilterAnchor) return;

        const pop = document.querySelector('[data-component="filter-icon"][data-id="searchFilters"]');
        if (!pop) return;

        let cleanupTracker = null;

        const closePop = () => {
            if (!pop.classList.contains("open")) return;
            pop.classList.remove("open");
            pop.hidden = true;
            cleanupTracker?.();
            cleanupTracker = null;
        };

        // Close on outside click
        document.addEventListener("click", (ev) => {
            if (!pop.classList.contains("open")) return;
            const inside = pop.contains(ev.target) || filterFab.contains(ev.target);
            if (!inside) closePop();
        });

        filterFab.addEventListener("click", () => {
            // keep the invisible anchor centered on the FAB (for any consumers)
            const r = filterFab.getBoundingClientRect();
            hiddenFilterAnchor.style.position = "fixed";
            hiddenFilterAnchor.style.left = Math.round(r.left + r.width * 0.5) + "px";
            hiddenFilterAnchor.style.top = Math.round(r.top + r.height * 0.5) + "px";

            const opening = !pop.classList.contains("open");
            pop.hidden = !opening;
            pop.classList.toggle("open", opening);

            if (opening) {
                // size hints so clamping is sane on first paint
                pop.style.minWidth = "220px";
                pop.style.maxInlineSize = "calc(100vw - 24px)";

                // place & start live tracking; no transform-based nudges
                cleanupTracker = trackOverlay(filterFab, pop, 8);
                pop.querySelector('[data-role="show-archived-toggle"]')?.focus();
            } else {
                closePop();
            }
        });
    }

    // If CSS background image failed, inject an <img> fallback so the icon appears
    (function ensureFabIcon() {
        if (!filterFab) return;
        const bg = getComputedStyle(filterFab).backgroundImage || "";
        if (bg === "none" || bg.trim() === "") {
            const img = document.createElement("img");
            img.src = "/images/filter-icon-blue.png";
            img.alt = "";
            img.width = 24;
            img.height = 24;
            img.decoding = "async";
            img.loading = "eager";
            img.style.pointerEvents = "none";
            filterFab.appendChild(img);
            img.addEventListener("error", () => { img.src = "../../images/filter-icon-blue.png"; });
        }
    })();

    // ----- Scoped CSS attr (razor isolation friendly) -------------------------
    const scopeAttrName = (() => {
        const maybe = (host) => {
            if (!host) return null;
            for (const a of host.attributes) if (/^b-[a-z0-9]+$/i.test(a.name)) return a.name;
            return null;
        };
        return maybe(document.querySelector(".asset-table")) ||
            maybe(document.querySelector(".table-container")) || null;
    })();
    const applyScope = el => { if (scopeAttrName) el.setAttribute(scopeAttrName, ""); };

    // ----- URL / initial query -------------------------------------------------
    const params = new URLSearchParams(window.location.search);
    const initialQ = (params.get("searchQuery") || "").trim();

    // ----- Helpers -------------------------------------------------------------
    function clearBody() {
        while (tbody.firstChild) tbody.removeChild(tbody.firstChild);
    }
    function setEmpty(isEmpty) {
        empty.hidden = !isEmpty;
    }

    // Normalize status text across DTO shapes
    const getStatus = (row) => String(row.status ?? row.Status ?? "").trim().toLowerCase();

    // Lightweight notifier (uses AIMS toast if present; falls back to alert)
    const notify = (msg) =>
        (window.AIMS?.Toast?.show?.(msg) || window.AIMS?.notify?.(msg) || window.toastr?.info?.(msg) || alert(msg));

    function setToolbarVisible(visible) {
        if (toolbar) toolbar.hidden = !visible;
        if (filterFab) filterFab.hidden = !visible;
        document.body.classList.toggle("is-blank", !visible);
    }

    function setArchivedToggleState({ checked = false, disabled = false } = {}) {
        showArchived = !!checked;
        if (archivedToggle) {
            archivedToggle.checked = !!checked;
            archivedToggle.disabled = !!disabled;
        }
        // Keep popover closed when disabled
        const pop = document.querySelector('[data-component="filter-icon"][data-id="searchFilters"]');
        if (disabled && pop) {
            pop.classList.remove("open");
            pop.hidden = true;
        }
    }

    // --- Ellipsis helpers  ---
    function isEllipsed(td) {
        const target = td.querySelector('.ellip') || td;
        const cs = getComputedStyle(target);

        const singleLine = cs.whiteSpace === "nowrap";
        const ellipsing = (cs.textOverflow === "ellipsis") || (cs.overflow === "hidden");
        if (!singleLine || !ellipsing) return false;

        // Try fast path on the actual target
        if ((target.scrollWidth - target.clientWidth) > 0.5) return true;

        // Fallback: measure text realistically (handles nested spans/buttons nearby)
        const r = document.createRange();
        r.selectNodeContents(target);
        const rects = r.getBoundingClientRect();
        const textW = r.getBoundingClientRect().width; // width of inline content
        const cellW = target.getBoundingClientRect().width;
        return textW > (cellW + 0.8);
    }

    // Re-run tooltips after any layout change (keeps titles accurate)
    function refreshVisibleTooltips() {
        const visibleRows = Array.from(document.querySelectorAll("#table-body > tr:not([hidden])"));
        visibleRows.forEach(tr => Array.from(tr.cells).forEach(td => {
            if (isEllipsed(td)) td.title = td.textContent.trim();
            else td.removeAttribute("title");
        }));
    }

    // ----- Public refresh helpers ---------------------------------------------
    AIMS.Search.refresh = function refresh() {
        applyFiltersAndRender({ page: currentPage });
    };
    AIMS.Search.refreshQuery = async function (newQuery) {
        const clean = (newQuery ?? "").trim();
        if (!clean) return;
        lastQuery = clean;
        await fetchInitial(clean, 1, pageSize);
    };

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
        btn.className = "icon-btn";
        btn.setAttribute("data-testid", "assign-user-btn");

        // normalize API ids
        const swId = row.softwareID ?? row.SoftwareID ?? row.softwareId ?? null;
        const hwId = row.hardwareID ?? row.HardwareID ?? row.hardwareId ?? null;
        const numericId = swId ?? hwId;
        const kind = (swId != null) ? 2 : 1; // 1=hardware, 2=software

        btn.dataset.assetTag = getTag(row);
        btn.dataset.assetNumericId = (numericId != null ? String(numericId) : "");
        btn.dataset.assetKind = String(kind);
        btn.dataset.currentUserId = currentUserId;

        // Decide enabled vs disabled based on STATUS
        const statusL = getStatus(row); // "assigned", "available", "in repair", "archived", etc.
        const isActionable = (statusL === "assigned" || statusL === "available");

        if (isActionable) {
            // mint (enabled) style
            btn.classList.add("icon-btn--mint");
            btn.title = (statusL === "assigned") ? "Unassign user" : "Assign user";
            btn.setAttribute("aria-label", (statusL === "assigned") ? "Unassign user" : "Assign user");
            btn.dataset.disabled = "0";
        } else {
            // disabled (gray) style
            btn.classList.add("icon-btn--disabled");
            btn.title = "Asset must be Available in order to assign";
            btn.setAttribute("aria-disabled", "true");
            btn.dataset.disabled = "1";
        }

        // inline person icon
        btn.innerHTML = `
        <svg class="icon-person" viewBox="0 0 16 16" width="20" height="20" aria-hidden="true" focusable="false">
            <path d="M8 9c3.314 0 6 2.239 6 5 0 .552-.448 1-1 1H3c-.552 0-1-.448-1-1 0-2.761 2.686-5 6-5Zm0-1a3 3 0 1 1 0-6 3 3 0 0 1 0 6Z"></path>
        </svg>`;
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
            tr.dataset.archived = isArchivedRow(row) ? "1" : "0";

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
            const normTag = getTag(row);
            tr.dataset.tag = normTag || "";
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
            tdTag.textContent = normTag || "-";
            applyScope(tdTag);

            const tdAssn = document.createElement("td");
            tdAssn.className = "col-assignment";
            tdAssn.appendChild(renderAssignmentCell(row));
            applyScope(tdAssn);

            const tdStat = document.createElement("td");
            tdStat.className = "col-status";
            const statusText = String(row.status ?? row.Status ?? "-").trim();
            const pill = document.createElement("span");
            pill.className = "status " + (statusText ? statusText.toLowerCase().replace(/\s+/g, "") : "");
            pill.textContent = statusText;
            tdStat.appendChild(pill);
            applyScope(tdStat);

            tr.append(tdName, tdType, tdTag, tdAssn, tdStat);
            tbody.appendChild(tr);
        }
        ensureNoResultsRow();
        applyZebra();
        updateToolbarCorners();
        refreshVisibleTooltips();
    }

    // --- Expand row on hover *only* if the hovered cell is ellipsed ---
    tbody.addEventListener("mouseover", (e) => {
        const td = e.target.closest("td");
        if (!td) return;
        if (!tbody.contains(td)) return;

        // Only expand when the hovered cell is actually truncated
        if (isEllipsed(td)) {
            const tr = td.parentElement;
            tr.classList.add("expand-row");
            td.classList.add("expanded-source");
        }
    });

    tbody.addEventListener("mouseout", (e) => {
        const td = e.target.closest("td");
        if (!td) return;
        if (!tbody.contains(td)) return;

        const tr = td.parentElement;
        // Only remove classes if we were the source that triggered expansion
        td.classList.remove("expanded-source");
        tr.classList.remove("expand-row");
    });

    // Click handling (delegated)
    tbody.addEventListener("click", (e) => {
        // 1) Assign / Unassign button click (let the modal flows handle it)
        const iconBtn = e.target.closest(".assign-btn, .icon-btn");
        if (iconBtn) {
            e.preventDefault();
            e.stopPropagation();

            // If disabled, show friendly message and exit
            if (iconBtn.dataset.disabled === "1" || iconBtn.classList.contains("icon-btn--disabled")) {
                notify("Asset must be Available in order to assign.");
                return;
            }

            const rowEl = iconBtn.closest("tr.result");
            const tag = rowEl?.dataset.tag || "";
            const numericId = iconBtn.dataset.assetNumericId || rowEl?.dataset.assetId || "";
            const assetKind = Number(iconBtn.dataset.assetKind || "1");
            const currentUserId = iconBtn.dataset.currentUserId || "";
            const nameEl = rowEl ? rowEl.querySelector(".assigned-name") : null;
            const currentDisplayName = (nameEl?.textContent || "Unassigned").trim();

            const isAssigned =
                (rowEl?.dataset.status || "").toLowerCase() === "assigned" ||
                (!!currentUserId);

            if (isAssigned) {
                window.dispatchEvent(new CustomEvent("unassign:open", {
                    detail: { currentUserId: currentUserId || null, assetTag: tag || null, assetNumericId: numericId || null, assetKind }
                }));
            } else {
                window.dispatchEvent(new CustomEvent("assign:open", {
                    detail: { assetTag: tag, assetNumericId: numericId, assetKind, currentUserId, currentDisplayName }
                }));
            }
            return;
        }

        // 2) Row → navigate to AssetDetails (Admins only)
        if (!CAN_ADMIN) return;

        const tr = e.target.closest("tr.result");
        if (!tr) return;

        // Ignore clicks on interactive controls inside the row
        if (e.target.closest("button, a, input, select, textarea, [role='button'], [role='link']")) return;

        const type = (tr.dataset.type || "Laptop").trim();
        const tag = (tr.dataset.tag || "").trim();
        if (!tag) { console.warn("Row click: missing Tag/AssetTag; not navigating.", tr); return; }

        // ALWAYS go to http://localhost:5119/AssetDetails/Index?category={type}&tag={tag}
        const url = new URL("http://localhost:5119/AssetDetails/Index");
        url.searchParams.set("category", type);
        url.searchParams.set("tag", tag);
        window.location.assign(url.toString());
    });

    // ----- Local filtering + render (replaces DOM-only filtering) --------------
    function applyFiltersAndRender({ page = 1 } = {}) {
        const v = (s) => (s || "").trim().toLowerCase();

        // The checkbox is the source of truth. If it’s checked, we must include archived.
        if (archivedToggle) showArchived = !!archivedToggle.checked;

        filteredItems = cacheAllItems.filter(row => {
            // Absolute rule: if toggle is OFF, archived rows never participate in paging or filters.
            if (!showArchived && isArchivedRow(row)) return false;
            if (activeFilters.name && !(row.assetName || "").toLowerCase().includes(v(activeFilters.name))) return false;
            if (activeFilters.type && (row.type || "").toLowerCase() !== v(activeFilters.type)) return false;
            if (activeFilters.tag && !getTag(row).toLowerCase().includes(v(activeFilters.tag))) return false;
            if (activeFilters.assignment) {
                const disp = (row.assignedEmployeeNumber && row.assignedTo)
                    ? `${row.assignedTo} (${row.assignedEmployeeNumber})`
                    : (row.assignedTo || "Unassigned");
                if (!disp.toLowerCase().includes(v(activeFilters.assignment))) return false;
            }
            if (activeFilters.status && String(row.status ?? row.Status ?? "").toLowerCase() !== v(activeFilters.status)) return false;
            return true;
        });

        const total = filteredItems.length;
        const totalPages = Math.max(1, Math.ceil(total / pageSize));
        currentPage = Math.min(Math.max(1, page), totalPages);

        const start = (currentPage - 1) * pageSize;
        const pageSlice = filteredItems.slice(start, start + pageSize);

        renderRows(pageSlice);
        lastResultMeta = { total, page: currentPage, pageSize };
        updatePager();
        setEmpty(total === 0);

        // Toggle spacer class on tbody so the dropdown has room when empty
        tbody.classList.toggle("is-empty", total === 0);

        updateToolbarCorners();
        refreshVisibleTooltips();
    }

    // Inputs/selects → live (debounced) local filtering
    inputName?.addEventListener("input", e => { activeFilters.name = e.target.value; debouncedApply(); });
    inputTag?.addEventListener("input", e => { activeFilters.tag = e.target.value; debouncedApply(); });
    inputAssn?.addEventListener("input", e => { activeFilters.assignment = e.target.value; debouncedApply(); });

    // ----- Pager helpers (local slice only) -----------------------------------
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
        if (currentPage > 1) applyFiltersAndRender({ page: currentPage - 1 });
    });
    btnNext?.addEventListener("click", () => {
        applyFiltersAndRender({ page: currentPage + 1 });
    });

    function activeQuery() {
        const p = new URLSearchParams(window.location.search);
        return (p.get("searchQuery") || "").trim();
    }

    // ----- Fetch (seed cache + progressive prefetch) ---------------------------
    async function fetchInitial(q, page = 1, pageSizeArg = 25) {
        const isBlank = !q || q.trim() === "";

        if (isBlank && !IS_SUPERVISOR) {
            cacheAllItems = []; filteredItems = [];
            clearBody();
            setEmpty(true);
            tbody.classList.add("is-empty");

            setToolbarVisible(false);
            setArchivedToggleState({ checked: false, disabled: true });

            if (pager) pager.hidden = true;
            if (adminWrapper) adminWrapper.classList.remove("table-has-rows");
            return;
        }

        // non-blank path → ensure UI is shown and toggle enabled
        setToolbarVisible(true);
        setArchivedToggleState({ checked: showArchived, disabled: false })

        lastQuery = q ?? "";// First-load rule: never query archived unless the user explicitly toggled it on.
        const key = makeCacheKey(lastQuery, showArchived);
        const cached = getEntry(key);
        // If we already have a cache entry, use it immediately.
        if (cached && (cached.items?.length || 0) > 0) {
            cacheKey = key;
            cacheAllItems = cached.items.slice(0, MAX_CLIENT_CACHE);
            cacheLoadedPages = new Set(cached.loadedPages || []);
            pageSize = cached.pageSize || pageSizeArg;
            activeFilters = { name: "", type: "", tag: "", assignment: "", status: "" };
            applyFiltersAndRender({ page: 1 });
            return;
        }

        // Prepare fresh working set for this key only (do not clear other keys)
        cacheKey = key;
        cacheAllItems = [];
        cacheLoadedPages = new Set();
        filteredItems = [];
        tbody.classList.add("is-empty");

        const url = new URL("/api/assets/search", window.location.origin);
        if (!isBlank) url.searchParams.set("q", lastQuery.trim());
        url.searchParams.set("page", String(page));
        url.searchParams.set("pageSize", String(pageSizeArg));
        url.searchParams.set("showArchived", showArchived ? "true" : "false");

        const ver = (window.__ASSETS_VER__ ? String(window.__ASSETS_VER__) : String(Date.now()));
        url.searchParams.set("_v", ver);

        aimsFetch.abort(url.toString());
        startLoading();

        try {
            const data = await aimsFetch(url.toString(), { ttl: 30 });
            await waitForMinimum();

            cacheAllItems = (data.items || []).slice(0, MAX_CLIENT_CACHE);
            // Strip archived rows on client if showArchived == false (defensive)
            if (!showArchived) {
                cacheAllItems = cacheAllItems.filter(r => !isArchivedRow(r));
            }
            cacheLoadedPages.add(data.page || page);
            pageSize = data.pageSize ?? pageSizeArg;

            // Persist this view in the registry
            setEntry(key, {
                items: cacheAllItems.slice(0, MAX_CLIENT_CACHE),
                loadedPages: Array.from(cacheLoadedPages),
                pageSize,
                ts: Date.now()
            });

            // If we just fetched ALL (showArchived=true), try to also seed an ACTIVE-ONLY cache
            // so flipping the toggle back can be instant without a re-fetch.
            if (showArchived) {
                const keyActive = makeCacheKey(lastQuery, /*archived*/ false);
                if (!getEntry(keyActive)) {
                    // Derive if an isArchived-like flag exists; otherwise skip derivation.
                    const sample = cacheAllItems[0] || {};
                    const hasFlag = ("isArchived" in sample) || ("IsArchived" in sample) || ("archived" in sample) || ("Archived" in sample);
                    if (hasFlag) {
                        const isArch = (r) => !!(r.isArchived ?? r.IsArchived ?? r.archived ?? r.Archived);
                        const activeOnly = cacheAllItems.filter(r => !isArch(r)).slice(0, MAX_CLIENT_CACHE);
                        setEntry(keyActive, {
                            items: activeOnly,
                            loadedPages: [],   // derived
                            pageSize,
                            ts: Date.now()
                        });
                    }
                }
            }

            // reset filter state on new query
            activeFilters = { name: "", type: "", tag: "", assignment: "", status: "" };

            applyFiltersAndRender({ page: 1 });

            // Progressive prefetch (one-shot), cap at MAX_CLIENT_CACHE
            const totalPages = Math.max(1, Math.ceil((data.total ?? 0) / pageSize));
            const pagesToGet = [];
            for (let p = 2; p <= totalPages; p++) if (!cacheLoadedPages.has(p)) pagesToGet.push(p);

            if (cacheAllItems.length < MAX_CLIENT_CACHE && pagesToGet.length) {
                const CONCURRENCY = 3;
                let i = 0;
                const runner = async () => {
                    while (i < pagesToGet.length && cacheAllItems.length < MAX_CLIENT_CACHE) {
                        const p = pagesToGet[i++];
                        const u = new URL("/api/assets/search", window.location.origin);
                        if (!isBlank) u.searchParams.set("q", lastQuery.trim());
                        u.searchParams.set("page", String(p));
                        u.searchParams.set("pageSize", String(pageSize));
                        u.searchParams.set("showArchived", showArchived ? "true" : "false");
                        u.searchParams.set("_v", ver);
                        try {
                            const d = await aimsFetch(u.toString(), { ttl: 30 });
                            for (const it of (d.items || [])) {
                                if (cacheAllItems.length >= MAX_CLIENT_CACHE) break;
                                cacheAllItems.push(it);
                            }
                            cacheLoadedPages.add(d.page || p);

                            // Update the registry entry as pages arrive
                            setEntry(key, {
                                items: cacheAllItems.slice(0, MAX_CLIENT_CACHE),
                                loadedPages: Array.from(cacheLoadedPages),
                                pageSize,
                                ts: Date.now()
                            });
                        } catch (e) {
                            console.warn("Prefetch failed page", p, e);
                        }
                    }
                };
                await Promise.all(new Array(CONCURRENCY).fill(0).map(runner));
                applyFiltersAndRender({ page: currentPage });
            }
        } catch (e) {
            console.error("Search fetch failed:", e);
            cacheAllItems = []; filteredItems = [];
            clearBody();
            setEmpty(true);
            tbody.classList.add("is-empty");
            if (pager) pager.hidden = true;
        } finally {
            if (window.GlobalSpinner?.hide) GlobalSpinner.hide();
        }
    }

    /* ---- Toolbar ↔ Header width sync ---------------------------------------- */
    function syncToolbarToHeader() {
        try {
            const ths = headerTable?.querySelectorAll("thead th");
            if (!ths || ths.length === 0 || !toolbarGrid || !toolbar) return;

            for (let i = 0; i < 5; i++) {
                const rect = ths[i].getBoundingClientRect();
                toolbarGrid.style.setProperty(`--col${i + 1}`, `${Math.round(rect.width)}px`);
            }
            toolbar.classList.add("synced");
        } catch { }
    }

    window.addEventListener("DOMContentLoaded", syncToolbarToHeader);
    window.addEventListener("load", syncToolbarToHeader);

    let _syncTimer = null;
    function scheduleSync() {
        clearTimeout(_syncTimer);
        _syncTimer = setTimeout(syncToolbarToHeader, 40);
    }
    window.addEventListener("load", () => { scheduleSync(); ensureNavbarClickable(); });
    window.addEventListener("resize", scheduleSync);
    if (headerTable && window.ResizeObserver) {
        const ro = new ResizeObserver(scheduleSync);
        ro.observe(headerTable);
    }

    // ----- Custom dropdowns (accessible custom select) -------------------------
    function makeCustomSelect(selectEl, { onChange } = {}) {
        if (!selectEl || selectEl.dataset.customized === "true") return null;

        const wrapper = document.createElement("div");
        wrapper.className = "aims-custom-select";
        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = "aims-custom-select__button";
        btn.setAttribute("aria-haspopup", "listbox");
        btn.setAttribute("aria-expanded", "false");
        btn.setAttribute("aria-label", selectEl.name);
        btn.innerHTML = `<span class="aims-custom-select__label"></span><span class="aims-custom-select__chev">▾</span>`;

        const list = document.createElement("div");
        list.className = "aims-custom-select__listbox";
        list.setAttribute("role", "listbox");
        list.setAttribute("tabindex", "-1");

        // size hints so clamping is sane
        list.style.minWidth = "0px"; // will be set on open
        list.style.maxWidth = "none";

        const opts = Array.from(selectEl.options || []);
        let activeIndex = Math.max(0, opts.findIndex(o => o.selected) || 0);

        opts.forEach((o, idx) => {
            const opt = document.createElement("div");
            opt.className = "aims-custom-select__option";
            opt.setAttribute("role", "option");
            opt.setAttribute("data-value", o.value);
            opt.textContent = o.textContent;

            if (idx === activeIndex) {
                opt.setAttribute("aria-selected", "true");
                opt.setAttribute("aria-current", "true");
                opt.setAttribute("data-active", "true");
            }

            // hover → visual active (mint)
            opt.addEventListener("mouseenter", () => {
                list.querySelectorAll(".aims-custom-select__option")
                    .forEach(el => el.removeAttribute("data-active"));
                opt.setAttribute("data-active", "true");
            });

            list.appendChild(opt);
        });

        // visually hide original select but keep it in the form
        selectEl.style.position = "absolute";
        selectEl.style.opacity = "0";
        selectEl.style.pointerEvents = "none";
        selectEl.tabIndex = -1;
        selectEl.parentElement.insertBefore(wrapper, selectEl);
        wrapper.appendChild(btn);
        wrapper.appendChild(list);
        wrapper.appendChild(selectEl);

        function updateButtonLabel() {
            const current = list.querySelector('.aims-custom-select__option[aria-selected="true"]');
            const label = btn.querySelector(".aims-custom-select__label");
            const text = current ? current.textContent : "";
            label.textContent = text || (selectEl.name || "Select");
            btn.dataset.value = current ? current.getAttribute("data-value") : "";
        }
        updateButtonLabel();

        // Position/listbox tracking (no transform drift)
        function positionListbox() {
            const r = btn.getBoundingClientRect();
            list.style.minWidth = `${Math.round(r.width)}px`;
            list.style.maxWidth = `${Math.round(Math.max(r.width, 240))}px`;
            placeOverlayBelowAnchor(btn, list, 4);
        }

        function open() {
            wrapper.classList.add("aims-custom-select--open");
            btn.setAttribute("aria-expanded", "true");
            list.style.display = "block";

            // initial position + begin live tracking while open
            positionListbox();
            if (!list._cleanupTracker) list._cleanupTracker = trackOverlay(btn, list, 4);

            const cur = list.querySelector('.aims-custom-select__option[aria-current="true"]');
            cur?.scrollIntoView({ block: "nearest" });
        }
        function close({ reason } = {}) {
            wrapper.classList.remove("aims-custom-select--open");
            btn.setAttribute("aria-expanded", "false");
            list.style.display = "none";

            // stop tracking on close
            if (list._cleanupTracker) { list._cleanupTracker(); list._cleanupTracker = null; }

            if (reason === "keyboard" || reason === "button") {
                if (document.activeElement !== btn) btn.focus({ preventScroll: true });
            }
        }
        function setActive(idx, selectIt = false) {
            const options = list.querySelectorAll(".aims-custom-select__option");
            if (!options.length) return;
            idx = (idx + options.length) % options.length;

            options.forEach(o => { o.removeAttribute("aria-current"); o.removeAttribute("data-active"); });
            const next = options[idx];
            next.setAttribute("aria-current", "true");
            next.setAttribute("data-active", "true");
            activeIndex = idx;

            if (selectIt) {
                options.forEach(o => o.setAttribute("aria-selected", "false"));
                next.setAttribute("aria-selected", "true");
                const val = next.getAttribute("data-value") || "";
                selectEl.value = val;
                updateButtonLabel();
                // mark “chosen” so hover behavior matches Admin
                wrapper.parentElement?.classList?.add("has-aims-select"); // harmless if not used
                wrapper.setAttribute("data-chosen", "true");
                if (typeof onChange === "function") onChange(val, next.textContent);
            }
            next.scrollIntoView({ block: "nearest" });
        }

        btn.addEventListener("click", () => {
            const isOpen = wrapper.classList.contains("aims-custom-select--open");
            if (isOpen) close({ reason: "button" }); else open();
        });

        // keep the overlay stuck to button on resize/scroll (via tracker)
        window.addEventListener("load", () => {
            if (wrapper.classList.contains("aims-custom-select--open")) positionListbox();
        });

        list.addEventListener("click", (e) => {
            const item = e.target.closest(".aims-custom-select__option");
            if (!item) return;
            const idx = Array.from(list.children).indexOf(item);
            setActive(idx, true);
            close({ reason: "list-click" });
        });
        list.addEventListener("keydown", (e) => {
            if (e.key === "Escape") { e.preventDefault(); close({ reason: "keyboard" }); return; }
            if (e.key === "Enter") { e.preventDefault(); setActive(activeIndex, true); close({ reason: "keyboard" }); return; }
            if (e.key === "ArrowDown") { e.preventDefault(); setActive(activeIndex + 1); return; }
            if (e.key === "ArrowUp") { e.preventDefault(); setActive(activeIndex - 1); return; }
            if (e.key === "Home") { e.preventDefault(); setActive(0); return; }
            if (e.key === "End") { e.preventDefault(); setActive(9999); return; }
        });

        document.addEventListener("click", (e) => {
            if (!wrapper.contains(e.target) && e.target !== list && !list.contains(e.target)) {
                close({ reason: "doc-click" });
            }
        });

        selectEl.dataset.customized = "true";
        return { wrapper, button: btn, listbox: list, selectEl };
    }

    // Create custom dropdowns and connect to filtering
    let customType = null, customStatus = null;
    function initCustomDropdowns() {
        const setType = (label) => {
            activeFilters.type = (label === "All Devices" ? "" : (label || "")).toLowerCase();
            debouncedApply();
        };
        const setStatus = (label) => {
            activeFilters.status = (label === "All Status" ? "" : (label || "")).toLowerCase();
            debouncedApply();
        };

        // Force custom dropdowns even without data-make-custom (parity with Admin)
        if (selectType) {
            customType = makeCustomSelect(selectType, { onChange: (_v, label) => setType(label) });
        }
        if (selectStat) {
            customStatus = makeCustomSelect(selectStat, { onChange: (_v, label) => setStatus(label) });
        }

        // still listen for native change (devtools/manual changes)
        selectType?.addEventListener("change", (e) => setType(e.target.value));
        selectStat?.addEventListener("change", (e) => setStatus(e.target.value));
    }

    // ----- Init ----------------------------------------------------------------
    (async function init() {
        hydrateCacheFromSession();
        setArchivedToggleState({ checked: showArchived, disabled: false });
        wireFilterFab();
        initCustomDropdowns();
        scheduleSync(); // sync toolbar widths on boot
        ensureNavbarClickable();

        if (IS_SUPERVISOR) {
            await fetchInitial("", 1, pageSize);
            applyZebra();
            scheduleSync();
            return;
        }
        if (initialQ.length > 0) {
            // Prefer cache for (q, archived=false)
            const key = makeCacheKey(initialQ, showArchived);
            const hit = getEntry(key);
            if (hit) {
                cacheKey = key;
                cacheAllItems = hit.items.slice(0, MAX_CLIENT_CACHE);
                cacheLoadedPages = new Set(hit.loadedPages || []);
                pageSize = hit.pageSize || pageSize;
                activeFilters = { name: "", type: "", tag: "", assignment: "", status: "" };
                applyFiltersAndRender({ page: 1 });
            } else {
                await fetchInitial(initialQ, 1, pageSize);
            }
        } else {
            cacheAllItems = []; filteredItems = [];
            clearBody();
            setEmpty(true);
            tbody.classList.add("is-empty");
            if (pager) pager.hidden = true;
            applyZebra();

            // Hide toolbar + FAB; force archived OFF + disable toggle
            setToolbarVisible(false);
            setArchivedToggleState({ checked: false, disabled: true });
        }
        // Now we allow the archive toggle to start notifying.
        _bootArchiveGuard = false;
        scheduleSync();
    })();

    // Re-sync columns after header resizes (rare)
    if (headerTable && window.ResizeObserver) {
        const hdrObs = new ResizeObserver(() => scheduleSync());
        hdrObs.observe(headerTable);
    }

    // ----- Inline UI refresh after successful assignment ----------------------
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

        // Update DOM
        const nameSpan = row.querySelector(".assigned-name");
        if (nameSpan) {
            nameSpan.textContent = userDisplay || "Unassigned";
            nameSpan.dataset.userId = userId || "";
        }
        const iconBtn = row.querySelector(".icon-btn, .assign-btn");
        if (iconBtn) iconBtn.dataset.currentUserId = userId || "";

        // Update status pill + dataset
        const statusCell = row.querySelector(".col-status");
        const pill = statusCell?.querySelector(".status");
        if (pill) {
            pill.textContent = "Assigned";
            pill.className = "status assigned";
        } else if (statusCell) {
            statusCell.textContent = "Assigned";
        }
        row.dataset.status = "Assigned";

        // Update cache too
        const tag = row.dataset.tag;
        const cid = row.dataset.assetId;
        const hit = cacheAllItems.find(it => {
            const t = getTag(it);
            const numId = String(it.hardwareID ?? it.HardwareID ?? it.softwareID ?? it.SoftwareID ?? "");
            return (t && t === tag) || numId === String(cid);
        });
        if (hit) {
            hit.assignedTo = userDisplay || "Unassigned";
            hit.assignedUserId = userId || "";
            hit.status = "Assigned";
        }

        if ((activeFilters.status || "") === "available") {
            applyFiltersAndRender({ page: currentPage });
        } else {
            applyZebra();
        }
    });

    function updateToolbarCorners() {
        if (!adminWrapper || !tbody) return;
        const hasVisibleRows = [...tbody.querySelectorAll("tr.result")].some(tr =>
            tr.style.display !== "none" && !tr.hidden
        );
        adminWrapper.classList.toggle("table-has-rows", hasVisibleRows);
    }
})();