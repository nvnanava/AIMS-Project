/* ======================================================================
   AIMS Script: admin.index.js
   ----------------------------------------------------------------------
   Purpose
   - Client-side logic for Admin/Index:
     * AAD user typeahead (via /aad-users)
     * Add/Edit user modals
     * Table filtering (role / inactive)
     * Column sorting
     * Demo row insert (until backend integration)

   How it works
   - Uses inline IDs/classes from Admin/Index.cshtml.
   - All handlers are exposed under AIMS.Admin for razor attributes:
       onclick="AIMS.Admin.openEditUserModal(this)"
       onchange="AIMS.Admin.toggleInactiveUsers()"
       ...
   - Any future server integration can swap the mock data insert with real calls.

   Conventions
   - No inline JS other than calling AIMS.Admin.* from the markup.
   - Keep DOM IDs stable: addUserModal, editUserModal, aadUserResults, adminTable,
     showInactive, roleFilter, addUserForm, editUserForm.
   - 4-space indentation, no tabs.

   Public API (AIMS.Admin)
   - openAddUserModal(), closeAddUserModal()
   - openEditUserModal(button), closeEditUserModal()
   - addUser(event), saveUserEdit(event)
   - toggleInactiveUsers(), filterByRole(), sortTable(colIdx)

   ====================================================================== */

