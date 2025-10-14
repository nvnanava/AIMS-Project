(function () {
    "use strict";

    const BLANK = "/images/asset-icons/blank-icon.png";

    const slugify = (t) =>
        String(t || "")
            .trim()
            .toLowerCase()
            .replace(/\s+/g, "-")
            .replace(/[^a-z0-9\-]/g, "");

    const alias = new Map([
        ["3d-printers", "3d-printer"],
        ["document-scanners", "document-scanner"],
        ["label-printers", "label-printer"],
        ["misc-cables", "misc-cables"],
        ["scanner-guns", "scanner-gun"],
        ["security-cameras", "security-camera"],
        ["smart-sensors", "smart-sensor"],
        ["standing-desks", "standing-desk"],
        ["vr-headsets", "vr-headset"],
        ["charging-cables", "charging-cable"],
        ["docking-stations", "docking-station"]
    ]);

    const normalizeSlug = (t) => alias.get(slugify(t)) ?? slugify(t);

    // Cache-bust real icons (not blank)
    const bust = (url) => {
        const rev = (window.__ASSET_REV__ ?? "");
        if (!rev) return url;
        return `${url}${url.includes("?") ? "&" : "?"}v=${encodeURIComponent(rev)}`;
    };

    function paintNumbersAndStatusFromRow(card, row) {
        const dot = card.querySelector(".status-dot");
        const elTotal = card.querySelector(".js-total");
        const elAvail = card.querySelector(".js-available");
        const elPct = card.querySelector(".js-percent");

        if (!row) {
            dot?.classList.remove("green", "red");
            dot?.classList.add("yellow");
            return;
        }

        const total = Number(row.total ?? row.Total ?? 0);
        const available = Number(row.available ?? row.Available ?? 0);
        const threshold = Number(row.threshold ?? row.Threshold ?? 0);
        const availablePercent = total ? Math.round((available / total) * 100) : 0;

        if (elTotal) elTotal.textContent = total;
        if (elAvail) elAvail.textContent = available;
        if (elPct) elPct.textContent = availablePercent;

        dot?.classList.remove("green", "red", "yellow");
        dot?.classList.add(threshold > 0 && available < threshold ? "red" : (total === 0 ? "yellow" : "green"));

        card.title = threshold > 0
            ? `Threshold: ${threshold} • Available: ${available}/${total}`
            : `Available: ${available}/${total}`;
    }

    // ---- ICONS ----
    function preloadAndSetIcons(cards) {
        cards.forEach(card => {
            const img = card.querySelector(".card-icon");
            if (!img) return;

            // Set stable attributes (do not touch CSS layout)
            if (!img.getAttribute("width")) img.setAttribute("width", "72");
            if (!img.getAttribute("height")) img.setAttribute("height", "72");
            img.decoding = "async";
            img.loading = "eager";

            // Single error fallback → blank placeholder (no further swaps)
            img.addEventListener("error", () => {
                if (!img.dataset._failedOnce) {
                    img.dataset._failedOnce = "1";
                    img.src = BLANK;
                }
            }, { once: true });
        });
    }

    // ---- NUMBERS / STATE (API) ----
    async function paintFromApi(cards) {
        const types = cards.map(c => c.dataset.assetType);
        const qs = encodeURIComponent(types.join(","));
        let rows = [];

        try {
            const res = await fetch(`/api/summary/cards?types=${qs}`, { cache: "no-store" });
            if (res.ok) rows = await res.json();
        } catch {
            // ignore network errors; leave existing numbers in place
        }

        const byType = new Map(
            rows.map(r => [String(r.assetType || r.AssetType).toLowerCase(), r])
        );

        cards.forEach((card) => {
            const key = String(card.dataset.assetType || "").toLowerCase();
            const row = byType.get(key);
            paintNumbersAndStatusFromRow(card, row);
        });
    }

    // Public API
    window.SummaryCards = {
        preloadAndSetIcons,
        paintFromApi,
        _normalizeSlug: normalizeSlug, // exposed for tests/debugging
        _bust: bust
    };
})();