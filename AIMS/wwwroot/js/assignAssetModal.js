// assignAssetModal.js
// Assign from page button (free-pick) or from 👤 event (locked to row)

(function () {
  document.addEventListener("DOMContentLoaded", function () {
    // ---- DOM ----
    const modalEl = document.getElementById("assignAssetModal");
    if (!modalEl) return;
    const modal = new bootstrap.Modal(modalEl);

    const assignBtn = document.getElementById("assign-asset-button"); // optional
    const userSelect = document.getElementById("userSelect");
    const assetSelect = document.getElementById("assetSelect");
    const assetRow = document.getElementById("assetRow") || assetSelect?.closest(".mb-3");
    const formEl = document.getElementById("assignAssetForm");

    const selectedBox = document.getElementById("selectedAssetBox");
    const selName = document.getElementById("selAssetName");
    const selTag = document.getElementById("selAssetTag");
    const selKind = document.getElementById("selAssetKind");

    // ---- utilities ----
    const CAN_ADMIN = (window.__CAN_ADMIN__ === true || window.__CAN_ADMIN__ === "true");
    const IS_SUPERVISOR = (window.__IS_SUPERVISOR__ === true || window.__IS_SUPERVISOR__ === "true");
    const CAN_ASSIGN = CAN_ADMIN || IS_SUPERVISOR;
    const assetsVer = (window.__ASSETS_VER__ ? String(window.__ASSETS_VER__) : String(Date.now()));

    function clearChildren(el) { while (el?.firstChild) el.removeChild(el.firstChild); }
    const norm = (v) => (v ?? "").toString();

    // create-at-most-one search input *for this select container*
    function ensureSearchInputFor(selectEl, id, placeholder, onInput) {
      if (!selectEl) return null;
      const parent = selectEl.parentElement;
      if (!parent) return null;

      // Remove any stray duplicates that might have been left by older code
      const existingAll = parent.querySelectorAll(`input#${id}[data-aims-search="1"]`);
      if (existingAll.length > 1) {
        existingAll.forEach((el, i) => { if (i > 0) el.remove(); });
      }
      let existing = parent.querySelector(`input#${id}[data-aims-search="1"]`);
      if (existing) return existing;

      const input = document.createElement("input");
      input.type = "text";
      input.placeholder = placeholder;
      input.className = "form-control mb-2";
      input.id = id;
      input.setAttribute("data-aims-search", "1");
      parent.insertBefore(input, selectEl);
      if (typeof onInput === "function") input.addEventListener("input", onInput);
      return input;
    }

    // --- data fetchers ---
    async function fetchUsers(searchTerm) {
      const resp = await fetch(`/api/user?searchString=${encodeURIComponent(searchTerm || "")}&_v=${assetsVer}`, { cache: "no-store" });
      if (!resp.ok) throw new Error(`Failed to load users (${resp.status})`);
      return resp.json();
    }

    async function populateUserDropdown(preselectUserId, searchTerm = "") {
      const users = await fetchUsers(searchTerm);
      clearChildren(userSelect);

      const ph = document.createElement("option");
      ph.disabled = true; ph.selected = true; ph.value = "";
      ph.textContent = users.length ? "Choose a user..." : "No users found";
      userSelect.appendChild(ph);

      const pre = norm(preselectUserId);
      users.forEach(u => {
        const opt = document.createElement("option");
        opt.value = u.fullName;
        opt.text = u.fullName;
        opt.dataset.user_id = u.userID;
        if (pre && norm(u.userID) === pre) opt.selected = true;
        userSelect.appendChild(opt);
      });
    }

    async function populateAssetDropdown(searchTerm = "") {
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
      const text = await resp.text();
      if (!resp.ok) throw new Error(`Failed to load assets: ${text}`);
      const results = JSON.parse(text);

      clearChildren(assetSelect);
      const ph = document.createElement("option");
      ph.disabled = true; ph.selected = true; ph.value = "";
      ph.text = results.length ? "Choose an asset..." : "No assets found";
      assetSelect.appendChild(ph);

      results.forEach(asset => {
        const opt = document.createElement("option");
        opt.value = `(${asset.assetID}) ${asset.assetName}`;
        opt.text = `(${asset.assetID}) ${asset.assetName}`;
        opt.dataset.asset_id = asset.assetID;     // numeric id
        opt.dataset.asset_kind = asset.assetKind;  // 1 or 2
        assetSelect.appendChild(opt);
      });
    }

    async function fetchAssetSummary(kind, id) {
      try {
        const url = kind === 2
          ? `/api/assets/one?softwareId=${id}&_v=${assetsVer}`
          : `/api/assets/one?hardwareId=${id}&_v=${assetsVer}`;
        const res = await fetch(url, { cache: "no-store" });
        if (!res.ok) return null;
        return await res.json();
      } catch { return null; }
    }

    // ===== Free-pick (Assign button) =====
    if (assignBtn) {
      assignBtn.addEventListener("click", async () => {
        if (!CAN_ASSIGN) return;

        // reset UI
        if (selectedBox) selectedBox.style.display = "none";
        if (assetRow) assetRow.style.display = "";
        const cb = document.getElementById("commentBox"); if (cb) cb.value = "";

        try {
          // single search inputs (no dupes)
          ensureSearchInputFor(userSelect, "assignUserSearchInput", "Search users...",
            (e) => populateUserDropdown(null, e.target.value));
          ensureSearchInputFor(assetSelect, "assignAssetSearchInput", "Search assets...",
            (e) => populateAssetDropdown(e.target.value));

          await Promise.all([populateUserDropdown(null, ""), populateAssetDropdown("")]);
          modal.show();
        } catch (err) {
          console.error(err);
        }
      });
    }

    // ===== From Search row (👤) – lock to row asset =====
    window.addEventListener("assign:open", async (ev) => {
      if (!CAN_ASSIGN) return;

      const d = ev.detail || {};
      const { assetTag, assetNumericId, assetKind, currentUserId, assetName } = d;

      const parsedId = Number.parseInt(String(assetNumericId ?? ""), 10);
      const valid = Number.isFinite(parsedId) && parsedId > 0;

      const cb = document.getElementById("commentBox"); if (cb) cb.value = "";

      if (valid) {
        // populate summary
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

        // lock asset picker with a single option, then hide row
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
        // normal free-pick fallback
        if (selectedBox) selectedBox.style.display = "none";
        if (assetRow) assetRow.style.display = "";
        ensureSearchInputFor(assetSelect, "assignAssetSearchInput", "Search assets...",
          (e) => populateAssetDropdown(e.target.value));
        try { await populateAssetDropdown(""); } catch (e) { console.error(e); }
      }

      // Users (single search input)
      ensureSearchInputFor(userSelect, "assignUserSearchInput", "Search users...",
        (e) => populateUserDropdown(null, e.target.value));
      try { await populateUserDropdown(d.currentUserId, ""); } catch (e) { console.error(e); }

      modal.show();
    });

    // ===== Submit =====
    formEl?.addEventListener("submit", async function (e) {
      e.preventDefault();

      const uOpt = userSelect.options[userSelect.selectedIndex];
      const aOpt = assetSelect.options[assetSelect.selectedIndex];
      if (!uOpt || !aOpt) return;

      const userID = Number(uOpt.dataset.user_id);
      const numericId = Number(aOpt.dataset.asset_id);
      const assetKind = Number(aOpt.dataset.asset_kind) === 2 ? 2 : 1;
      const comment = (document.getElementById("commentBox")?.value || "").trim();

      if (!Number.isFinite(numericId) || numericId <= 0) {
        const toastElement = document.getElementById("errorToast");
        if (toastElement) {
          const errorToast = new bootstrap.Toast(toastElement, { delay: 3000 });
          toastElement.querySelector('.toast-body').innerHTML = "No valid asset selected.";
          errorToast.show();
        } else {
          alert("No valid asset selected.");
        }
        return;
      }

      const payload = assetKind === 2
        ? { userID, assetKind, SoftwareID: numericId, ...(comment ? { comment } : {}) }
        : { userID, assetKind, HardwareID: numericId, ...(comment ? { comment } : {}) };

      try {
        const resp = await fetch('/api/assign/create', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'Cache-Control': 'no-cache, no-store'
          },
          body: JSON.stringify(payload)
        });
        if (!resp.ok) {
          const errText = await resp.text();
          throw new Error(`Assignment Failed: ${errText}`);
        }

        // toast + notify + refresh
        new bootstrap.Toast(document.getElementById("assignToast"), { delay: 3000 }).show();

        // Let Search page update inline and/or refresh
        window.dispatchEvent(new CustomEvent('assign:saved', {
          detail: { assetId: String(numericId), userId: userID, assetKind }
        }));
        window.dispatchEvent(new Event('assets:changed'));
        if (typeof window.refreshSearchTable === 'function') {
          // slight delay to let server commit + cache-stamp bump
          setTimeout(() => window.refreshSearchTable(), 150);
        }

        modal.hide();
      } catch (error) {
        const toastElement = document.getElementById("errorToast");
        if (toastElement) {
          const errorToast = new bootstrap.Toast(toastElement, { delay: 3000 });
          toastElement.querySelector('.toast-body').innerHTML =
            (error && error.message) ? error.message : String(error);
          errorToast.show();
        } else {
          alert((error && error.message) ? error.message : String(error));
        }
      }
    });

    // Upload handler (kept placeholder)
    const fileInput = document.getElementById("userAgreementUpload");
    fileInput?.addEventListener("change", function () {
      const selectedFile = fileInput.files?.[0];
      if (selectedFile) {
        // TODO: Upload using Graph; log in audit
      }
    });
  });
})();