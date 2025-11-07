/* ======================================================================
   AIMS Script: admin.index.js
   ----------------------------------------------------------------------
   Purpose
   - Client-side logic for Admin/Index:
     * AAD user typeahead (via /aad-users, debounced with abort support)
     * Add/Edit user modals (Bootstrap)
     * Table filtering (role / show inactive)
     * Column sorting
     * Demo row insert (until backend integration)
     * Re-apply zebra striping after sort/filter/insert

   How it works
   - Uses IDs/classes rendered by Admin/Index.cshtml.
   - All handlers are exposed under AIMS.Admin for razor attributes:
       onclick="AIMS.Admin.openEditUserModal(this)"
       onchange="AIMS.Admin.toggleInactiveUsers()"
       onchange="AIMS.Admin.filterByRole()"
       ...
   - Table uses a fixed header + scrollable body; JS only manipulates
     #adminTable tbody (rows), not the header markup.
   - When backend integration is ready, swap mock inserts/typeahead with real calls.

   Conventions
   - No inline JS other than calling AIMS.Admin.* from the markup.
   - Keep DOM IDs stable: addUserModal, editUserModal, addUserForm, editUserForm,
     adminTable, showInactive, roleFilter, aadUserResults.
   - 4-space indentation, no tabs.
   - After any state change that affects visible rows, call stripeAdminTable().

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
    const escapeHtml = (s) =>
        (s || "").replace(
            /[&<>"']/g,
            (m) =>
                ({
                    "&": "&amp;",
                    "<": "&lt;",
                    ">": "&gt;",
                    '"': "&quot;",
                    "'": "&#39;",
                }[m])
        );
    const escapeRegExp = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const highlight = (text, q) => {
        const safe = escapeHtml(text ?? "");
        if (!q) return safe;
        const re = new RegExp(`(${escapeRegExp(q)})`, "ig");
        return safe.replace(re, "<mark>$1</mark>");
    };
    const text = (node, value) => (node.textContent = value ?? "");

    // ----- Bootstrap modal helpers -----
    function showModalById(id) {
        const el = document.getElementById(id);
        if (!el) return;
        const m = bootstrap.Modal.getOrCreateInstance(el);
        m.show();
        return m;
    }
    function hideModalById(id) {
        const el = document.getElementById(id);
        if (!el) return;
        const m = bootstrap.Modal.getOrCreateInstance(el);
        m.hide();
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
            const users = await aimsFetch(url, { signal: aadAbortCtrl.signal });
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
            resultsList.innerHTML = `<div class="aad-hint">No results for "${escapeHtml(
                query
            )}"</div>`;
            return;
        }

        const frag = document.createDocumentFragment();
        items.forEach((u) => {
            const name = u.displayName || "";
            const email = u.mail || u.userPrincipalName || "";
            const btn = document.createElement("button");
            btn.type = "button";
            btn.className = "aad-user-item";
            btn.innerHTML = `
                <div class="aad-line"><strong>${highlight(
                    name,
                    query
                )}</strong></div>
                <div class="aad-sub">${highlight(email, query)}</div>`;
            btn.onclick = () => {
                const nameInput = document.getElementById("userName");
                const emailInput = document.getElementById("userEmail");
                const idInput = document.getElementById("graphObjectId");
                if (nameInput) nameInput.value = name;
                if (emailInput) emailInput.value = email;
                if (idInput) idInput.value = u.id || ""; // set AAD object id

                //: preflight check
                if (u.id) checkUserExists(u.id);

                resultsList.innerHTML = "";
            };
            frag.appendChild(btn);
        });
        resultsList.appendChild(frag);
    }

    // ----- Modals --------------------------------------------------------
    function openAddUserModal() {
        const form = document.getElementById("addUserForm");
        form?.reset();
        document.getElementById("aadUserResults")?.replaceChildren();
        showModalById("addUserModal");
        document.getElementById("userName")?.focus();
    }
    function closeAddUserModal() {
        hideModalById("addUserModal");
    }

    function openEditUserModal(button) {
        const row = button.closest("tr");
        const index = Array.from(row.parentNode.children).indexOf(row);
        document.getElementById("editUserIndex").value = index;

        const cells = row.getElementsByTagName("td");
        document.getElementById("editUserName").value =
            cells[1].innerText.trim();
        document.getElementById("editUserEmail").value =
            cells[2].innerText.trim();
        document.getElementById("editUserRole").value =
            cells[3].innerText.trim();
        document.getElementById("editUserStatus").value =
            cells[4].innerText.trim();
        document.getElementById("editUserSeparationDate").value =
            cells[5].innerText.trim();

        showModalById("editUserModal");
    }
    function closeEditUserModal() {
        hideModalById("editUserModal");
    }

    // ----- Table Filters / Sorting --------------------------------------

    function toggleInactiveUsers() {
        applyAdminTableFilters();
    }
    function filterByRole() {
        applyAdminTableFilters();
    }

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

        rows.forEach((r) => tbody.appendChild(r));
        table.setAttribute("data-sort-dir", asc ? "asc" : "desc");

        stripeAdminTable();
    }
    function getRoleNameFromId(id) {
        const sel = document.getElementById("userRole");
        if (!sel) return null;
        const opt = [...sel.options].find((o) => Number(o.value) === id);
        return opt ? opt.textContent : null;
    }

    function stripeAdminTable() {
        const rows = Array.from(
            document.querySelectorAll("#adminTable tbody tr.user-row")
        );
        let visibleIndex = 0;
        rows.forEach((r) => {
            r.classList.remove("even-row", "odd-row");
            if (r.style.display === "none") return;
            r.classList.add(visibleIndex % 2 === 0 ? "even-row" : "odd-row");
            visibleIndex++;
        });
    }

    // function applyAdminTableFilters() {
    //     const showInactive = document.getElementById("showInactive")?.checked ?? true;
    //     const roleFilter = document.getElementById("roleFilter")?.value ?? "All";

    //     document.querySelectorAll("#adminTable tbody tr.user-row").forEach(row => {
    //         const isInactive = row.classList.contains("inactive");
    //         const userRole = row.cells[3]?.innerText.trim() || "";
    //         let visible = true;
    //         if (!showInactive && isInactive) visible = false;
    //         if (roleFilter !== "All" && userRole !== roleFilter) visible = false;
    //         row.style.display = visible ? "" : "none";
    //     });

    //     stripeAdminTable(); // <— add this
    // }
    // Add this somewhere after other helper functions, near your other AIMS.Admin methods

    AIMS.Admin.applyAdminTableFilters = function () {
        const showInactive = document.getElementById("showInactive")?.checked ?? true;
        const roleFilter = document.getElementById("roleFilter")?.value ?? "All";
        const searchQuery = document.getElementById("userSearch")?.value.trim().toLowerCase() || "";

        const rows = document.querySelectorAll("#adminTable tbody tr.user-row");

        if (searchQuery === "") {
            rows.forEach(row => (row.style.display = "none"));
            AIMS.Admin.stripeAdminTable?.();
            return;
        }

        rows.forEach(row => {
            const isInactive = row.classList.contains("inactive");
            const userRole = row.cells[3]?.innerText.trim() || "";
            const name = row.cells[1]?.innerText.trim().toLowerCase() || "";

            let visible = true;
            if (!showInactive && isInactive) visible = false;
            if (roleFilter !== "All" && userRole !== roleFilter) visible = false;

            const matchesSearch = name.includes(searchQuery);
            if (!matchesSearch) visible = false;

            row.style.display = visible ? "" : "none";
        });

        AIMS.Admin.stripeAdminTable?.();
    };



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

        stripeAdminTable();
    }

    async function addUser(e) {
        e.preventDefault();
        const btn = document.querySelector("#addUserForm button[type=submit]");
        btn?.setAttribute("disabled", "disabled"); // disable to prevent multiple submits

        const role = document.getElementById("userRole")?.value;
        const status = document.getElementById("userStatus")?.value || "Active";
        const graphId = document.getElementById("graphObjectId")?.value;

        if (!graphId) {
            alert("Please pick a user from the Azure AD suggestions first.");
            btn?.removeAttribute("disabled"); // Re-enable button
            return;
        }

        const roleVal = document.getElementById("userRole")?.value ?? "";
        const roleId = parseInt(roleVal, 10);
        if (!Number.isInteger(roleId)) {
            alert("Please choose a role.");
            btn?.removeAttribute("disabled"); // Re-enable button
            return;
        }

        try {
            const resp = await fetch("/api/admin/users", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ graphObjectId: graphId, roleId }), // roleId (camelCase)
            });

            if (!resp.ok) {
                const msg = await resp.text().catch(() => "");
                alert(msg || "Failed to save user.");
                btn?.removeAttribute("disabled"); // Re-enable button on error
                return;
            }

            const saved = await resp.json();

            // Insert the saved row into the table
            insertUserRow({
                Name: saved.fullName,
                Email: saved.email,
                Role: getRoleNameFromId(roleId) || "",
                Status: status,
                SeparationDate: "",
            });

            applyAdminTableFilters();
            hideModalById("addUserModal");
            document.getElementById("addUserForm")?.reset();
            document.getElementById("aadUserResults")?.replaceChildren();
            btn?.removeAttribute("disabled"); // Re-enable button after success
        } catch (error) {
            console.error("Error adding user:", error);
            alert("An error occurred while adding the user. Please try again.");
            btn?.removeAttribute("disabled"); // Re-enable button on error
        }
    }

    async function checkUserExists(graphId) {
        const notice = document.getElementById("userExistsNotice");
        const submitBtn = document.querySelector(
            "#addUserForm button[type=submit]"
        );
        try {
            const resp = await fetch(
                `/api/admin/users/exists?graphObjectId=${encodeURIComponent(
                    graphId
                )}`
            );
            if (!resp.ok) throw new Error("exists check failed");
            const { exists } = await resp.json();
            if (exists) {
                notice?.classList.remove("d-none");
                submitBtn?.setAttribute("disabled", "disabled");
            } else {
                notice?.classList.add("d-none");
                submitBtn?.removeAttribute("disabled");
            }
        } catch {
            // on error, be conservative: allow submit but hide notice
            notice?.classList.add("d-none");
            submitBtn?.removeAttribute("disabled");
        }
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

        row.cells[5].innerText =
            document.getElementById("editUserSeparationDate").value || " ";
        hideModalById("editUserModal");
        applyAdminTableFilters();
        stripeAdminTable();
    }

    // ----- DOM Ready -----------------------------------------------------
    window.addEventListener("DOMContentLoaded", () => {
        // ----- Hide all users initially -----
        const rows = document.querySelectorAll("#adminTable tbody tr.user-row");
        rows.forEach(row => (row.style.display = "none"));

        // ----- Typeahead search for AAD (debounced) -----
        const nameInput = document.getElementById("userName");
        if (nameInput) {
            nameInput.addEventListener("input", () => {
                const q = nameInput.value.trim();
                clearTimeout(aadDebounceTimer);
                aadDebounceTimer = setTimeout(() => searchAAD(q), 250);
            });
        }

        // ----- Click-away to close the AAD suggestion list -----
        document.addEventListener("click", (e) => {
            const box = document.getElementById("aadUserResults");
            const nameEl = document.getElementById("userName");
            if (
                box &&
                nameEl &&
                !box.contains(e.target) &&
                e.target !== nameEl
            ) {
                box.innerHTML = "";
            }
        });

        // ----- Keep striping updated -----
        stripeAdminTable();
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
        sortTable,
    });


    // document.addEventListener("DOMContentLoaded", () => {
    //     // Focus on search bar for convenience
    //     document.getElementById("userSearch")?.focus();

    //     // Hide all rows initially
    //     AIMS.Admin.applyAdminTableFilters();
    // });

    document.addEventListener("DOMContentLoaded", () => {
        // Hide all users initially
        const rows = document.querySelectorAll("#adminTable tbody tr.user-row");
        rows.forEach(row => (row.style.display = "none"));
    });


})();
