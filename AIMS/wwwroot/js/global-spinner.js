// ---------- Global overlay controls ----------
const GlobalSpinner = (() => {
    const overlay = document.getElementById('global-loading-overlay');
    const LOADING = { delayBeforeShowMs: 140, minVisibleMs: 500, fadeMs: 140 };

    let _showTimer = null;
    let _shownAt = 0;

    function show() {
        clearTimeout(_showTimer);
        _showTimer = setTimeout(() => {
            overlay.classList.add('is-visible');
            _shownAt = performance.now();
        }, LOADING.delayBeforeShowMs);
    }

    function hide() {
        clearTimeout(_showTimer);
        if (!overlay.classList.contains('is-visible')) {
            overlay.style.display = 'none';
            overlay.classList.remove('is-visible');
            return;
        }
        const elapsed = performance.now() - _shownAt;
        const wait = Math.max(0, LOADING.minVisibleMs - elapsed);
        setTimeout(() => {
            overlay.classList.remove('is-visible');
            setTimeout(() => { overlay.style.display = 'none'; }, LOADING.fadeMs);
        }, wait);
    }

    const mo = new MutationObserver(() => {
        if (overlay.classList.contains('is-visible')) {
            overlay.style.display = 'flex';
            overlay.style.opacity = '1';
        } else {
            overlay.style.opacity = '0';
        }
    });
    mo.observe(overlay, { attributes: true, attributeFilter: ['class'] });

    return { show, hide, LOADING };
})();