document.addEventListener('DOMContentLoaded', () => {
    const form = document.getElementById('announcementForm');
    const editor = document.getElementById('announcementEditor');
    const field = document.getElementById('Input_ContentHtml');
    if (!form || !editor || !field) return;
    document.querySelectorAll('.announcement-toolbar [data-command]').forEach(button => {
        button.addEventListener('click', () => {
            editor.focus();
            document.execCommand(button.dataset.command, false, button.dataset.value || null);
        });
    });
    document.getElementById('announcementLink').addEventListener('click', () => {
        const selection = window.getSelection();
        const selectedRange = selection && selection.rangeCount && editor.contains(selection.anchorNode)
            ? selection.getRangeAt(0).cloneRange() : null;
        const url = window.prompt('Paste an HTTPS link');
        if (!url) return;
        try {
            if (new URL(url).protocol !== 'https:') throw new Error('Only HTTPS links are supported');
            editor.focus();
            if (selectedRange) {
                selection.removeAllRanges();
                selection.addRange(selectedRange);
            }
            document.execCommand('createLink', false, url);
        } catch { window.alert('Enter a valid HTTPS link.'); }
    });
    editor.addEventListener('paste', event => {
        event.preventDefault();
        document.execCommand('insertText', false, event.clipboardData.getData('text/plain'));
    });
    form.addEventListener('submit', () => { field.value = editor.innerHTML; });
});
