# Admin configuration and access

CampusFlow Admin capabilities are protected by focused tenant permissions. Access to one
administrative area does not grant access to every Admin function. The standard ABP Users
and Roles screens remain the authoritative place to assign users to roles and manage role
membership.

The tenant data seed creates these starting roles:

| Role | Intended access |
| --- | --- |
| CampusFlow Payment Plan Manager | Payment-plan configuration |
| CampusFlow Registration Manager | Course-selection terms, rules, and advisor routing |
| CampusFlow Advisor | Advisor review queue |
| CampusFlow Student Support | Read-only student impersonation |
| CampusFlow Access Administrator | User/role assignments and CampusFlow access management |

These roles are defaults, not hard-coded identity checks. Authorized access administrators
may create additional roles and combine the granular permissions differently.

## Payment-plan configuration

The Payment Plans Admin page publishes a new `PaymentPlanPolicy` version. It configures the
enrollment fee, part-time balance divisor, residential and standard minimum payments,
residential attendance types, and seasonal installment labels. Publishing retires the
previous active policy without deleting it, preserving the policy used by historical bill
approvals.

## Bill Approval configuration

The Bill Approval Admin page owns one `BillApprovalTermConfiguration` per Elements term.
Each term selects an immutable agreement version and payment-plan version, plus its own open
and close timestamps and enabled state. Copy forward carries those choices into a future
term while leaving the new configuration disabled for review. Initial Nelson drafts begin
30 days before the term start and close at the term end.

When Bill Approval term configurations exist, the student page offers only enabled terms
whose windows are currently open. More than one term may be open, and students can switch
between them without mixing approval records. The selected term configuration supplies the
exact agreement and payment-plan versions used for that approval.

## Course Selection configuration

The Course Selection Admin page owns one `RegistrationTermConfiguration` per Elements
term. Each term has its own opening and closing timestamps, enabled state, advisor-review
and capacity behavior, and attendance-type mapping rows. Administrators choose terms from
the Elements term calendar rather than entering identifiers manually. A configuration can
be copied forward, then assigned to a future term and adjusted before saving.

More than one term may be open at once. Course Selection intersects the student's eligible
Elements terms with enabled CampusFlow configurations whose windows are currently open.
The student sees every matching term and can switch between them without mixing schedules,
course choices, advisor reviews, or registration operations. If no term configurations
exist, the earlier global policy remains as a transition fallback; after the first term is
configured, only explicitly configured open terms are offered.

Other registration gates—holds, prior-balance thresholds, and per-term exceptions—should be
added as tenant policy fields and must not be implemented as student-ID allowlists or
hard-coded dates.

## Admin form controls

User-facing Admin dropdowns should use the CampusFlow branded picker pattern: a clear
trigger, tenant colors, a contained white option menu, visible selected state, keyboard
support, and a bounded scrolling region for long lists. Avoid exposing the operating
system's large native select menu as the primary experience. Native selects remain
appropriate as progressive fallbacks or for small internal controls where a custom picker
would add unnecessary complexity.

Nelson's initial disabled drafts are generated for the terms currently returned by Elements.
Their placeholder windows begin 61 days before the term start and end on the term end date,
matching the relative timing used by the legacy portals. Attendance mappings include the
current Summer conversions. Drafts remain disabled until reviewed. Seeding preserves every
existing mapping and appends only student attendance types that are missing.
