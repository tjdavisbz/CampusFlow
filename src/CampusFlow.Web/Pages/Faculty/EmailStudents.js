document.addEventListener('DOMContentLoaded', () => {
    const selectAll = document.getElementById('faculty-select-all');
    if (!selectAll) return;
    const students = Array.from(document.querySelectorAll('input[name="StudentUids"]:not(:disabled)'));
    selectAll.addEventListener('change', () => {
        students.forEach((student, index) => { student.checked = selectAll.checked && index < 100; });
    });
    students.forEach(student => student.addEventListener('change', () => {
        selectAll.checked = students.slice(0, 100).every(item => item.checked);
    }));
});
