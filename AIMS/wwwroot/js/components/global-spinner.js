// ---------- Global overlay controls  ----------
(function () {
    // public shape up-front
    const LOADING = { delayBeforeShowMs: 140, minVisibleMs: 500, fadeMs: 140 };

    // Early-call buffering
    let _ready = false;
    let _wantShown = false;
    let _impl = { show() { }, hide() { } }; // temporary until DOM is ready

    // Public object (stable reference)
    window.GlobalSpinner = window.GlobalSpinner || {};
    window.GlobalSpinner.LOADING = Object.assign(LOADING, window.GlobalSpinner.LOADING || {});
    window.GlobalSpinner.show = () => {
        _wantShown = true;
        _impl.show();
    };
    window.GlobalSpinner.hide = () => {
        _wantShown = false;
        _impl.hide();
    };

    function wireImpl() {
        const overlay = document.getElementById('global-loading-overlay');
        if (!overlay) return; // try again on load if needed

        const cfg = window.GlobalSpinner.LOADING;
        let t = null, shownAt = 0;

        function reallyShow() {
            clearTimeout(t);
            t = setTimeout(() => {
                overlay.style.display = 'flex';
                overlay.classList.add('is-visible');
                overlay.setAttribute('aria-hidden', 'false');
                shownAt = performance.now();
            }, cfg.delayBeforeShowMs || 0);
        }
        function reallyHide() {
            clearTimeout(t);
            const elapsed = performance.now() - shownAt;
            const wait = Math.max(0, (cfg.minVisibleMs || 0) - elapsed);
            setTimeout(() => {
                overlay.classList.remove('is-visible');
                overlay.setAttribute('aria-hidden', 'true');
                setTimeout(() => { overlay.style.display = 'none'; }, cfg.fadeMs || 0);
            }, wait);
        }

        // smooth opacity as class flips
        new MutationObserver(() => {
            overlay.style.opacity = overlay.classList.contains('is-visible') ? '1' : '0';
        }).observe(overlay, { attributes: true, attributeFilter: ['class'] });

        // swap in the real implementation
        _impl = { show: reallyShow, hide: reallyHide };
        _ready = true;

        // honor any early call intent
        if (_wantShown) _impl.show();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', wireImpl);
    } else {
        // DOM already ready (e.g., hot reload)
        wireImpl();
    }
})();