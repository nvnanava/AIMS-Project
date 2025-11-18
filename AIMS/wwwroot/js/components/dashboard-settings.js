(() => {
    "use strict";

    const VIS_STORAGE_KEY = "dashboard:visibleTypes";

    // ---------------- utilities ----------------
    const readVisibleSet = () => {
        try {
            const raw = localStorage.getItem(VIS_STORAGE_KEY);
            if (!raw) return null; // "not set" -> default all visible
            const arr = JSON.parse(raw) || [];
            return new Set(arr.map(s => String(s).toLowerCase()));
        } catch {
            return null;
        }
    };

    const writeVisibleSet = (set) => {
        try {
            const arr = Array.from(set);
            localStorage.setItem(VIS_STORAGE_KEY, JSON.stringify(arr));
        } catch {/* ignore quota */ }
    };

    const el = (tag, attrs = {}, ...children) => {
        const e = document.createElement(tag);
        for (const [k, v] of Object.entries(attrs)) {
            if (k === "class") e.className = v;
            else if (k === "dataset") Object.assign(e.dataset, v);
            else if (k.startsWith("on") && typeof v === "function") e.addEventListener(k.substring(2), v);
            else if (v !== undefined && v !== null) e.setAttribute(k, v);
        }
        for (const c of children) {
            if (c == null) continue;
            e.append(c.nodeType ? c : document.createTextNode(String(c)));
        }
        return e;
    };

    const getJson = async (url) => {
        const data = await aimsFetch(url, { credentials: "same-origin", cache: "no-store" });
        return data;
    };

    // ---------------- data loaders ----------------
    async function getUniqueTypes() {
        const raw = await getJson("/api/assets/types/unique");

        let types = [];
        if (Array.isArray(raw)) {
            if (raw.length && typeof raw[0] === "string") {
                types = raw;
            } else {
                types = raw.map(o => {
                    if (!o || typeof o !== "object") return null;
                    return o.assetType ?? o.type ?? o.name ?? o.Type ?? o.AssetType ?? null;
                }).filter(Boolean);
            }
        }

        // Normalize + dedupe
        const norm = Array.from(new Set(types.map(t => String(t).trim()))).filter(Boolean);
        if (!norm.length) console.warn("[dashboard-settings] unique() returned no usable types:", raw);
        return norm;
    }

    async function loadData() {
        const types = await getUniqueTypes();

        const thresholds = await getJson("/api/thresholds");
        const byTypeThreshold = new Map(
            thresholds.map(t => [String(t.assetType || t.AssetType).toLowerCase(),
            Number(t.thresholdValue ?? t.ThresholdValue ?? 0)])
        );

        const params = new URLSearchParams();
        params.set("types", types.join(","));
        const summary = await getJson(`/api/summary/cards?${params.toString()}`);
        const byTypeSummary = new Map(
            summary.map(r => [String(r.assetType || r.AssetType).toLowerCase(), r])
        );

        let visibleSet = readVisibleSet();
        if (!visibleSet) {
            // default: all types visible
            visibleSet = new Set(types.map(t => t.toLowerCase()));
            writeVisibleSet(visibleSet);
        }

        const rows = types.map(t => {
            const key = t.toLowerCase();
            const s = byTypeSummary.get(key);

            const total = Number(s?.total ?? 0);
            const available = Number(s?.available ?? 0);
            const threshold = byTypeThreshold.get(key) ?? 0;

            const isSoftware = (key === "software");
            const used = Math.max(0, total - available);
            const usedPct = total ? Math.round((used / total) * 100) : 0;

            const isLow = isSoftware
                ? (threshold > 0 && usedPct >= threshold) // software uses % used
                : (threshold > 0 && available < threshold); // hardware uses count available

            return {
                assetType: t,
                threshold,
                total,
                available,
                isLow,
                visible: visibleSet.has(key),
            };
        }).sort((a, b) => a.assetType.localeCompare(b.assetType));

        return { rows, visibleSet };
    }

    // ---------------- renderers ----------------
    function renderRows(rows, visibleSet) {
        const tbody = document.querySelector("#ds-table tbody");
        if (!tbody) return;
        tbody.innerHTML = "";

        const frag = document.createDocumentFragment();

        for (const r of rows) {
            const tr = el("tr", { "data-asset-type": r.assetType });

            // visible checkbox
            const chk = el("input", {
                type: "checkbox",
                checked: r.visible ? "checked" : null,
                onchange: (e) => {
                    const key = r.assetType.toLowerCase();
                    if (e.target.checked) visibleSet.add(key);
                    else visibleSet.delete(key);
                }
            });

            // threshold input (percent for software; count otherwise)
            const isSoftware = r.assetType.trim().toLowerCase() === "software";
            const thresholdInput = el("input", {
                type: "number",
                min: "0",
                max: isSoftware ? "100" : undefined,
                step: "1",
                value: r.threshold,
                "aria-label": `Threshold for ${r.assetType}${isSoftware ? " (% used)" : ""}`
            });
            thresholdInput.dataset.original = String(r.threshold);

            // add a little suffix for clarity
            const thresholdCell = el("td", {});
            thresholdCell.append(thresholdInput);
            if (isSoftware) thresholdCell.append(" %");

            // status capsule
            const statusClass = (r.total === 0) ? "empty" : (r.isLow ? "low" : "ok");
            const statusCapsule = el("span", { class: `aimsds-capsule ${statusClass}` },
                (r.total === 0) ? "Empty" : (r.isLow ? "LOW" : "OK")
            );

            tr.append(
                el("td", {}, chk),
                el("td", {}, r.assetType),
                thresholdCell,
                el("td", {}, String(r.available)),
                el("td", {}, String(r.total)),
                el("td", {}, statusCapsule),
            );
            tr._thresholdInput = thresholdInput;
            frag.appendChild(tr);
        }

        tbody.appendChild(frag);
    }

    function attachFilter() {
        const filter = document.getElementById("ds-filter");
        const reset = document.getElementById("ds-reset");
        const tbody = document.querySelector("#ds-table tbody");
        if (!filter || !reset || !tbody) return;

        const apply = () => {
            const q = (filter.value || "").trim().toLowerCase();
            for (const tr of tbody.querySelectorAll("tr")) {
                const name = tr.getAttribute("data-asset-type")?.toLowerCase() || "";
                tr.style.display = (!q || name.includes(q)) ? "" : "none";
            }
        };

        filter.addEventListener("input", apply);
        reset.addEventListener("click", () => { filter.value = ""; apply(); });
        apply();
    }

    // ---------------- save + post-save refresh ----------------
    async function saveChanges() {
        const btn = document.getElementById("ds-save");
        const lbl = document.getElementById("ds-message");
        const tbody = document.querySelector("#ds-table tbody");
        const rowEls = [...(tbody?.querySelectorAll("tr") ?? [])];

        // Lock UI
        btn && (btn.disabled = true);
        lbl && (lbl.textContent = "Saving…");

        // 1) PUT thresholds only if changed
        const putOps = [];
        for (const tr of rowEls) {
            const assetType = tr.getAttribute("data-asset-type");
            const input = tr._thresholdInput;
            if (!assetType || !input) continue;

            // normalize per-type (Software = % used 0–100; others = non-negative count)
            const isSoftware = (assetType || "").trim().toLowerCase() === "software";
            const newValRaw = parseInt(input.value ?? "0", 10);
            if (Number.isNaN(newValRaw)) continue;

            const normalized = isSoftware
                ? Math.max(0, Math.min(100, newValRaw))
                : Math.max(0, newValRaw);

            // reflect any clamping back into the UI so dataset.original comparison makes sense
            if (normalized !== newValRaw) input.value = String(normalized);

            const oldVal = parseInt(input.dataset.original ?? "0", 10);
            if (normalized === oldVal) continue;

            putOps.push(
                fetch(`/api/thresholds/${encodeURIComponent(assetType)}`, {
                    method: "PUT",
                    credentials: "same-origin",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ assetType, thresholdValue: normalized })
                })
            );
        }
        
        // 2) Persist visibility from current checkboxes
        const visibleNow = new Set();
        for (const tr of rowEls) {
            const type = tr.getAttribute("data-asset-type") || "";
            const checked = tr.querySelector("input[type=checkbox]")?.checked;
            if (checked) visibleNow.add(type.toLowerCase());
        }
        writeVisibleSet(visibleNow);

        // Update page immediately: toggle .d-none only (no inline display!)
        document.querySelectorAll(".card[data-asset-type]").forEach(card => {
            const key = (card.dataset.assetType || "").trim().toLowerCase();
            card.closest(".card-link")?.classList.toggle("d-none", !visibleNow.has(key));
        });

        // Let the pager recompute (it already filters on .d-none)
        window.dispatchEvent(new CustomEvent("dashboard:visibility-changed"));

        try {
            // Finish writes
            const resps = await Promise.all(putOps);
            for (const r of resps) {
                if (!r.ok) throw new Error(`Save failed: ${r.status} ${await r.text()}`);
            }

            // 3) Refetch thresholds + summary (cache-busted)
            const ts = Date.now();
            const types = await getUniqueTypes();

            const params = new URLSearchParams();
            params.set("types", types.join(","));
            params.set("ts", String(ts));

            const [freshThresholds, freshSummary] = await Promise.all([
                fetch(`/api/thresholds?ts=${ts}`, { cache: "no-store", credentials: "same-origin" }).then(r => r.json()),
                fetch(`/api/summary/cards?${params.toString()}`, { cache: "no-store", credentials: "same-origin" }).then(r => r.json()),
            ]);

            const thMap = new Map(
                freshThresholds.map(t => [String(t.assetType || t.AssetType).toLowerCase(), Number(t.thresholdValue ?? t.ThresholdValue ?? 0)])
            );
            const sumMap = new Map(
                freshSummary.map(r => [String(r.assetType || r.AssetType).toLowerCase(), r])
            );

            const freshRows = types.map(t => {
                const key = t.toLowerCase();
                const s = sumMap.get(key);
                const threshold = thMap.get(key) ?? 0;
                const available = Number(s?.available ?? 0);
                const total = Number(s?.total ?? 0);
                const isSoftware = key === "software";
                const used = Math.max(0, total - available);
                const usedPct = total ? Math.round((used / total) * 100) : 0;
                const isLow = isSoftware
                    ? (threshold > 0 && usedPct >= threshold)
                    : (threshold > 0 && available < threshold);
                return { assetType: t, threshold, total, available, isLow, visible: visibleNow.has(key) };
            }).sort((a, b) => a.assetType.localeCompare(b.assetType));

            // 4) Re-render modal rows & bump "original" baselines
            renderRows(freshRows, visibleNow);
            document.querySelectorAll("#ds-table tbody input[type=number]").forEach(inp => {
                inp.dataset.original = inp.value;
            });
            attachFilter();

            // 5) Update card numbers + status (no layout-affecting changes)
            const byKey = new Map(freshRows.map(r => [r.assetType.toLowerCase(), r]));
            document.querySelectorAll(".card[data-asset-type]").forEach(card => {
                const key = (card.dataset.assetType || "").toLowerCase();
                const d = byKey.get(key);
                if (!d) return;

                const totalEl = card.querySelector(".js-total");
                const availEl = card.querySelector(".js-available");
                const pctEl = card.querySelector(".js-percent");
                if (totalEl) totalEl.textContent = d.total;
                if (availEl) availEl.textContent = d.available;
                if (pctEl) pctEl.textContent = d.total ? Math.round((d.available / d.total) * 100) : 0;

                const dot = card.querySelector(".status-dot");
                if (dot) {
                    dot.classList.remove("red", "green", "yellow");
                    if (d.total === 0) dot.classList.add("yellow");
                    else if (d.isLow) dot.classList.add("red");
                    else dot.classList.add("green");
                }

                const txt = card.querySelector(".availability, .runningLow");
                if (txt) {
                    // hard reset to the correct single class
                    txt.classList.remove("runningLow", "availability");
                    txt.classList.add(d.isLow ? "runningLow" : "availability");
                }

                card.title = d.threshold > 0
                    ? `Threshold: ${d.threshold} • Available: ${d.available}/${d.total}`
                    : `Available: ${d.available}/${d.total}`;
            });

            lbl && (lbl.textContent = "Saved. Refreshed.");
        } catch (err) {
            console.error(err);
            lbl && (lbl.textContent = "Save failed. See console.");
        } finally {
            btn && (btn.disabled = false);
            setTimeout(() => { if (lbl) lbl.textContent = ""; }, 2000);
        }
    }

    // ---- visibility helper (reused by page + modal) ----
    const normType = (s) => String(s || "").trim().toLowerCase().replace(/\s+/g, " ");
    const normLoose = (s) => {
        const k = normType(s);
        return k.endsWith("s") ? k.slice(0, -1) : k;
    };

    // Exposed for Home to call on load and when it needs to re-apply
    window.applyCardVisibilityFromStorage = function applyCardVisibilityFromStorage() {
        try {
            const raw = localStorage.getItem(VIS_STORAGE_KEY);
            if (!raw) return;

            const arr = JSON.parse(raw) || [];
            const visible = new Set([...arr.map(normType), ...arr.map(normLoose)]);

            document.querySelectorAll(".card[data-asset-type]").forEach(card => {
                const key = normType(card.dataset.assetType);
                const loose = normLoose(key);
                const show = visible.has(key) || visible.has(loose);
                card.closest(".card-link")?.classList.toggle("d-none", !show);
            });

            // Let the pager recompute to avoid empty pages
            window.dispatchEvent(new CustomEvent("dashboard:visibility-changed"));
        } catch (e) {
            console.warn("[visibility]", e);
        }
    };

    // ---------------- public init ----------------
    window.initDashboardSettingsModal = async function initDashboardSettingsModal() {
        const first = await loadData();
        renderRows(first.rows, first.visibleSet);
        attachFilter();

        const modalEl = document.getElementById("dashboardSettingsModal");
        if (modalEl) {
            modalEl.addEventListener("shown.bs.modal", async () => {
                try {
                    const fresh = await loadData();
                    renderRows(fresh.rows, fresh.visibleSet);
                    attachFilter();
                } catch (e) {
                    console.error("Settings modal refresh failed", e);
                }
            });
        }

        document.getElementById("ds-save")?.addEventListener("click", saveChanges);
    };

    // Auto-init once the page is ready (only if the modal exists)
    if (!window.__dsInit) {
        window.__dsInit = true;
        document.addEventListener("DOMContentLoaded", () => {
            const modalEl = document.getElementById("dashboardSettingsModal");
            if (modalEl && typeof window.initDashboardSettingsModal === "function") {
                window.initDashboardSettingsModal().catch(err =>
                    console.error("dashboard settings init failed", err)
                );
            }

            // Ensure visibility is applied on first load
            if (typeof window.applyCardVisibilityFromStorage === "function") {
                window.applyCardVisibilityFromStorage();
            }
        });
    }
})();