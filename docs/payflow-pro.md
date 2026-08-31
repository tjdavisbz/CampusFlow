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
```

Test mode always uses PayPal's `pilot-payflowpro` API. Live mode uses the production endpoint. Never put the password in a committed settings file.

## Sandbox test

1. Configure/link a Payflow test account in Payflow Manager. A normal PayPal Payments sandbox account alone is not sufficient.
2. Enable the integration with `TestMode=true`.
3. On the CampusFlow payment page use Visa `4111 1111 1111 1111`, a future expiration date, and the test-account CVV rules.
4. Confirm that the CampusFlow result page shows an approved transaction and a Payflow reference.

CampusFlow recalculates the student's balance on the server, rejects amounts above that balance, sends the sale directly to Payflow Pro, and records the immediate gateway result. Payment attempts retain only operational status, amount, gateway result, and Payflow reference—never card data.

Because the direct flow sends card data through CampusFlow, production hosting must enforce HTTPS and the deployment must be included in the university's PCI compliance review.

## Database

Run the database migrator after deploying this feature. The migration adds `AppPayflowPayments`, which provides a durable audit trail and safe recovery path when a browser or gateway response is interrupted.

Approved payments are posted to Elements through its Billing API using transaction document `PAYWEB` (203), AR type 8048, and the Payflow reference as the batch-entry reference. Live payments use the batch comment `Online Credit Card Payments`; test payments use `Test Online Credit Card Payments`. CampusFlow creates the appropriate batch when it is absent and checks the reference before inserting, so retrying an interrupted account update does not duplicate the payment.

Gateway approval and Elements posting are deliberately tracked separately. If Elements is temporarily unavailable, CampusFlow still reports the payment as received and retries the account update when the student next opens checkout. Never ask a student to submit the card again after Payflow has returned an approval.
