// The Identity Users edit modal is supplied by ABP. Enhance that modal without
// replacing its account, role, and audit forms.
(function () {
    if (typeof abp === 'undefined' || typeof $ === 'undefined') return;
    if (!abp.auth.isGranted('CampusFlow.Admin.AccessManagement')) return;
    if (!window.location.pathname.toLowerCase().startsWith('/identity/users')) return;

    function element(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }

    function request(url, method, data) {
        return abp.ajax({
            url: url,
            type: method,
            contentType: 'application/json',
            data: data === undefined ? undefined : JSON.stringify(data)
        });
    }

    $(document).on('shown.bs.modal', '.modal', function () {
        const modal = this;
        const userId = modal.querySelector('input[name="UserInfo.Id"]')?.value;
        if (!userId || modal.querySelector('.staff-student-editor')) return;

        const firstTab = modal.querySelector('.tab-pane');
        if (!firstTab) return;

        const section = element('section', 'staff-student-editor border-top mt-4 pt-3');
        section.appendChild(element('h5', 'mb-2', 'Student portal account'));
        section.appendChild(element('p', 'text-muted small mb-3',
            'Link this staff login to the person’s own student record. This grants normal student portal access, not a read-only preview.'));
        const status = element('div', 'mb-3');
        status.setAttribute('role', 'status');
        const searchLabel = element('label', 'form-label', 'Find student by name, ID, or email');
        const search = element('input', 'form-control', undefined);
        search.type = 'search';
        search.placeholder = 'Search Elements students';
        search.minLength = 2;
        searchLabel.appendChild(search);
        const results = element('div', 'mt-2');
        section.append(status, searchLabel, results);
        firstTab.appendChild(section);

        const baseUrl = '/api/admin/staff-student-links/' + encodeURIComponent(userId);

        async function refresh() {
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
                        abp.notify.success('Student link removed.');
                    } catch (_) {
                        remove.disabled = false;
                    }
                });
                current.append(detail, remove);
                status.appendChild(current);
            } catch (_) {
                status.textContent = 'Unable to load the student link for this user.';
            }
        }

        async function findStudents() {
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
                            abp.notify.success('Student portal account linked.');
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
        }

        let searchTimer;
        search.addEventListener('input', () => {
            window.clearTimeout(searchTimer);
            searchTimer = window.setTimeout(findStudents, 300);
        });
        refresh();
    });
})();
