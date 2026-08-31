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
});
