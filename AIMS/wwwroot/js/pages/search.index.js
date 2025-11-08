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

    // Role flags
    const CAN_ADMIN = (window.__CAN_ADMIN__ === true || window.__CAN_ADMIN__ === "true");
    const IS_SUPERVISOR = (window.__IS_SUPERVISOR__ === true || window.__IS_SUPERVISOR__ === "true");

    // ----- Paging / cache state -----------------------------------------------
    let currentPage = 1;
    let pageSize = 5;           // keep in sync with default on server
    let lastResultMeta = null;  // { total, page, pageSize }
    let lastQuery = null;

    // ----- Show archived state -------------------------------------------------
    let showArchived = false;

    // ----- Client cache for cross-page filtering ------------------------------
    const MAX_CLIENT_CACHE = 5000;
    let cacheKey = null;           // `${q}|${archived?1:0}`
    let cacheAllItems = [];        // full result set for current cacheKey
    let cacheLoadedPages = new Set();
    let filteredItems = [];        // result after client filters
    let activeFilters = { name: "", type: "", tag: "", assignment: "", status: "" };

    const debounce = (fn, ms = 250) => { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); }; };
    const debouncedApply = debounce(() => applyFiltersAndRender({ page: 1 }), 250);

    const makeCacheKey = (q, archived) => `${(q || "").trim().toLowerCase()}|${archived ? "1" : "0"}`;

    // Initialize global filter icon logic (archive-filter.js from _Layout)
    try {
        AIMSFilterIcon.init("searchFilters", {
            onChange: ({ showArchived: newVal }) => {
                showArchived = newVal;
                const q = (lastQuery ?? activeQuery() ?? "").trim();
                fetchInitial(q, 1, pageSize);
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

        const closePop = (ev) => {
            if (!pop.classList.contains('open')) return;
            const inside = pop.contains(ev.target) || filterFab.contains(ev.target);
            if (!inside) {
                pop.classList.remove('open');
                pop.hidden = true;
            }
        };

        filterFab.addEventListener("click", () => {
            const r = filterFab.getBoundingClientRect();
            hiddenFilterAnchor.style.position = "fixed";
            hiddenFilterAnchor.style.left = Math.round(r.left + r.width * 0.5) + "px";
            hiddenFilterAnchor.style.top = Math.round(r.top + r.height * 0.5) + "px";

            // position popover
            pop.style.position = "fixed";
            pop.style.left = Math.max(8, r.left - 8) + "px";
            pop.style.top = Math.round(r.bottom + 8) + "px";
            const willOpen = !pop.classList.contains('open');
            pop.hidden = !willOpen;
            pop.classList.toggle('open', willOpen);
            if (willOpen) pop.querySelector('[data-role="show-archived-toggle"]')?.focus();
        });

        document.addEventListener("click", closePop);
    }

    // If CSS background image failed, inject an <img> fallback so the icon appears
    (function ensureFabIcon() {
        if (!filterFab) return;
        const bg = getComputedStyle(filterFab).backgroundImage || "";
        if (bg === "none" || bg.trim() === "") {
            const img = document.createElement("img");
            img.src = "/img/filter-icon-blue.png";
            img.alt = "";
            img.width = 24;
            img.height = 24;
            img.decoding = "async";
            img.loading = "eager";
            img.style.pointerEvents = "none";
            filterFab.appendChild(img);
            img.addEventListener("error", () => { img.src = "../../img/filter-icon-blue.png"; });
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
    const clearBody = () => { while (tbody.firstChild) tbody.removeChild(tbody.firstChild); };
    const setEmpty = (isEmpty) => { empty.hidden = !isEmpty; };

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
        btn.className = "assign-btn";
        btn.setAttribute("data-testid", "assign-user-btn");

        // normalize API ids
        const swId = row.softwareID ?? row.SoftwareID ?? row.softwareId ?? null;
        const hwId = row.hardwareID ?? row.HardwareID ?? row.hardwareId ?? null;
        const numericId = swId ?? hwId;
        const kind = (swId != null) ? 2 : 1; // 1=hardware, 2=software

        btn.dataset.assetTag = (row.tag ?? "") + "";
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
        updateToolbarCorners();
    }

    // Click handling (delegated)
    tbody.addEventListener("click", (e) => {
        const btn = e.target.closest(".assign-btn");
        if (btn) {
            e.preventDefault();
            e.stopPropagation();

            const rowEl = btn.closest("tr");
            const tag = rowEl?.dataset.tag || "";
            const numericId = btn.dataset.assetNumericId || "";
            const assetKind = Number(btn.dataset.assetKind || "1");

            const currentUserId = btn.dataset.currentUserId || "";
            const nameEl = rowEl ? rowEl.querySelector(".assigned-name") : null;
            const currentDisplayName = (nameEl?.textContent || "Unassigned").trim();

            const isAssigned = (rowEl?.dataset.status || "").toLowerCase() === "assigned"
                || (currentUserId && currentUserId !== "");

            if (isAssigned) {
                window.dispatchEvent(new CustomEvent("unassign:open", {
                    detail: { currentUserId: currentUserId || null, assetTag: tag || null, assetNumericId: numericId || null, assetKind }
                }));
                return;
            }

            window.dispatchEvent(new CustomEvent("assign:open", {
                detail: { assetTag: tag, assetNumericId: numericId, assetKind, currentUserId, currentDisplayName }
            }));
            return;
        }

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

    // ----- Local filtering + render (replaces DOM-only filtering) --------------
    function applyFiltersAndRender({ page = 1 } = {}) {
        const v = (s) => (s || "").trim().toLowerCase();

        filteredItems = cacheAllItems.filter(row => {
            if (activeFilters.name && !(row.assetName || "").toLowerCase().includes(v(activeFilters.name))) return false;
            if (activeFilters.type && (row.type || "").toLowerCase() !== v(activeFilters.type)) return false;
            if (activeFilters.tag && !(row.tag || "").toLowerCase().includes(v(activeFilters.tag))) return false;
            if (activeFilters.assignment) {
                const disp = (row.assignedEmployeeNumber && row.assignedTo)
                    ? `${row.assignedTo} (${row.assignedEmployeeNumber})`
                    : (row.assignedTo || "Unassigned");
                if (!disp.toLowerCase().includes(v(activeFilters.assignment))) return false;
            }
            if (activeFilters.status && (row.status || "").toLowerCase() !== v(activeFilters.status)) return false;
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
    }

    // Inputs/selects → live (debounced) local filtering
    inputName?.addEventListener("input", e => { activeFilters.name = e.target.value; debouncedApply(); });
    inputTag?.addEventListener("input", e => { activeFilters.tag = e.target.value; debouncedApply(); });
    inputAssn?.addEventListener("input", e => { activeFilters.assignment = e.target.value; debouncedApply(); });

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
            if (pager) pager.hidden = true;
            if (adminWrapper) adminWrapper.classList.remove("table-has-rows");
            return;
        }

        lastQuery = q ?? "";
        const key = makeCacheKey(lastQuery, showArchived);
        const sameCache = (key === cacheKey);

        // If same query+archive-state, reuse cache (no server hit)
        if (sameCache && cacheAllItems.length > 0) {
            applyFiltersAndRender({ page: 1 });
            return;
        }

        // New cache
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
            cacheLoadedPages.add(data.page || page);
            pageSize = data.pageSize ?? pageSizeArg;

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

        // Position the listbox as a fixed overlay under the button (no layout shift)
        function positionListbox() {
            const r = btn.getBoundingClientRect();
            list.style.position = "fixed";
            list.style.left = `${Math.round(r.left)}px`;
            list.style.top = `${Math.round(r.bottom + 4)}px`;
            list.style.minWidth = `${Math.round(r.width)}px`;
            list.style.maxWidth = `${Math.round(Math.max(r.width, 240))}px`;
            list.style.zIndex = "2000";
        }

        function open() {
            wrapper.classList.add("aims-custom-select--open");
            btn.setAttribute("aria-expanded", "true");
            positionListbox();
            list.style.display = "block";
            list.focus({ preventScroll: true });
            const cur = list.querySelector('.aims-custom-select__option[aria-current="true"]');
            cur?.scrollIntoView({ block: "nearest" });
        }
        function close({ reason } = {}) {
            wrapper.classList.remove("aims-custom-select--open");
            btn.setAttribute("aria-expanded", "false");
            list.style.display = "none";
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

        // keep the overlay stuck to button on resize/scroll
        window.addEventListener("resize", () => { if (wrapper.classList.contains("aims-custom-select--open")) positionListbox(); }, { passive: true });
        window.addEventListener("scroll", () => { if (wrapper.classList.contains("aims-custom-select--open")) positionListbox(); }, { passive: true });

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
            await fetchInitial(initialQ, 1, pageSize);
        } else {
            cacheAllItems = []; filteredItems = [];
            clearBody();
            setEmpty(true);
            tbody.classList.add("is-empty");
            if (pager) pager.hidden = true;
            applyZebra();
        }
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
        const iconBtn = row.querySelector(".assign-btn");
        if (iconBtn) iconBtn.dataset.currentUserId = userId || "";

        const statusCell = row.querySelector(".col-status");
        if (statusCell) statusCell.textContent = "Assigned";
        row.dataset.status = "Assigned";

        // Update cache too
        const tag = row.dataset.tag;
        const cid = row.dataset.assetId;
        const hit = cacheAllItems.find(it => (it.tag && it.tag === tag) || (String(it.hardwareID ?? it.softwareID ?? "") === String(cid)));
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