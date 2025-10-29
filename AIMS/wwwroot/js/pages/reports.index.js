// ~/js/pages/reports.index.js
/* ======================================================================
   AIMS Page Script: Reports/Index
   ----------------------------------------------------------------------
   Responsibilities
   - Load report rows from /api/reports/list.
   - Client-side search filter.
   - Per-row click → open the correct preview modal (by Type column).
   - CSV download for selected rows.
   - Wire up three “Generate report” modals and show a toast on success.

   Conventions
   - No inline scripts in the .cshtml.
   - Uses Bootstrap (from layout) for Modal/Toast.
   - Main table body id: #reports-table-body.

   Notes
   - Keep selectors narrowly scoped to .report-table for row events
     so modal tables remain unaffected.
   ====================================================================== */

(() => {
    // -------- Elements ---------------------------------------------------
    const tableBody = document.getElementById("reports-table-body");
    const searchInput = document.getElementById("reportSearch");
    const toastEl = document.getElementById("reportToast");
    const reportToast = toastEl ? new bootstrap.Toast(toastEl, { delay: 3000 }) : null;

    // -------- Helpers ----------------------------------------------------
    function setLoading(message = "Loading...") {
        if (!tableBody) return;
        tableBody.innerHTML = `<tr><td colspan="6" class="no-results-cell">${message}</td></tr>`;
    }

    function renderRows(reports) {
        if (!tableBody) return;

        if (!Array.isArray(reports) || reports.length === 0) {
            setLoading("No reports available");
            return;
        }

        tableBody.innerHTML = "";
        const frag = document.createDocumentFragment();

        reports.forEach(r => {
            const tr = document.createElement("tr");
            tr.innerHTML = `
                <td><input class="form-check-input report-checkbox" type="checkbox" data-id="${r.reportID}"></td>
                <td class="cell-name">
                    <span class="report-preview-btn"
                            data-report-id="${r.reportID}"
                            style="cursor: pointer;">
                        ${r.name || "-"}
                    </span>
                </td>
                <td><span class="cell-text">${r.type || "-"}</span></td>
                <td><span class="cell-text">${r.description || "-"}</span></td>
                <td><span class="cell-text">${r.generatedByOfficeString || "-"}</span></td>
                <td><span class="cell-text">${r.dateCreated ? new Date(r.dateCreated).toLocaleString() : "-"}</span></td>
                <td><span class="cell-text">${r.generatedByUserName || "-"}</span></td>
            `;
            frag.appendChild(tr);
        });

        tableBody.appendChild(frag);
        wireRowModalOpeners();
    }

    // Dynamically open preview modal and load content
    function wireRowModalOpeners() {
        tableBody.addEventListener("click", async (e) => {
            const btn = e.target.closest(".report-preview-btn");
            if (!btn) return;

            const reportId = btn.dataset.reportId;
            const modalEl = document.getElementById("reportPreviewModal");
            const modalTitle = document.getElementById("reportPreviewModalLabel");
            const modalBody = document.getElementById("reportPreviewContent");
            const modal = new bootstrap.Modal(modalEl);

            // Show the modal immediately
            modalTitle.textContent = "Loading report preview...";
            modalBody.innerHTML = `<div class="text-center text-muted p-4">Loading...</div>`;
            modal.show();

            try {
                const resp = await fetch(`/api/reports/preview/${reportId}`, { cache: "no-store" });
                if (!resp.ok) throw new Error(`Failed to load report (HTTP ${resp.status})`);

                const data = await resp.json();

                // Set modal title to the report name
                modalTitle.textContent = data.name || `Report ${reportId}`;

                if (!data.previewRows || data.previewRows.length === 0) {
                    modalBody.innerHTML = `<p class="text-muted text-center p-4">No data available in this report.</p>`;
                    return;
                }

                // Build table dynamically
                const headers = Object.keys(data.previewRows[0]);
                let html = `
                <p><strong>Created:</strong> ${new Date(data.dateCreated).toLocaleString()}</p>
                <p><strong>Total Rows:</strong> ${data.totalRows}</p>
                <div class="table-responsive">
                    <table class="table table-striped table-sm align-middle">
                        <thead class="table-light">
                            <tr>${headers.map(h => `<th>${h}</th>`).join("")}</tr>
                        </thead>
                        <tbody>
                            ${data.previewRows.map(row => `
                                <tr>${headers.map(h => `<td>${row[h] ?? ""}</td>`).join("")}</tr>
                            `).join("")}
                        </tbody>
                    </table>
                </div>
            `;

                modalBody.innerHTML = html;

            } catch (err) {
                console.error(err);
                modalTitle.textContent = "Error loading report";
                modalBody.innerHTML = `<div class="text-danger text-center p-4">${err.message}</div>`;
            }
        });
    }



    // -------- Data load --------------------------------------------------
    async function loadReports() {
        setLoading();
        try {
            const resp = await fetch("/api/reports/list", { cache: "no-store" });
            if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
            const reports = await resp.json();
            renderRows(reports);
        } catch (err) {
            console.error(err);
            setLoading("Error loading reports");
        }
    }

    // -------- Search filter ----------------------------------------------
    function wireSearch() {
        if (!searchInput || !tableBody) return;
        searchInput.addEventListener("input", () => {
            const term = searchInput.value.toLowerCase();
            const rows = tableBody.querySelectorAll("tr");
            let visible = 0;

            rows.forEach(row => {
                const text = row.innerText.toLowerCase();
                const show = text.includes(term);
                row.style.display = show ? "" : "none";
                if (show) visible++;
            });

            // Inject “no results” row if needed
            const existing = document.getElementById("no-results-row");
            if (existing) existing.remove();
            if (visible === 0) {
                const tr = document.createElement("tr");
                tr.id = "no-results-row";
                tr.innerHTML = `<td colspan="6" class="no-results-cell">No matching results found</td>`;
                tableBody.appendChild(tr);
            }
        });
    }

    // -------- CSV download -----------------------------------------------
    async function downloadSelectedCsv() {
        const selected = document.querySelectorAll(".report-checkbox:checked");
        if (selected.length === 0) {
            alert("Please select at least one report to download.");
            return;
        }

        for (const cb of selected) {
            const id = cb.getAttribute("data-id");
            try {
                const resp = await fetch(`api/reports/download/${id}`);
                if (!resp.ok) throw new Error(`Failed to download report ${id}`);

                // Try to pick filename from content-disposition
                const disp = resp.headers.get("Content-Disposition");
                let filename = `report_${id}.csv`;
                if (disp) {
                    const utf8 = disp.match(/filename\*=UTF-8''([^;]+)/i);
                    const ascii = disp.match(/filename="?([^"]+)"?/i);
                    if (utf8) filename = decodeURIComponent(utf8[1]);
                    else if (ascii) filename = ascii[1];
                }

                const blob = await resp.blob();
                const url = URL.createObjectURL(blob);
                const a = document.createElement("a");
                a.href = url;
                a.download = filename;
                a.click();
                URL.revokeObjectURL(url);
            } catch (err) {
                console.error(err);
                alert(`Error downloading report ${id}: ${err.message}`);
            }
        }
    }

    function wireCsvDownload() {
        const btn = document.querySelector(".download-csv-button");
        if (btn) btn.addEventListener("click", downloadSelectedCsv);
    }

    // -------- Generate report handlers -----------------------------------
    function wireGenerateHandlers() {
        // Assignment
        document.getElementById("generateAssignmentReportBtn")?.addEventListener("click", async () => {
            const reportName = (document.getElementById("reportName")?.value || "").trim();
            const startDate = document.getElementById("dateRange1")?.value || "";
            const endDate = document.getElementById("dateRange2")?.value || "";
            const description = (document.getElementById("inputDescription")?.value || "").trim();
            const CreatorUserID = 4; // replace with real user id

            // alert(window.pageData.user);

            if (!reportName) return alert("Please enter a report name.");
            if (!startDate) return alert("Please select a start date.");

            const params = new URLSearchParams({
                start: startDate, reportName, CreatorUserID, type: "Assignment"
            });
            if (endDate) params.append("end", endDate);
            if (description) params.append("desc", description);

            try {
                const resp = await fetch(`/api/reports?${params.toString()}`, { method: "POST" });
                if (!resp.ok) throw new Error(await resp.text());
                bootstrap.Modal.getInstance(document.getElementById("generateAssignmentReport"))?.hide();
                reportToast?.show();
                loadReports();
            } catch (e) {
                console.error(e);
                alert("Failed to generate report: " + e.message);
            }
        });

        // Office
        document.getElementById("generateOfficeReportBtn")?.addEventListener("click", async () => {
            const reportName = (document.getElementById("officeReportName")?.value || "").trim();
            const officeId = (document.getElementById("officeName")?.value || "").trim();
            const startDate = document.getElementById("officeStartDate")?.value || "";
            const endDate = document.getElementById("officeEndDate")?.value || "";
            const description = (document.getElementById("officeDescription")?.value || "").trim();
            const CreatorUserID = 1;

            if (!reportName) return alert("Please enter a report name.");
            if (!officeId) return alert("Please select an office number.");
            if (!startDate) return alert("Please select a start date.");

            const params = new URLSearchParams({
                start: startDate, reportName, CreatorUserID, type: "Office", OfficeID: officeId
            });
            if (endDate) params.append("end", endDate);
            if (description) params.append("desc", description);

            try {
                const resp = await fetch(`/?${params.toString()}`, { method: "POST" });
                if (!resp.ok) throw new Error(await resp.text());
                bootstrap.Modal.getInstance(document.getElementById("generateOfficeReport"))?.hide();
                reportToast?.show();
                loadReports();
            } catch (e) {
                console.error(e);
                alert("Failed to generate report: " + e.message);
            }
        });

        // Custom
        document.getElementById("generateCustomReportBtn")?.addEventListener("click", async () => {
            const reportName = (document.getElementById("customReportName")?.value || "").trim();
            const startDate = document.getElementById("customStartDate")?.value || "";
            const endDate = document.getElementById("customEndDate")?.value || "";
            const description = (document.getElementById("customDescription")?.value || "").trim();
            const CreatorUserID = 1;

            const customOptions = {
                seeHardware: document.getElementById("seeHardware")?.checked ?? true,
                seeSoftware: document.getElementById("seeSoftware")?.checked ?? true,
                seeUsers: document.getElementById("seeUsers")?.checked ?? true,
                seeOffice: document.getElementById("seeOffice")?.checked ?? true,
                seeExpiration: document.getElementById("seeExpiration")?.checked ?? false,
                filterByMaintenance: document.getElementById("filterByMaintenance")?.checked ?? false,
            };

            if (!reportName) return alert("Please enter a report name.");
            if (!startDate) return alert("Please select a start date.");

            const params = new URLSearchParams({
                start: startDate, reportName, CreatorUserID, type: "Custom"
            });
            if (endDate) params.append("end", endDate);
            if (description) params.append("desc", description);

            try {
                const resp = await fetch(`/?${params.toString()}`, { method: "POST", body: JSON.stringify(customOptions) });
                if (!resp.ok) throw new Error(await resp.text());
                bootstrap.Modal.getInstance(document.getElementById("generateCustomReport"))?.hide();
                reportToast?.show();
                loadReports();
            } catch (e) {
                console.error(e);
                alert("Failed to generate report: " + e.message);
            }
        });
    }

    // -------- Boot -------------------------------------------------------
    function init() {
        wireSearch();
        wireCsvDownload();
        wireGenerateHandlers();
        loadReports();

        // Show toast on any of the three primary actions (progressive enhancement)
        ["generateAssignmentReportBtn", "generateOfficeReportBtn", "generateCustomReportBtn"].forEach(id => {
            document.getElementById(id)?.addEventListener("click", () => reportToast?.show());
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();