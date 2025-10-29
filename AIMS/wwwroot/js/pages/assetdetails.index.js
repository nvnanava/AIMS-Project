/* ======================================================================
   AIMS Script: assetdetails.index.js
   ====================================================================== */

(() => {
    // Namespace
    window.AIMS = window.AIMS || {};
    AIMS.AssetDetails = AIMS.AssetDetails || {};

    // ---- Bootstrap config (no inline JS in the view) ----
    const cfgEl = document.getElementById("assetdetails-config");
    const specsData = safeParseJSON(cfgEl?.dataset?.specs) || {};
    const SERVER_CATEGORY = cfgEl?.dataset?.category || "";
    const IS_ADMIN = (cfgEl?.dataset?.isAdmin || "false") === "true";

    // ---- Constants / guards ----
    const ALLOWED_TYPES = new Set(["monitor", "laptop", "desktop", "software", "headset", "charging cable"]);
    const AUTO_FIX_CATEGORY = true;

    // ---- Pager state & DOM handles ----
    let currentPage = 1;
    let pageSize = 50;
    const pageCache = new Map(); // page -> items[]
    let lastMeta = { total: -1, page: 1, pageSize };

    const pager = document.getElementById("asset-pager");
    const btnPrev = document.getElementById("asset-prev");
    const btnNext = document.getElementById("asset-next");
    const lblStatus = document.getElementById("asset-status");

    // ----------------------------- Utils ------------------------------
    function safeParseJSON(s) { try { return JSON.parse(s || "{}"); } catch { return {}; } }
    function escapeHtml(str) {
        return String(str ?? "").replace(/[&<>"']/g, c => ({
            "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
        }[c]));
    }
    function normalizeCategory(raw) {
        const c = String(raw || "").trim().toLowerCase();
        const map = { monitors: "monitor", laptops: "laptop", desktops: "desktop", headsets: "headset", softwares: "software" };
        return (map[c] || c);
    }
    function applyEmpty(isEmpty) {
        const el = document.getElementById("emptyMessage");
        if (el) el.style.display = isEmpty ? "block" : "none";
    }
    function clearTable() {
        const tbody = document.getElementById("assetTableBody");
        if (tbody) tbody.innerHTML = "";
    }
    function statusClassFor(status) {
        switch (String(status || "").toLowerCase()) {
            case "available": return "status-available";
            case "assigned": return "status-assigned";
            case "marked for survey": return "status-marked-for-survey";
            case "in repair":
            case "damaged": return "status-in-repair";
            case "full": return "status-assigned";
            case "expired": return "status-in-repair";
            default: return "";
        }
    }


    function setStatusHeaderFor() {
        const th = document.getElementById("status-col-header");
        if (!th) return;

        const wrapper = th.firstElementChild || th; // <div class="d-flex ..."> or <th>
        const icon = wrapper.querySelector('[data-component="filter-icon"]'); // keep existing icon


        while (wrapper.firstChild) wrapper.removeChild(wrapper.firstChild);

        const label = document.createElement("span");
        label.className = "status-label";
        label.textContent = "Status";

        wrapper.appendChild(label);
        if (icon) wrapper.appendChild(icon);

        th.title = "";
    }


    function getCurrentCategory() {
        const urlParams = new URLSearchParams(window.location.search);
        return (urlParams.get("category") || SERVER_CATEGORY || "").trim();
    }
    function redirectToCorrectCategory(correctCategory, tag) {
        const url = new URL("/AssetDetails/Index", window.location.origin);
        url.searchParams.set("category", correctCategory);
        url.searchParams.set("tag", tag);
        window.location.replace(url.toString());
    }
    function showCategoryMismatchError(expected, actual) {
        const msg = document.createElement("div");
        msg.style.cssText = `
            margin:12px auto; max-width:720px; padding:12px 14px;
            border:1px solid #e67e22; background:#fff7ec; color:#8c4b00;
            border-radius:8px; font-size:14px;`;
        msg.innerHTML = `
            <strong>Wrong page for this asset.</strong><br/>
            You’re viewing the <em>${escapeHtml(actual)}</em> page, but tag belongs to <em>${escapeHtml(expected)}</em>.
            Please switch to the correct category or use the search again.`;
        document.body.prepend(msg);
    }

    // --- Date helpers for Expired status (treat date-only strings safely) ---
    function todayLocalYMD() {
        const d = new Date();
        d.setHours(0, 0, 0, 0);
        const tz = d.getTimezoneOffset();
        const local = new Date(d.getTime() - tz * 60000);
        return local.toISOString().slice(0, 10); // YYYY-MM-DD
    }
    function extractYMD(val) {
        if (!val) return null;
        if (typeof val === "string" && /^\d{4}-\d{2}-\d{2}/.test(val)) {
            return val.slice(0, 10);
        }
        const dt = new Date(val);
        if (!isNaN(dt.valueOf())) {
            const tz = dt.getTimezoneOffset();
            const local = new Date(dt.getTime() - tz * 60000);
            return local.toISOString().slice(0, 10);
        }
        return null;
    }
    function isExpired(expVal) {
        const exp = extractYMD(expVal);
        if (!exp) return false;
        return exp < todayLocalYMD();
    }

    // --------------------------- Rendering ----------------------------
    function renderRows(rows) {
    clearTable();

    (rows || []).forEach((asset) => {
        const typeLower = (asset.type || "").toLowerCase();

        if (typeLower.includes("software")) {
            // Software: show assigned seats vs total seats, or "—" if no data
            const assigned = asset.assignedSeats ?? asset.SeatsUsed ?? 0;
            const total = asset.totalSeats ?? asset.SeatsTotal ?? "?";

            asset.displaySeatOrTag = (assigned || total !== "?")
                ? `Seat ${assigned} of ${total}`
                : "—";
        } else {
            // Hardware: display tag or ID
            asset.displaySeatOrTag = asset.assetTag || asset.tag || asset.hardwareID || "N/A";
        }

        renderRow(asset);
    });
}


    function makeEditButton(asset) {
        return `
        <button type="button"
                class="action-btn blue-pencil"
                aria-label="Edit ${escapeHtml(asset.assetName)}"
                title="Edit asset"
                data-bs-toggle="modal"
                data-hardware-id="${asset.hardwareID || ""}"
                data-software-id="${asset.softwareID || ""}"
                data-bs-target="#editAssetModal"
                data-name="${escapeHtml(asset.assetName || "")}"
                data-type="${escapeHtml(asset.type || "")}"
                data-tag="${escapeHtml(asset.tag || "")}"
                data-comments="${escapeHtml(asset.comment || "")}"
                data-status="${escapeHtml(asset.status || "")}">
          <svg viewBox="0 0 16 16" width="16" height="16" class="pencil-svg" aria-hidden="true">
            <path d="M12.854.146a.5.5 0 0 0-.707 0L10.5 1.793 14.207 5.5l1.647-1.646a.5.5 0 0 0 0-.708l-3-3z"></path>
            <path d="M.146 13.854a.5.5 0 0 0 .168.11l4.39 1.464a.5.5 0 0 0 .498-.13l9-9L10.5 1.793l-9 9a.5.5 0 0 0-.13.498L.854 15.854a.5.5 0 0 0-.708-.708L.146 13.854z"></path>
          </svg>
        </button>`;
    }

    function makeArchiveButton(asset) {
        const id = asset.hardwareID ?? asset.softwareID;
        const name = escapeHtml(asset.assetName || "");
        const type = escapeHtml(asset.type || "software");
        return `
        <button type="button"
                class="action-btn red-archive"
                aria-label="Archive ${name}"
                title="Archive asset"
                onclick="AIMS.AssetDetails.archiveAsset('${id}', '${name}', '${type}')">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16"
               fill="currentColor" viewBox="0 0 16 16" class="bi bi-archive-fill">
            <path d="M12.643 15C13.979 15 15 13.845 15 12.5V5H1v7.5C1 13.845 2.021 15 3.357 15zM5.5 7h5a.5.5 0 0 1 0 1h-5a.5.5 0 0 1 0-1M.8 1a.8.8 0 0 0-.8.8V3a.8.8 0 0 0 .8.8h14.4A.8.8 0 0 0 16 3V1.8a.8.8 0 0 0-.8-.8z"/>
          </svg>
        </button>`;
    }

    function makeUnarchiveButton(asset) {
        const id = asset.hardwareID ?? asset.softwareID;
        const name = escapeHtml(asset.assetName || "");
        const type = escapeHtml(asset.type || "software");
        return `
        <button type="button"
                class="action-btn green-unarchive"
                aria-label="Unarchive ${name}"
                title="Unarchive asset"
                onclick="AIMS.AssetDetails.unarchiveAsset('${id}', '${name}', '${type}')">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16"
               fill="currentColor" viewBox="0 0 16 16" class="bi bi-archive-fill">
            <path d="M12.643 15C13.979 15 15 13.845 15 12.5V5H1v7.5C1 13.845 2.021 15 3.357 15zM5.5 7h5a.5.5 0 0 1 0 1h-5a.5.5 0 0 1 0-1M.8 1a.8.8 0 0 0-.8.8V3a.8.8 0 0 0 .8.8h14.4A.8.8 0 0 0 16 3V1.8a.8.8 0 0 0-.8-.8z"/>
          </svg>
        </button>`;
    }

    function renderRow(asset) {
        const tbody = document.getElementById("assetTableBody");
        if (!tbody) return;

        const isSoftware = String(asset.type || "").toLowerCase() === "software";
        let fourthCellHtml = "";

        if (isSoftware) {
            const usedRaw = asset.licenseSeatsUsed;
            const totalRaw = asset.licenseTotalSeats;
            const used = (usedRaw == null ? NaN : Number(usedRaw));
            const total = (totalRaw == null ? NaN : Number(totalRaw));

            const expVal = asset.softwareLicenseExpiration ?? asset.licenseExpiration ?? asset.expiration;
            const expired = isExpired(expVal);

            let statusText;
            if (expired) {
                statusText = "Expired";
            } else if (!Number.isNaN(total) && total > 0 && !Number.isNaN(used) && used >= total) {
                statusText = "Full";
            } else {
                statusText = "Available";
            }

            const sc = statusClassFor(statusText);
            fourthCellHtml = `<span class="status-badge ${sc}">${escapeHtml(statusText)}</span>`;
        } else {
            const sc = statusClassFor(asset.status);
            fourthCellHtml = `<span class="status-badge ${sc}">${escapeHtml(asset.status ?? "")}</span>`;
        }

        const isArchived =
            asset.isArchived === true ||
            String(asset.status || "").toLowerCase().includes("archived");

        let adminButtons = "";
        if (IS_ADMIN) {
            const editButton = makeEditButton(asset);
            const archiveButton = isArchived ? makeUnarchiveButton(asset) : makeArchiveButton(asset);
            adminButtons = editButton + archiveButton;
        }

        const row = document.createElement("tr");
        row.addEventListener("click", (ev) =>
            AIMS.AssetDetails.showPopup(asset.assetName, asset.displaySeatOrTag, ev, asset.type)
        );

        const safeName = escapeHtml(asset.assetName ?? "");
        const safeType = escapeHtml(asset.type ?? "");
        const safeSeatOrTag = escapeHtml(asset.displaySeatOrTag ?? "");

        row.innerHTML = `
            <td>${safeName}</td>
            <td>${safeType}</td>
            <td>
                <span style="cursor:pointer;"
                      onclick="AIMS.AssetDetails.showPopup('${safeName}', '${safeSeatOrTag}', event, '${safeType}')">
                    ${safeSeatOrTag}
                </span>
            </td>
            <td>${fourthCellHtml}</td>
            <td class="actions-cell text end" onclick="event.stopPropagation()">
                ${adminButtons}
            </td>`;
        tbody.appendChild(row);
    }

    // --------------------------- Pager UI ----------------------------
    function updatePager(items) {
        if (!pager || !btnPrev || !btnNext || !lblStatus) return;
        const total = lastMeta?.total ?? -1;
        const page = currentPage;
        const size = lastMeta?.pageSize ?? pageSize;

        let hasNext, totalPages;
        if (total === -1) {
            hasNext = (items.length === size);
            totalPages = page + (hasNext ? 1 : 0);
            lblStatus.textContent = `Page ${page}`;
        } else {
            totalPages = Math.max(1, Math.ceil(total / size));
            hasNext = page < totalPages;
            lblStatus.textContent = `Page ${page} of ${totalPages}`;
        }
        const hasPrev = page > 1;
        btnPrev.disabled = !hasPrev;
        btnNext.disabled = !hasNext;
        pager.hidden = (page === 1 && items.length === 0);
    }

    // --------------------------- Data Layer --------------------------
    async function getPage(category, page, size) {
        if (pageCache.has(page)) {
            const items = pageCache.get(page) || [];
            lastMeta.page = page;
            lastMeta.pageSize = size;
            return { items, total: lastMeta.total, page, pageSize: size };
        }

        const safe = normalizeCategory(category);
        if (!ALLOWED_TYPES.has(safe)) return { items: [], total: 0, page, pageSize: size };

        const url = new URL("/api/assets", window.location.origin);
        url.searchParams.set("page", String(page));
        url.searchParams.set("pageSize", String(size));
        url.searchParams.set("category", safe);
        url.searchParams.set("scope", "all");
        url.searchParams.set("totalsMode", "lookahead");

        const data = await aimsFetch(url.toString());


        const items = Array.isArray(data.items) ? data.items : [];
        pageCache.set(page, items);
        lastMeta = {
            total: (typeof data.total === "number" ? data.total : -1),
            page: data.page || page,
            pageSize: data.pageSize || size
        };
        return { items, total: lastMeta.total, page: lastMeta.page, pageSize: lastMeta.pageSize };
    }

    // --------------------------- Loaders -----------------------------
    async function loadCategoryPaged(category, page = 1) {
        try {
            setStatusHeaderFor();

            const { items } = await getPage(category, page, pageSize);
            currentPage = page;
            renderRows(items);
            applyEmpty(items.length === 0 && page === 1);
            updatePager(items);
        } catch (err) {
            console.error(err);
            clearTable();
            applyEmpty(true);
            updatePager([]);
        }
    }

    async function loadOneByTag(tag) {
        try {
            const asset = await aimsFetch(`/api/assets/one?tag=${encodeURIComponent(tag)}&devBypass=true`);
            // Header is always "Status"
            setStatusHeaderFor();

            const currentCategory = getCurrentCategory();
            const assetCategory = (asset.type || "").trim();
            if (assetCategory && currentCategory &&
                assetCategory.toLowerCase() !== currentCategory.toLowerCase()) {
                if (AUTO_FIX_CATEGORY) {
                    redirectToCorrectCategory(assetCategory, asset.tag || tag);
                    return;
                } else {
                    showCategoryMismatchError(assetCategory, currentCategory);
                }
            }

            clearTable();
            applyEmpty(false);
            renderRow(asset);
            pager.hidden = true;
        } catch (err) {
            console.error(err);
            clearTable();
            applyEmpty(true);
            pager.hidden = true;
        }
    }

    // --------------------------- Popup -------------------------------
    function showPopup(assetName, seatOrTag, event, type) {
        const popup = document.getElementById("popup");
        if (!popup) return;

        const rect = event.target.getBoundingClientRect();
        const label = (String(type || "").toLowerCase().includes("software") ? "Seat Number" : "Tag Number");

        popup.style.top = (rect.top + window.scrollY + 20) + "px";
        popup.style.left = (rect.left + window.scrollX + 20) + "px";
        popup.innerHTML = `
            <strong>Asset Name:</strong> ${escapeHtml(assetName ?? "")}<br>
            <strong>${label}:</strong> ${escapeHtml(seatOrTag ?? "")}<br>`;
        popup.style.display = "block";
        popup.setAttribute("aria-hidden", "false");
        event.stopPropagation();
    }

    document.addEventListener("click", (ev) => {
        const popup = document.getElementById("popup");
        if (popup && popup.style.display === "block" && !popup.contains(ev.target)) {
            popup.style.display = "none";
            popup.setAttribute("aria-hidden", "true");
        }
    });

    // --------------------- Archive / Unarchive -----------------------
    async function archiveAsset(id, name, type) {
        if (!id) return;
        if (!window.confirm(`Are you sure you want to archive "${name}"? `)) return;

        const isSoftware = (String(type || "").toLowerCase() === "software");
        const endpoint = isSoftware ? `/api/software/archive/${id}` : `/api/hardware/archive/${id}`;

        try {
            const updated = await aimsFetch(endpoint, { method: "PUT" });
            alert(`"${name}" was successfully archived.`);
            await updateRowInUIAndCache(updated);
        } catch (err) {
            console.error("Error archiving asset:", err);
            if (err.isValidation && err.data) {
                showServerErrorsInline(err.data);
            } else {
                alert(`Failed to unarchive "${name}".`);
            }
        }
    }

    async function unarchiveAsset(id, name, type) {
        if (!id) return;
        if (!window.confirm(`Are you sure you want to unarchive "${name}"? `)) return;

        const isSoftware = (String(type || "").toLowerCase() === "software");

        const endpoint = isSoftware ? `/api/software/unarchive/${id}` : `/api/hardware/unarchive/${id}`;

        try {
            const updated = await aimsFetch(endpoint, { method: "PUT" });
            alert(`"${name}" was successfully unarchived.`);
            await updateRowInUIAndCache(updated);
        } catch (err) {
            console.error("Error unarchiving asset:", err);
            if (err.isValidation && err.data) {
                showServerErrorsInline(err.data);
            } else {
                alert(`Failed to unarchive "${name}".`);
            }
        }
    }

    // --------------- Cache/DOM updates after actions -----------------
    async function updateRowInUIAndCache(update) {
        const id = update.hardwareID || update.softwareID;
        const items = pageCache.get(currentPage) || [];

        // Single-asset view?
        const urlParams = new URLSearchParams(window.location.search);
        const tag = urlParams.get("tag");
        if (tag) {
            clearTable();
            renderRow(update);
            pager.hidden = true;
            return;
        }

        // Paged view: update cached row or reload page
        if (items && items.length) {
            const idx = items.findIndex(a => (a.hardwareID || a.softwareID) === id);
            if (idx !== -1) {
                items[idx] = update;
                renderRows(items);
                return;
            }
        }

        const currentCategory = getCurrentCategory();
        pageCache.clear();
        await loadCategoryPaged(currentCategory, currentPage);
    }

    // --------------------------- Wiring ------------------------------
    if (btnPrev) btnPrev.addEventListener("click", async () => {
        if (currentPage > 1) {
            const cat = getCurrentCategory();
            await loadCategoryPaged(cat, currentPage - 1);
        }
    });
    if (btnNext) btnNext.addEventListener("click", async () => {
        const cat = getCurrentCategory();
        await loadCategoryPaged(cat, currentPage + 1);
    });

    // --------------------------- Startup -----------------------------
    window.addEventListener("DOMContentLoaded", async () => {
        const urlParams = new URLSearchParams(window.location.search);
        const tag = urlParams.get("tag");
        const source = (urlParams.get("source") || "").toLowerCase();
        const category = getCurrentCategory();

        const th = document.getElementById("seatOrTagHeader");
        if (th) th.textContent = (category.toLowerCase().includes("software") ? "Seat Usage" : "Tag #");

        // Keep header + icon intact
        setStatusHeaderFor();

        if (tag) {
            await loadOneByTag(tag);
            return;
        }
        if (source === "card") {
            await loadCategoryPaged(category, 1);
            return;
        }

        clearTable();
        applyEmpty(true);
        pager.hidden = true;
    });

    // Public API for markup-triggered actions
    Object.assign(AIMS.AssetDetails, {
        showPopup,
        archiveAsset,
        unarchiveAsset
    });
})();
