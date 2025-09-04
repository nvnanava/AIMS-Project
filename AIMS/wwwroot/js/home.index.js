(() => {
  document.addEventListener("DOMContentLoaded", () => {
    // ==============================
    // Elements
    // ==============================
    const track = document.getElementById("card-carousel-track");
    const viewport = document.querySelector(".carousel-viewport");
    const leftArrow = document.querySelector(".carousel-arrow.left");
    const rightArrow = document.querySelector(".carousel-arrow.right");

    const viewToggleBtn = document.getElementById("view-toggle");
    const viewDropdown = document.getElementById("view-dropdown");
    const switchButtons = document.querySelectorAll("[data-role]");
    let currentRole = "Admin";

    const roleMessage = document.getElementById("role-message");
    const welcomeTitle = document.getElementById("welcome-title");
    const welcomeSubtitle = document.getElementById("welcome-subtitle");

    const filterToggleBtn = document.getElementById("filter-button-toggle");
    const filterDropdown = document.getElementById("filterDropdown");

    // Containers to show/hide per role
    const filterMyAssetsContainer = document.getElementById("filter-my-assets-container");
    const filterDirectReportsContainer = document.getElementById("filter-direct-reports-container");
    const filterTypeContainer = document.getElementById("filter-type-container");
    const filterStatusContainer = document.getElementById("filter-status-container");

    // My Assets
    const filterMyAssets = document.getElementById("filter-my-assets");

    // Direct Reports
    const toggleReportsBtn = document.getElementById("toggle-direct-reports");
    const reportsPanel = document.getElementById("reports-panel");
    const reportsList = document.getElementById("reports-list");
    const allReportsCB = document.getElementById("filter-all-reports");

    // Types
    const toggleTypesBtn = document.getElementById("toggle-type-dropdown");
    const typesDropdown = document.getElementById("types-dropdown");
    const typesList = document.getElementById("types-list");
    const allTypesCB = document.getElementById("filter-all-types");

    // Status
    const toggleStatusBtn = document.getElementById("toggle-status-dropdown");
    const statusDropdown = document.getElementById("status-dropdown");
    const statusList = document.getElementById("status-list");
    const allStatusCB = document.getElementById("filter-all-status");

    // Search
    const searchInput = document.getElementById("asset-search-input") || document.getElementById("search-input");
    const searchBtn = document.getElementById("asset-search-addon") || document.getElementById("search-addon");

    // Data from Razor
    const tableData = window.__TABLE_DATA__ ?? [];
    const users = window.__USERS__ ?? [];

    // ==============================
    // Helpers
    // ==============================
    const $all = (sel, root = document) => Array.from(root.querySelectorAll(sel));

    const setHidden = (el, hidden) => {
      if (!el) return;
      el.hidden = !!hidden;
      const controller = document.querySelector(`[aria-controls="${el.id}"]`);
      if (controller) controller.setAttribute("aria-expanded", String(!hidden));
    };

    function applyRowStriping() {
      const allRows = $all("#table-body tr");
      allRows.forEach(r => r.classList.remove("even-row", "odd-row"));
      const visible = allRows.filter(r => r.style.display !== "none");
      visible.forEach((r, i) => r.classList.add(i % 2 === 0 ? "even-row" : "odd-row"));
    }

    function resetTableVisibility() {
      $all("#table-body tr").forEach(r => { r.style.display = ""; });
      applyRowStriping();
    }

    // ==============================
    // Direct reports
    // ==============================
    function populateDirectReports(supervisorID) {
      if (!reportsList) return;
      reportsList.innerHTML = "";

      const reports = users.filter(u => u.Supervisor === supervisorID);
      for (const rep of reports) {
        const label = document.createElement("label");
        label.classList.add("report-child");
        label.innerHTML = `
          <input type="checkbox" class="filter-report" data-report="${rep.ID}" value="${rep.ID}">
          <span>${rep.Name}</span>
        `;
        reportsList.appendChild(label);
      }
      syncAllReportsCheckbox();
    }

    function reportChildren() {
      return Array.from(reportsList?.querySelectorAll(".filter-report") ?? []);
    }

    function typeChildren() {
      return Array.from(typesList?.querySelectorAll(".filter-type") ?? []);
    }

    function statusChildren() {
      return Array.from(statusList?.querySelectorAll(".filter-status") ?? []);
    }

    function syncAllReportsCheckbox() {
      if (!allReportsCB) return;
      const kids = reportChildren();
      const checkedKids = kids.filter(cb => cb.checked).length;
      if (checkedKids === 0) {
        allReportsCB.checked = false; allReportsCB.indeterminate = false;
      } else if (checkedKids === kids.length) {
        allReportsCB.checked = true; allReportsCB.indeterminate = false;
      } else {
        allReportsCB.checked = false; allReportsCB.indeterminate = true;
      }
    }

    function syncAllTypesCheckbox() {
      if (!allTypesCB) return;
      const kids = typeChildren();
      const checkedKids = kids.filter(cb => cb.checked).length;
      if (checkedKids === 0) {
        allTypesCB.checked = false; allTypesCB.indeterminate = false;
      } else if (checkedKids === kids.length) {
        allTypesCB.checked = true; allTypesCB.indeterminate = false;
      } else {
        allTypesCB.checked = false; allTypesCB.indeterminate = true;
      }
    }

    function syncAllStatusCheckbox() {
      if (!allStatusCB) return;
      const kids = statusChildren();
      const checkedKids = kids.filter(cb => cb.checked).length;
      if (checkedKids === 0) {
        allStatusCB.checked = false; allStatusCB.indeterminate = false;
      } else if (checkedKids === kids.length) {
        allStatusCB.checked = true; allStatusCB.indeterminate = false;
      } else {
        allStatusCB.checked = false; allStatusCB.indeterminate = true;
      }
    }

    // ==============================
    // Role switching
    // ==============================
    function applyRole(role) {
      currentRole = role;

      const showCards = role === "Admin" || role === "IT Help Desk";
      const showAssignBtn = role === "Admin";
      const showFiltersButton = true;
      const showWelcome = role === "Supervisor";

      document.querySelector("[data-testid='summary-cards-section']")?.style && (
        document.querySelector("[data-testid='summary-cards-section']").style.display = showCards ? "flex" : "none"
      );
      document.querySelector("[data-testid='assign-asset-section']")?.style && (
        document.querySelector("[data-testid='assign-asset-section']").style.display = showAssignBtn ? "block" : "none"
      );
      document.querySelector("[data-testid='view-full-list-button']")?.style && (
        document.querySelector("[data-testid='view-full-list-button']").style.display = showCards ? "block" : "none"
      );
      document.querySelector("[data-testid='filter-button']")?.style && (
        document.querySelector("[data-testid='filter-button']").style.display = showFiltersButton ? "block" : "none"
      );

      // Role-specific visibility inside the dropdown
      if (role === "Supervisor") {
        // Supervisor sees My Assets + Direct Reports + Type + Status
        filterMyAssetsContainer?.classList.remove("d-none");
        filterDirectReportsContainer?.classList.remove("d-none");
        filterTypeContainer?.classList.remove("d-none");
        filterStatusContainer?.classList.remove("d-none");

        setHidden(roleMessage, false);
        if (welcomeTitle) welcomeTitle.textContent = "Welcome John Smith!";
        if (welcomeSubtitle) welcomeSubtitle.textContent = "Here's an overview of your assets along with those assigned to your team.";

        populateDirectReports("28809"); // simulated until Graph wired
        filterForSupervisor("28809");
      } else {
        // Admin / IT Help Desk: only Type + Status
        filterMyAssetsContainer?.classList.add("d-none");
        filterDirectReportsContainer?.classList.add("d-none");
        filterTypeContainer?.classList.remove("d-none");
        filterStatusContainer?.classList.remove("d-none");

        setHidden(roleMessage, true);
        resetTableVisibility();
      }
    }

    // ==============================
    // Filtering
    // ==============================
    function filterForSupervisor(supervisorID) {
      const reportIDs = users.filter(u => u.Supervisor === supervisorID).map(u => u.ID);
      const rows = $all("#table-body tr");
      rows.forEach(row => {
        const assignedToCell = row.querySelector("td:nth-child(4)");
        const txt = assignedToCell?.textContent ?? "";
        const isSupervisorAsset = txt.includes(`(${supervisorID})`);
        const isDirectReportAsset = reportIDs.some(id => txt.includes(`(${id})`));
        row.style.display = (isSupervisorAsset || isDirectReportAsset) ? "" : "none";
      });
      applyRowStriping();
    }

    function filterTable() {
      const searchTerm = (searchInput?.value ?? "").trim().toLowerCase();
      const selectedTypes = $all(".filter-type:checked").map(cb => cb.value.trim());
      const selectedStatuses = $all(".filter-status:checked").map(cb => cb.value.trim());

      const matchesRow = (cells) => {
        const matchesSearch = Array.from(cells).some(c => (c.textContent || "").toLowerCase().includes(searchTerm));
        const typeText = (cells[1]?.textContent ?? "").trim();
        const matchesType = selectedTypes.length === 0 || selectedTypes.includes(typeText);
        const statusText = (cells[4]?.textContent ?? "").trim();
        const matchesStatus = selectedStatuses.length === 0 || selectedStatuses.includes(statusText);
        return matchesSearch && matchesType && matchesStatus;
      };

      const supervisorID = "28809";
      const selectedReports = $all(".filter-report:checked").map(cb => cb.dataset.report);
      const showMy = !!(filterMyAssets?.checked);
      const directIDs = users.filter(u => u.Supervisor === supervisorID).map(u => u.ID);

      $all("#table-body tr").forEach(row => {
        const cells = row.querySelectorAll("td");

        if (currentRole !== "Supervisor") {
          // Admin / IT Help Desk, no supervisor scoping
          row.style.display = matchesRow(cells) ? "" : "none";
          return;
        }

        // Supervisor view with ownership scoping
        const assignedToText = cells[3]?.textContent ?? "";
        const idMatch = assignedToText.match(/\((\d+)\)/);
        const assignedId = idMatch ? idMatch[1] : "";
        const isSupervisorAsset = assignedId === supervisorID;
        const isDirectReportAsset = selectedReports.includes(assignedId);
        const isInSupervisorView = isSupervisorAsset || directIDs.includes(assignedId);
        const ownershipActive = showMy || selectedReports.length > 0;

        let visible;
        if (ownershipActive) {
          visible = matchesRow(cells) && ((showMy && isSupervisorAsset) || isDirectReportAsset);
        } else {
          visible = matchesRow(cells) && isInSupervisorView;
        }
        row.style.display = visible ? "" : "none";
      });

      applyRowStriping();
    }

    // ==============================
    // Listeners (Dropdown show/hide)
    // ==============================
    filterToggleBtn?.addEventListener("click", () => {
      if (!filterDropdown) return;
      const isOpen = !filterDropdown.classList.contains("show");

      filterDropdown.classList.toggle("show", isOpen);
      if (isOpen) {
        filterDropdown.removeAttribute("hidden");
      } else {
        filterDropdown.setAttribute("hidden", "");
      }
      filterToggleBtn.setAttribute("aria-expanded", String(isOpen));
    });

    // Click outside to close the dropdown
    document.addEventListener("click", (e) => {
      if (!filterDropdown || !filterToggleBtn) return;
      const clickedInside = filterDropdown.contains(e.target) || filterToggleBtn.contains(e.target);
      if (!clickedInside && filterDropdown.classList.contains("show")) {
        filterDropdown.classList.remove("show");
        filterDropdown.setAttribute("hidden", "");
        filterToggleBtn.setAttribute("aria-expanded", "false");
      }
    });

    // Types expand/collapse
    toggleTypesBtn?.addEventListener("click", () => {
      const opening = typesDropdown?.hidden;
      setHidden(typesDropdown, !opening);
      toggleTypesBtn.setAttribute("aria-expanded", String(opening));
    });

    // Status expand/collapse
    toggleStatusBtn?.addEventListener("click", () => {
      const opening = statusDropdown?.hidden;
      setHidden(statusDropdown, !opening);
      toggleStatusBtn.setAttribute("aria-expanded", String(opening));
    });

    // Reports expand/collapse
    toggleReportsBtn?.addEventListener("click", () => {
      const opening = reportsPanel?.hidden;
      setHidden(reportsPanel, !opening);
      toggleReportsBtn.setAttribute("aria-expanded", String(opening));
    });

    // ==============================
    // Listeners (parent/children sync)
    // ==============================
    // Types: parent “All Types” controls children
    allTypesCB?.addEventListener("change", (e) => {
      const checked = e.target.checked;
      typeChildren().forEach(cb => cb.checked = checked);
      filterTable();
    });

    // Status: parent “All Statuses” controls children
    allStatusCB?.addEventListener("change", (e) => {
      const checked = e.target.checked;
      allStatusCB.indeterminate = false;
      statusChildren().forEach(cb => cb.checked = checked);
      filterTable();
    });

    // Reports: parent “All My Reports” controls children
    allReportsCB?.addEventListener("change", (e) => {
      const checked = e.target.checked;
      allReportsCB.indeterminate = false;
      reportChildren().forEach(cb => cb.checked = checked);
      filterTable();
    });

    // Children sync back to parent
    typesList?.addEventListener("change", (e) => {
      if (e.target?.classList.contains("filter-type")) {
        syncAllTypesCheckbox();
        filterTable();
      }
    });

    statusList?.addEventListener("change", (e) => {
      if (e.target?.classList.contains("filter-status")) {
        syncAllStatusCheckbox();
        filterTable();
      }
    });

    reportsList?.addEventListener("change", (e) => {
      if (e.target?.classList.contains("filter-report")) {
        syncAllReportsCheckbox();
        filterTable();
      }
    });

    // My Assets checkbox
    filterMyAssets?.addEventListener("change", filterTable);

    // Search
    searchBtn?.addEventListener("click", filterTable);
    searchInput?.addEventListener("input", filterTable);

    // ==============================
    // View switch wiring
    // ==============================
    viewToggleBtn?.addEventListener("click", () => setHidden(viewDropdown, !viewDropdown.hidden));
    switchButtons.forEach(btn => {
      btn.addEventListener("click", () => {
        applyRole(btn.dataset.role);
        setHidden(viewDropdown, true);
      });
    });

    // Set initial role view
    applyRole("Admin");

    // ==============================
    // Initial sync of “All” parents (Types/Status/Reports)
    // ==============================
    syncAllTypesCheckbox();
    syncAllStatusCheckbox();
    // If we eventully pre-populate reports on load, we can call:
    // syncAllReportsCheckbox();

    // Run an initial table filter to respect any pre-checked boxes or search text
    filterTable();

    // ==============================
    // Carousel (clone-less)
    // ==============================
    if (track && viewport && leftArrow && rightArrow) {
      const BASE_GAP = 45;
      const MAX_VISIBLE = 6;
      const TRANSITION_MS = 300;

      let isAnimating = false;
      let step = 0;
      let visibleCount = 0;
      let total = 0;
      let firstIndex = 0;

      const cards = () =>
        Array.from(track.querySelectorAll(".card")).map(c => c.closest(".card-link") ?? c);
      const clamp = (i, n) => ((i % n) + n) % n;

      function applyOrder() {
        const list = cards();
        const n = list.length;
        list.forEach((el, i) => {
          const order = clamp(i - firstIndex, n);
          el.style.order = String(order);
        });
      }

      function measure() {
        const vStyle = getComputedStyle(viewport);
        const inner = viewport.clientWidth
          - parseFloat(vStyle.paddingLeft || "0")
          - parseFloat(vStyle.paddingRight || "0");

        const list = cards();
        total = list.length;

        const first = list[0]?.querySelector(".card") || list[0];
        const cardW = first ? Math.round(first.offsetWidth) : 0;
        if (!cardW) return;

        let canFit = Math.floor((inner + BASE_GAP) / Math.max(1, cardW + BASE_GAP));
        canFit = Math.max(1, Math.min(canFit, MAX_VISIBLE, total));

        let gap = 0;
        if (canFit > 1) {
          const leftover = inner - canFit * cardW;
          gap = Math.max(BASE_GAP, leftover / (canFit - 1));
          gap = Math.round(gap * 100) / 100;
        }

        const rowWidth = canFit * cardW + (canFit - 1) * gap;
        if (rowWidth > inner + 0.5 && canFit > 1) {
          canFit -= 1;
          if (canFit > 1) {
            const leftover2 = inner - canFit * cardW;
            gap = Math.max(BASE_GAP, leftover2 / (canFit - 1));
            gap = Math.round(gap * 100) / 100;
          } else {
            gap = 0;
          }
        }

        track.style.gap = `${gap}px`;
        step = cardW + gap;
        visibleCount = canFit;

        const allFit = total <= visibleCount;
        leftArrow.style.display = allFit ? "none" : "flex";
        rightArrow.style.display = allFit ? "none" : "flex";
      }

      function relayout() {
        if (isAnimating) return;
        const saved = track.style.transition;
        track.style.transition = "none";
        track.style.transform = "translateX(0)";
        measure();
        applyOrder();
        track.getBoundingClientRect();
        track.style.transition = saved || `transform ${TRANSITION_MS}ms ease`;
      }

      function scroll(dir) {
        if (isAnimating || step <= 0 || total <= visibleCount) return;
        isAnimating = true;

        const move = dir === "right" ? -step : step;
        track.style.transition = `transform ${TRANSITION_MS}ms ease`;
        track.style.transform = `translateX(${move}px)`;

        const onEnd = (e) => {
          if (e.target !== track || e.propertyName !== "transform") return;
          track.removeEventListener("transitionend", onEnd);

          firstIndex = clamp(firstIndex + (dir === "right" ? 1 : -1), total);
          applyOrder();

          track.style.transition = "none";
          track.style.transform = "translateX(0)";
          track.getBoundingClientRect();
          track.style.transition = `transform ${TRANSITION_MS}ms ease`;

          isAnimating = false;
        };
        track.addEventListener("transitionend", onEnd);
      }

      // Init carousel
      track.style.display = "flex";
      track.style.transform = "translateX(0)";
      measure();
      applyOrder();
      track.style.transition = `transform ${TRANSITION_MS}ms ease`;

      leftArrow.addEventListener("click", () => scroll("left"));
      rightArrow.addEventListener("click", () => scroll("right"));

      // Responsiveness
      const ro = new ResizeObserver(relayout);
      ro.observe(viewport);
      window.addEventListener("resize", relayout);
      window.addEventListener("load", relayout);
    }
  });
})();