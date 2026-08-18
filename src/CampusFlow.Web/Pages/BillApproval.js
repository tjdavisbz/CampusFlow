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
    const paymentForm = document.querySelector('#billPaymentForm');
    const deferredContinue = document.querySelector('#deferredContinue');
    const paymentPurposeInput = document.querySelector('#paymentPurposeInput');
    const paymentAmountLabel = document.querySelector('#paymentAmountLabel');
    const paymentAmountValue = document.querySelector('#paymentAmountValue');
    const paymentSubmitText = document.querySelector('#paymentSubmitButton span');
    const updatePaymentAction = () => {
        const selectedChoice = document.querySelector('[name="paymentChoice"]:checked');
        const isDeferred = selectedChoice?.value === 'Deferred';
        const deferredPaymentDue = paymentForm?.dataset.deferredDueToday === 'true';
        const requiresPayment = !isDeferred || deferredPaymentDue;
        if (paymentForm) paymentForm.hidden = !requiresPayment;
        if (deferredContinue) deferredContinue.hidden = !isDeferred || deferredPaymentDue;
        if (paymentPurposeInput) paymentPurposeInput.value = isDeferred ? 'Deferred' : 'PayNow';
        const amount = isDeferred ? paymentForm?.dataset.deferredAmount : paymentForm?.dataset.payNowAmount;
        if (paymentAmountLabel) paymentAmountLabel.textContent = isDeferred ? 'Payment-plan amount due today' : 'Payment amount';
        if (paymentAmountValue && amount) paymentAmountValue.textContent = amount;
        if (paymentSubmitText && amount) paymentSubmitText.textContent = `Pay ${amount} securely`;
        if (planPreview) planPreview.hidden = !isDeferred;
    };
    document.querySelectorAll('[name="paymentChoice"]').forEach(choice => choice.addEventListener('change', () => {
        updatePaymentAction();
        updateSubmission();
    }));
    document.querySelector('[data-processing-form]')?.addEventListener('submit', event => {
        if (!event.currentTarget.checkValidity() || finalButton?.disabled) return;
        const modal = document.querySelector('#agreementProcessing');
        if (modal) modal.hidden = false;
        document.body.classList.add('is-processing-agreement');
        if (finalButton) finalButton.disabled = true;
    });
    paymentForm?.addEventListener('submit', event => {
        if (!event.currentTarget.checkValidity()) return;
        const modal = document.querySelector('#paymentProcessing');
        if (modal) modal.hidden = false;
        document.body.classList.add('is-processing-payment');
        const submit = paymentForm.querySelector('[type="submit"]');
        if (submit) submit.disabled = true;
    });
    updatePaymentAction();
    updateSubmission();
    const initialStep = document.querySelector('.approval-page')?.dataset.initialStep;
    if (initialStep && steps.some(step => step.dataset.step === initialStep)) show(initialStep);
});
