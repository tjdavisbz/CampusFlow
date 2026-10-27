// Bind the student selector rendered in the Identity Users edit modal.
(function () {
    if (!window.location.pathname.toLowerCase().startsWith('/identity/users')) return;

    function element(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }

    function request(url, method, data) {
        return window.abp.ajax({
            url: url,
            type: method,
            contentType: 'application/json',
            data: data === undefined ? undefined : JSON.stringify(data)
        });
    }

    function initialize(section) {
        if (section.dataset.initialized || !window.abp?.ajax) return;
        const userId = section.dataset.staffUserId;
        if (!userId) return;
        section.dataset.initialized = 'true';
        const status = section.querySelector('.staff-student-editor-status');
        const search = section.querySelector('.staff-student-editor-search');
        const results = section.querySelector('.staff-student-editor-results');

        const baseUrl = '/api/admin/staff-student-links/' + encodeURIComponent(userId);

        const refresh = async () => {
            status.textContent = 'Loading student link…';
            try {
                const info = await request(baseUrl, 'GET');
                status.replaceChildren();
                if (!info.student) {
                    status.appendChild(element('p', 'text-muted mb-0', 'No student record is linked.'));
                    return;
                }
                const current = element('div', 'alert alert-light border d-flex justify-content-between align-items-center gap-2');
                const detail = element('div');
                detail.append(
                    element('strong', 'd-block', info.student.name),
                    element('span', 'small text-muted', 'Student ID ' + info.student.studentId + ' · ' + (info.student.email || ''))
                );
                const remove = element('button', 'btn btn-outline-danger btn-sm', 'Remove link');
                remove.type = 'button';
                remove.addEventListener('click', async () => {
                    if (!window.confirm('Remove this staff-to-student link?')) return;
                    remove.disabled = true;
                    try {
                        await request(baseUrl, 'DELETE');
                        await refresh();
                        window.abp.notify.success('Student link removed.');
                    } catch (_) {
                        remove.disabled = false;
                    }
                });
                current.append(detail, remove);
                status.appendChild(current);
            } catch (_) {
                status.textContent = 'Unable to load the student link for this user.';
            }
        };

        const findStudents = async () => {
            const query = search.value.trim();
            results.replaceChildren();
            if (query.length < 2) return;
            results.appendChild(element('p', 'text-muted small', 'Searching…'));
            try {
                const students = await request(baseUrl + '/students?q=' + encodeURIComponent(query), 'GET');
                results.replaceChildren();
                if (query !== search.value.trim()) return;
                if (!students.length) {
                    results.appendChild(element('p', 'text-muted small', 'No matching students found.'));
                    return;
                }
                for (const student of students) {
                    const row = element('div', 'border rounded p-2 mb-2 d-flex justify-content-between align-items-center gap-2');
                    const detail = element('div');
                    detail.append(
                        element('strong', 'd-block', student.name),
                        element('span', 'small text-muted', 'Student ID ' + student.studentId + ' · ' + (student.email || ''))
                    );
                    const select = element('button', 'btn btn-primary btn-sm', 'Link');
                    select.type = 'button';
                    select.addEventListener('click', async () => {
                        if (!window.confirm('Link this staff login to student ' + student.studentId + '?')) return;
                        select.disabled = true;
                        try {
                            await request(baseUrl, 'POST', { externalStudentId: student.externalStudentId });
                            search.value = '';
                            results.replaceChildren();
                            await refresh();
                            window.abp.notify.success('Student portal account linked.');
                        } catch (_) {
                            select.disabled = false;
                        }
                    });
                    row.append(detail, select);
                    results.appendChild(row);
                }
            } catch (_) {
                results.textContent = 'Student search failed. Please try again.';
            }
        };

        let searchTimer;
        search.addEventListener('input', () => {
            window.clearTimeout(searchTimer);
            searchTimer = window.setTimeout(findStudents, 300);
        });
        refresh();

    }

    function start() {
        const scan = () => document.querySelectorAll('.staff-student-editor').forEach(initialize);
        new MutationObserver(scan).observe(document.body, { childList: true, subtree: true });
        scan();
        window.addEventListener('load', scan, { once: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
        start();
    }
})();
