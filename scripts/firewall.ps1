<#
.SYNOPSIS
  Opens TCP 3000 on this laptop to phones on the local network for the demo, or closes it again.

.DESCRIPTION
  The demo stack serves the dashboard and the supplier page on port 3000 (docs/RUNBOOK.md, "Demo laptop", step 4).
  Windows Firewall blocks inbound connections to it by default, so a phone on the hotspot cannot open a QR link.
  This adds one inbound rule, "TraceGreen demo (TCP 3000)", that allows TCP 3000 from the local subnet only (the
  hotspot's or the Wi-Fi's), on every network profile, because Windows often files a Mobile Hotspot network as Public.
  Running it again changes nothing; -Remove deletes the rule after the demo. Run it from an elevated PowerShell.
  The Linux equivalent is in docs/RUNBOOK.md, "Linux", "Firewall".

.PARAMETER Remove
  Delete the rule instead of adding it.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/firewall.ps1

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/firewall.ps1 -Remove
#>
[CmdletBinding()]
param(
  [switch]$Remove
)

$ErrorActionPreference = 'Stop'
$RuleName = 'TraceGreen demo (TCP 3000)'
$Port = 3000

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
  Write-Host 'Run this from an elevated PowerShell (right-click PowerShell, "Run as administrator"):'
  Write-Host "  powershell -ExecutionPolicy Bypass -File scripts/firewall.ps1$(if ($Remove) { ' -Remove' })"
  exit 1
}

$existing = @(Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue)

if ($Remove) {
  if ($existing.Count -eq 0) {
    Write-Host "No rule named '$RuleName'; nothing to remove."
  } else {
    $existing | Remove-NetFirewallRule
    Write-Host "Removed '$RuleName'. Port $Port is closed to the network again."
  }
  exit 0
}

if ($existing.Count -gt 0) {
  Write-Host "'$RuleName' is already in place; nothing changed."
} else {
  $rule = @{
    DisplayName   = $RuleName
    Description   = 'Lets phones on the local network open the TraceGreen demo on this laptop. Remove after the demo: scripts/firewall.ps1 -Remove'
    Direction     = 'Inbound'
    Action        = 'Allow'
    Protocol      = 'TCP'
    LocalPort     = $Port
    RemoteAddress = 'LocalSubnet'
    Profile       = 'Any'
  }
  New-NetFirewallRule @rule | Out-Null
  Write-Host "Added '$RuleName': inbound TCP $Port from the local subnet, all network profiles."
}
Write-Host 'Next: node scripts/demo-check.mjs, then open the link on a phone on the same network.'
Write-Host 'After the demo: powershell -ExecutionPolicy Bypass -File scripts/firewall.ps1 -Remove'
