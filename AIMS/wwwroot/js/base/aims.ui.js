/* ======================================================================
   AIMS Global UI: Custom Select (portal + a11y + Bootstrap-safe)
   ====================================================================== */
(() => {
    "use strict";

    // Track currently opened dropdown (only one at a time)
    let openInstance = null;

    // Global key trap in capture phase so Bootstrap modal never sees arrows/enter
    function globalKeyTrap(e) {
        if (!openInstance) return;
        if (openInstance.handleGlobalKey(e)) {
            e.preventDefault();
            e.stopImmediatePropagation();
            e.stopPropagation();
        }
    }
    function attachGlobalTrap() { document.addEventListener("keydown", globalKeyTrap, true); }
    function detachGlobalTrap() { document.removeEventListener("keydown", globalKeyTrap, true); }

    function buildCustomSelect(nativeSel) {
        if (!nativeSel || nativeSel.dataset.aimsEnhanced === "1") return;

        const nativeId = nativeSel.id || `aimsSel_${Math.random().toString(36).slice(2)}`;

        // Wrap: keep native in DOM (form submit, label association) but hide it
        const wrap = document.createElement("div");
        wrap.className = "aims-select-wrap";
        nativeSel.parentNode.insertBefore(wrap, nativeSel);
        wrap.appendChild(nativeSel);

        Object.assign(nativeSel.style, {
            position: "absolute", opacity: "0", pointerEvents: "none", width: "0", height: "0", margin: "0", padding: "0"
        });

        // Toggle button
        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = "aims-select-toggle";
        btn.id = nativeId + "__toggle";
        btn.setAttribute("role", "combobox");
        btn.setAttribute("aria-haspopup", "listbox");
        btn.setAttribute("aria-expanded", "false");

        // Listbox
        const menu = document.createElement("div");
        menu.className = "aims-select-menu";
        menu.id = nativeId + "__listbox";
        menu.setAttribute("role", "listbox");
        menu.tabIndex = -1;
        menu.style.display = "none";
        btn.setAttribute("aria-controls", menu.id);

        // Link labels to the toggle (not the hidden select)
        document.querySelectorAll(`label[for="${nativeSel.id}"]`).forEach(lab => {
            if (!lab.id) lab.id = nativeId + "__label";
            btn.setAttribute("aria-labelledby", `${lab.id} ${btn.id}`);
            lab.addEventListener("click", (e) => { e.preventDefault(); btn.focus(); });
        });

        // Current text
        btn.innerHTML = `
      <span class="aims-select-label">${getSelectedText(nativeSel) || "Select…"}</span>
      <span class="aims-select-caret">▾</span>
    `;

        // Items
        const items = [];
        Array.from(nativeSel.options).forEach((opt, idx) => {
            const item = document.createElement("button");
            item.type = "button";
            item.className = "aims-select-item";
            item.setAttribute("role", "option");
            item.id = `${nativeId}__opt_${idx}`;
            item.dataset.index = String(idx);
            item.dataset.value = opt.value;
            item.textContent = opt.textContent;
            if (opt.selected) item.setAttribute("aria-selected", "true");

            item.addEventListener("click", () => selectValue(opt.value, true));
            item.addEventListener("mousemove", () => setActiveIndex(idx));
            items.push(item);
            menu.appendChild(item);
        });

        wrap.appendChild(btn);
        wrap.appendChild(menu);

        nativeSel.classList.add("is-enhanced", "aims-select");
        nativeSel.dataset.aimsEnhanced = "1";
        nativeSel.tabIndex = -1;
        nativeSel.setAttribute("aria-hidden", "true");

        // State
        let open = false;
        let activeIndex = Math.max(0, Array.from(nativeSel.options).findIndex(o => o.selected));
        let portalMounted = false;

        updateActiveItem(false);

        // Helpers
        function getSelectedText(sel) {
            const opt = sel.options[sel.selectedIndex];
            return opt ? opt.textContent : "";
        }
        function focusActiveItem() {
            const el = items[activeIndex];
            if (el) el.focus({ preventScroll: true });
            btn.setAttribute("aria-activedescendant", el ? el.id : "");
        }

        function setOpen(v) {
            v = !!v;
            if (open === v) return;
            open = v;
            wrap.dataset.open = open ? "true" : "false";
            btn.setAttribute("aria-expanded", open ? "true" : "false");

            if (open) {
                if (openInstance && openInstance !== api) openInstance.setOpen(false);
                openInstance = api;

                attachGlobalTrap();
                attachFocusTrap();
                toggleModalChromeFocus(true);

                mountToPortal();
                menu.style.display = "block";
                positionMenu();
                focusActiveItem();

                window.addEventListener("scroll", positionMenu, true);
                window.addEventListener("resize", positionMenu, true);
            } else {
                if (openInstance === api) {
                    openInstance = null;
                    detachGlobalTrap();
                    detachFocusTrap();
                    toggleModalChromeFocus(false);
                }

                menu.style.display = "none";
                unmountFromPortal();

                window.removeEventListener("scroll", positionMenu, true);
                window.removeEventListener("resize", positionMenu, true);
            }
        }

        function setActiveIndex(i, andFocus = true) {
            const max = nativeSel.options.length - 1;
            activeIndex = Math.max(0, Math.min(i, max));
            updateActiveItem(andFocus);
        }

        function updateActiveItem(andFocus = true) {
            items.forEach((it, i) => it.dataset.active = i === activeIndex ? "true" : "false");
            if (open && andFocus) {
                focusActiveItem();
                const r = items[activeIndex].getBoundingClientRect();
                const m = menu.getBoundingClientRect();
                if (r.top < m.top || r.bottom > m.bottom) {
                    items[activeIndex].scrollIntoView({ block: "nearest" });
                }
            }
        }

        function selectValue(val, markChosen) {
            const opts = Array.from(nativeSel.options);
            const idx = opts.findIndex(o => o.value === val);
            if (idx < 0) return;

            nativeSel.selectedIndex = idx;
            nativeSel.dispatchEvent(new Event("change", { bubbles: true }));
            btn.querySelector(".aims-select-label").textContent = opts[idx].textContent;

            items.forEach(it => it.removeAttribute("aria-selected"));
            const chosen = items[idx];
            if (chosen) chosen.setAttribute("aria-selected", "true");

            setActiveIndex(idx);
            if (markChosen) wrap.dataset.chosen = "true";
            setOpen(false);
            btn.focus();
        }

        // Toggle via mouse
        btn.addEventListener("click", () => setOpen(!open));

        // Keyboard on button (capture & stop so modal never sees it)
        btn.addEventListener("keydown", (e) => {
            if (["ArrowDown", "ArrowUp", " ", "Enter"].includes(e.key)) {
                e.preventDefault(); e.stopImmediatePropagation(); e.stopPropagation();
            }
            if (e.key === "ArrowDown" || e.key === " " || e.key === "Enter") {
                setOpen(true);
                setActiveIndex(nativeSel.selectedIndex >= 0 ? nativeSel.selectedIndex : 0, true);
            } else if (e.key === "ArrowUp") {
                setOpen(true);
                setActiveIndex(nativeSel.selectedIndex >= 0 ? nativeSel.selectedIndex : items.length - 1, true);
            }
        }, true);

        // Keyboard inside menu (capture & stop)
        menu.addEventListener("keydown", (e) => {
            if (!open) return;
            if (["ArrowDown", "ArrowUp", "Home", "End", "Escape", " ", "Enter", "Tab"].includes(e.key)) {
                e.preventDefault(); e.stopImmediatePropagation(); e.stopPropagation();
            }
            const n = items.length;
            if (e.key === "Escape") { setOpen(false); btn.focus(); }
            else if (e.key === "ArrowDown") { setActiveIndex(activeIndex + 1, true); }
            else if (e.key === "ArrowUp") { setActiveIndex(activeIndex - 1, true); }
            else if (e.key === "Home") { setActiveIndex(0, true); }
            else if (e.key === "End") { setActiveIndex(n - 1, true); }
            else if (e.key === "Enter" || e.key === " ") {
                const val = nativeSel.options[activeIndex]?.value;
                if (val != null) selectValue(val, true);
            } else if (e.key === "Tab") {
                // keep focus within list while open
                focusActiveItem();
            }
        }, true);

        // Click-away (capture) so Bootstrap modal doesn’t treat it as outside-click
        document.addEventListener("mousedown", (e) => {
            if (!open) return;
            if (!wrap.contains(e.target) && !menu.contains(e.target)) {
                e.stopPropagation();
                setOpen(false);
            }
        }, true);

        // Reflect external changes
        nativeSel.addEventListener("change", () => {
            const val = nativeSel.value;
            const opts = Array.from(nativeSel.options);
            const idx = opts.findIndex(o => o.value === val);
            if (idx >= 0) {
                btn.querySelector(".aims-select-label").textContent = opts[idx].textContent;
                items.forEach(it => it.toggleAttribute("aria-selected", it.dataset.value === val));
                setActiveIndex(idx, false);
            }
        });

        // Portal helpers
        function mountToPortal() {
            if (portalMounted) return;
            menu.dataset.portal = "true";
            document.body.appendChild(menu);
            portalMounted = true;
        }
        function unmountFromPortal() {
            if (!portalMounted) return;
            menu.removeAttribute("data-portal");
            wrap.appendChild(menu);
            Object.assign(menu.style, { left: "", top: "", minWidth: "", maxWidth: "", maxHeight: "" });
            portalMounted = false;
        }
        function positionMenu() {
            const r = btn.getBoundingClientRect();
            const vw = Math.max(document.documentElement.clientWidth, window.innerWidth || 0);
            const vh = Math.max(document.documentElement.clientHeight, window.innerHeight || 0);

            const minW = Math.round(r.width);
            const margin = 6;

            const belowTop = Math.round(r.bottom + margin);
            const maxHBelow = Math.max(120, vh - belowTop - 12);
            const maxHAbove = Math.max(120, (r.top - 12));
            const placeAbove = maxHBelow < 160 && maxHAbove > maxHBelow;

            menu.style.minWidth = `${minW}px`;
            menu.style.maxWidth = `${Math.max(minW, Math.min(420, vw - 24))}px`;
            menu.style.maxHeight = `${placeAbove ? maxHAbove : maxHBelow}px`;

            const left = Math.round(Math.min(Math.max(12, r.left), vw - minW - 12));
            menu.style.left = `${left}px`;

            if (placeAbove) {
                const h = menu.offsetHeight || 0;
                menu.style.top = `${Math.round(r.top - 6 - h)}px`;
            } else {
                menu.style.top = `${belowTop}px`;
            }
        }

        // Prevent modal chrome from stealing focus while open
        function toggleModalChromeFocus(disable) {
            const modal = btn.closest(".modal");
            if (!modal) return;
            const killers = modal.querySelectorAll('.btn-close, [data-bs-dismiss="modal"]');
            killers.forEach(el => {
                if (disable) {
                    if (!el.dataset.aimsPrevTabindex) {
                        el.dataset.aimsPrevTabindex = el.getAttribute("tabindex") ?? "";
                    }
                    el.setAttribute("tabindex", "-1");
                } else {
                    if (el.dataset.aimsPrevTabindex !== undefined) {
                        const prev = el.dataset.aimsPrevTabindex;
                        if (prev === "") el.removeAttribute("tabindex");
                        else el.setAttribute("tabindex", prev);
                        delete el.dataset.aimsPrevTabindex;
                    }
                }
            });
        }

        // Focus-in trap to keep focus inside when open
        let focusTrapAttached = false;
        function onFocusIn(e) {
            if (!open) return;
            const t = e.target;
            if (wrap.contains(t) || menu.contains(t)) return;
            e.stopImmediatePropagation(); e.stopPropagation(); e.preventDefault();
            setTimeout(() => focusActiveItem(), 0);
        }
        function attachFocusTrap() {
            if (focusTrapAttached) return;
            document.addEventListener("focusin", onFocusIn, true);
            focusTrapAttached = true;
        }
        function detachFocusTrap() {
            if (!focusTrapAttached) return;
            document.removeEventListener("focusin", onFocusIn, true);
            focusTrapAttached = false;
        }

        // Instance API for global trap
        const api = {
            setOpen,
            handleGlobalKey(e) {
                if (!open) return false;
                const k = e.key;
                if (["ArrowDown", "ArrowUp", "Home", "End", "Escape", " ", "Enter", "Tab"].includes(k)) {
                    if (k === "Escape") { setOpen(false); btn.focus(); }
                    else if (k === "ArrowDown") { setActiveIndex(activeIndex + 1, true); }
                    else if (k === "ArrowUp") { setActiveIndex(activeIndex - 1, true); }
                    else if (k === "Home") { setActiveIndex(0, true); }
                    else if (k === "End") { setActiveIndex(items.length - 1, true); }
                    else if (k === "Enter" || k === " ") {
                        const val = nativeSel.options[activeIndex]?.value;
                        if (val != null) selectValue(val, true);
                    } else if (k === "Tab") {
                        // keep focus within the list while open
                        focusActiveItem();
                    }
                    return true;
                }
                return false;
            }
        };
    } // buildCustomSelect

    function enhanceAllIn(root) {
        root.querySelectorAll("select.aims-select").forEach(buildCustomSelect);
    }

    // Init globally + on modal open
    document.addEventListener("DOMContentLoaded", () => {
        enhanceAllIn(document);
        document.body.addEventListener("shown.bs.modal", (ev) => {
            const modal = ev.target;
            if (modal) enhanceAllIn(modal);
        });
    });

})();