const campusFlowIcons = [
    { rel: 'icon', type: 'image/png', sizes: '32x32', href: '/favicon-32x32.png' },
    { rel: 'icon', type: 'image/png', sizes: '192x192', href: '/favicon-192x192.png' },
    { rel: 'apple-touch-icon', sizes: '180x180', href: '/apple-touch-icon.png' }
];

campusFlowIcons.forEach(icon => {
    const existingIcon = document.head.querySelector(`link[rel="${icon.rel}"][sizes="${icon.sizes}"]`);
    const iconLink = existingIcon ?? document.createElement('link');

    Object.entries(icon).forEach(([attribute, value]) => iconLink.setAttribute(attribute, value));

    if (!existingIcon) {
        document.head.appendChild(iconLink);
    }
});

document.addEventListener('DOMContentLoaded', () => {
    const copyright = document.querySelector('.lpx-footbar-copyright');

    if (copyright) {
        copyright.textContent = '© 2026 Nelson University';
    }

    document.querySelectorAll('.lpx-user-profile .user-full-name').forEach(userName => {
        const separatorIndex = userName.textContent.indexOf('\\');
        if (separatorIndex >= 0) {
            userName.textContent = userName.textContent.slice(separatorIndex + 1).trim();
        }
    });

    // Open navigation on arrival at the student dashboard. Subsequent pages keep
    // their content visible, and closing the menu is respected for this visit.
    if (document.querySelector('[data-auto-open-mobile-menu="true"]')) {
        const toggle = document.querySelector('[data-lpx-toggle="mobile-navbar"]');
        const menu = document.getElementById('mobile-navbar');
        if (!toggle || !menu) return;
        let interacted = false;
        const isOpen = () => !menu.classList.contains('d-none');
        const sync = () => {
            toggle.setAttribute('aria-expanded', String(isOpen()));
            toggle.setAttribute('aria-label', isOpen() ? 'Close navigation menu' : 'Open navigation menu');
        };
        toggle.removeAttribute('aria-hidden');
        toggle.setAttribute('role', 'button');
        toggle.setAttribute('tabindex', '0');
        toggle.setAttribute('aria-controls', menu.id);
        sync();
        new MutationObserver(sync).observe(menu, { attributes: true, attributeFilter: ['class'] });
        toggle.addEventListener('click', () => { interacted = true; });
        toggle.addEventListener('keydown', event => {
            if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                toggle.click();
            }
        });
        document.addEventListener('keydown', event => {
            if (event.key === 'Escape' && isOpen() && toggle.getClientRects().length) {
                toggle.click();
                toggle.focus();
            }
        });
        const openOnArrival = () => {
            // LeptonX binds its own toggle during DOMContentLoaded.
            if (!interacted && window.matchMedia('(max-width: 767px)').matches && !isOpen()) toggle.click();
        };
        if (document.readyState === 'complete') openOnArrival();
        else window.addEventListener('load', openOnArrival, { once: true });
    }
});
