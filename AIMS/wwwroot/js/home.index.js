(() => {
    document.addEventListener("DOMContentLoaded", () => {
      // ---------- Elements ----------
      const track      = document.getElementById("card-carousel-track");
      const viewport   = document.querySelector(".carousel-viewport");
      const leftArrow  = document.querySelector(".carousel-arrow.left");
      const rightArrow = document.querySelector(".carousel-arrow.right");
  
      const viewToggleBtn = document.getElementById("view-toggle");
      const viewDropdown  = document.getElementById("view-dropdown");
      const switchButtons = document.querySelectorAll("[data-role]"); // <button data-role="Admin">...</button>
  
      const roleMessage     = document.getElementById("role-message");
      const welcomeTitle    = document.getElementById("welcome-title");
      const welcomeSubtitle = document.getElementById("welcome-subtitle");
  
      const filterToggleBtn  = document.getElementById("filter-button-toggle");
      const filterDropdown   = document.getElementById("filterDropdown");
      const toggleReportsBtn = document.getElementById("toggle-direct-reports");
      const reportsDropdown  = document.getElementById("reports-dropdown");
      const toggleTypesBtn   = document.getElementById("toggle-type-dropdown");
      const typesDropdown    = document.getElementById("types-dropdown");
      const filterMyAssets   = document.getElementById("filter-my-assets");
      const filterAllReports = document.getElementById("filter-all-reports");
      const filterAllTypes   = document.getElementById("filterByTypeAll");
  
      // NOTE: support both id names to match your current view
      const searchInput = document.getElementById("asset-search-input") || document.getElementById("search-input");
      const searchBtn   = document.getElementById("asset-search-addon") || document.getElementById("search-addon");
  
      // ---------- Data from Razor ----------
      const tableData = window.__TABLE_DATA__ ?? [];
      const users     = window.__USERS__ ?? [];
  
      // ---------- Helpers ----------
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
  
      function populateDirectReports(supervisorID) {
        if (!reportsDropdown) return;
        reportsDropdown.innerHTML = "";
        const reports = users.filter(u => u.Supervisor === supervisorID);
        for (const rep of reports) {
          const label = document.createElement("label");
          label.innerHTML = `
            <input type="checkbox" class="filter-report" data-report="${rep.ID}" value="${rep.ID}">
            ${rep.Name}
          `;
          reportsDropdown.appendChild(label);
          reportsDropdown.appendChild(document.createElement("br"));
        }
      }
  
      // ---------- Role switching ----------
      function applyRole(role) {
        const showCards     = role === "Admin" || role === "IT Help Desk";
        const showAssignBtn = role === "Admin";
        const showFilters   = role !== "Admin";
        const showWelcome   = role === "Supervisor";
  
        document.querySelector("[data-testid='summary-cards-section']").style.display = showCards ? "flex" : "none";
        document.querySelector("[data-testid='assign-asset-section']").style.display  = showAssignBtn ? "block" : "none";
        document.querySelector("[data-testid='view-full-list-button']").style.display = showCards ? "block" : "none";
        document.querySelector("[data-testid='filter-button']").style.display         = showFilters ? "block" : "none";
  
        setHidden(roleMessage, !showWelcome);
        if (showWelcome) {
          welcomeTitle.textContent = "Welcome John Smith!";
          welcomeSubtitle.textContent = "Here's an overview of your assets along with those assigned to your team.";
        }
  
        if (role === "Supervisor") {
          populateDirectReports("28809"); // simulated until Graph wired
          filterForSupervisor("28809");
        } else {
          resetTableVisibility();
        }
      }
  
      viewToggleBtn?.addEventListener("click", () => setHidden(viewDropdown, !viewDropdown.hidden));
      switchButtons.forEach(btn => {
        btn.addEventListener("click", () => {
          applyRole(btn.dataset.role);
          setHidden(viewDropdown, true);
        });
      });
      applyRole("Admin"); // initial
  
      // ---------- Filtering ----------
      function filterForSupervisor(supervisorID) {
        const reportIDs = users.filter(u => u.Supervisor === supervisorID).map(u => u.ID);
        const rows = $all("#table-body tr");
        rows.forEach(row => {
          const assignedToCell = row.querySelector("td:nth-child(4)");
          const txt = assignedToCell?.textContent ?? "";
          const isSupervisorAsset   = txt.includes(`(${supervisorID})`);
          const isDirectReportAsset = reportIDs.some(id => txt.includes(`(${id})`));
          row.style.display = (isSupervisorAsset || isDirectReportAsset) ? "" : "none";
        });
        applyRowStriping();
      }
  
      function filterTable() {
        const searchTerm = (searchInput?.value ?? "").trim().toLowerCase();
        const selectedTypes   = $all(".filter-type:checked").map(cb => cb.value.trim());
        const selectedReports = $all(".filter-report:checked").map(cb => cb.dataset.report);
        const showMy          = !!(filterMyAssets?.checked);
        const supervisorID    = "28809"; // simulated
  
        const rows = $all("#table-body tr");
        rows.forEach(row => {
          const cells = row.querySelectorAll("td");
          const assignedToText = cells[3]?.textContent ?? "";
          const typeText       = (cells[1]?.textContent ?? "").trim();
  
          const idMatch    = assignedToText.match(/\((\d+)\)/);
          const assignedId = idMatch ? idMatch[1] : "";
  
          const matchesSearch = Array.from(cells).some(c => c.textContent.toLowerCase().includes(searchTerm));
          const matchesType   = selectedTypes.length === 0 || selectedTypes.includes(typeText);
  
          const directIDs = users.filter(u => u.Supervisor === supervisorID).map(u => u.ID);
          const isSupervisorAsset   = assignedId === supervisorID;
          const isDirectReportAsset = selectedReports.includes(assignedId);
          const isInSupervisorView  = isSupervisorAsset || directIDs.includes(assignedId);
  
          const ownershipActive = showMy || selectedReports.length > 0;
  
          let visible;
          if (ownershipActive) {
            visible = matchesSearch && matchesType && ((showMy && isSupervisorAsset) || isDirectReportAsset);
          } else {
            visible = matchesSearch && matchesType && isInSupervisorView;
          }
          row.style.display = visible ? "" : "none";
        });
  
        applyRowStriping();
      }
  
      // Wire filter controls
      // replace your current filterToggleBtn click handler with this:
filterToggleBtn?.addEventListener("click", () => {
  if (!filterDropdown) return;
  const isOpen = !filterDropdown.classList.contains("show");

  // toggle Bootstrap-compatible visibility
  filterDropdown.classList.toggle("show", isOpen);

  // also keep the native 'hidden' attribute in sync
  if (isOpen) {
    filterDropdown.removeAttribute("hidden");
  } else {
    filterDropdown.setAttribute("hidden", "");
  }

  // ARIA state
  filterToggleBtn.setAttribute("aria-expanded", String(isOpen));
});
      toggleReportsBtn?.addEventListener("click", () => setHidden(reportsDropdown, !reportsDropdown.hidden));
      toggleTypesBtn?.addEventListener("click", () => setHidden(typesDropdown, !typesDropdown.hidden));
  
      filterMyAssets?.addEventListener("change", filterTable);
      filterAllReports?.addEventListener("change", (e) => {
        const checked = e.target.checked;
        $all(".filter-report").forEach(cb => cb.checked = checked);
        filterTable();
      });
      filterAllTypes?.addEventListener("change", (e) => {
        const checked = e.target.checked;
        $all(".filter-type").forEach(cb => cb.checked = checked);
        filterTable();
      });
      reportsDropdown?.addEventListener("change", (e) => {
        if (e.target?.classList.contains("filter-report")) filterTable();
      });
      $all(".filter-type").forEach(cb => cb.addEventListener("change", filterTable));
  
      searchBtn?.addEventListener("click", filterTable);
      searchInput?.addEventListener("input", filterTable);
  
      // ---------- Carousel (clone-less) ----------
      if (track && viewport && leftArrow && rightArrow) {
        const BASE_GAP = 45;
        const MAX_VISIBLE = 6;
        const TRANSITION_MS = 300;
  
        let isAnimating = false;
        let step = 0;         // card width + gap
        let visibleCount = 0; // how many fully fit
        let total = 0;        // total items
        let firstIndex = 0;   // logical first (via CSS order)
  
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
          leftArrow.style.display  = allFit ? "none" : "flex";
          rightArrow.style.display = allFit ? "none" : "flex";
        }
  
        function relayout() {
          if (isAnimating) return;
          const saved = track.style.transition;
          track.style.transition = "none";
          track.style.transform  = "translateX(0)";
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
          track.style.transform  = `translateX(${move}px)`;
  
          const onEnd = (e) => {
            if (e.target !== track || e.propertyName !== "transform") return;
            track.removeEventListener("transitionend", onEnd);
  
            firstIndex = clamp(firstIndex + (dir === "right" ? 1 : -1), total);
            applyOrder();
  
            track.style.transition = "none";
            track.style.transform  = "translateX(0)";
            track.getBoundingClientRect();
            track.style.transition = `transform ${TRANSITION_MS}ms ease`;
  
            isAnimating = false;
          };
          track.addEventListener("transitionend", onEnd);
        }
  
        // Init
        track.style.display = "flex";
        track.style.transform = "translateX(0)";
        measure();
        applyOrder();
        track.style.transition = `transform ${TRANSITION_MS}ms ease`;
  
        leftArrow.addEventListener("click",  () => scroll("left"));
        rightArrow.addEventListener("click", () => scroll("right"));
  
        const ro = new ResizeObserver(relayout);
        ro.observe(viewport);
        window.addEventListener("resize", relayout);
        window.addEventListener("load",   relayout);
      }
    });
  })();