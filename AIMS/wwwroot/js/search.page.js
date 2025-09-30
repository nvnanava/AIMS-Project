/* Search page client logic
   - Uses server-rendered window.__CAN_ADMIN__ / window.__IS_SUPERVISOR__ (no whoami fetch)
   - Supervisors auto-load blank (server scopes to self + direct reports)
   - Admin/Helpdesk and others do NOT auto-load on blank
*/
(function () {
    // ----- DOM -----
    const tbody = document.getElementById("table-body");
    const empty = document.getElementById("search-empty");

    // Header filters
    const inputName = document.querySelector('[name="Asset Name"]');
    const selectType = document.querySelector('[name="Type"]');
    const inputTag = document.querySelector('[name="Tag #"]');
    const inputAssn = document.querySelector('[name="Assignment"]');
    const selectStat = document.querySelector('[name="Status"]');

    // Scoped CSS attr (safe if none)
    const scopeAttrName = (() => {
        const host = document.querySelector(".asset-table") || document.body;
        for (const a of host.attributes) if (/^b-[a-z0-9]+$/i.test(a.name)) return a.name;
        const cont = document.querySelector(".table-container") || document.body;
        for (const a of cont.attributes) if (/^b-[a-z0-9]+$/i.test(a.name)) return a.name;
        return null;
    })();
    const applyScope = el => { if (scopeAttrName) el.setAttribute(scopeAttrName, ""); };

    // URL query (?searchQuery=...)
    const params = new URLSearchParams(window.location.search);
    const initialQ = (params.get("searchQuery") || "").trim();

    // Helpers
    const clearBody = () => { while (tbody.firstChild) tbody.removeChild(tbody.firstChild); };
    const setEmpty = e => { empty.hidden = !e; };

    // Zebra striping
    function applyZebra() {
        if (!tbody) return;
        const rows = Array.from(tbody.querySelectorAll(':scope > tr'));
        let i = 0;
        rows.forEach(tr => {
            const hidden = tr.hidden || tr.style.display === 'none';
            tr.classList.remove('zebra-even', 'zebra-odd');
            if (hidden) return;
            tr.classList.add((i++ % 2 === 0) ? 'zebra-even' : 'zebra-odd');
        });
    }
    if (tbody) {
        const mo = new MutationObserver(applyZebra);
        mo.observe(tbody, { childList: true, subtree: false, attributes: true, attributeFilter: ['style', 'hidden'] });
        window.addEventListener('load', applyZebra);
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

    function renderRows(items) {
        clearBody();
        for (const row of (items || [])) {
            const tr = document.createElement("tr");
            tr.className = "result";
            if (canDeepLink) {
                tr.classList.add("row-clickable");     // pointer + hover for admins
                tr.style.cursor = "pointer";
                tr.title = "View details";
            } else {
                tr.setAttribute("aria-disabled", "true"); // supervisors: not clickable
                tr.style.cursor = "default";
            }
            tr.setAttribute("applied-filters", "0");
            tr.dataset.tag = row.tag || "";
            tr.dataset.type = row.type || "";
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
            const assnText = (row.assignedEmployeeNumber && row.assignedTo)
                ? `${row.assignedTo} (${row.assignedEmployeeNumber})`
                : (row.assignedTo || "Unassigned");
            tdAssn.textContent = assnText;
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

    // ---- CLICK HANDLER (delegated) ----
    let canDeepLink = !!window.__CAN_ADMIN__; // reserved for future UI toggles
    tbody.addEventListener('click', (e) => {
        if (!canDeepLink) return;
        const tr = e.target.closest('tr.result');
        if (!tr) return;

        const tag = tr.dataset.tag || "";
        const type = (tr.dataset.type || "").trim();
        if (!tag) return;

        const url = new URL('/Home/AssetDetailsComponent', window.location.origin);
        const category = type || 'Laptop';
        url.searchParams.set('category', category);
        url.searchParams.set('tag', tag);
        window.location.href = url.toString();
    });

    // ---- Client-side filtering ----
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
    if (inputName) inputName.addEventListener("input", e => applyFilterFor("Asset Name", e.target.value));
    if (selectType) selectType.addEventListener("change", e => applyFilterFor("Type", e.target.value));
    if (inputTag) inputTag.addEventListener("input", e => applyFilterFor("Tag #", e.target.value));
    if (inputAssn) inputAssn.addEventListener("input", e => applyFilterFor("Assignment", e.target.value));
    if (selectStat) selectStat.addEventListener("change", e => applyFilterFor("Status", e.target.value));

    // ---- Loading (use GlobalSpinner + keep table blank until min time) ----
    const LOCAL_MIN_VISIBLE_MS = 500;
    let localShownAt = 0;

    function startLoading() {
        localShownAt = performance.now();
        clearBody();
        setEmpty(false);
        GlobalSpinner.show();
    }

    function waitForMinimum() {
        const elapsed = performance.now() - localShownAt;
        const wait = Math.max(0, LOCAL_MIN_VISIBLE_MS - elapsed);
        return new Promise(resolve => setTimeout(resolve, wait));
    }

    // ---- INIT ----
    (async function init() {
        // Supervisors: auto-load blank (server will scope)
        if (window.__IS_SUPERVISOR__) {
            await fetchInitial("", 1, 25);
            applyZebra();
            return;
        }
        // Others: only fetch if there's a query
        if (initialQ.length > 0) {
            await fetchInitial(initialQ, 1, 25);
        } else {
            clearBody();
            setEmpty(true);
            applyZebra();
        }
    })();

    // ---- Fetch ----
    async function fetchInitial(q, page = 1, pageSize = 25) {
        const isBlank = !q || q.trim() === "";

        // Only supervisors may fetch blank
        if (isBlank && !window.__IS_SUPERVISOR__) {
            clearBody();
            setEmpty(true);
            return;
        }

        const url = new URL("/api/assets/search", window.location.origin);
        if (!isBlank) url.searchParams.set("q", q.trim());
        url.searchParams.set("page", String(page));
        url.searchParams.set("pageSize", String(pageSize));

        startLoading();

        try {
            const res = await fetch(url.toString(), { cache: "no-store" });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();

            await waitForMinimum();
            renderRows(data.items || []);
            if (!data.items || data.items.length === 0) setEmpty(true);
        } catch (e) {
            console.error("Search fetch failed:", e);
            clearBody();
            setEmpty(true);
        } finally {
            GlobalSpinner.hide();
        }
    }
})();