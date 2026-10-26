(() => {
    const token = document.querySelector('#privacy-reveal-token input[name="__RequestVerificationToken"]')?.value;
    const hideAll = () => {
        document.querySelectorAll('.privacy-entry').forEach(entry => {
            entry.querySelector('.privacy-revealed-value').textContent = '';
            entry.querySelector('.privacy-revealed').hidden = true;
            entry.querySelector('.privacy-reveal').textContent = 'Show';
        });
    };
    let hideTimer;
    document.querySelectorAll('.privacy-entry').forEach(entry => {
        const button = entry.querySelector('.privacy-reveal');
        const revealed = entry.querySelector('.privacy-revealed');
        button.addEventListener('click', async () => {
            if (!revealed.hidden) { hideAll(); return; }
            hideAll();
            button.disabled = true;
            try {
                const body = new FormData();
                body.append('__RequestVerificationToken', token);
                body.append('id', button.dataset.entryId);
                const response = await fetch(window.privacyPasswordsRevealUrl, {
                    method: 'POST', body, credentials: 'same-origin', cache: 'no-store'
                });
                if (!response.ok) throw new Error('Unable to show this entry. Please try again.');
                const result = await response.json();
                entry.querySelector('.privacy-revealed-value').textContent = result.value;
                revealed.hidden = false;
                button.textContent = 'Hide';
                clearTimeout(hideTimer);
                hideTimer = setTimeout(hideAll, 60000);
            } catch (error) {
                alert(error.message);
            } finally {
                button.disabled = false;
            }
        });
        entry.querySelector('.privacy-hide').addEventListener('click', hideAll);
    });
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) hideAll();
    });
    document.querySelectorAll('.privacy-delete-form').forEach(form => {
        form.addEventListener('submit', event => {
            if (!confirm('Remove this privacy-password entry? This cannot be undone.')) event.preventDefault();
        });
    });
})();
