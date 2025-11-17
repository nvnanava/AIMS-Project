// Hardware: simple (auto-resolve assignment + comment, shows Assigned To)
// Software: show assigned users picker (search only among assigned users)

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

        // Assignee row (hardware only)
        const selAssigneeRow = document.getElementById("unSelAssigneeRow");
        const selAssigneeName = document.getElementById("unSelAssigneeName");

        // Software-only picker
        const userRow = document.getElementById("unassignUserRow");
        const userPicker = document.getElementById("unassignUserPicker");
        const userList = document.getElementById("unassignUserList");
        const userSearchInput = document.getElementById("unassignUserSearchInput");
        const userHiddenInput = document.getElementById("unassignUserIdHidden");

        const safeText = (el, txt) => { if (el) el.textContent = txt; };
        const safeVal = (el, val) => { if (el) el.value = val; };

        const clearChildren = (el) => {
            while (el?.firstChild) el.removeChild(el.firstChild);
            if (el && typeof el.scrollTop === "number") {
                el.scrollTop = 0;
            }
        };

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

        let currentAssetKind = 1;
        let currentAssetId = null;
        let currentTotalSeats = null;
        let currentUsedSeats = null;

        // Track whether software has active assignees (for auto-focus)
        let hasSoftwareAssignees = false;

        // Keyboard active index for software list (like assign modal)
        let activeUserIndex = -1;

        // Track which user we are unassigning (for unassign:saved)
        let currentUnassignUserId = null;
        let currentUnassignDisplayName = null;
        let currentUnassignEmployeeNumber = null;

        // Prevent scrollIntoView from firing while we're initially populating the list
        let suppressAutoScroll = false;

        // Comment is required now (HTML5 validation + JS mirror)
        if (commentBox) {
            commentBox.required = true;
        }

        // Make userList focusable for keyboard nav
        if (userList && !userList.hasAttribute("tabindex")) {
            userList.tabIndex = 0;
        }

        async function fetchActiveAssignments() {
            const url = `/api/assign/list?status=active&_r=${Date.now()}`; // cache-buster
            const list = await aimsFetch(url, {
                headers: cacheHeaders,
                ttl: 0 // disable aimsFetch TTL cache for this call
            });
            return Array.isArray(list) ? list : (list?.items ?? []);
        }

        async function fetchAssetSummary(kind, id) {
            try {
                const url = kind === 2
                    ? `/api/assets/one?softwareId=${id}&_v=${assetsVer}`
                    : `/api/assets/one?hardwareId=${id}&_v=${assetsVer}`;
                return await aimsFetch(url, { cache: "no-store" });
            } catch {
                return null;
            }
        }

        function updateUserActiveIndex(newIndex) {
            if (!userList) return;
            const options = Array.from(userList.querySelectorAll(".aims-userpicker__option"))
                .filter(o => o.style.display !== "none");
            if (!options.length) return;

            const clamped = Math.max(0, Math.min(options.length - 1, newIndex));
            activeUserIndex = clamped;

            options.forEach((opt, idx) => {
                if (idx === activeUserIndex) {
                    opt.setAttribute("data-active", "true");
                    if (!suppressAutoScroll) {
                        opt.scrollIntoView({ block: "nearest" });
                    }
                } else {
                    opt.removeAttribute("data-active");
                }
            });
        }

        // Select user button:
        // - sets hidden fields
        // - fills search input
        // - tracks display name/emp#
        // - moves focus to required comment box
        function selectUserButton(btn, assignmentId, userId) {
            if (!btn) return;

            const buttons = Array.from(userList.querySelectorAll(".aims-userpicker__option"));
            buttons.forEach(b => {
                b.removeAttribute("aria-selected");
                b.removeAttribute("data-active");
            });
            btn.setAttribute("aria-selected", "true");
            btn.setAttribute("data-active", "true");

            // update hidden fields
            safeVal(hiddenId, String(assignmentId));
            safeVal(userHiddenInput, userId != null ? String(userId) : "");

            // read explicit display name + emp# from data attributes
            const baseDisplay =
                (btn.getAttribute("data-display-name") || btn.textContent || "").trim() ||
                String(assignmentId);
            const empNum = btn.getAttribute("data-employee-number") || "";

            // show user display (name + emp#) instead of numeric assignment id
            safeText(selAssnId, baseDisplay);

            // mirror into the search box (like assign modal)
            if (userSearchInput) {
                userSearchInput.value = empNum ? `${baseDisplay} (${empNum})` : baseDisplay;
            }

            // remember for unassign:saved event (name-first, id is secondary)
            currentUnassignUserId = userId != null ? Number(userId) : null;
            currentUnassignDisplayName = baseDisplay || null;
            currentUnassignEmployeeNumber = empNum || null;

            // jump to required comment box
            if (commentBox) {
                commentBox.focus();
                if (typeof commentBox.select === "function") {
                    commentBox.select();
                }
            }
        }

        async function populateSoftwareAssignees(softwareId) {
            if (!userList) return;

            suppressAutoScroll = true;
            clearChildren(userList);
            safeVal(hiddenId, "");
            safeVal(userHiddenInput, "");
            safeText(selAssnId, "");

            hasSoftwareAssignees = false;
            activeUserIndex = -1;

            if (!Number.isFinite(softwareId) || softwareId <= 0) {
                const empty = document.createElement("div");
                empty.className = "aims-userpicker__empty";
                empty.textContent = "No software selected.";
                userList.appendChild(empty);

                if (typeof userList.scrollTop === "number") {
                    userList.scrollTop = 0;
                }

                suppressAutoScroll = false;
                return;
            }

            try {
                const all = await fetchActiveAssignments();
                const matches = all.filter(a =>
                    Number(a.softwareID ?? a.softwareId ?? -1) === softwareId &&
                    (a.unassignedAtUtc == null || a.unassignedAtUtc === undefined)
                );

                if (!matches.length) {
                    const empty = document.createElement("div");
                    empty.className = "aims-userpicker__empty";
                    empty.textContent = "No active users currently assigned to this software.";
                    userList.appendChild(empty);

                    if (typeof userList.scrollTop === "number") {
                        userList.scrollTop = 0;
                    }

                    suppressAutoScroll = false;
                    return;
                }

                hasSoftwareAssignees = true;

                // Build list of buttons
                matches.forEach(a => {
                    const userId = a.userID ?? a.userId;

                    const baseName =
                        a.userFullName ||
                        a.user ||
                        a.userName ||
                        a.assignedTo ||
                        (userId != null ? `User #${userId}` : "Unknown user");

                    const empNum =
                        a.employeeNumber ??
                        a.EmployeeNumber ??
                        a.assignedEmployeeNumber ??
                        "";

                    const display = empNum ? `${baseName} (${empNum})` : baseName;

                    const btn = document.createElement("button");
                    btn.type = "button";
                    btn.className = "aims-userpicker__option";
                    btn.setAttribute("role", "option");
                    btn.setAttribute("data-user-id", userId != null ? String(userId) : "");
                    btn.setAttribute("data-assignment-id", String(a.assignmentID ?? a.assignmentId));
                    btn.setAttribute("data-display-name", baseName);
                    btn.setAttribute("data-employee-number", empNum);
                    btn.textContent = display;

                    btn.addEventListener("click", () => {
                        selectUserButton(btn, a.assignmentID ?? a.assignmentId, userId);
                    });

                    userList.appendChild(btn);
                });

                // ensure we start at top after repopulating list
                if (typeof userList.scrollTop === "number") {
                    userList.scrollTop = 0;
                }
            } catch (e) {
                console.error("[unassign] populateSoftwareAssignees failed:", e);
                const empty = document.createElement("div");
                empty.className = "aims-userpicker__empty";
                empty.textContent = "Could not load assigned users.";
                userList.appendChild(empty);

                if (typeof userList.scrollTop === "number") {
                    userList.scrollTop = 0;
                }
            } finally {
                suppressAutoScroll = false;
            }
        }

        // Simple local filter + keyboard nav for the assigned users list
        function bindUserSearchFilter() {
            if (!userSearchInput || userSearchInput._aimsBoundFilter) return;
            userSearchInput._aimsBoundFilter = true;

            // Text filter
            userSearchInput.addEventListener("input", () => {
                const term = (userSearchInput.value || "").toLowerCase().trim();
                const buttons = Array.from(userList.querySelectorAll(".aims-userpicker__option"));
                buttons.forEach(btn => {
                    const txt = (btn.textContent || "").toLowerCase();
                    btn.style.display = term && !txt.includes(term) ? "none" : "";
                });

                // reset active index when filter changes
                activeUserIndex = -1;
                const opts = userList.querySelectorAll(".aims-userpicker__option");
                opts.forEach(o => o.removeAttribute("data-active"));
            });

            // Keyboard from search input → list (ArrowDown/Enter)
            userSearchInput.addEventListener("keydown", (ev) => {
                const options = Array.from(
                    userList.querySelectorAll(".aims-userpicker__option")
                ).filter(o => o.style.display !== "none");
                if (!options.length) return;

                if (ev.key === "ArrowDown") {
                    ev.preventDefault();
                    userList.focus();
                    activeUserIndex = 0;
                    updateUserActiveIndex(0);
                } else if (ev.key === "Enter") {
                    ev.preventDefault();
                    const firstVisible = options[0];
                    if (firstVisible) {
                        const assnId = firstVisible.getAttribute("data-assignment-id");
                        const userId = firstVisible.getAttribute("data-user-id");
                        selectUserButton(
                            firstVisible,
                            Number(assnId),
                            userId ? Number(userId) : null
                        );
                    }
                }
            });

            // Keyboard inside list – ArrowUp/Down/Home/End/Enter/Escape
            if (!userList._aimsBoundKey) {
                userList._aimsBoundKey = true;
                userList.addEventListener("keydown", (ev) => {
                    const options = Array.from(
                        userList.querySelectorAll(".aims-userpicker__option")
                    ).filter(o => o.style.display !== "none");
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
                            const btn = options[activeUserIndex];
                            const assnId = btn.getAttribute("data-assignment-id");
                            const userId = btn.getAttribute("data-user-id");
                            selectUserButton(
                                btn,
                                Number(assnId),
                                userId ? Number(userId) : null
                            );
                        }
                    } else if (ev.key === "Escape") {
                        ev.preventDefault();
                        userList.blur();
                        userSearchInput.focus();
                        userSearchInput.select();
                    }
                });
            }
        }

        // Always reset scroll of the modal body + list when opening
        modalEl.addEventListener("show.bs.modal", () => {
            const body = modalEl.querySelector(".modal-body");
            if (body && typeof body.scrollTop === "number") {
                body.scrollTop = 0;
            }
            if (userList && typeof userList.scrollTop === "number") {
                userList.scrollTop = 0;
            }
        });

        // Auto-focus behavior like Assign:
        // When modal is shown AND it's a software asset with 1+ assignees → focus search bar.
        modalEl.addEventListener("shown.bs.modal", () => {
            if (
                currentAssetKind === 2 &&
                hasSoftwareAssignees &&
                userRow &&
                userRow.style.display !== "none" &&
                userSearchInput
            ) {
                userSearchInput.focus();
                userSearchInput.select();
            }
        });

        // Populate modal from Search event
        window.addEventListener("unassign:open", async (ev) => {
            const d = ev.detail || {};
            try {
                console.debug("[unassign] open detail:", d);
                const assetKind = Number(d.assetKind) === 2 ? 2 : 1;
                const assetId = Number(d.assetNumericId);
                const userId = Number(d.currentUserId ?? d.userId);
                let assignmentIdRaw = d.assignmentID ?? d.assignmentId;
                let assignmentId = Number.isFinite(Number(assignmentIdRaw)) ? Number(assignmentIdRaw) : NaN;

                currentAssetKind = assetKind;
                currentAssetId = assetId;

                // reset current user info for this open
                currentUnassignUserId = null;
                currentUnassignDisplayName = null;
                currentUnassignEmployeeNumber = null;

                // ALWAYS clear the software picker UI on open so nothing is preselected
                if (userSearchInput) {
                    userSearchInput.value = "";
                }
                clearChildren(userList);
                activeUserIndex = -1;
                hasSoftwareAssignees = false;

                // Start with whatever the caller sent
                currentTotalSeats = Number.isFinite(Number(d.totalSeats)) ? Number(d.totalSeats) : null;
                currentUsedSeats = Number.isFinite(Number(d.usedSeats)) ? Number(d.usedSeats) : null;

                // Prefer API for fresh data
                let apiName = "";
                let apiTag = "";
                const one = Number.isFinite(assetId) ? await fetchAssetSummary(assetKind, assetId) : null;
                if (one) {
                    apiName = (one.assetName ?? one.softwareName ?? "").trim();
                    apiTag = (one.tag ?? one.assetTag ?? "").trim();

                    if (assetKind === 2) {
                        const apiTotal = Number(one.licenseTotalSeats ?? one.LicenseTotalSeats ?? NaN);
                        const apiUsed = Number(one.licenseSeatsUsed ?? one.LicenseSeatsUsed ?? NaN);

                        if (Number.isFinite(apiTotal)) currentTotalSeats = apiTotal;
                        if (Number.isFinite(apiUsed)) currentUsedSeats = apiUsed;
                    }
                }

                let name = apiName || (d.assetName || "").trim();
                let tag = apiTag || (d.assetTag || "").trim() || (Number.isFinite(assetId) ? `(${assetId})` : "—");

                safeText(selName, name || "—");
                safeText(selTag, tag);
                safeText(selKind, assetKind === 2 ? "Software" : "Hardware");

                // Reset fields
                safeText(selAssnId, "");
                safeVal(hiddenId, "");
                safeVal(userHiddenInput, "");
                if (commentBox) {
                    commentBox.value = "";
                    commentBox.classList.remove("is-invalid");
                }

                // Toggle assignee row visibility based on kind
                if (assetKind === 1) {
                    if (selAssigneeRow) selAssigneeRow.style.display = "";
                } else {
                    if (selAssigneeRow) selAssigneeRow.style.display = "none";
                    safeText(selAssigneeName, "—");
                }

                if (assetKind === 1) {
                    // ---------------- HARDWARE: simple auto-resolve ----------------
                    if (userRow) {
                        userRow.style.display = "none";
                    }
                    // already cleared userSearchInput + userList above

                    // Resolve assignmentId if not provided
                    let assigneeLabel = d.currentUserName || d.currentUserFullName || d.assignedTo || "";
                    let resolvedUserId = Number.isFinite(userId) && userId > 0 ? userId : null;
                    let resolvedEmpNum = "";

                    if (!Number.isFinite(assignmentId) || !assignmentId) {
                        try {
                            const list = await fetchActiveAssignments();
                            let match = null;

                            if (Number.isFinite(userId) && userId > 0) {
                                match = list.find(a =>
                                    Number(a.userID ?? a.userId ?? -1) === userId &&
                                    Number(a.hardwareID ?? a.hardwareId ?? -1) === assetId
                                );
                            }

                            if (!match) {
                                match = list.find(a =>
                                    Number(a.hardwareID ?? a.hardwareId ?? -1) === assetId
                                );
                            }

                            if (match) {
                                assignmentId = Number(match.assignmentID ?? match.assignmentId);

                                resolvedUserId = Number(match.userID ?? match.userId ?? resolvedUserId ?? NaN) || null;

                                const baseName =
                                    assigneeLabel ||
                                    match.userFullName ||
                                    match.userName ||
                                    match.assignedTo ||
                                    (match.userID != null ? `User #${match.userID}` : "");

                                const empNum =
                                    match.employeeNumber ??
                                    match.EmployeeNumber ??
                                    match.assignedEmployeeNumber ??
                                    "";

                                resolvedEmpNum = empNum || "";
                                assigneeLabel = empNum ? `${baseName} (${empNum})` : baseName;
                            }
                        } catch (e) {
                            console.warn("[unassign] failed to resolve hardware assignment:", e);
                        }
                    }

                    // keep assignment id only in hidden field for POST
                    safeVal(hiddenId, Number.isFinite(assignmentId) ? String(assignmentId) : "");
                    // don't show assignment id in the summary box anymore
                    safeText(selAssnId, "");

                    if (selAssigneeRow) {
                        safeText(selAssigneeName, assigneeLabel || "—");
                    }

                    // remember which user we're unassigning for hardware
                    currentUnassignUserId = resolvedUserId;
                    currentUnassignDisplayName = assigneeLabel || null;
                    currentUnassignEmployeeNumber = resolvedEmpNum || null;

                    if (!hiddenId?.value) {
                        console.warn("Unassign modal: could not resolve AssignmentID for hardware", { assetId, userId });
                        showToast("Couldn’t find the active assignment for this asset.");
                    }
                } else {
                    // ---------------- SOFTWARE: show assigned-users picker ----------------
                    if (userRow) userRow.style.display = "";
                    bindUserSearchFilter();
                    await populateSoftwareAssignees(assetId);
                }

                modal.show();

                // 👇 Force scroll reset AFTER Bootstrap finishes opening + layout
                setTimeout(() => {
                    const body = modalEl.querySelector(".modal-body");
                    if (body && typeof body.scrollTop === "number") {
                        body.scrollTop = 0;
                    }
                    if (userList && typeof userList.scrollTop === "number") {
                        userList.scrollTop = 0;
                    }
                }, 0);
            } catch (err) {
                console.error("[unassign] handler error:", err);
                showToast("Something went wrong opening the unassign modal.");
                try { modal.show(); } catch { /* ignore */ }
            }
        });

        // Submit unassign
        formEl.addEventListener("submit", async (e) => {
            e.preventDefault();

            const id = hiddenId.value;
            if (!id) {
                alert("No assignment selected to unassign.");
                return;
            }

            // Mirror Assign's validation behavior (HTML5 "required")
            if (!formEl.checkValidity()) {
                formEl.classList.add("was-validated");
                formEl.reportValidity();
                return;
            }

            const rawComment = commentBox.value.trim();
            const comment = rawComment;

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

                // Software: derive new seat counts from the search row chip and broadcast seat:updated
                if (currentAssetKind === 2 && Number.isFinite(currentAssetId)) {
                    try {
                        const tr = document.querySelector(`[data-id='sw-${currentAssetId}']`);
                        if (!tr) {
                            console.warn("[unassign] no table row found for softwareId", currentAssetId);
                        } else {
                            const chip = tr.querySelector(".seats-count");
                            if (!chip || !chip.textContent || !chip.textContent.includes("/")) {
                                console.warn("[unassign] no seats-count chip text to parse", { chipText: chip?.textContent });
                            } else {
                                const [usedStr, totalStr] = chip.textContent.split("/").map(s => s.trim());
                                const prevUsed = Number(usedStr);
                                const total = Number(totalStr);

                                if (Number.isFinite(prevUsed) && Number.isFinite(total)) {
                                    const newUsed = Math.max(0, prevUsed - 1);

                                    window.dispatchEvent(new CustomEvent("seat:updated", {
                                        detail: {
                                            softwareId: currentAssetId,
                                            licenseSeatsUsed: newUsed,
                                            licenseTotalSeats: total
                                        }
                                    }));

                                    currentTotalSeats = total;
                                    currentUsedSeats = newUsed;
                                } else {
                                    console.warn("[unassign] parsed non-finite counts from chip", {
                                        prevUsed, total, raw: chip.textContent
                                    });
                                }
                            }
                        }
                    } catch (e2) {
                        console.warn("[unassign] seat chip update failed after unassign:", e2);
                    }
                }

                // Unified unassign:saved event with actual user name + emp#, parallel to assign:saved
                const detail = {
                    assetKind: currentAssetKind,
                    assetNumericId: currentAssetId,
                    assetTag: (selTag?.textContent || "").trim() || null,
                    // Name-first; ID is still available but not the primary thing
                    unassignedUserId: currentUnassignUserId,
                    unassignedFromName: currentUnassignDisplayName,
                    unassignedEmployeeNumber: currentUnassignEmployeeNumber
                };

                window.dispatchEvent(new CustomEvent("unassign:saved", { detail }));

                const tEl = document.getElementById("unassignToast");
                if (tEl) new bootstrap.Toast(tEl, { delay: 3000 }).show();

                modal.hide();
            } catch (err) {
                const toastElement = document.getElementById("errorToast");
                const message =
                    (err && err.message) ? err.message : "Unassign failed.";
                if (toastElement) {
                    const errorToast = new bootstrap.Toast(toastElement, { delay: 3000 });
                    toastElement.querySelector(".toast-body").innerHTML = message;
                    errorToast.show();
                } else {
                    alert(message);
                }
            }
        });
    });
})();