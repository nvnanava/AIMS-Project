// ~/js/pages/home.index.js
/* ======================================================================
   AIMS Page Script: Home/Index
   ----------------------------------------------------------------------
   Purpose
   - Card pagination + strict grid mode for the Home cards viewport.
   - 2 fixed rows; columns flow 3 → 2 → 1 based on GRID width (not viewport).
   - Pre-hydration: do not touch pager/dots/cards.
   - First hydrated pass: hide-all-except-slice, draw dots once.
   - Subsequent passes: diff only; rebuild dots only if count changed.

   Conventions
   - Runs after DOM is ready; no inline JS in the view.
   - Pairs with CSS in ~/css/pages/home.index.css.
   - Uses .mode-3x2 / .mode-2x2 / .mode-2x1 classes set on #cards-grid.
   - Public hook window.__homeReRender() can be called by settings panel.

   Notes
   - Keep constants (ROWS/MIN widths/HYSTERESIS/COL_LOCK_MS) together.
   - Avoid layout thrash: ResizeObserver is throttled via rAF + guards.
   - Accessibility: pager buttons get aria labels; dots set aria-current.
   ====================================================================== */

(() => {
    const grid = document.getElementById('cards-grid');
    if (!grid) return;

    const prev = document.getElementById('cards-prev');
    const next = document.getElementById('cards-next');
    const dotsWrap = document.getElementById('cards-dots');
    const scopedAttribute = Array.from(grid.attributes).find(a => a.name.startsWith('b-'))?.name;

    // ---- Tunables ------------------------------------------------------------
    const ROWS = 2;
    const MIN_CARD_W_3 = 360;
    const MIN_CARD_W_2 = 360;
    const HYSTERESIS = 16;
    const COL_LOCK_MS = 400;

    // ---- State ---------------------------------------------------------------
    let currentPage = 1;
    let cols = 3;
    let cardsPerPage = cols * ROWS;
    let lastTotalPages = 0;
    let colLockUntil = 0;

    // RO guards
    let lastInlineSize = 0;
    let roPending = false;

    // Slice + visibility memo
    let lastSlice = { start: -1, end: -1, cols: -1, total: -1 };
    const currentlyShown = new Set(); // Set<HTMLElement>
    let pagerDrawnOnce = false;

    const allCards = () => Array.from(grid.querySelectorAll('.card-link'));
    const getEligibleCards = () => allCards().filter(c => !c.classList.contains('d-none'));

    function getGapPx() {
        const cs = getComputedStyle(grid);
        return parseFloat(cs.columnGap || cs.gap || '24') || 24;
    }
    function simulateCardWidth(containerPx, columns, gap) {
        if (columns <= 1) return containerPx;
        const totalGaps = gap * (columns - 1);
        return (containerPx - totalGaps) / columns;
    }
    function pickColumns(containerPx) {
        const gap = getGapPx();
        const w3 = simulateCardWidth(containerPx, 3, gap);
        if (cols === 3 ? w3 >= (MIN_CARD_W_3 - HYSTERESIS) : w3 >= (MIN_CARD_W_3 + HYSTERESIS)) return 3;
        const w2 = simulateCardWidth(containerPx, 2, gap);
        if (cols === 2 ? w2 >= (MIN_CARD_W_2 - HYSTERESIS) : w2 >= (MIN_CARD_W_2 + HYSTERESIS)) return 2;
        return 1;
    }
    function applyModeClass(c) {
        grid.classList.remove('mode-3x2', 'mode-2x2', 'mode-2x1');
        if (c === 3) grid.classList.add('mode-3x2');
        else if (c === 2) grid.classList.add('mode-2x2');
        else grid.classList.add('mode-2x1');
    }

    function renderDots(totalPages) {
        if (!dotsWrap) return;

        // Only rebuild when count changes
        if (dotsWrap.children.length === totalPages && lastTotalPages === totalPages) {
            for (let i = 0; i < dotsWrap.children.length; i++) {
                const el = dotsWrap.children[i];
                const active = (i + 1) === currentPage;
                el.classList.toggle('active', active);
                if (active) el.setAttribute('aria-current', 'page');
                else el.removeAttribute('aria-current');
            }
            return;
        }

        lastTotalPages = totalPages;
        dotsWrap.innerHTML = '';
        for (let i = 1; i <= totalPages; i++) {
            const dot = document.createElement('button');
            dot.type = 'button';
            dot.className = 'pager-dot' + (i === currentPage ? ' active' : '');
            dot.setAttribute('aria-label', 'Go to page ' + i);
            if (i === currentPage) dot.setAttribute('aria-current', 'page');
            if (scopedAttribute) dot.setAttribute(scopedAttribute, '');
            dot.addEventListener('click', () => { currentPage = i; render(false); });
            dotsWrap.appendChild(dot);
        }
    }

    function updatePager(totalPages) {
        const pager = document.querySelector('.cards-pager');
        if (!pager) return;

        const shouldShow = (totalPages > 1);
        const nowShown = pager.getAttribute('data-shown') === '1';
        if (shouldShow !== nowShown) {
            pager.style.visibility = shouldShow ? 'visible' : 'hidden';
            pager.setAttribute('data-shown', shouldShow ? '1' : '0');
        }

        if (prev) prev.disabled = (currentPage === 1);
        if (next) next.disabled = (currentPage === totalPages);
    }

    function render(forceResetPage = false, widthOverride) {
        const containerPx = (typeof widthOverride === 'number' && widthOverride > 0) ? widthOverride : grid.clientWidth;

        const now = performance.now();
        let newCols = cols;
        if (now >= colLockUntil) newCols = pickColumns(containerPx);

        const modeChanged = newCols !== cols;
        if (modeChanged) {
            cols = newCols;
            cardsPerPage = cols * ROWS;
            forceResetPage = true;
            applyModeClass(cols);
        }

        const eligible = getEligibleCards();
        const total = eligible.length;
        const totalPages = Math.max(1, Math.ceil(total / cardsPerPage));

        if (forceResetPage) currentPage = 1;
        currentPage = Math.min(currentPage, totalPages);

        const start = (currentPage - 1) * cardsPerPage;
        const end = Math.min(start + cardsPerPage, total);

        const isHydrated = grid.classList.contains('hydrated');
        if (!isHydrated) {
            // PRE-HYDRATION: do NOT touch pager/dots/cards
            return;
        }

        // First hydrated pass → one-time hide-all-except-slice
        const firstHydrated = (currentlyShown.size === 0);
        if (firstHydrated) {
            const nextShown = new Set();
            for (let i = start; i < end; i++) nextShown.add(eligible[i]);

            for (const node of eligible) {
                const shouldShow = nextShown.has(node);
                if (shouldShow) {
                    node.classList.remove('is-hidden');
                    node.style.display = '';
                    currentlyShown.add(node);
                } else {
                    node.classList.add('is-hidden');
                    node.style.display = 'none';
                }
            }

            renderDots(totalPages);
            updatePager(totalPages);
            pagerDrawnOnce = true;

            lastSlice = { start, end, cols, total };
            return;
        }

        // No-op if identical slice and mode stable
        const sameSlice = (!modeChanged &&
            lastSlice.start === start &&
            lastSlice.end === end &&
            lastSlice.cols === cols &&
            lastSlice.total === total);
        if (sameSlice) {
            updatePager(totalPages);
            return;
        }

        // Subsequent passes: diff show/hide only
        const nextShown = new Set();
        for (let i = start; i < end; i++) nextShown.add(eligible[i]);

        for (const node of currentlyShown) {
            if (!nextShown.has(node)) {
                node.classList.add('is-hidden');
                node.style.display = 'none';
            }
        }
        for (const node of nextShown) {
            if (!currentlyShown.has(node)) {
                node.classList.remove('is-hidden');
                node.style.display = '';
            }
        }
        currentlyShown.clear();
        for (const n of nextShown) currentlyShown.add(n);

        // Dots/pager (rebuild only if count changed)
        if (!pagerDrawnOnce || totalPages !== lastTotalPages) {
            renderDots(totalPages);
            pagerDrawnOnce = true;
        } else {
            // still update active state
            renderDots(totalPages);
        }
        updatePager(totalPages);

        lastSlice = { start, end, cols, total };
    }

    // Public hook the settings panel can call
    window.__homeReRender = () => render(true);

    // Visibility events
    window.addEventListener('storage', (e) => {
        if (e.key === 'dashboard:visibleTypes') render(true);
    });
    window.addEventListener('dashboard:visibility-changed', () => {
        render(true);
    });

    // Pager controls
    prev?.addEventListener('click', () => {
        if (currentPage > 1) { currentPage--; render(false); }
    });
    next?.addEventListener('click', () => {
        const totalPages = Math.max(1, Math.ceil(getEligibleCards().length / cardsPerPage));
        if (currentPage < totalPages) { currentPage++; render(false); }
    });

    // ResizeObserver (guarded)
    const ro = new ResizeObserver((entries) => {
        if (grid.classList.contains('hydrating') || !grid.classList.contains('hydrated')) return;

        const entry = entries[0];
        const inlineSize = (entry.contentBoxSize && entry.contentBoxSize[0] && entry.contentBoxSize[0].inlineSize)
            ? entry.contentBoxSize[0].inlineSize
            : entry.contentRect.width;

        if (Math.abs(inlineSize - lastInlineSize) < 1) return;
        lastInlineSize = inlineSize;

        if (performance.now() < colLockUntil) return;
        if (roPending) return;

        roPending = true;
        requestAnimationFrame(() => {
            roPending = false;
            render(false, inlineSize);
        });
    });
    ro.observe(grid);

    // Initial activation
    const initial = () => {
        applyModeClass(cols);

        // Mark hydrated but keep hidden for one frame while we slice the page
        grid.classList.add('hydrated', 'hydrating');

        // Do the first slice while hidden; DO NOT draw dots/pager earlier
        render(true);

        // Reveal next frame; start lock window
        requestAnimationFrame(() => {
            grid.classList.remove('hydrating');
            colLockUntil = performance.now() + COL_LOCK_MS;
        });
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => {
            requestAnimationFrame(initial);
        });
    } else {
        requestAnimationFrame(initial);
    }
})();