# Student password requirements

For synchronized students, password writeback enforces the student's effective on-premises AD policy, including any fine-grained policy, password history, minimum age, complexity and custom filters. The CampusFlow reset page must not describe guessed tenant defaults as the complete student policy.

`Get-StudentPasswordPolicy.ps1` reads the effective AD policy for one selected student, falling back to that account's domain default when there is no fine-grained policy. Run it on a domain-connected Windows computer with the ActiveDirectory PowerShell module (RSAT), using a permitted signed-in account:

```powershell
.\Get-StudentPasswordPolicy.ps1 -StudentUserPrincipalName 'terryjdavis@my.nelson.edu'
```

If necessary, specify `-Server` with the appropriate domain/controller. It prints only policy settings as JSON; it neither changes policy nor resets a password. It does not export the student's profile, credentials, password hashes, SSN or verification answer. Review the resulting policy scope before using the values for every student. Custom filters and the banned-password list require separate confirmation from the AD administrator; the output does not claim to enumerate them.

After the settings are confirmed, show the applicable minimum length, complexity, history/reuse and minimum-age guidance on the password-entry page. Provider enforcement remains authoritative. A policy rejection must allow a bounded explicit retry with a different password, while a genuinely unconfirmed write must stay protected against duplicate submission. Do not claim that a generic policy rejection identifies a specific failed rule.

References:

- [Microsoft writeback policy enforcement](https://learn.microsoft.com/en-us/entra/identity/authentication/passwords-faq)
- [Effective user password policy](https://learn.microsoft.com/en-us/powershell/module/activedirectory/get-aduserresultantpasswordpolicy?view=windowsserver2025-ps)
- [Domain default password policy](https://learn.microsoft.com/en-us/powershell/module/activedirectory/get-addefaultdomainpasswordpolicy?view=windowsserver2025-ps)
