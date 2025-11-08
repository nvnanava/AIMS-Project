// auditRowModal.js
// Handles opening the Audit Row modal + Preview Agreement button

(() => {
    "use strict";

    const $ = (sel) => document.querySelector(sel);

    function openRowModalFromTR(tr) {
        if (!tr) return;

        const cells = Array.from(tr.cells).map(td => (td.textContent || "").trim());

        const idEl = $("#ar-id");
        const tsEl = $("#ar-ts");
        const userEl = $("#ar-user");
        const actEl = $("#ar-action");
        const assetEl = $("#ar-asset");
        const prevEl = $("#ar-prev");
        const newEl = $("#ar-new");
        /** @type {HTMLTextAreaElement|null} */
        const descEl = $("#ar-desc");

        if (idEl) idEl.textContent = cells[0] || "—";
        if (tsEl) tsEl.textContent = cells[1] || "—";
        if (userEl) userEl.textContent = cells[2] || "—";
        if (actEl) actEl.textContent = cells[3] || "—";
        if (assetEl) assetEl.textContent = cells[4] || "—";
        if (prevEl) prevEl.textContent = cells[5] || "—";
        if (newEl) newEl.textContent = cells[6] || "—";
        if (descEl) descEl.value = cells[7] || "";

        // --- Configure Preview Agreement button (with assignmentId fallback) ---
        /** @type {HTMLButtonElement|null} */
        const previewBtn = $("#auditPreviewBtn");
        if (previewBtn) {
            let url = tr.dataset.previewUrl || "";
            const assignmentId = tr.dataset.assignmentId;
            if (!url && assignmentId) {
                url = `/api/assign/${assignmentId}/agreement`;
            }

            previewBtn.dataset.previewUrl = url || "";
            const hasUrl = !!url;

            previewBtn.disabled = !hasUrl;
            previewBtn.classList.toggle("d-none", !hasUrl);
        }

        const modalEl = $("#auditRowModal");
        if (modalEl && window.bootstrap?.Modal) {
            const m = window.bootstrap.Modal.getOrCreateInstance(modalEl);
            m.show();
        }
    }

    document.addEventListener("DOMContentLoaded", () => {
        // Row click → open modal
        const tbody = document.getElementById("auditTableBody");
        if (tbody) {
            tbody.addEventListener("click", (e) => {
                const target = /** @type {HTMLElement|null} */ (e.target);
                const tr = target?.closest("tr");
                if (!tr) return;
                openRowModalFromTR(tr);
            });
        }

        // Preview button → open agreement in new tab
        /** @type {HTMLButtonElement|null} */
        const previewBtn = document.getElementById("auditPreviewBtn");
        if (previewBtn) {
            previewBtn.addEventListener("click", (e) => {
                e.preventDefault();
                const url = previewBtn.dataset.previewUrl;
                if (!url) return;
                window.open(url, "_blank", "noopener,noreferrer");
            });
        }
    });
})();