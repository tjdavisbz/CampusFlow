(() => {
    'use strict';
    const audit = document.querySelector('.audit-page');
    if (!audit) return;
    let original = null;
    window.addEventListener('beforeprint', () => {
        if (original) return;
        original = Array.from(audit.querySelectorAll('details')).map(element => ({ element, open: element.open }));
        original.forEach(({ element }) => { element.open = true; });
    });
    window.addEventListener('afterprint', () => {
        original?.forEach(({ element, open }) => { element.open = open; });
        original = null;
    });
    audit.querySelector('[data-print-degree-audit]')?.addEventListener('click', () => window.print());
})();
