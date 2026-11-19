<#
Read-only policy export. Run in Windows PowerShell on a domain-connected
computer with the ActiveDirectory module (RSAT), using your normal account.
No passwords, password hashes, SSNs, recovery answers or user profile are exported.
This does not change policy or reset a password.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$StudentUserPrincipalName,
    [string]$Server
)
$ErrorActionPreference = 'Stop'
Import-Module ActiveDirectory -ErrorAction Stop
$connection = @{}
if ($Server) { $connection.Server = $Server }
# Escape the LDAP value rather than interpolating it into a PowerShell filter.
$escapedUpn = $StudentUserPrincipalName.Replace('\', '\5c').Replace('*', '\2a').Replace('(', '\28').Replace(')', '\29').Replace([string][char]0, '\00')
$students = @(Get-ADUser @connection -LDAPFilter "(userPrincipalName=$escapedUpn)" -ResultSetSize 2)
if ($students.Count -ne 1) { throw 'Exactly one student account must match in the selected domain.' }
$student = $students[0]
$policy = Get-ADUserResultantPasswordPolicy @connection -Identity $student
$source = 'FineGrained'
if ($null -eq $policy) {
    $domainDn = (($student.DistinguishedName -split ',') | Where-Object { $_ -like 'DC=*' }) -join ','
    if (-not $domainDn) { throw 'Unable to determine the selected account domain.' }
    $policy = Get-ADDefaultDomainPasswordPolicy @connection -Identity $domainDn
    $source = 'DomainDefault'
}
[pscustomobject][ordered]@{
    FormatVersion = 1
    CheckedAtUtc = [DateTime]::UtcNow.ToString('o')
    PolicySource = $source
    PolicyName = $policy.Name
    MinPasswordLength = [int]$policy.MinPasswordLength
    ComplexityEnabled = [bool]$policy.ComplexityEnabled
    PasswordHistoryCount = [int]$policy.PasswordHistoryCount
    MinimumPasswordAgeSeconds = [double]$policy.MinPasswordAge.TotalSeconds
    MaximumPasswordAgeSeconds = [double]$policy.MaxPasswordAge.TotalSeconds
    LockoutThreshold = [int]$policy.LockoutThreshold
    LockoutDurationSeconds = [double]$policy.LockoutDuration.TotalSeconds
    LockoutObservationWindowSeconds = [double]$policy.LockoutObservationWindow.TotalSeconds
    ScopeNote = 'Effective AD policy for the selected account. Custom password filters and banned-password lists are not included. Confirm scope before publishing to all students.'
} | ConvertTo-Json -Depth 3
