// Seat mode chooser – intermediate Assign/Unassign modal
(function wireSeatModeModal() {
    console.log("[SeatMode] booting…");

    const modalEl = document.getElementById("seatModeModal");
    console.log("[SeatMode] modalEl =", modalEl);
    console.log("[SeatMode] window.bootstrap =", window.bootstrap);

    if (!modalEl || !window.bootstrap) {
        console.warn("[SeatMode] Missing modal element or bootstrap; exiting.");
        return;
    }

    const modal = new bootstrap.Modal(modalEl, { backdrop: "static", keyboard: true });

    const assetLine = modalEl.querySelector("[data-role='seat-mode-asset']");
    const summaryLine = modalEl.querySelector("[data-role='seat-mode-summary']");
    const assignBtn = modalEl.querySelector("[data-role='seat-mode-assign']");
    const unassignBtn = modalEl.querySelector("[data-role='seat-mode-unassign']");

    const hidTag = modalEl.querySelector("[data-role='seat-mode-assetTag']");
    const hidId = modalEl.querySelector("[data-role='seat-mode-assetNumericId']");
    const hidKind = modalEl.querySelector("[data-role='seat-mode-assetKind']");

    let lastPayload = null;

    // Open the modal when Search.js raises seat:choose:open
    window.addEventListener("seat:choose:open", (ev) => {
        const detail = ev.detail || {};
        lastPayload = detail;

        const { assetTag, assetNumericId, assetKind, seats } = detail;
        const totalSeats = seats?.totalSeats ?? seats?.TotalSeats ?? null;
        const usedSeats = seats?.usedSeats ?? seats?.UsedSeats ?? null;
        const availSeats = seats?.avail ??
            (totalSeats != null && usedSeats != null ? Math.max(0, totalSeats - usedSeats) : null);

        if (hidTag) hidTag.value = assetTag || "";
        if (hidId) hidId.value = assetNumericId ?? "";
        if (hidKind) hidKind.value = assetKind ?? 2;

        if (assetLine) {
            assetLine.textContent = assetTag
                ? `Software asset ${assetTag}`
                : "Selected software asset";
        }

        if (summaryLine) {
            if (totalSeats != null && usedSeats != null) {
                summaryLine.textContent =
                    `Currently ${usedSeats} of ${totalSeats} seats are in use` +
                    (availSeats != null ? ` (${availSeats} available).` : ".");
            } else {
                summaryLine.textContent =
                    "This software has seats in use and free seats available. What would you like to do?";
            }
        }

        modal.show();
    });

    // Assign path → re-dispatch as assign:open
    assignBtn?.addEventListener("click", () => {
        if (!lastPayload) return;
        const { assetTag, assetNumericId, assetKind, currentUserId } = lastPayload;
        modal.hide();

        window.dispatchEvent(new CustomEvent("assign:open", {
            detail: {
                assetTag: assetTag ?? null,
                assetNumericId: assetNumericId ?? null,
                assetKind: assetKind ?? 2,
                currentUserId: currentUserId ?? null
            }
        }));
    });

    // Unassign path → re-dispatch as unassign:open
    unassignBtn?.addEventListener("click", () => {
        if (!lastPayload) return;
        const { assetTag, assetNumericId, assetKind, currentUserId } = lastPayload;
        modal.hide();

        window.dispatchEvent(new CustomEvent("unassign:open", {
            detail: {
                assetTag: assetTag ?? null,
                assetNumericId: assetNumericId ?? null,
                assetKind: assetKind ?? 2,
                currentUserId: currentUserId ?? null
            }
        }));
    });
})();