/* ============================================================================
   Home: Card pagination + strict grid mode
   - Always 2 rows; columns switch 3 → 2 → 1 from the grid width (not viewport)
   - Hysteresis reduces mode flapping near thresholds
   - Only current-page cards are visible
   ============================================================================ */

(() => {
    const grid = document.getElementById('cards-grid');
    if (!grid) return;

    const allCards = Array.from(grid.querySelectorAll('.card-link'));
    const prev = document.getElementById('cards-prev');
    const next = document.getElementById('cards-next');
    const dotsWrap = document.getElementById('cards-dots');

    // ---- Tunables ------------------------------------------------------------
    const ROWS = 2;           // fixed two rows: 3x2 / 2x2 / 1x2
    const MIN_CARD_W_3 = 360; // px: minimum comfortable card width for 3 columns
    const MIN_CARD_W_2 = 360; // px: minimum comfortable card width for 2 columns
    const HYSTERESIS = 16;   // px: buffer to avoid jitter around thresholds

    // ---- State ---------------------------------------------------------------
    let currentPage = 1;
    let cols = 3;                   // default layout: 3 columns
    let cardsPerPage = cols * ROWS; // 6 / 4 / 2

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

        // Try 3 cols first
        const w3 = simulateCardWidth(containerPx, 3, gap);
        if (cols === 3) {
            if (w3 >= (MIN_CARD_W_3 - HYSTERESIS)) return 3;
        } else {
            if (w3 >= (MIN_CARD_W_3 + HYSTERESIS)) return 3;
        }

        // Then 2 cols
        const w2 = simulateCardWidth(containerPx, 2, gap);
        if (cols === 2) {
            if (w2 >= (MIN_CARD_W_2 - HYSTERESIS)) return 2;
        } else {
            if (w2 >= (MIN_CARD_W_2 + HYSTERESIS)) return 2;
        }

        // Otherwise 1 col
        return 1;
    }

    function applyModeClass(c) {
        grid.classList.remove('mode-3x2', 'mode-2x2', 'mode-2x1');
        if (c === 3) grid.classList.add('mode-3x2');
        else if (c === 2) grid.classList.add('mode-2x2');
        else grid.classList.add('mode-2x1');
    }

    function renderDots(totalPages) {
        dotsWrap.innerHTML = '';
        for (let i = 1; i <= totalPages; i++) {
            const dot = document.createElement('button');
            dot.type = 'button';
            dot.className = 'pager-dot' + (i === currentPage ? ' active' : '');
            dot.setAttribute('aria-label', 'Go to page ' + i);
            dot.addEventListener('click', () => { currentPage = i; render(); });
            dotsWrap.appendChild(dot);
        }
    }

    function render() {
        const containerPx = grid.clientWidth;
        const newCols = pickColumns(containerPx);

        if (newCols !== cols) {
            cols = newCols;
            cardsPerPage = cols * ROWS;
            currentPage = 1; // reset when mode changes
            applyModeClass(cols);
        }

        const total = allCards.length;
        const totalPages = Math.max(1, Math.ceil(total / cardsPerPage));
        currentPage = Math.min(currentPage, totalPages);

        const start = (currentPage - 1) * cardsPerPage;
        const end = start + cardsPerPage;

        allCards.forEach((card, idx) => {
            card.style.display = (idx >= start && idx < end) ? '' : 'none';
        });

        prev.disabled = (currentPage === 1);
        next.disabled = (currentPage === totalPages);

        const pager = document.querySelector('.cards-pager');
        if (pager) pager.style.display = (totalPages > 1 ? 'flex' : 'none');

        renderDots(totalPages);
    }

    // Pager controls
    prev.addEventListener('click', () => {
        if (currentPage > 1) { currentPage--; render(); }
    });
    next.addEventListener('click', () => {
        const totalPages = Math.ceil(allCards.length / cardsPerPage);
        if (currentPage < totalPages) { currentPage++; render(); }
    });

    // Container-based resizing (preferred)
    const ro = new ResizeObserver(() => render());
    ro.observe(grid);

    // Safety: also respond to viewport resize
    let t;
    window.addEventListener('resize', () => {
        clearTimeout(t);
        t = setTimeout(render, 100);
    });

    // Initial activation
    applyModeClass(cols);
    render();
})();