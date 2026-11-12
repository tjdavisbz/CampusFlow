document.addEventListener('DOMContentLoaded', () => {
    const completion = document.querySelector('[data-completion-url]');
    if (completion) {
        let attempts = 0;
        let failures = 0;
        const delayed = () => {
            completion.textContent = 'Your approval is saved. Processing is taking longer than expected. Check Bill Approval History later, or contact Student Billing if it does not finish.';
        };
        const checkCompletion = async () => {
            if (++attempts > 24) { delayed(); return; }
            try {
                const response = await fetch(completion.dataset.completionUrl, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
                if (response.status === 404) {
                    completion.textContent = 'Your approval status has changed. Open Bill Approval History to review the current status.';
                    return;
                }
                if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('Status unavailable');
                const status = await response.json();
                if (typeof status.registrationComplete !== 'boolean' || typeof status.pdfAvailable !== 'boolean' || typeof status.needsReview !== 'boolean') throw new Error('Invalid status');
                failures = 0;
                if (String(status.registrationComplete) !== completion.dataset.registrationComplete ||
                    String(status.pdfAvailable) !== completion.dataset.pdfAvailable) {
                    window.location.reload();
                    return;
                }
                if (status.needsReview) {
                    completion.textContent = 'Your bill approval is saved. Student Billing needs to review the remaining registration or agreement steps.';
                    return;
                }
            } catch {
                if (++failures >= 3) { delayed(); return; }
            }
            window.setTimeout(checkCompletion, 5000);
        };
        window.setTimeout(checkCompletion, 3000);
    }
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
    const paymentFeeValue = document.querySelector('#paymentFeeValue');
    const paymentTotalValue = document.querySelector('#paymentTotalValue');
    const paymentSubmitText = document.querySelector('#paymentSubmitButton span');
    const currency = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });
    const updatePaymentAction = () => {
        const selectedChoice = document.querySelector('[name="paymentChoice"]:checked');
        const isDeferred = selectedChoice?.value === 'Deferred';
        const deferredPaymentDue = paymentForm?.dataset.deferredDueToday === 'true';
        const requiresPayment = !isDeferred || deferredPaymentDue;
        if (paymentForm) paymentForm.hidden = !requiresPayment;
        if (deferredContinue) deferredContinue.hidden = !isDeferred || deferredPaymentDue;
        if (paymentPurposeInput) paymentPurposeInput.value = isDeferred ? 'Deferred' : 'PayNow';
        const amount = Number(isDeferred ? paymentForm?.dataset.deferredAmount : paymentForm?.dataset.payNowAmount) || 0;
        const feeRate = (Number(paymentForm?.dataset.feeRate) || 0) / 100;
        const fee = Math.round((amount * feeRate + Number.EPSILON) * 100) / 100;
        const total = amount + fee;
        if (paymentAmountLabel) paymentAmountLabel.textContent = isDeferred ? 'Payment-plan amount due today' : 'Payment amount';
        if (paymentAmountValue) paymentAmountValue.textContent = currency.format(amount);
        if (paymentFeeValue) paymentFeeValue.textContent = currency.format(fee);
        if (paymentTotalValue) paymentTotalValue.textContent = currency.format(total);
        if (paymentSubmitText) paymentSubmitText.textContent = `Pay ${currency.format(total)} securely`;
        if (planPreview) planPreview.hidden = !isDeferred;
        const balanceSummary = document.querySelector('#approvalRemainingBalance');
        if (balanceSummary) balanceSummary.textContent = currency.format(Number(isDeferred ? balanceSummary.dataset.deferred : balanceSummary.dataset.base) || 0);
        const headline = document.querySelector('#approvalHeadlineBalance');
        if (headline && balanceSummary) headline.textContent = currency.format(Math.max(0, Number(isDeferred ? balanceSummary.dataset.deferred : balanceSummary.dataset.base) || 0));
        const planFeeSummary = document.querySelector('#approvalPlanFee');
        if (planFeeSummary) planFeeSummary.hidden = !isDeferred;
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
