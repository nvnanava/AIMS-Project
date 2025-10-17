// unassignAssetModal.js
// Dropdown-less. Populates from 'unassign:open'.
// If assignmentID is missing, resolves it from /api/assign/list and fills summary.

(function () {
    document.addEventListener("DOMContentLoaded", function () {
        const modalEl = document.getElementById("unassignAssetModal");
        if (!modalEl) return;
        const modal = new bootstrap.Modal(modalEl);

        const formEl = document.getElementById("unassignAssetForm");
        const selName = document.getElementById("unSelAssetName");
        const selTag = document.getElementById("unSelAssetTag");
        const selKind = document.getElementById("unSelAssetKind");
        const selAssnId = document.getElementById("unSelAssignmentId");
        const hiddenId = document.getElementById("unassignAssignmentId");
        const commentBox = document.getElementById("unassignCommentBox");

        const assetsVer = (window.__ASSETS_VER__ ? String(window.__ASSETS_VER__) : String(Date.now()));
        const cacheHeaders = { "Cache-Control": "no-cache, no-store" };

        async function fetchActiveAssignments() {
            const res = await fetch(`/api/assign/list?status=active&_v=${assetsVer}`, { headers: cacheHeaders });
            if (!res.ok) throw new Error("Failed to load assignments");
            return res.json();
        }

        async function fetchAssetSummary(kind, id) {
            try {
                const url = kind === 2
                    ? `/api/assets/one?softwareId=${id}&_v=${assetsVer}`
                    : `/api/assets/one?hardwareId=${id}&_v=${assetsVer}`;
                const res = await fetch(url, { cache: "no-store" });
                if (!res.ok) return null;
                return await res.json(); // { assetName, tag, ... }
            } catch { return null; }
        }

        // Populate modal from Search event
        window.addEventListener("unassign:open", async (ev) => {
            const d = ev.detail || {};
            const assetKind = Number(d.assetKind) === 2 ? 2 : 1;
            const assetId = Number(d.assetNumericId);
            const userId = Number(d.currentUserId);
            let assignmentId = d.assignmentID ? Number(d.assignmentID) : NaN;

            // Resolve assignmentId if not provided
            if (!Number.isFinite(assignmentId)) {
                try {
                    const list = await fetchActiveAssignments();
                    const match = list.find(a =>
                        Number(a.userID) === userId &&
                        (assetKind === 1 ? Number(a.hardwareID ?? -1) === assetId
                            : Number(a.softwareID ?? -1) === assetId)
                    );
                    if (match) assignmentId = Number(match.assignmentID);
                } catch { /* ignore */ }
            }

            // Fill name/tag if missing
            let name = (d.assetName || "").trim();
            let tag = (d.assetTag || "").trim();
            if (!name || !tag) {
                const one = Number.isFinite(assetId) ? await fetchAssetSummary(assetKind, assetId) : null;
                if (one) {
                    if (!name) name = one.assetName || "";
                    if (!tag) tag = one.tag || String(assetId || "—");
                }
            }

            selName.textContent = name || "—";
            selTag.textContent = tag || (Number.isFinite(assetId) ? `(${assetId})` : "—");
            selKind.textContent = assetKind === 2 ? "Software" : "Hardware";
            selAssnId.textContent = Number.isFinite(assignmentId) ? String(assignmentId) : "—";
            hiddenId.value = Number.isFinite(assignmentId) ? String(assignmentId) : "";

            commentBox.value = "";
            modal.show();
        });

        // Submit unassign
        formEl.addEventListener("submit", async (e) => {
            e.preventDefault();
            const id = hiddenId.value;
            if (!id) {
                alert("No assignment selected.");
                return;
            }
            const comment = commentBox.value.trim();

            const url = new URL(`/api/assign/close`, window.location.origin);
            url.searchParams.set("AssignmentID", id);
            if (comment) url.searchParams.set("comment", comment);
            url.searchParams.set("_v", assetsVer);

            try {
                const resp = await fetch(url.toString(), { method: "POST", headers: cacheHeaders });
                if (!resp.ok) {
                    const msg = await resp.text().catch(() => "");
                    throw new Error(msg || "Unassign failed.");
                }

                // toast + refresh
                const tEl = document.getElementById("unassignToast");
                if (tEl) new bootstrap.Toast(tEl, { delay: 3000 }).show();

                window.dispatchEvent(new Event('assets:changed'));
                if (typeof window.refreshSearchTable === "function") {
                    setTimeout(() => window.refreshSearchTable(), 150);
                }

                modal.hide();
            } catch (err) {
                const toastElement = document.getElementById("errorToast");
                if (toastElement) {
                    const errorToast = new bootstrap.Toast(toastElement, { delay: 3000 });
                    toastElement.querySelector('.toast-body').innerHTML =
                        (err && err.message) ? err.message : "Unassign failed.";
                    errorToast.show();
                } else {
                    alert((err && err.message) ? err.message : "Unassign failed.");
                }
            }
        });
    });
})();