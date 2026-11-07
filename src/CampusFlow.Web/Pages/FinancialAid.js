document.addEventListener("DOMContentLoaded", () => {
    const dialog = document.getElementById("aid-decision-dialog");
    const form = document.getElementById("aid-decision-form");
    if (!dialog || !form) return;
    const field = id => document.getElementById(id);
    const singleAction = form.action;
    const submit = field("aid-dialog-submit");
    document.querySelectorAll("[data-aid-decision], [data-aid-accept-all]").forEach(button => {
        button.addEventListener("click", () => {
            const bulk = button.hasAttribute("data-aid-accept-all");
            const decline = !bulk && button.dataset.aidDecision === "Decline";
            form.action = bulk ? form.dataset.acceptAllAction : singleAction;
            field("aid-dialog-title").textContent = bulk ? "Accept all available awards?" : decline ? "Decline this award?" : "Accept this award?";
            field("aid-dialog-message").textContent = bulk
                ? "Only the available, undecided awards shown for this semester will be accepted. Previously recorded decisions stay unchanged."
                : decline ? "This award will not be available toward your student account." : "This award will be marked as accepted in Elements.";
            field("aid-dialog-award").textContent = bulk ? `${button.dataset.awardCount} awards — ${button.dataset.termName}` : button.dataset.awardName;
            field("aid-dialog-amount").textContent = button.dataset.awardAmount;
            field("aid-dialog-award-id").value = bulk ? "" : button.dataset.awardId;
            field("aid-dialog-decision").value = decline ? "Decline" : "Accept";
            field("aid-dialog-term").value = bulk ? button.dataset.externalTermId : "";
            field("aid-dialog-open-term").value = button.dataset.termCode;
            field("aid-dialog-award-ids").value = bulk ? button.dataset.awardIds : "";
            field("aid-dialog-confirmed").value = bulk ? "true" : "false";
            submit.textContent = bulk ? "Yes, accept these awards" : decline ? "Yes, decline award" : "Yes, accept award";
            submit.disabled = false;
            submit.classList.toggle("decline", decline);
            dialog.classList.toggle("decline", decline);
            dialog.showModal();
        });
    });
    field("aid-dialog-cancel").addEventListener("click", () => dialog.close());
    dialog.addEventListener("click", event => { if (event.target === dialog) dialog.close(); });
    form.addEventListener("submit", () => { submit.disabled = true; submit.textContent = "Submitting…"; });
    const page = document.querySelector(".aid-page");
    if (page?.dataset.restoreRequested === "true") {
        const term = [...document.querySelectorAll(".aid-term")].find(x => x.dataset.termCode === page.dataset.restoreTerm);
        if (term) { term.open = true; term.scrollIntoView({block: "start"}); }
    }
});