(() => {
    "use strict";

    // ----- Namespace -----------------------------------------------------
    window.AIMS = window.AIMS || {};
    AIMS.Admin = AIMS.Admin || {};

    // ----- State ---------------------------------------------------------
    let aadDebounceTimer = null;
    let aadAbortCtrl = null;

    // ----- Utils ---------------------------------------------------------
    function escapeHtml(s) {
        return (s || "").replace(/[&<>"']/g, m => ({
            "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
        })[m]);
    }

    function escapeRegExp(s) {
        return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    }

    function highlight(text, q) {
        const safe = escapeHtml(text ?? "");
        if (!q) return safe;
        const re = new RegExp(`(${escapeRegExp(q)})`, "ig");
        return safe.replace(re, "<mark>$1</mark>");
    }

    function text(node, value) {
        node.textContent = value ?? "";
    }

    // ----- AAD search ----------------------------------------------------
    async function searchAAD(query) {
        const resultsList = document.getElementById("aadUserResults");
        if (!resultsList) return;
        resultsList.innerHTML = "";
        if (!query) return;

        if (aadAbortCtrl) aadAbortCtrl.abort();
        aadAbortCtrl = new AbortController();

        resultsList.innerHTML = `<div class="aad-hint">Searching…</div>`;
        try {
            const url = `/aad-users?search=${encodeURIComponent(query)}&top=8`;
            const resp = await fetch(url, { signal: aadAbortCtrl.signal });
            if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
            const users = await resp.json();
            renderAADResults(users, query);
        } catch (e) {
            if (e.name === "AbortError") return;
            resultsList.innerHTML = `<div class="aad-error">Error searching</div>`;
            console.error(e);
        }
    }

    function renderAADResults(items, query) {
        const resultsList = document.getElementById("aadUserResults");
        if (!resultsList) return;

        resultsList.innerHTML = "";
        if (!Array.isArray(items) || items.length === 0) {
            resultsList.innerHTML = `<div class="aad-hint">No results for "${escapeHtml(query)}"</div>`;
            return;
        }

        const frag = document.createDocumentFragment();
        items.forEach(u => {
            const name = u.displayName || "";
            const email = u.mail || u.userPrincipalName || "";
            const btn = document.createElement("button");
            btn.type = "button";
            btn.className = "aad-user-item";
            btn.innerHTML = `
                <div class="aad-line"><strong>${highlight(name, query)}</strong></div>
                <div class="aad-sub">${highlight(email, query)}</div>
            `;
            btn.onclick = () => {
                const nameInput = document.getElementById("userName");
                const emailInput = document.getElementById("userEmail");
                if (nameInput) nameInput.value = name;
                if (emailInput) emailInput.value = email;
                resultsList.innerHTML = "";
            };
            frag.appendChild(btn);
        });
        resultsList.appendChild(frag);
    }

    // ----- Modals --------------------------------------------------------
    function openAddUserModal() {
        const modal = document.getElementById("addUserModal");
        if (!modal) return;
        modal.classList.add("show");
        const name = document.getElementById("userName");
        const email = document.getElementById("userEmail");
        const results = document.getElementById("aadUserResults");
        if (name) name.value = "";
        if (email) email.value = "";
        if (results) results.innerHTML = "";
        if (name) name.focus();
    }

    function closeAddUserModal() {
        document.getElementById("addUserModal")?.classList.remove("show");
    }

    function openEditUserModal(button) {
        const row = button.closest("tr");
        const index = Array.from(row.parentNode.children).indexOf(row);
        document.getElementById("editUserIndex").value = index;

        const cells = row.getElementsByTagName("td");
        document.getElementById("editUserName").value = cells[1].innerText.trim();
        document.getElementById("editUserEmail").value = cells[2].innerText.trim();
        document.getElementById("editUserRole").value = cells[3].innerText.trim();
        document.getElementById("editUserStatus").value = cells[4].innerText.trim();
        document.getElementById("editUserSeparationDate").value = cells[5].innerText.trim();

        document.getElementById("editUserModal").classList.add("show");
    }

    function closeEditUserModal() {
        document.getElementById("editUserModal")?.classList.remove("show");
    }

    // ----- Table Filters / Sorting --------------------------------------
    function applyAdminTableFilters() {
        const showInactive = document.getElementById("showInactive")?.checked ?? true;
        const roleFilter = document.getElementById("roleFilter")?.value ?? "All";

        document.querySelectorAll("#adminTable tbody tr.user-row").forEach(row => {
            const isInactive = row.classList.contains("inactive");
            const userRole = row.cells[3]?.innerText.trim() || "";
            let visible = true;
            if (!showInactive && isInactive) visible = false;
            if (roleFilter !== "All" && userRole !== roleFilter) visible = false;
            row.style.display = visible ? "" : "none";
        });
    }

    function toggleInactiveUsers() { applyAdminTableFilters(); }
    function filterByRole() { applyAdminTableFilters(); }

    function sortTable(n) {
        const table = document.getElementById("adminTable");
        if (!table) return;
        const tbody = table.tBodies[0];
        const rows = Array.from(tbody?.rows ?? []);
        const asc = table.getAttribute("data-sort-dir") !== "asc";

        rows.sort((a, b) => {
            const x = (a.cells[n]?.innerText || "").toLowerCase();
            const y = (b.cells[n]?.innerText || "").toLowerCase();
            return asc ? x.localeCompare(y) : y.localeCompare(x);
        });

        rows.forEach(r => tbody.appendChild(r));
        table.setAttribute("data-sort-dir", asc ? "asc" : "desc");
    }

    // ----- Insert / Save -------------------------------------------------
    function insertUserRow(u) {
        const tbody = document.querySelector("#adminTable tbody");
        if (!tbody) return;

        const tr = document.createElement("tr");
        const statusClass = (u.Status || "Active").toLowerCase();
        tr.className = `user-row ${statusClass}`;

        const tdActions = document.createElement("td");
        tdActions.innerHTML = `
            <button class="icon-btn" title="Edit" onclick="AIMS.Admin.openEditUserModal(this)">
                <svg viewBox="0 0 16 16" width="16" height="16" class="pencil-svg" aria-hidden="true">
                    <path d="M12.146.146a.5.5 0 01.708 0l3 3a.5.5 0 010 .708l-9.793 9.793a.5.5 0 01-.168.11l-5 2a.5.5 0 01-.65-.65l2-5a.5.5 0 01.11-.168L12.146.146zM11.207 2L3 10.207V13h2.793L14 4.793 11.207 2z"></path>
                </svg>
            </button>`;

        const tdName = document.createElement("td");
        text(tdName, u.Name);
        const tdEmail = document.createElement("td");
        text(tdEmail, u.Email);
        const tdRole = document.createElement("td");
        text(tdRole, u.Role);

        const tdStatus = document.createElement("td");
        tdStatus.innerHTML = `<span class="badge ${statusClass}">${u.Status}</span>`;

        const tdSep = document.createElement("td");
        text(tdSep, u.SeparationDate || " ");

        tr.append(tdActions, tdName, tdEmail, tdRole, tdStatus, tdSep);

        if (tbody.firstChild) tbody.insertBefore(tr, tbody.firstChild);
        else tbody.appendChild(tr);
    }

    function addUser(e) {
        e.preventDefault();
        const name = document.getElementById("userName")?.value.trim();
        const email = document.getElementById("userEmail")?.value.trim();
        const role = document.getElementById("userRole")?.value;
        const status = document.getElementById("userStatus")?.value;
        if (!name || !email) return;

        const user = { Name: name, Email: email, Role: role, Status: status, SeparationDate: "" };
        insertUserRow(user);
        applyAdminTableFilters();
        closeAddUserModal();
        document.getElementById("addUserForm")?.reset();
    }

    function saveUserEdit(event) {
        event.preventDefault();
        const idx = document.getElementById("editUserIndex").value;
        const row = document.querySelectorAll(".user-row")[idx];
        if (!row) return;

        row.cells[1].innerText = document.getElementById("editUserName").value;
        row.cells[2].innerText = document.getElementById("editUserEmail").value;
        row.cells[3].innerText = document.getElementById("editUserRole").value;

        const status = document.getElementById("editUserStatus").value;
        row.cells[4].innerHTML = `<span class="badge ${status.toLowerCase()}">${status}</span>`;
        row.classList.remove("active", "inactive");
        row.classList.add(status.toLowerCase());

        row.cells[5].innerText = document.getElementById("editUserSeparationDate").value || " ";
        closeEditUserModal();
    }

    // ----- DOM Ready -----------------------------------------------------
    window.addEventListener("DOMContentLoaded", () => {
        // Typeahead search for AAD (debounced)
        const nameInput = document.getElementById("userName");
        if (nameInput) {
            nameInput.addEventListener("input", () => {
                const q = nameInput.value.trim();
                clearTimeout(aadDebounceTimer);
                aadDebounceTimer = setTimeout(() => searchAAD(q), 250);
            });
        }

        // Click-away to close the AAD suggestion list
        document.addEventListener("click", (e) => {
            const box = document.getElementById("aadUserResults");
            const nameEl = document.getElementById("userName");
            if (box && nameEl && !box.contains(e.target) && e.target !== nameEl) {
                box.innerHTML = "";
            }
        });

        // Initial filters (respect “Show Inactive” default and role filter)
        applyAdminTableFilters();
    });

    // ----- Expose public API --------------------------------------------
    Object.assign(AIMS.Admin, {
        openAddUserModal,
        closeAddUserModal,
        openEditUserModal,
        closeEditUserModal,
        addUser,
        saveUserEdit,
        toggleInactiveUsers,
        filterByRole,
        sortTable
    });
})();