//Moved Admin js logic for AAD search, modal handling, table filtering, and user addition into this file.

(() => {
    let aadDebounceTimer = null; // for debouncing input
    let aadAbortCtrl = null; // for aborting fetch

    // ---- Utilities ----
    function highlight(text, q) {
        // highlight search term in text, keep or get rid of depending on client input on 10/17
        const safe = escapeHtml(text ?? ""); // prevent XSS
        if (!q) return safe; // nothing to highlight
        const re = new RegExp(`(${escapeRegExp(q)})`, "ig");
        return safe.replace(re, "<mark>$1</mark>"); // wrap matches in <mark>
    }
    function escapeHtml(s) {
        //  HTML escaping to prevent XSS
        return (s || "").replace(
            /[&<>"']/g, // match any of these characters
            (m) =>
                ({
                    "&": "&amp;", // replace with corresponding HTML entity
                    "<": "&lt;",
                    ">": "&gt;",
                    '"': "&quot;",
                    "'": "&#39;",
                }[m])
        );
    }
    function escapeRegExp(s) {
        return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"); // escape  regex characters
    }
    function text(node, value) {
        node.textContent = value ?? "";
    }

    // ---- AAD search / render from Entra ----
    async function searchAAD(query) {
        // search AAD users via backend
        const resultsList = document.getElementById("aadUserResults"); // container for results
        if (!resultsList) return; // safety check
        resultsList.innerHTML = "";
        if (!query) return;

        if (aadAbortCtrl) aadAbortCtrl.abort();
        aadAbortCtrl = new AbortController();

        resultsList.innerHTML = `<div class="aad-hint">Searching…</div>`; // show searching hint
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
        // render search results into the resultsList
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

    // ---- Modal ----
    function openAddUserModal() {
        // open the modal, clear previous inputs/results
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
        // close the modal
        const modal = document.getElementById("addUserModal");
        if (modal) modal.classList.remove("show");
    }

    // ---- Table filters (role + inactive) ----
    function applyAdminTableFilters() {
        const showInactive =
            document.getElementById("showInactive")?.checked ?? true;
        const roleFilter =
            document.getElementById("roleFilter")?.value ?? "All";

        document
            .querySelectorAll("#adminTable tbody tr.user-row")
            .forEach((row) => {
                const isInactive = row.classList.contains("inactive");
                const userRole = row.cells[3]?.innerText.trim() || "";
                let visible = true;
                if (!showInactive && isInactive) visible = false;
                if (roleFilter !== "All" && userRole !== roleFilter)
                    visible = false;
                row.style.display = visible ? "" : "none";
            });
    }
    function toggleInactiveUsers() {
        // checkbox change handler
        applyAdminTableFilters();
    }
    function filterByRole() {
        applyAdminTableFilters();
    }

    // ---- Insert new user row (client-side for demo, server side implemented in Sprint8) ----
    function insertUserRow(u) {
        const tbody = document.querySelector("#adminTable tbody");
        if (!tbody) return;

        const tr = document.createElement("tr");
        const statusClass = (u.Status || "Active").toLowerCase(); // "active" | "inactive"
        tr.className = `user-row ${statusClass}`;

        const tdActions = document.createElement("td");
        tdActions.innerHTML = `
      <button class="icon-btn" title="Edit">
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
        // form submit handler
        e.preventDefault();
        const name = document.getElementById("userName")?.value.trim();
        const email = document.getElementById("userEmail")?.value.trim();
        const role = document.getElementById("userRole")?.value;
        const status = document.getElementById("userStatus")?.value;

        if (!name || !email) return;

        const user = {
            Name: name,
            Email: email,
            Role: role,
            Status: status,
            SeparationDate: "",
        };
        insertUserRow(user);
        applyAdminTableFilters();
        closeAddUserModal();
        document.getElementById("addUserForm")?.reset();
    }

    window.addEventListener("DOMContentLoaded", () => {
        // typeahead
        const nameInput = document.getElementById("userName");
        if (nameInput) {
            nameInput.addEventListener("input", () => {
                const q = nameInput.value.trim();
                clearTimeout(aadDebounceTimer);
                aadDebounceTimer = setTimeout(() => searchAAD(q), 250);
            });
        }

        // click-away to close results
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

        // initial table filter
        applyAdminTableFilters();
    });

    // Expose for inline attributes in  .cshtml
    window.openAddUserModal = openAddUserModal;
    window.closeAddUserModal = closeAddUserModal;
    window.toggleInactiveUsers = toggleInactiveUsers;
    window.filterByRole = filterByRole;
    window.addUser = addUser;
})();
