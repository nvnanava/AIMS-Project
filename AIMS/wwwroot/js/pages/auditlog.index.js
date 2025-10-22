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