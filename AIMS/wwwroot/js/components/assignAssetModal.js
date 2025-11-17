// assignAssetModal.js
// Assign from page button (free-pick) or from 👤 event (locked to row)

(function () {
  document.addEventListener("DOMContentLoaded", function () {
    // ---- DOM ----
    const modalEl = document.getElementById("assignAssetModal");
    if (!modalEl) return;
    const modal = new bootstrap.Modal(modalEl);

    const assignBtn = document.getElementById("assign-asset-button");
    const assetSelect = document.getElementById("assetSelect");
    const assetRow = document.getElementById("assetRow") || assetSelect?.closest(".mb-3");
    const formEl = document.getElementById("assignAssetForm");

    const selectedBox = document.getElementById("selectedAssetBox");
    const selName = document.getElementById("selAssetName");
    const selTag = document.getElementById("selAssetTag");
    const selKind = document.getElementById("selAssetKind");

    // Custom user picker
    const userPicker = document.getElementById("userPicker");
    const userList = document.getElementById("userList");
    const userSearchInput = document.getElementById("assignUserSearchInput");
    const userHiddenInput = document.getElementById("userIdHidden");

    const commentBox = document.getElementById("commentBox");

    // ---- utilities ----
    const CAN_ADMIN = (window.__CAN_ADMIN__ === true || window.__CAN_ADMIN__ === "true");
    const IS_SUPERVISOR = (window.__IS_SUPERVISOR__ === true || window.__IS_SUPERVISOR__ === "true");
    const CAN_ASSIGN = CAN_ADMIN || IS_SUPERVISOR;
    const assetsVer = (window.__ASSETS_VER__ ? String(window.__ASSETS_VER__) : String(Date.now()));

    function clearChildren(el) {
      while (el?.firstChild) el.removeChild(el.firstChild);
      if (el && typeof el.scrollTop === "number") {
        el.scrollTop = 0;
      }
    }
    const norm = (v) => (v ?? "").toString();

    // Focus search when modal opens
    modalEl.addEventListener("shown.bs.modal", () => {
      if (userSearchInput) {
        userSearchInput.focus();
        userSearchInput.select();
      }
    });

    /* ======================================================================
       USER PICKER (Custom — no Bootstrap select)
       ====================================================================== */

    const USERS_ENDPOINT = "/api/users/search";
    const USER_PAGE_SIZE = 25;

    let userSearchTerm = "";
    let userPage = 0;
    let userHasMore = true;
    let userIsLoading = false;
    let userSearchDebounce = null;
    let userScrollHooked = false;
    let activeUserIndex = -1; // for keyboard navigation in userList
    let currentSoftwareIdForUserSearch = null; // used to filter out users who already have this software

    function resetUserPaging(term) {
      userSearchTerm = (term ?? "").trim();
      userPage = 0;
      userHasMore = true;
      activeUserIndex = -1;
    }

    async function fetchUsers(searchTerm, page) {
      const url = new URL(USERS_ENDPOINT, window.location.origin);

      if (searchTerm) {
        url.searchParams.set("searchString", String(searchTerm).trim());
      }
      url.searchParams.set("skip", String(page * USER_PAGE_SIZE));
      url.searchParams.set("take", String(USER_PAGE_SIZE));

      if (typeof assetsVer !== "undefined") {
        url.searchParams.set("_v", assetsVer);
      }

      // tell the API which software we're assigning (if any)
      if (currentSoftwareIdForUserSearch != null) {
        url.searchParams.set("softwareId", String(currentSoftwareIdForUserSearch));
      }

      const data = await aimsFetch(url.toString(), { ttl: 5 });

      if (Array.isArray(data)) return data;
      if (Array.isArray(data?.items)) return data.items;
      return [];
    }

    function selectUserOption(optionEl, { moveFocusToComment = true } = {}) {
      if (!optionEl || !userHiddenInput || !userSearchInput) return;

      const userId = optionEl.getAttribute("data-user-id") || "";
      const label = optionEl.getAttribute("data-display-name") || optionEl.textContent || "";

      // Update hidden form value
      userHiddenInput.value = userId;

      // Mark selected + active
      const options = Array.from(userList.querySelectorAll(".aims-userpicker__option"));
      options.forEach((opt, idx) => {
        opt.removeAttribute("aria-selected");
        opt.removeAttribute("data-active");
        if (opt === optionEl) {
          opt.setAttribute("aria-selected", "true");
          opt.setAttribute("data-active", "true");
          activeUserIndex = idx;
        }
      });

      // Show in search box
      userSearchInput.value = label;

      // ✅ Move focus to comment when user explicitly chooses a name
      if (moveFocusToComment && commentBox) {
        commentBox.focus();
        if (typeof commentBox.select === "function") {
          commentBox.select();
        }
      }
    }

    function updateUserActiveIndex(newIndex) {
      const options = Array.from(userList.querySelectorAll(".aims-userpicker__option"));
      if (!options.length) return;

      const clamped = Math.max(0, Math.min(options.length - 1, newIndex));
      activeUserIndex = clamped;

      options.forEach((opt, idx) => {
        if (idx === activeUserIndex) {
          opt.setAttribute("data-active", "true");
          opt.scrollIntoView({ block: "nearest" });
        } else {
          opt.removeAttribute("data-active");
        }
      });
    }

    async function populateUserList(preselectUserId, { append = false } = {}) {
      if (!userList || userIsLoading) return;
      if (append && !userHasMore) return;

      userIsLoading = true;

      try {
        const users = await fetchUsers(userSearchTerm, userPage);

        if (!append) {
          clearChildren(userList);
        }

        if (!users.length && !append) {
          const empty = document.createElement("div");
          empty.className = "aims-userpicker__empty";
          empty.textContent = userSearchTerm ? "No users found for this search." : "No users found.";
          userList.appendChild(empty);
          userHasMore = false;
          return;
        }

        const pre = norm(preselectUserId);
        let foundPreselect = false;

        users.forEach((u) => {
          const displayName = u.name || u.employeeNumber || "(Unknown user)";
          const userId = u.userID;
          const employeeNumber = u.employeeNumber || "";

          const btn = document.createElement("button");
          btn.type = "button";
          btn.className = "aims-userpicker__option";
          btn.setAttribute("role", "option");
          btn.setAttribute("data-user-id", userId);
          btn.setAttribute("data-display-name", displayName);
          btn.setAttribute("data-employee-number", employeeNumber);
          btn.textContent = displayName;

          // Click → set search box, hidden field, and jump to comment box
          btn.addEventListener("click", () => {
            selectUserOption(btn, { moveFocusToComment: true });
          });

          // Click → set search box, hidden field, and jump to comment box
          btn.addEventListener("click", () => {
            selectUserOption(btn, { moveFocusToComment: true });
          });

          // Preselect from currentUserId (from 👤) once on initial load
          if (!append && pre && norm(userId) === pre) {
            btn.setAttribute("aria-selected", "true");
            // if (!userHiddenInput.value) {
            //   userHiddenInput.value = userId;
            //   userSearchInput.value = displayName;
            // }
            foundPreselect = true;
          }

          userList.appendChild(btn);
        });

        if (!append && foundPreselect) {
          const options = Array.from(userList.querySelectorAll(".aims-userpicker__option"));
          const idx = options.findIndex(opt => opt.getAttribute("aria-selected") === "true");
          if (idx >= 0) {
            activeUserIndex = idx;
            options[idx].setAttribute("data-active", "true");
          }
        }

        if (users.length < USER_PAGE_SIZE) {
          userHasMore = false;
        }
      } catch (err) {
        console.error("User lookup failed", err);
      } finally {
        userIsLoading = false;
      }
    }

    function setupUserSearch(preselectUserId, options = {}) {
      if (!userPicker || !userList || !userSearchInput) return;

      const { softwareId = null } = options;

      // store context for fetchUsers()
      const parsedSoftId = Number.parseInt(String(softwareId ?? ""), 10);
      currentSoftwareIdForUserSearch =
        Number.isFinite(parsedSoftId) && parsedSoftId > 0
          ? parsedSoftId
          : null;

      // Reset state
      resetUserPaging("");
      if (userHiddenInput) userHiddenInput.value = "";
      userSearchInput.value = "";
      clearChildren(userList);

      // Input → debounce API search (attach once, reuse handler)
      if (!userSearchInput._aimsBoundInput) {
        userSearchInput.addEventListener("input", (e) => {
          const term = e.target.value || "";
          if (userSearchDebounce) clearTimeout(userSearchDebounce);

          userSearchDebounce = setTimeout(() => {
            resetUserPaging(term);
            clearChildren(userList);
            if (userHiddenInput) userHiddenInput.value = "";
            populateUserList(null, { append: false });
          }, 200);
        });
        userSearchInput._aimsBoundInput = true;
      }

      // Keyboard: from search box, ArrowDown goes into list, Enter picks first
      if (!userSearchInput._aimsBoundKey) {
        userSearchInput.addEventListener("keydown", (ev) => {
          const options = Array.from(userList.querySelectorAll(".aims-userpicker__option"));
          if (ev.key === "ArrowDown") {
            if (!options.length) return;
            ev.preventDefault();
            userList.focus();
            activeUserIndex = 0;
            updateUserActiveIndex(0);
          } else if (ev.key === "Enter") {
            if (!options.length) return;
            ev.preventDefault();
            selectUserOption(options[0], { moveFocusToComment: true });
          }
        });
        userSearchInput._aimsBoundKey = true;
      }

      // Keyboard: inside list – ArrowUp/Down/Home/End/Enter/Escape
      if (!userList._aimsBoundKey) {
        userList.addEventListener("keydown", (ev) => {
          const options = Array.from(userList.querySelectorAll(".aims-userpicker__option"));
          if (!options.length) return;

          if (ev.key === "ArrowDown") {
            ev.preventDefault();
            if (activeUserIndex < 0) {
              activeUserIndex = 0;
            } else {
              activeUserIndex = Math.min(options.length - 1, activeUserIndex + 1);
            }
            updateUserActiveIndex(activeUserIndex);
          } else if (ev.key === "ArrowUp") {
            ev.preventDefault();
            if (activeUserIndex <= 0) {
              // jump back to search bar
              activeUserIndex = -1;
              userList.blur();
              userSearchInput.focus();
              userSearchInput.select();
            } else {
              activeUserIndex = Math.max(0, activeUserIndex - 1);
              updateUserActiveIndex(activeUserIndex);
            }
          } else if (ev.key === "Home") {
            ev.preventDefault();
            updateUserActiveIndex(0);
          } else if (ev.key === "End") {
            ev.preventDefault();
            updateUserActiveIndex(options.length - 1);
          } else if (ev.key === "Enter") {
            ev.preventDefault();
            if (activeUserIndex >= 0 && activeUserIndex < options.length) {
              selectUserOption(options[activeUserIndex], { moveFocusToComment: true });
            }
          } else if (ev.key === "Escape") {
            ev.preventDefault();
            userList.blur();
            userSearchInput.focus();
            userSearchInput.select();
          }
        });
        userList._aimsBoundKey = true;
      }

      // Infinite scroll on the list
      if (!userScrollHooked) {
        userScrollHooked = true;
        userList.addEventListener("scroll", () => {
          const el = userList;
          const nearBottom = el.scrollTop + el.clientHeight >= el.scrollHeight - 4;
          if (nearBottom && userHasMore && !userIsLoading) {
            userPage += 1;
            populateUserList(null, { append: true });
          }
        });
      }

      // Initial load
      populateUserList(preselectUserId ?? null, { append: false });
    }

    /* ======================================================================
       ASSET DROPDOWN (still Bootstrap <select> with custom search input)
       ====================================================================== */

    function ensureSearchInputFor(selectEl, id, placeholder, onInput) {
      if (!selectEl) return null;
      const parent = selectEl.parentElement;
      if (!parent) return null;

      let existing = parent.querySelector(`input#${id}[data-aims-search="1"]`);
      if (existing) {
        if (onInput) {
          existing.removeEventListener("input", existing._aimsHandler || (() => { }));
          existing._aimsHandler = onInput;
          existing.addEventListener("input", onInput);
        }
        return existing;
      }

      const input = document.createElement("input");
      input.type = "search";
      input.placeholder = placeholder;
      input.className = "aimsds-input aimsds-input--search mb-2";
      input.id = id;
      input.setAttribute("data-aims-search", "1");

      input.autocomplete = "off";
      input.setAttribute("autocomplete", "new-password");
      input.spellcheck = false;
      input.autocapitalize = "off";
      input.autocorrect = "off";

      parent.insertBefore(input, selectEl);

      if (typeof onInput === "function") {
        input._aimsHandler = onInput;
        input.addEventListener("input", onInput);
      }

      return input;
    }

    async function populateAssetDropdown(searchTerm = "") {
      if (!assetSelect) return;

      const url =
        `/api/diag/assets?q=${encodeURIComponent(searchTerm)}` +
        `&searchString=${encodeURIComponent(searchTerm)}` +
        `&onlyAvailable=true&take=30&_v=${assetsVer}`;

      const resp = await fetch(url, { cache: "no-store" });
      if (resp.status === 204) {
        clearChildren(assetSelect);
        const opt = document.createElement("option");
        opt.disabled = true; opt.selected = true; opt.value = "";
        opt.text = "No assets";
        assetSelect.appendChild(opt);
        return;
      }

      const results = await aimsFetch(url);

      clearChildren(assetSelect);
      const ph = document.createElement("option");
      ph.disabled = true; ph.selected = true; ph.value = "";
      ph.text = results.length ? "Choose an asset..." : "No assets found";
      assetSelect.appendChild(ph);

      results.forEach(asset => {
        const opt = document.createElement("option");
        opt.value = `(${asset.assetID}) ${asset.assetName}`;
        opt.text = `(${asset.assetID}) ${asset.assetName}`;
        opt.dataset.asset_id = asset.assetID;
        opt.dataset.asset_kind = asset.assetKind;  // 1 or 2
        assetSelect.appendChild(opt);
      });
    }

    async function fetchAssetSummary(kind, id) {
      try {
        const url = kind === 2
          ? `/api/assets/one?softwareId=${id}&_v=${assetsVer}`
          : `/api/assets/one?hardwareId=${id}&_v=${assetsVer}`;
        return await aimsFetch(url);
      } catch {
        return null;
      }
    }

    /* ======================================================================
       FREE-PICK ASSIGN BUTTON (top-right)
       ====================================================================== */

    if (assignBtn) {
      assignBtn.addEventListener("click", async () => {
        if (!CAN_ASSIGN) return;

        if (selectedBox) selectedBox.style.display = "none";
        if (assetRow) assetRow.style.display = "";
        if (commentBox) commentBox.value = "";
        if (userHiddenInput) userHiddenInput.value = "";
        if (userSearchInput) userSearchInput.value = "";
        clearChildren(userList);

        try {
          // Users: custom picker (no software filter yet)
          setupUserSearch(null, { softwareId: null });  // 🔹 CHANGED

          // Assets: search + dropdown
          ensureSearchInputFor(
            assetSelect,
            "assignAssetSearchInput",
            "Search assets…",
            (e) => populateAssetDropdown(e.target.value)
          );

          await populateAssetDropdown("");
          modal.show();
        } catch (err) {
          console.error(err);
        }
      });
    }

    /* ======================================================================
       ASSIGN FROM SEARCH ROW (👤)
       ====================================================================== */

    window.addEventListener("assign:open", async (ev) => {
      if (!CAN_ASSIGN) return;

      const d = ev.detail || {};
      const { assetTag, assetNumericId, assetKind, currentUserId, assetName } = d;

      const parsedId = Number.parseInt(String(assetNumericId ?? ""), 10);
      const valid = Number.isFinite(parsedId) && parsedId > 0;

      if (commentBox) commentBox.value = "";
      if (userHiddenInput) userHiddenInput.value = "";
      if (userSearchInput) userSearchInput.value = "";
      clearChildren(userList);

      if (valid) {
        let name = (assetName || "").trim();
        let tag = (assetTag || "").trim();
        if (!name || !tag) {
          const one = await fetchAssetSummary(assetKind, parsedId);
          if (one) {
            name = name || (one.assetName || "");
            tag = tag || (one.tag || "");
          }
        }
        selName.textContent = name || "—";
        selTag.textContent = tag || `(${parsedId})`;
        selKind.textContent = (Number(assetKind) === 2 ? "Software" : "Hardware");
        selectedBox.style.display = "";

        clearChildren(assetSelect);
        const opt = document.createElement("option");
        opt.value = `(${parsedId})`;
        opt.text = name ? `${name} (${tag || parsedId})` : `(${parsedId}) Selected from search`;
        opt.dataset.asset_id = parsedId;
        opt.dataset.asset_kind = Number(assetKind) === 2 ? 2 : 1;
        assetSelect.appendChild(opt);
        assetSelect.selectedIndex = 0;
        if (assetRow) assetRow.style.display = "none";
      } else {
        if (selectedBox) selectedBox.style.display = "none";
        if (assetRow) assetRow.style.display = "";
        ensureSearchInputFor(
          assetSelect,
          "assignAssetSearchInput",
          "Search assets…",
          (e) => populateAssetDropdown(e.target.value)
        );
        try { await populateAssetDropdown(""); } catch (e) { console.error(e); }
      }

      // If this is a software row, filter out users who already have this software
      const softwareIdForUserList =
        Number(assetKind) === 2 && valid ? parsedId : null;

      // Users: custom picker
      setupUserSearch(currentUserId ?? null, {
        softwareId: softwareIdForUserList
      });

      modal.show();
    });

    /* ======================================================================
       SUBMIT HANDLER
       ====================================================================== */

    formEl?.addEventListener("submit", async function (e) {
      e.preventDefault();

      // Manual check: hidden inputs don't participate in HTML5 "required"
      if (!userHiddenInput || !userHiddenInput.value) {
        const toastElement = document.getElementById("errorToast");
        const msg = "Please choose a user before assigning.";
        if (toastElement) {
          const errorToast = new bootstrap.Toast(toastElement, { delay: 3000 });
          toastElement.querySelector(".toast-body").innerHTML = msg;
          errorToast.show();
        } else {
          alert(msg);
        }
        // Also visually nudge the search box
        if (userSearchInput) {
          userSearchInput.focus();
          userSearchInput.classList.add("is-invalid");
          setTimeout(() => userSearchInput.classList.remove("is-invalid"), 1200);
        }
        return;
      }

      // HTML5 validation for the rest (asset + comment)
      if (!formEl.checkValidity()) {
        formEl.classList.add("was-validated");
        formEl.reportValidity();
        return;
      }

      const selectedUserId = Number(userHiddenInput.value);
      const aOpt = assetSelect.options[assetSelect.selectedIndex];
      if (!aOpt) return;

      const numericId = Number(aOpt.dataset.asset_id);
      const assetKind = Number(aOpt.dataset.asset_kind) === 2 ? 2 : 1;

      if (!Number.isFinite(numericId) || numericId <= 0) {
        const toastElement = document.getElementById("errorToast");
        const msg = "No valid asset selected.";
        if (toastElement) {
          const errorToast = new bootstrap.Toast(toastElement, { delay: 3000 });
          toastElement.querySelector(".toast-body").innerHTML = msg;
          errorToast.show();
        } else {
          alert(msg);
        }
        return;
      }

      const rawComment = (commentBox?.value || "").trim();
      const comment = rawComment;

      const isSoftware = assetKind === 2;
      const url = isSoftware ? "/api/software/assign" : "/api/assign/create";

      const payload = isSoftware
        ? {
          softwareId: numericId,
          userId: selectedUserId,
          ...(comment ? { comment } : {})
        }
        : {
          userID: selectedUserId,
          assetKind,
          HardwareID: numericId,
          ...(comment ? { comment } : {})
        };

      try {
        const resp = await fetch(url, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "Accept": "application/json"
          },
          credentials: "same-origin",
          body: JSON.stringify(payload)
        });

        if (!resp.ok) {
          let msg = `Assignment Failed: ${resp.status}`;
          try {
            const data = await resp.json();
            if (data?.message) msg = data.message;
          } catch {
            try {
              const text = await resp.text();
              if (text) msg = text;
            } catch { /* ignore */ }
          }
          throw new Error(msg);
        }

        let data = null;
        try {
          data = await resp.json();
        } catch {
          // no-op (204 or non-JSON body)
        }

        if (isSoftware && data) {
          window.dispatchEvent(new CustomEvent("seat:updated", {
            detail: {
              softwareId: data.softwareId ?? data.softwareID ?? data.SoftwareID,
              licenseSeatsUsed: data.licenseSeatsUsed ?? data.LicenseSeatsUsed,
              licenseTotalSeats: data.licenseTotalSeats ?? data.LicenseTotalSeats
            }
          }));
        }

        new bootstrap.Toast(document.getElementById("assignToast"), { delay: 3000 }).show();

        // Look up selected user option in the list
        let assignedDisplayName = userSearchInput?.value?.trim() || "";
        let assignedEmployeeNumber = null;

        if (userList) {
          const selectedUserBtn = userList.querySelector(
            ".aims-userpicker__option[aria-selected='true']"
          );
          if (selectedUserBtn) {
            // If API gave us name + emp#, this will usually be "Name (12345)" or similar
            assignedDisplayName = selectedUserBtn.getAttribute("data-display-name") || assignedDisplayName;
            assignedEmployeeNumber = selectedUserBtn.getAttribute("data-employee-number") || null;
          }
        }

        window.dispatchEvent(new CustomEvent("assign:saved", {
          detail: {
            assetId: String(numericId),
            userId: selectedUserId,
            assetKind,
            assignedToName: assignedDisplayName,
            assignedEmployeeNumber: assignedEmployeeNumber
          }
        }));

        // fresh pull + cache invalidation.
        // The UI will already look correct thanks to assign:saved.
        //window.dispatchEvent(new Event("assets:changed"));

        modal.hide();

        if (typeof window.refreshSearchTable === "function") {
          setTimeout(() => window.refreshSearchTable(), 150);
        }

        modal.hide();
      } catch (error) {
        const toastElement = document.getElementById("errorToast");
        const message =
          (error && error.message)
            ? error.message
            : String(error);

        if (toastElement) {
          const errorToast = new bootstrap.Toast(toastElement, { delay: 3000 });
          toastElement.querySelector(".toast-body").innerHTML = message;
          errorToast.show();
        } else {
          alert(message);
        }
      }
    });

    // Upload handler (placeholder)
    const fileInput = document.getElementById("userAgreementUpload");
    fileInput?.addEventListener("change", function () {
      const selectedFile = fileInput.files?.[0];
      if (selectedFile) {
        // TODO: Upload using Graph; log in audit
      }
    });
  });
})();