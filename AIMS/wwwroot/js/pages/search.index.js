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
    const inlineArchivedToggle = document.getElementById("showArchivedToggle");

    // Normalize many "truthy" representations used by APIs/DBs
    const asTrue = (v) => {
        const t = (typeof v === "string") ? v.trim().toLowerCase() : v;
        return t === true || t === 1 || t === "1" || t === "true" || t === "y" || t === "yes";
    };

    // Role flags
    const asBool = v => String(v).toLowerCase() === "true" || String(v) === "1";
    const CAN_ADMIN = asBool(window.__CAN_ADMIN__);
    const IS_SUPERVISOR = asBool(window.__IS_SUPERVISOR__);

    // ---- Normalizers ------------------------------------------------------------
    const getTag = (row) => String(row.assetTag ?? row.AssetTag ?? row.tag ?? row.Tag ?? "");

    // ----- Paging / cache state ---------------------------------------------
    let currentPage = 1;
    let pageSize = 25;           // keep in sync with default on server
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
    let _justDidFreshSearch = false;  // force-reset filters/URL on a new search term
    let restoredFiltersApplied = false; // set true in init() when we rehydrate
    // Facet caches (for dropdown option lists derived from current results)
    let facetTypeValues = [];
    let facetStatusValues = [];


    // ----- Persistence (reloads only) -------------------------------------------
    const STORAGE_TOGGLE_KEY = "aims.search.showArchived";
    const STORAGE_CACHE_KEY = "aims.search.cache";
    const STORAGE_FILTERS_KEY = "aims.search.filters.v1";

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

    // --- Filters snapshot persistence -----------------------------------------
    function persistFiltersSnapshot() {
        try {
            const snapshot = {
                q: (lastQuery ?? activeQuery() ?? "").trim(),
                filters: { ...activeFilters },     // name, type, tag, assignment, status (lowercased labels for selects)
                page: currentPage,
                archived: !!showArchived
            };
            sessionStorage.setItem(STORAGE_FILTERS_KEY, JSON.stringify(snapshot));
        } catch { /* ignore */ }
    }

    function readFiltersSnapshotForQuery(q) {
        try {
            const raw = sessionStorage.getItem(STORAGE_FILTERS_KEY);
            if (!raw) return null;
            const obj = JSON.parse(raw);
            if ((obj?.q ?? "") !== (q ?? "")) return null;
            return obj;
        } catch { return null; }
    }

    // Match a native <select> option by its TEXT (case-insensitive), then fire 'change'
    function setSelectByLowerText(selectEl, lowerTxt) {
        if (!selectEl) return;
        const want = (lowerTxt || "").trim().toLowerCase();
        if (!want) return;
        const opts = Array.from(selectEl.options || []);
        const hit = opts.find(o => (o.textContent || "").trim().toLowerCase() === want);
        if (hit) {
            selectEl.value = hit.value;
            selectEl.dispatchEvent(new Event("change", { bubbles: true }));
        }
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

    // ---- Hard reset of all client-side filters/state for a "fresh" search ----
    function resetAllFiltersAndState() {
        // Clear in-memory filters
        activeFilters = { name: "", type: "", tag: "", assignment: "", status: "" };
        currentPage = 1;

        // Clear inputs
        if (inputName) inputName.value = "";
        if (inputTag) inputTag.value = "";
        if (inputAssn) inputAssn.value = "";

        // Reset selects to their "All" labels (native + custom chrome)
        if (selectType) {
            selectType.selectedIndex = 0;
            // If custom select exists, reflect label
            if (customType?.button) {
                customType.button.querySelector(".aims-custom-select__label").textContent = "All Devices";
                customType.button.dataset.value = "All Devices";
            }
        }
        if (selectStat) {
            selectStat.selectedIndex = 0;
            if (customStatus?.button) {
                customStatus.button.querySelector(".aims-custom-select__label").textContent = "All Status";
                customStatus.button.dataset.value = "All Status";
            }
        }

        // Reset archived toggle to OFF and clear persisted snapshots
        setArchivedToggleState({ checked: false, disabled: false });
        try { localStorage.removeItem(STORAGE_TOGGLE_KEY); sessionStorage.removeItem(STORAGE_FILTERS_KEY); } catch {}
    }


    const debounce = (fn, ms = 250) => { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); }; };
    const debouncedApply = debounce(() => applyFiltersAndRender({ page: 1 }), 250);

    const makeCacheKey = (q, archivedFlag) =>
        `${(q || "").trim().toLowerCase()}|${archivedFlag ? "1" : "0"}`;

    // ----- Helpers: detect archived on any DTO shape -----
    const isArchivedRow = (r) => {
        const raw = (r?.isArchived ?? r?.IsArchived ?? r?.archived ?? r?.Archived);
        const flagged = asTrue(raw);
        const statusText = String(r?.status ?? r?.Status ?? "").trim().toLowerCase();
        return flagged || statusText === "archived";
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

    // Centralized handler so header toggle + FAB popover stay in sync
    function handleShowArchivedChanged(newVal) {
        // Ignore any initial notify while booting; we will do our own first fetch.
        if (_bootArchiveGuard) return;

        const next = !!newVal;

        // This updates both the inline header checkbox and the popover checkbox
        setArchivedToggleState({ checked: next, disabled: false });

        // Persist the toggle state
        try {
            localStorage.setItem(STORAGE_TOGGLE_KEY, next ? "1" : "0");
        } catch { /* non-fatal */ }

        const q = (lastQuery ?? activeQuery() ?? "").trim();
        const key = makeCacheKey(q, next);
        const hit = getEntry(key);

        // Try to reuse cached results for this q+flag; otherwise fetch.
        if (hit) {
            cacheKey = key;
            cacheAllItems = hit.items.slice(0, MAX_CLIENT_CACHE);
            cacheLoadedPages = new Set(hit.loadedPages || []);
            pageSize = hit.pageSize || pageSize;

            if (!restoredFiltersApplied) {
                activeFilters = { name: "", type: "", tag: "", assignment: "", status: "" };
            }

            applyFiltersAndRender({ page: restoredFiltersApplied ? (currentPage || 1) : 1 });
            persistFiltersSnapshot();
            return;
        }

        // If turning OFF archived and we don't have an active-only cache yet,
        // derive it from the current items immediately to keep UI snappy.
        if (!next && cacheAllItems.length) {
            const derived = cacheAllItems.filter(r => !isArchivedRow(r)).slice(0, MAX_CLIENT_CACHE);
            const k = makeCacheKey(q, /*archived*/ false);
            setEntry(k, {
                items: derived,
                loadedPages: [],
                pageSize,
                ts: Date.now()
            });

            cacheKey = k;
            cacheAllItems = derived;
            cacheLoadedPages = new Set();
            activeFilters = { name: "", type: "", tag: "", assignment: "", status: "" };
            applyFiltersAndRender({ page: 1 });
            persistFiltersSnapshot();
            return;
        }

        // Otherwise fall back to server fetch.
        fetchInitial(q, 1, pageSize);
        persistFiltersSnapshot();
    }

    // Initialize global filter icon logic (archive-filter.js from _Layout)
    try {
        AIMSFilterIcon.init("searchFilters", {
            onChange: ({ showArchived: newVal }) => {
                handleShowArchivedChanged(newVal);
            }
        });
    } catch (e) {
        console.warn("AIMSFilterIcon not initialized:", e);
    }

    // Wire the inline header toggle ("Show Archived Assets") into the same flow
    if (inlineArchivedToggle) {
        inlineArchivedToggle.addEventListener("change", (e) => {
            handleShowArchivedChanged(e.target.checked);
        });
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

    function findRowBySoftwareId(id) {
        const n = Number(id);
        return cacheAllItems.find(r => Number(r.softwareID ?? r.SoftwareID ?? r.softwareId) === n);
    }

    // Normalize status text; if archived, force "archived"
    const getStatus = (row) => {
        if (isArchivedRow(row)) return "archived";
        return String(row.status ?? row.Status ?? "").trim().toLowerCase();
    };

    // Compute a unified status string for hardware or software
    const computeStatus = (row) => {
        // archived check (for any DTO shape)
        const archived = isArchivedRow(row);

        const swId = row.softwareID ?? row.SoftwareID ?? row.softwareId ?? null;
        const isSoftware = swId != null;
        const totalSeats = Number(
            (row.licenseTotalSeats ?? row.LicenseTotalSeats ?? (isSoftware ? 0 : NaN))
        );
        const usedSeats = Number(
            (row.licenseSeatsUsed ?? row.LicenseSeatsUsed ?? (isSoftware ? 0 : NaN))
        );

        if (isSoftware) {
            if (archived) return "Archived";
            const full = usedSeats >= totalSeats && totalSeats > 0;
            return full ? "Seats Full" : "Available";
        }

        // Hardware fallback: respect archived + raw status if present
        if (archived) return "Archived";
        const raw = String(row.status ?? row.Status ?? "").trim();
        return raw || "Available";
    };

    function getAssignmentSearchText(row) {
        const lower = (s) => (s ?? "").toString().trim().toLowerCase();
        const chunks = [];

        // Multi-seat / software style assignments: try a few likely properties
        const multi = row.assignedUsers
            ?? row.AssignedUsers
            ?? row.seatAssignments
            ?? row.SeatAssignments
            ?? null;

        if (Array.isArray(multi)) {
            for (const u of multi) {
                const name =
                    u?.displayName ??
                    u?.name ??
                    u?.fullName ??
                    u?.FullName ??
                    "";
                const emp =
                    u?.employeeNumber ??
                    u?.EmployeeNumber ??
                    u?.employeeId ??
                    "";

                if (name) chunks.push(lower(name));
                if (emp) chunks.push(lower(emp));
            }
        } else if (typeof multi === "string") {
            // e.g. "Alice (12345); Bob (67890)"
            chunks.push(lower(multi));
        }

        // Single-assignment hardware fields as a fallback
        const singleName = lower(row.assignedTo ?? row.AssignedTo);
        const singleEmp = lower(row.assignedEmployeeNumber ?? row.AssignedEmployeeNumber);

        if (singleName) chunks.push(singleName);
        if (singleEmp) chunks.push(singleEmp);

        // If nothing at all, treat as "unassigned"
        if (!chunks.length) return "unassigned";

        // Join everything into one search string
        return chunks.join(" | ");
    }

    // Lightweight notifier (routes to global error toast if available; falls back gracefully)
    function showErrorToast(msg) {
        try {
            const el = document.getElementById("errorToast");
            if (!el) {
                // fall back to any global AIMS/toastr/alert if toast HTML not present
                notify(msg);
                return;
            }

            const body = el.querySelector(".toast-body");
            if (body) {
                body.textContent = msg;
            }

            // Use Bootstrap 5 Toast API
            const toast = bootstrap.Toast.getOrCreateInstance(el);
            toast.show();
        } catch {
            notify(msg);
        }
    }

    // Generic notifier used as a fallback
    const notify = (msg) =>
    (window.AIMS?.Toast?.show?.(msg)
        || window.AIMS?.notify?.(msg)
        || window.toastr?.info?.(msg)
        || alert(msg));

    function setToolbarVisible(visible) {
        if (toolbar) toolbar.hidden = !visible;
        if (filterFab) filterFab.hidden = !visible;
        document.body.classList.toggle("is-blank", !visible);
    }

    function hideArchivedToggles() {
        // Popover checkbox (FAB filters panel)
        const pop = archivedToggle?.closest('[data-component="filter-icon"]');
        if (pop) {
            pop.hidden = true;              // HTML hidden attribute
            pop.style.display = "none";     // belt-and-suspenders
        }

        // Header checkbox next to the Search title
        const header = inlineArchivedToggle?.closest(".toggle-container");
        if (header) {
            header.hidden = true;           // HTML hidden attribute
            header.style.display = "none";  // ensure it’s gone visually
        }
    }

    function showArchivedToggles() {
        const pop = archivedToggle?.closest('[data-component="filter-icon"]');
        if (pop) {
            pop.hidden = false;
            pop.style.display = "";         // revert to stylesheet default
        }

        const header = inlineArchivedToggle?.closest(".toggle-container");
        if (header) {
            header.hidden = false;
            header.style.display = "";
        }
    }

    function setArchivedToggleState({ checked = false, disabled = false } = {}) {
        showArchived = !!checked;

        // Popover checkbox inside the filter icon panel
        if (archivedToggle) {
            archivedToggle.checked = !!checked;
            archivedToggle.disabled = !!disabled;
        }

        // Inline header checkbox next to the page title
        if (inlineArchivedToggle) {
            inlineArchivedToggle.checked = !!checked;
            inlineArchivedToggle.disabled = !!disabled;
        }

        // Keep popover closed when disabled
        const pop = document.querySelector('[data-component="filter-icon"][data-id="searchFilters"]');
        if (disabled && pop) {
            pop.classList.remove("open");
            pop.hidden = true;
        }
    }

    // Sensitivity config and forgiving/strict variants
    const ELLIPSIS_SENSITIVITY = {
        pxSlack: 16,
        ratio: 0.93
    };

    // Strict: use for tooltips (only when truly clipped)
    function isTrulyEllipsed(td) {
        const target = td.querySelector('.ellip') || td;
        const cs = getComputedStyle(target);

        const singleLine = cs.whiteSpace === "nowrap";
        const ellipsing = (cs.textOverflow === "ellipsis") || (cs.overflow === "hidden");
        if (!singleLine || !ellipsing) return false;

        if ((target.scrollWidth - target.clientWidth) > 0.5) return true;

        const r = document.createRange();
        r.selectNodeContents(target);
        const textW = r.getBoundingClientRect().width;
        const cellW = target.getBoundingClientRect().width;
        return textW > (cellW + 0.8);
    }

    // Forgiving: use for hover expansion (expand sooner)
    function isNearEllipsed(td) {
        const target = td.querySelector('.ellip') || td;
        const cs = getComputedStyle(target);

        const singleLine = cs.whiteSpace === "nowrap";
        const ellipsing = (cs.textOverflow === "ellipsis") || (cs.overflow === "hidden");
        if (!singleLine || !ellipsing) return false;

        // If actually ellipsed, expand.
        if ((target.scrollWidth - target.clientWidth) > 0.5) return true;

        // Pixel slack: allow near-overflow
        const delta = target.scrollWidth - target.clientWidth;
        if (delta > -ELLIPSIS_SENSITIVITY.pxSlack) return true;

        // Ratio: content almost fills the cell
        const r = document.createRange();
        r.selectNodeContents(target);
        const textW = r.getBoundingClientRect().width;
        const cellW = target.getBoundingClientRect().width;
        if (cellW > 0 && (textW / cellW) >= ELLIPSIS_SENSITIVITY.ratio) return true;

        return false;
    }

    // Re-run tooltips after any layout change (keeps titles accurate)
    function refreshVisibleTooltips() {
        const visibleRows = Array.from(document.querySelectorAll("#table-body > tr:not([hidden])"));
        visibleRows.forEach(tr => Array.from(tr.cells).forEach(td => {
            if (isTrulyEllipsed(td)) td.title = td.textContent.trim();
            else td.removeAttribute("title");
        }));
    }

    // ----- Public refresh helpers ---------------------------------------------
    AIMS.Search.refresh = function refresh() {
        applyFiltersAndRender({ page: currentPage });
    };
    /**
     * Start a (possibly fresh) search for a new query.
     * When fresh=true (default), this:
     *  - Resets all filters to defaults
     *  - Turns OFF "Show archived"
     *  - Updates the URL's ?searchQuery= param via history.pushState
     *  - Clears any restored filter snapshot for the old query
     */
    AIMS.Search.refreshQuery = async function (newQuery, { fresh = true } = {}) {
        const clean = String(newQuery ?? "").trim();
        if (!clean) return;

        // Treat any change in search term as a fresh search unless explicitly disabled
        const currentParamQ = activeQuery();
        const isNewTerm = clean !== (currentParamQ || "") || clean !== (lastQuery || "");
        const doFresh = fresh || isNewTerm;

        if (doFresh) {
            _justDidFreshSearch = true;
            resetAllFiltersAndState();
            restoredFiltersApplied = false; // ensure fetchInitial doesn’t re-apply old snapshot
            // Update the browser URL so reloads / sharing keep the term
            try {
                const url = new URL(window.location.href);
                url.searchParams.set("searchQuery", clean);
                history.pushState({}, "", url.toString());
            } catch { /* non-fatal */ }
        }

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
            r.className = "no-results-row";
            applyScope(r);
            const td = document.createElement("td");
            applyScope(td);
            td.colSpan = 5;
            td.textContent = "No matching results found";
            r.appendChild(td);
            tbody.appendChild(r);
        }
        r.style.display = "none";
        return r;
    }

    // Assignment cell (includes 👤 button for Admin/Supervisor)
    function renderAssignmentCell(row) {
        const canShowButton = CAN_ADMIN;

        const isSoftware = (row.softwareID ?? row.SoftwareID ?? row.softwareId) != null;
        const totalSeats = Number(row.licenseTotalSeats ?? row.LicenseTotalSeats ?? 0);
        const usedSeats = Number(row.licenseSeatsUsed ?? row.LicenseSeatsUsed ?? 0);
        const currentUserId = String(row.assignedUserId ?? row.assignedEmployeeNumber ?? "");

        const wrap = document.createElement("span");
        wrap.className = "assn-cell";
        applyScope(wrap);

        if (isSoftware) {
            const seatChip = document.createElement("span");
            seatChip.className = "seats-count";
            // show "used / total"
            const safeUsed = Math.max(0, Math.min(usedSeats, totalSeats));
            seatChip.textContent = `${safeUsed}/${totalSeats}`;
            seatChip.title = `${safeUsed} of ${totalSeats} seats in use`;
            wrap.appendChild(seatChip);
        } else {
            const displayName = (row.assignedEmployeeNumber && row.assignedTo)
                ? `${row.assignedTo} (${row.assignedEmployeeNumber})`
                : (row.assignedTo || "Unassigned");
            const name = document.createElement("span");
            name.className = "assigned-name";
            name.dataset.userId = currentUserId;
            name.textContent = displayName;
            wrap.appendChild(name);
        }

        if (!canShowButton) return wrap;

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
        const statusL = getStatus(row); // "assigned", "available", "archived", ...
        const statusText = computeStatus(row).toLowerCase();
        const isActionable = isSoftware
            ? (statusText !== "archived") // software: anything but Archived can open seat mgmt
            : (statusL === "assigned" || statusL === "available");


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

        const frag = document.createDocumentFragment();
        frag.appendChild(wrap);
        frag.appendChild(document.createTextNode(" "));
        frag.appendChild(btn);
        return frag;
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
            if (swId != null) {
                tr.setAttribute("data-id", `sw-${swId}`);
            }

            // set raw now; we'll overwrite with computed below for consistency
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
            const statusText = computeStatus(row);
            const pill = document.createElement("span");
            pill.className = "status " + (statusText ? statusText.toLowerCase().replace(/\s+/g, "") : "");
            pill.textContent = statusText;
            tdStat.appendChild(pill);
            applyScope(tdStat);

            // keep dataset.status aligned with the pill (computed, not raw)
            tr.dataset.status = statusText;

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
        if (isNearEllipsed(td)) {
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
        const iconBtn = e.target.closest(".icon-btn");
        if (iconBtn) {
            e.preventDefault();
            e.stopPropagation();

            // If disabled, show friendly toast and exit
            if (iconBtn.dataset.disabled === "1" || iconBtn.classList.contains("icon-btn--disabled")) {
                showErrorToast("This asset isn’t actionable right now (archived or locked).");
                return;
            }

            const rowEl = iconBtn.closest("tr.result");
            const tag = rowEl?.dataset.tag || "";
            const numericId = iconBtn.dataset.assetNumericId || rowEl?.dataset.assetId || "";
            const assetKind = Number(iconBtn.dataset.assetKind || "1");
            const currentUserId = iconBtn.dataset.currentUserId || "";

            // Prefer matching by ID for the correct kind; only fall back to tag if needed.
            const rowData = cacheAllItems.find(it => {
                const itTag = getTag(it);

                const itSwId = it.softwareID ?? it.SoftwareID ?? it.softwareId ?? null;
                const itHwId = it.hardwareID ?? it.HardwareID ?? it.hardwareId ?? null;

                const idMatch = assetKind === 2
                    ? (itSwId != null && String(itSwId) === String(numericId))
                    : (itHwId != null && String(itHwId) === String(numericId));

                // If ID matches for the right kind, we’re done.
                if (idMatch) return true;

                // Fallback: tag match (only if tag is non-empty)
                if (itTag && itTag === tag) return true;

                return false;
            });

            // Seat counts only matter for software; for hardware they’ll be ignored.
            const totalSeats = Number(rowData?.licenseTotalSeats ?? rowData?.LicenseTotalSeats ?? NaN);
            const usedSeats = Number(rowData?.licenseSeatsUsed ?? rowData?.LicenseSeatsUsed ?? NaN);

            // Source of truth: assetKind (1 = hardware, 2 = software)
            const isSoftware = (assetKind === 2);

            if (isSoftware) {
                const avail = Math.max(0, totalSeats - usedSeats);
                const currentUserIdSoft = String(
                    rowData?.assignedUserId ?? rowData?.AssignedUserId ?? ""
                ).trim();

                // Mixed case: some seats used, some available → open seat mode chooser
                if (avail > 0 && usedSeats > 0) {
                    window.dispatchEvent(new CustomEvent("seat:choose:open", {
                        detail: {
                            assetTag: tag,
                            assetNumericId: numericId,
                            assetKind: 2,
                            seats: { totalSeats, usedSeats, avail },
                            currentUserId: currentUserIdSoft || null
                        }
                    }));
                    return;
                }

                // Simple available-only case → go straight to Assign
                if (avail > 0) {
                    window.dispatchEvent(new CustomEvent("assign:open", {
                        detail: {
                            assetTag: tag,
                            assetNumericId: numericId,
                            assetKind: 2,
                            currentUserId: currentUserIdSoft || null
                        }
                    }));
                    return;
                }

                // Seats are full → open Unassign
                window.dispatchEvent(new CustomEvent("unassign:open", {
                    detail: {
                        assetTag: tag,
                        assetNumericId: numericId,
                        assetKind: 2,
                        currentUserId: currentUserIdSoft || null,
                        totalSeats,
                        usedSeats
                    }
                }));
                return;
            }

            // --- Hardware fallback ---
            const nameEl = rowEl ? rowEl.querySelector(".assigned-name") : null;
            const currentDisplayName = (nameEl?.textContent || "Unassigned").trim();

            // If the label literally says "Unassigned", treat as unassigned.
            // Everything else is “assigned”.
            const isAssigned = currentDisplayName.toLowerCase() !== "unassigned";

            if (isAssigned) {
                window.dispatchEvent(new CustomEvent("unassign:open", {
                    detail: {
                        currentUserId: currentUserId || null,
                        assetTag: tag || null,
                        assetNumericId: numericId || null,
                        assetKind
                    }
                }));
            } else {
                window.dispatchEvent(new CustomEvent("assign:open", {
                    detail: {
                        assetTag: tag,
                        assetNumericId: numericId,
                        assetKind,
                        currentUserId: "",                 // None, we know it’s unassigned
                        currentDisplayName                 // "Unassigned"
                    }
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

        // ALWAYS go to /AssetDetails/Index?category={type}&tag={tag}
        const url = new URL("/AssetDetails/Index", window.location.origin);
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
                const haystack = getAssignmentSearchText(row);
                if (!haystack.includes(v(activeFilters.assignment))) return false;
            }
            if (activeFilters.status && computeStatus(row).toLowerCase() !== v(activeFilters.status)) return false;
            return true;
        });

        // Rebuild facet option lists (Type / Status) from the current result set,
        // but exclude each facet’s own filter when computing its unique values.
        recomputeFacets();
        refreshFacetDropdowns(); // ensures the custom dropdown menus persist selection and only show valid options

        const total = filteredItems.length;
        const totalPages = Math.max(1, Math.ceil(total / pageSize));
        currentPage = Math.min(Math.max(1, page), totalPages);

        const noRow = ensureNoResultsRow();
        noRow.style.display = (total === 0 && (lastQuery ?? "").trim() !== "") ? "" : "none";

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
        persistFiltersSnapshot();
    }

    // Inputs/selects → live (debounced) local filtering + persist
    inputName?.addEventListener("input", e => { activeFilters.name = e.target.value; debouncedApply(); persistFiltersSnapshot(); });
    inputTag?.addEventListener("input", e => { activeFilters.tag = e.target.value; debouncedApply(); persistFiltersSnapshot(); });
    inputAssn?.addEventListener("input", e => { activeFilters.assignment = e.target.value; debouncedApply(); persistFiltersSnapshot(); });

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

        pager.hidden = (totalPages <= 1);
    }

    btnPrev?.addEventListener("click", () => {
        if (currentPage > 1) { applyFiltersAndRender({ page: currentPage - 1 }); persistFiltersSnapshot(); }
    });
    btnNext?.addEventListener("click", () => {
        applyFiltersAndRender({ page: currentPage + 1 }); persistFiltersSnapshot();
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
            hideArchivedToggles();

            if (pager) pager.hidden = true;
            if (adminWrapper) adminWrapper.classList.remove("table-has-rows");
            return;
        }

        // non-blank path → ensure UI is shown and toggle enabled
        setToolbarVisible(true);
        setArchivedToggleState({ checked: showArchived, disabled: false })
        showArchivedToggles();

        lastQuery = q ?? "";
        // First-load rule: never query archived unless the user explicitly toggled it on.
        const key = makeCacheKey(lastQuery, showArchived);
        const cached = getEntry(key);
        // If we already have a cache entry, use it immediately.
        if (cached && (cached.items?.length || 0) > 0) {
            cacheKey = key;
            cacheAllItems = cached.items.slice(0, MAX_CLIENT_CACHE);
            cacheLoadedPages = new Set(cached.loadedPages || []);
            pageSize = cached.pageSize || pageSizeArg;
            if (!restoredFiltersApplied) {
                activeFilters = { name: "", type: "", tag: "", assignment: "", status: "" };
            }
            applyFiltersAndRender({ page: restoredFiltersApplied ? (currentPage || 1) : 1 });
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
                        const isArch = (row) => {
                            const raw = (row.isArchived ?? row.IsArchived ?? row.archived ?? row.Archived);
                            return asTrue(raw);
                        };
                        const activeOnly = cacheAllItems.filter(r => !isArch(r)).slice(0, MAX_CLIENT_CACHE);
                        setEntry(keyActive, {
                            items: activeOnly,
                            loadedPages: [],
                            pageSize,
                            ts: Date.now()
                        });
                    }
                }
            }

            // On a fresh search we already reset; otherwise keep restored snapshot
            if (_justDidFreshSearch) {
                // ensure selects’ option lists reflect the fresh state
                recomputeFacets();
                refreshFacetDropdowns();
                _justDidFreshSearch = false;
            } else if (!restoredFiltersApplied) {
                activeFilters = { name: "", type: "", tag: "", assignment: "", status: "" };
            }

            applyFiltersAndRender({ page: restoredFiltersApplied ? (currentPage || 1) : 1 });

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
        const ths = headerTable?.querySelectorAll("thead th");
        if (!ths?.length || !toolbarGrid || !toolbar) return;
        ths.forEach((th, i) => {
            const rect = th.getBoundingClientRect();
            toolbarGrid.style.setProperty(`--col${i + 1}`, `${Math.round(rect.width)}px`);
        });
        toolbar.classList.add("synced");
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

        // give the list an ID and wire aria-controls
        const listId = `${selectEl.id || selectEl.name || "aims-select"}-listbox`;
        list.id = listId;
        btn.setAttribute("aria-controls", listId);

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

        // Insert wrapper where the <select> was
        selectEl.parentElement.insertBefore(wrapper, selectEl);
        wrapper.appendChild(btn);
        wrapper.appendChild(selectEl);     // keep the real <select> with the button

        // Portal the dropdown list to <body> so it escapes table clipping
        document.body.appendChild(list);

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

            // ensure something is active
            const cur = list.querySelector('.aims-custom-select__option[aria-current="true"]')
                || list.querySelector('.aims-custom-select__option[aria-selected="true"]')
                || list.querySelector('.aims-custom-select__option');

            if (cur) {
                cur.setAttribute("data-active", "true");
                cur.scrollIntoView({ block: "nearest" });
            }

            // move focus into the listbox so Arrow keys work
            list.focus({ preventScroll: true });
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
                wrapper.parentElement?.classList?.add("has-aims-select");
                wrapper.setAttribute("data-chosen", "true");
                if (typeof onChange === "function") onChange(val, next.textContent);
            }
            next.scrollIntoView({ block: "nearest" });
        }

        btn.addEventListener("click", () => {
            const isOpen = wrapper.classList.contains("aims-custom-select--open");
            if (isOpen) close({ reason: "button" }); else open();
        });

        btn.addEventListener("keydown", (e) => {
            const isOpen = wrapper.classList.contains("aims-custom-select--open");

            // Open on Enter / Space / ArrowDown / ArrowUp
            if (!isOpen && (e.key === "Enter" || e.key === " " || e.key === "ArrowDown" || e.key === "ArrowUp")) {
                e.preventDefault();
                open();
                return;
            }

            // Close on Escape when open
            if (isOpen && e.key === "Escape") {
                e.preventDefault();
                close({ reason: "keyboard" });
                return;
            }
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
            customType = makeCustomSelect(selectType, { onChange: (_v, label) => { setType(label); persistFiltersSnapshot(); } });
        }
        if (selectStat) {
            customStatus = makeCustomSelect(selectStat, { onChange: (_v, label) => { setStatus(label); persistFiltersSnapshot(); } });
        }

        // still listen for native change (devtools/manual changes)
        selectType?.addEventListener("change", (e) => { setType(e.target.value); persistFiltersSnapshot(); });
        selectStat?.addEventListener("change", (e) => { setStatus(e.target.value); persistFiltersSnapshot(); });
    }

    // --- Facets: compute and wire dropdown contents to current results ----------
    function recomputeFacets() {
        // Base set excludes archived when toggle is off and excludes the facet itself.
        const v = (s) => (s || "").trim().toLowerCase();
        const baseForBoth = cacheAllItems.filter(row => {
            if (!showArchived && isArchivedRow(row)) return false;
            if (activeFilters.name && !(row.assetName || "").toLowerCase().includes(v(activeFilters.name))) return false;
            if (activeFilters.tag && !getTag(row).toLowerCase().includes(v(activeFilters.tag))) return false;
            if (activeFilters.assignment) {
                const disp = (row.assignedEmployeeNumber && row.assignedTo)
                    ? `${row.assignedTo} (${row.assignedEmployeeNumber})`
                    : (row.assignedTo || "Unassigned");
                if (!disp.toLowerCase().includes(v(activeFilters.assignment))) return false;
            }
            return true;
        });

        // Type facet (do not apply existing Type filter while enumerating)
        const typeSet = new Set();
        for (const r of baseForBoth) {
            const t = String(r.type || "").trim();
            if (t) typeSet.add(t);
        }
        facetTypeValues = Array.from(typeSet).sort((a, b) => a.localeCompare(b));

        // Status facet (do not apply existing Status filter while enumerating)
        const statusSet = new Set();
        for (const r of baseForBoth) {
            const s = String(computeStatus(r) || "").trim();
            if (s) statusSet.add(s);
        }
        facetStatusValues = Array.from(statusSet).sort((a, b) => a.localeCompare(b));
    }

    function refreshFacetDropdowns() {
        if (customType && selectType) {
            repopulateSelect(customType, selectType, facetTypeValues, "All Devices", activeFilters.type);
        }
        if (customStatus && selectStat) {
            repopulateSelect(customStatus, selectStat, facetStatusValues, "All Status", activeFilters.status);
        }
    }

    // Update both the hidden native <select> and the custom listbox to only include provided values.
    // `currentFilterLc` is the lowercase of the selected label to persist selection if still present.
    function repopulateSelect(custom, nativeSelect, values, allLabel, currentFilterLc) {
        // 1) Native select options
        while (nativeSelect.options.length) nativeSelect.remove(0);
        const addOpt = (text, val = text) => {
            const o = document.createElement("option");
            o.textContent = text;
            o.value = text; // we use label text as value consistently
            nativeSelect.appendChild(o);
        };
        addOpt(allLabel, "");
        values.forEach(v => addOpt(v));

        // Choose selection
        const desiredLabel = (currentFilterLc || "").trim();
        // Default: if there is no explicit filter, stay on "All ..." even if only one value exists
        let effectiveLabel = allLabel;

        // If we *do* have an explicit filter and it still exists, keep it
        if (desiredLabel && values.some(v => v.toLowerCase() === desiredLabel)) {
            effectiveLabel = values.find(v => v.toLowerCase() === desiredLabel);
        }

        // Keep activeFilters in sync with whatever label we picked
        if (custom === customType) {
            activeFilters.type = (effectiveLabel === allLabel ? "" : effectiveLabel.toLowerCase());
        } else if (custom === customStatus) {
            activeFilters.status = (effectiveLabel === allLabel ? "" : effectiveLabel.toLowerCase());
        }
        
        // Set native selected index
        const toSelect = Array.from(nativeSelect.options).findIndex(o =>
            (o.textContent || "").trim().toLowerCase() === (effectiveLabel || allLabel).toLowerCase()
        );
        nativeSelect.selectedIndex = Math.max(0, toSelect);

        // 2) Custom listbox options
        const list = custom.listbox;
        list.innerHTML = "";
        const mk = (label) => {
            const div = document.createElement("div");
            div.className = "aims-custom-select__option";
            div.setAttribute("role", "option");
            div.setAttribute("data-value", label);
            div.textContent = label;
            return div;
        };
        const allDiv = mk(allLabel);
        list.appendChild(allDiv);
        values.forEach(v => list.appendChild(mk(v)));

        // Mark selected
        const want = (effectiveLabel || allLabel).toLowerCase();
        const options = Array.from(list.children);
        options.forEach(o => {
            const lab = (o.textContent || "").trim().toLowerCase();
            if (lab === want) {
                o.setAttribute("aria-selected", "true");
                o.setAttribute("aria-current", "true");
                o.setAttribute("data-active", "true");
            } else {
                o.removeAttribute("aria-selected");
                o.removeAttribute("aria-current");
                o.removeAttribute("data-active");
            }
        });
        // Update button label & data
        custom.button.querySelector(".aims-custom-select__label").textContent = (effectiveLabel || allLabel);
        custom.button.dataset.value = (effectiveLabel || allLabel);
        // After repopulating, ensure wrapper reflects that a choice exists (prevents hover flash)
        custom.wrapper.setAttribute("data-chosen", "true");
    }


    // ----- Init ----------------------------------------------------------------
    (async function init() {
        hydrateCacheFromSession();
        setArchivedToggleState({ checked: showArchived, disabled: false });
        wireFilterFab();
        initCustomDropdowns();
        scheduleSync(); // sync toolbar widths on boot

        const qForThisPage = IS_SUPERVISOR ? "" : initialQ;
        const restored = readFiltersSnapshotForQuery(qForThisPage);

        if (restored) {
            // 1) Archive toggle: prefer explicit snapshot value only on reload of same query
            showArchived = !!restored.archived;
            if (archivedToggle) archivedToggle.checked = showArchived;

            // 2) Filters object
            activeFilters = { ...(restored.filters || {}) };

            // 3) Reflect into inputs
            if (inputName) inputName.value = activeFilters.name || "";
            if (inputTag) inputTag.value = activeFilters.tag || "";
            if (inputAssn) inputAssn.value = activeFilters.assignment || "";

            // 4) Reflect into selects using the *label text* stored in activeFilters
            if (activeFilters.type) setSelectByLowerText(selectType, activeFilters.type);
            if (activeFilters.status) setSelectByLowerText(selectStat, activeFilters.status);
            // Also sync the custom dropdown chrome immediately after native selection
            queueMicrotask(() => {
                refreshFacetDropdowns();
            });

            // 5) Page
            if (typeof restored.page === "number" && restored.page > 0) {
                currentPage = restored.page;
            }
            restoredFiltersApplied = true;
        }

        // From here, avoid blowing away filters we just restored:
        if (IS_SUPERVISOR) {
            await fetchInitial("", 1, pageSize);
            applyFiltersAndRender({ page: restoredFiltersApplied ? (currentPage || 1) : 1 });
            applyZebra();
            scheduleSync();
            // Now we allow the archive toggle to start notifying.
            _bootArchiveGuard = false;
            return;
        }

        if (initialQ.length > 0) {
            const key = makeCacheKey(initialQ, showArchived);
            const hit = getEntry(key);
            if (hit) {
                cacheKey = key;
                cacheAllItems = hit.items.slice(0, MAX_CLIENT_CACHE);
                cacheLoadedPages = new Set(hit.loadedPages || []);
                pageSize = hit.pageSize || pageSize;

                // IMPORTANT: don't clobber restored filters if we have them
                if (!restoredFiltersApplied) {
                    activeFilters = { name: "", type: "", tag: "", assignment: "", status: "" };
                }

                applyFiltersAndRender({ page: currentPage || 1 });
            } else {
                await fetchInitial(initialQ, 1, pageSize);
                if (restoredFiltersApplied) {
                    applyFiltersAndRender({ page: currentPage || 1 });
                }
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
            hideArchivedToggles();
        }

        // Now we allow the archive toggle to start notifying.
        _bootArchiveGuard = false;
        scheduleSync();
    })();

    // Invalidate caches and re-fetch when assets change (assign/unassign/etc.)
    window.addEventListener('assets:changed', async () => {
        try {
            // Drop in-memory registry
            CACHE.clear?.();

            // Drop persisted session cache so reloads don't resurrect stale data
            try { sessionStorage.removeItem(STORAGE_CACHE_KEY); } catch { }

            // Re-query the current term with the current archived toggle
            const q = (lastQuery ?? activeQuery() ?? "").trim();
            await fetchInitial(q, currentPage || 1, pageSize);
            applyFiltersAndRender({ page: currentPage || 1 });
        } catch (e) {
            console.warn("assets:changed refresh failed", e);
            // fallback: at least redraw current page from whatever we have
            applyFiltersAndRender({ page: currentPage || 1 });
        }
    });

    // Keep hardware row + cache in sync after an assign
    window.addEventListener("assign:saved", (ev) => {
        const detail = ev.detail || {};
        const assetKind = Number(detail.assetKind ?? detail.AssetKind);
        // We only care about hardware here (kind = 1)
        if (assetKind !== 1) return;

        const idStr = String(
            detail.assetNumericId ??
            detail.assetId ??
            detail.AssetID ??
            ""
        );

        if (!idStr) {
            console.warn("assign:saved: missing asset id in detail", detail);
            return;
        }

        // ---- 1) Patch in-memory cache so future refreshes are correct ----
        const row = cacheAllItems.find(r =>
            String(r.hardwareID ?? r.HardwareID ?? r.hardwareId ?? "") === idStr
        );

        // Values we expect the event to send back
        const assignedToName = detail.assignedToName ?? detail.assignedName ?? null;
        const assignedEmployeeNumber = detail.assignedEmployeeNumber ?? detail.employeeNumber ?? null;
        const assignedUserId = detail.assignedUserId ?? detail.userId ?? null;

        if (row) {
            if (assignedToName != null) {
                row.assignedTo = assignedToName;
                row.AssignedTo = assignedToName;
            }
            if (assignedEmployeeNumber != null) {
                row.assignedEmployeeNumber = assignedEmployeeNumber;
                row.AssignedEmployeeNumber = assignedEmployeeNumber;
            }

            // Normalize status to Assigned so computeStatus() is consistent
            row.status = "Assigned";
            row.Status = "Assigned";
        }

        // ---- 2) Patch the visible table row ----
        const tr = tbody.querySelector(`tr.result[data-asset-id="${idStr}"]`);
        if (!tr) return;

        // Assignment cell text
        const nameEl = tr.querySelector(".col-assignment .assigned-name");
        if (nameEl) {
            let displayName = nameEl.textContent.trim();

            if (assignedToName && assignedEmployeeNumber) {
                displayName = `${assignedToName} (${assignedEmployeeNumber})`;
            } else if (assignedToName) {
                displayName = assignedToName;
            } else {
                // fallback if we only know it's now assigned
                displayName = "Assigned";
            }

            nameEl.textContent = displayName;

            if (assignedUserId != null) {
                nameEl.dataset.userId = String(assignedUserId);
            }
        }

        // Person icon button state
        const iconBtn = tr.querySelector(".col-assignment .icon-btn");
        if (iconBtn) {
            if (assignedUserId != null) {
                iconBtn.dataset.currentUserId = String(assignedUserId);
            }

            iconBtn.title = "Unassign user";
            iconBtn.setAttribute("aria-label", "Unassign user");
            iconBtn.classList.remove("icon-btn--disabled");
            iconBtn.classList.add("icon-btn--mint");
            iconBtn.dataset.disabled = "0";
        }

        // Status pill
        const pill = tr.querySelector(".col-status .status");
        if (pill) {
            pill.textContent = "Assigned";
            pill.className = "status assigned";
        }

        // row.dataset.status must match what computeStatus() would produce
        tr.dataset.status = "Assigned";

        applyZebra();
        refreshVisibleTooltips();
    });

    // Keep hardware row + cache in sync after an unassign
    window.addEventListener("unassign:saved", (ev) => {
        const detail = ev.detail || {};
        const assetKind = Number(detail.assetKind);
        const assetNumericId = detail.assetNumericId;

        // We only care about hardware here (kind = 1)
        if (assetKind !== 1) return;

        const idStr = String(assetNumericId || "");

        // ---- 1) Patch in-memory cache so future refreshes are correct ----
        const row = cacheAllItems.find(r =>
            String(r.hardwareID ?? r.HardwareID ?? r.hardwareId ?? "") === idStr
        );

        if (row) {
            // Clear assignment fields so the displayName logic collapses to "Unassigned"
            row.assignedTo = null;
            row.AssignedTo = null;
            row.assignedEmployeeNumber = null;
            row.AssignedEmployeeNumber = null;

            // normalize status field
            row.status = "Available";
            row.Status = "Available";
        }

        // ---- 2) Patch the visible table row ----
        const tr = tbody.querySelector(`tr.result[data-asset-id="${idStr}"]`);
        if (!tr) return;

        // Assignment cell name
        const nameEl = tr.querySelector(".col-assignment .assigned-name");
        if (nameEl) {
            nameEl.textContent = "Unassigned";
            // Clear any user id we were tracking on the DOM
            nameEl.removeAttribute("data-user-id");
        }

        // Person icon button: make sure it looks like "Assign", not "Unassign"
        const iconBtn = tr.querySelector(".col-assignment .icon-btn");
        if (iconBtn) {
            // Clear stale currentUserId so future clicks treat this as unassigned
            iconBtn.dataset.currentUserId = "";

            // Flip title / aria-label to assign mode
            iconBtn.title = "Assign user";
            iconBtn.setAttribute("aria-label", "Assign user");

            // Make sure it's in the enabled mint style
            iconBtn.classList.remove("icon-btn--disabled");
            iconBtn.classList.add("icon-btn--mint");
            iconBtn.dataset.disabled = "0";
        }

        // Status pill
        const pill = tr.querySelector(".col-status .status");
        if (pill) {
            pill.textContent = "Available";
            pill.className = "status available";
        }

        // Keep row dataset consistent with what computeStatus() would say
        tr.dataset.status = "Available";

        // Reapply zebra striping after any changes
        applyZebra();
    });

    window.addEventListener("seat:updated", (ev) => {
        const detail = ev.detail || {};

        const softwareId = Number(
            detail.softwareId ??
            detail.softwareID ??
            detail.SoftwareID
        );

        const licenseSeatsUsed = Number(
            detail.licenseSeatsUsed ??
            detail.LicenseSeatsUsed
        );

        const licenseTotalSeats = Number(
            detail.licenseTotalSeats ??
            detail.LicenseTotalSeats
        );

        if (!Number.isFinite(softwareId)) {
            console.warn("seat:updated: missing softwareId in detail", detail);
            return;
        }

        const row = findRowBySoftwareId(softwareId);
        if (!row) {
            console.warn("seat:updated: no cached row for softwareId", softwareId);
            return;
        }

        // merge fresh counts
        row.LicenseSeatsUsed = licenseSeatsUsed;
        row.licenseSeatsUsed = licenseSeatsUsed;
        row.LicenseTotalSeats = licenseTotalSeats;
        row.licenseTotalSeats = licenseTotalSeats;

        const s = computeStatus(row);

        const tr = document.querySelector(`[data-id='sw-${softwareId}']`);
        if (!tr) return;

        const pill = tr.querySelector(".status");
        if (pill) {
            pill.textContent = s;
            pill.className = "status " + s.toLowerCase().replace(/\s+/g, "");
        }

        const chip = tr.querySelector(".seats-count");
        let _chip = chip;
        if (!_chip) {
            const assnCell = tr.querySelector(".col-assignment .assn-cell");
            if (assnCell) {
                _chip = document.createElement("span");
                _chip.className = "seats-count";
                assnCell.prepend(_chip);
            }
        }
        if (_chip && Number.isFinite(licenseSeatsUsed) && Number.isFinite(licenseTotalSeats)) {
            const u = Math.max(0, Math.min(licenseSeatsUsed, licenseTotalSeats));
            _chip.textContent = `${u}/${licenseTotalSeats}`;
            _chip.title = `${u} of ${licenseTotalSeats} seats in use`;
        }

        if ((activeFilters.status || "") !== "") {
            applyFiltersAndRender({ page: currentPage });
        } else {
            applyZebra();
            refreshVisibleTooltips();
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