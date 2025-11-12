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

        // Helpers to avoid null derefs
        const safeText = (el, txt) => { if (el) el.textContent = txt; };
        const safeVal  = (el, val) => { if (el) el.value = val; };
        const showToast = (msg) => {
            const t = document.getElementById("errorToast");
            if (t) {
                const body = t.querySelector(".toast-body");
                if (body) body.innerHTML = msg;
                new bootstrap.Toast(t, { delay: 3000 }).show();
            }
        };

        const assetsVer = (window.__ASSETS_VER__ ? String(window.__ASSETS_VER__) : String(Date.now()));
        const cacheHeaders = { "Cache-Control": "no-cache, no-store" };

        async function fetchActiveAssignments() {
            const list = await aimsFetch(`/api/assign/list?status=active&_v=${assetsVer}`, { headers: cacheHeaders });
            // Normalize to array
            return Array.isArray(list) ? list : (list?.items ?? []);
        }

        async function fetchAssetSummary(kind, id) {
            try {
                const url = kind === 2
                    ? `/api/assets/one?softwareId=${id}&_v=${assetsVer}`
                    : `/api/assets/one?hardwareId=${id}&_v=${assetsVer}`;
                return await aimsFetch(url, { cache: "no-store" });
            } catch { return null; }
        }

        // Populate modal from Search event
        window.addEventListener("unassign:open", async (ev) => {
            const d = ev.detail || {};
            try {
                console.debug("[unassign] open detail:", d);
                const assetKind = Number(d.assetKind) === 2 ? 2 : 1;
                const assetId   = Number(d.assetNumericId);
                const userId    = Number(d.currentUserId ?? d.userId);
                // accept assignmentId in either casing
                let assignmentIdRaw = d.assignmentID ?? d.assignmentId;
                let assignmentId = Number.isFinite(Number(assignmentIdRaw)) ? Number(assignmentIdRaw) : NaN;

                // Resolve assignmentId if not provided
                if (!Number.isFinite(assignmentId)) {
                    try {
                        const list = await fetchActiveAssignments();
                        // Prefer exact (asset + user) match when userId is available.
                        let match = null;
                        if (Number.isFinite(userId) && userId > 0) {
                        match = list.find(a =>
                            Number(a.userID) === userId &&
                            (assetKind === 1
                            ? Number(a.hardwareID ?? -1) === assetId
                            : Number(a.softwareID ?? -1) === assetId)
                        );
                        }
                        // Fallback: if no userId, try first active assignment for this asset.
                        if (!match) {
                        match = list.find(a =>
                            (assetKind === 1
                            ? Number(a.hardwareID ?? -1)
                            : Number(a.softwareID ?? -1)) === assetId
                        );
                        }
                        if (match) assignmentId = Number(match.assignmentID);
                    } catch (e) {
                        console.warn("[unassign] failed to fetch/resolve assignment list:", e);
                    }
                }

                // Fill name/tag if missing
                let name = (d.assetName || "").trim();
                let tag  = (d.assetTag || "").trim();
                if (!name || !tag) {
                    const one = Number.isFinite(assetId) ? await fetchAssetSummary(assetKind, assetId) : null;
                    if (one) {
                        if (!name) name = one.assetName || "";
                        if (!tag)  tag  = one.tag || String(assetId || "—");
                    }
                }

                // Safely populate fields
                safeText(selName, name || "—");
                safeText(selTag, tag || (Number.isFinite(assetId) ? `(${assetId})` : "—"));
                safeText(selKind, assetKind === 2 ? "Software" : "Hardware");
                safeText(selAssnId, Number.isFinite(assignmentId) ? String(assignmentId) : "—");
                safeVal(hiddenId, Number.isFinite(assignmentId) ? String(assignmentId) : "");

                // If we still couldn't resolve, nudge the user rather than failing silently.
                if (!hiddenId?.value) {
                    console.warn("Unassign modal: could not resolve AssignmentID for", { assetKind, assetId, userId });
                    showToast("Couldn’t find the active assignment for this asset.");
                }
                if (commentBox) commentBox.value = "";
                modal.show();
            } catch (err) {
                console.error("[unassign] handler error:", err);
                showToast("Something went wrong opening the unassign modal.");
                // Fallback: try to at least show the modal shell so it’s visible during debugging
                try { modal.show(); } catch {}
            }
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