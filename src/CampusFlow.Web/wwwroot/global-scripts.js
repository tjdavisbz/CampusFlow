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
