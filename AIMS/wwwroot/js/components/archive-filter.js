// AIMS Filter Icon: persist state and notify listeners.
(function () {
  function storageKey(id) { return `filter:${id}:showArchived`; }

  function init(id, options) {
    const root = document.querySelector(`[data-component="filter-icon"][data-id="${id}"]`);
    if (!root) return;
    const toggle = root.querySelector('[data-role="show-archived-toggle"]');
    if (!toggle) return;

    const saved = localStorage.getItem(storageKey(id));
    if (saved !== null) toggle.checked = (saved === "true");

    const notify = () => {
      const detail = { id, showArchived: toggle.checked };
      document.dispatchEvent(new CustomEvent('aims:filter:changed', { detail }));
      if (options && typeof options.onChange === 'function') options.onChange(detail);
    };

    toggle.addEventListener('change', () => {
      localStorage.setItem(storageKey(id), String(toggle.checked));
      notify();
    });

    notify();
  }

  window.AIMSFilterIcon = { init };
})();
