document.addEventListener('DOMContentLoaded', () => {
    const steps = [...document.querySelectorAll('[data-step]')];
    const nav = [...document.querySelectorAll('[data-step-target]')];
    const show = name => {
        steps.forEach(step => { step.hidden = step.dataset.step !== name; step.classList.toggle('active', step.dataset.step === name); });
        nav.forEach(button => button.classList.toggle('active', button.dataset.stepTarget === name));
        document.querySelector('.approval-layout')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    };
    document.querySelectorAll('[data-next]').forEach(button => button.addEventListener('click', () => show(button.dataset.next)));
    nav.forEach(button => button.addEventListener('click', () => show(button.dataset.stepTarget)));
    const accepted = document.querySelector('#agreementAccepted');
    const continueButton = document.querySelector('#agreementContinue');
    const acceptedInput = document.querySelector('#agreementAcceptedInput');
    const finalButton = document.querySelector('#finalApprovalButton');
    const paymentChoiceInput = document.querySelector('#paymentChoiceInput');
    const updateSubmission = () => {
        if (continueButton) continueButton.disabled = !accepted?.checked;
        if (acceptedInput) acceptedInput.value = accepted?.checked ? 'true' : 'false';
        const selectedChoice = document.querySelector('[name="paymentChoice"]:checked');
        if (paymentChoiceInput && selectedChoice) paymentChoiceInput.value = selectedChoice.value;
        const canSubmitPayment = finalButton?.dataset.hasBalance !== 'true' || selectedChoice?.value === 'Deferred';
        if (finalButton) finalButton.disabled = !accepted?.checked || !canSubmitPayment;
    };
    accepted?.addEventListener('change', updateSubmission);
    const planPreview = document.querySelector('#planPreview');
    document.querySelectorAll('[name="paymentChoice"]').forEach(choice => choice.addEventListener('change', () => {
        if (planPreview) planPreview.hidden = choice.value !== 'Deferred' || !choice.checked;
        updateSubmission();
    }));
    document.querySelector('[data-processing-form]')?.addEventListener('submit', event => {
        if (!event.currentTarget.checkValidity() || finalButton?.disabled) return;
        const modal = document.querySelector('#agreementProcessing');
        if (modal) modal.hidden = false;
        document.body.classList.add('is-processing-agreement');
        if (finalButton) finalButton.disabled = true;
    });
    updateSubmission();
});
