# PayPal Payflow Pro

CampusFlow uses a direct Payflow Pro sale so students enter card details without leaving the portal. Card numbers and security codes exist only for the duration of the encrypted request to PayPal; CampusFlow never stores or logs them.

## Configuration

The checked-in configuration keeps the gateway disabled and in test mode. Supply credentials with user secrets locally or environment variables in Azure:

```text
Payments__Payflow__Enabled=true
Payments__Payflow__TestMode=true
Payments__Payflow__Partner=PaperlessTrans
Payments__Payflow__Vendor=<vendor login>
Payments__Payflow__User=<optional separate user; otherwise vendor is used>
Payments__Payflow__Password=<secret>
Payments__Payflow__Currency=USD
Payments__Payflow__ConvenienceFeeRate=0.0285
```

Test mode always uses PayPal's `pilot-payflowpro` API. Live mode uses the production endpoint. Never put the password in a committed settings file.

## Sandbox test

1. Configure/link a Payflow test account in Payflow Manager. A normal PayPal Payments sandbox account alone is not sufficient.
2. Enable the integration with `TestMode=true`.
3. On the CampusFlow payment page use Visa `4111 1111 1111 1111`, a future expiration date, and the test-account CVV rules.
4. Confirm that the CampusFlow result page shows an approved transaction and a Payflow reference.

CampusFlow recalculates the student's balance on the server, rejects payment amounts above that balance, adds the configured convenience fee, sends the combined total as one sale to Payflow Pro, and records the immediate gateway result. The checkout displays the payment, fee, and total card charge separately. Payment attempts retain only operational status, payment amount, convenience-fee amount, gateway result, and Payflow reference—never card data.

Because the direct flow sends card data through CampusFlow, production hosting must enforce HTTPS and the deployment must be included in the university's PCI compliance review.

## Database

Run the database migrator after deploying this feature. The migration adds `AppPayflowPayments`, which provides a durable audit trail and safe recovery path when a browser or gateway response is interrupted.

Approved card charges are posted to Elements through its Billing API as two batch entries: the payment uses `PAYWEB` (203), AR type 8048, and the convenience fee uses `CC FEE` (1755), AR type 0. The Payflow reference identifies the payment entry and the same reference with a `-FEE` suffix identifies the fee entry. New live batches use `Online Credit Card Payments - yyyy-MM-dd`; test batches use `Test Online Credit Card Payments - yyyy-MM-dd`. The date is the original payment creation date converted from UTC to `America/Chicago` (overridable with `Payments__Payflow__ElementsPosting__TimeZoneId`). Elements receives that calendar date at midnight without a UTC suffix. A legacy undated-comment batch is eligible only when its batch name starts with the same date. CampusFlow checks the payment reference across matching open payment batches before choosing a daily batch, so a retry finds entries previously placed in the wrong day’s batch. Ambiguous daily batches or duplicate references stop posting for review. A new daily batch is created on the first payment for that day, not by a midnight scheduled job.

Gateway approval and Elements posting are deliberately tracked separately. If Elements is temporarily unavailable, CampusFlow still reports the payment as received and retries the account update when the student next opens checkout. Never ask a student to submit the card again after Payflow has returned an approval.

## Online Payment Report correction (2026-10-27)

The deployed payment document remains `PAYWEB` (203); fees remain `CC FEE` (1755) and are excluded from the payment report. The live report now filters `TransDate >= CAST(@StartDate AS date)` and `TransDate < DATEADD(day, 1, CAST(@EndDate AS date))`, including the entire end date. The previous inclusive midnight end bound excluded newly timestamped payments. No existing billing entries were updated. The 22 entries in batch 2147371689 were 11 payments and 11 fees; their UTC dates and batch membership require a separately reviewed correction before historical Central-day totals are accurate. Report backups and publication evidence are in `SAGU SSRS Project/Migration/Online Payment Fix - 2026-10-27/`.
