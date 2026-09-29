<#
.SYNOPSIS
  Links this project to a Hapbeat workspace checkout with directory junctions (idempotent).

.DESCRIPTION
  A standalone clone needs none of these links: packages resolve from the Git URLs in
  Packages/manifest.json and the hands fall back to the public placeholder model.
  Inside the Hapbeat workspace this script creates:

    Assets/HapbeatPrivate             -> <workspace>/hapbeat-demos/private-assets/unity/gloveball/HapbeatPrivate
    Packages/com.hapbeat.sdk          -> <workspace>/repos-sdk/hapbeat-unity-sdk
    Packages/com.hapbeat.demo-switch  -> <workspace>/hapbeat-demos/unity/packages/com.hapbeat.demo-switch

  Each link is created only when its target exists. The package links are embedded packages,
  so Unity uses the live workspace sources instead of the pinned Git URLs. All three paths are
  git-ignored. Run it with the Unity Editor closed.

.EXAMPLE
  .\tools\link-workspace.ps1
  .\tools\link-workspace.ps1 -WorkspaceRoot 'C:\GitHub\Hapbeat\hapbeat-sdk-workspace'
#>
[CmdletBinding()]
param(
    # Default: the parent of this tools/ folder.
    [string] $ProjectPath,

    # Default: the project sits at <workspace>/hapbeat-demos/unity/gloveball.
    [string] $WorkspaceRoot
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 does not populate $PSScriptRoot inside param() defaults.
if ([string]::IsNullOrWhiteSpace($ProjectPath)) { $ProjectPath = Split-Path -Parent $PSScriptRoot }
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $WorkspaceRoot = Join-Path $ProjectPath '..\..\..'
}
if (-not (Test-Path -LiteralPath $WorkspaceRoot)) { throw "Workspace root not found: $WorkspaceRoot" }
$WorkspaceRoot = (Resolve-Path -LiteralPath $WorkspaceRoot).Path

$links = @(
    @{ Path = 'Assets\HapbeatPrivate';            Target = 'hapbeat-demos\private-assets\unity\gloveball\HapbeatPrivate' },
    @{ Path = 'Packages\com.hapbeat.sdk';         Target = 'repos-sdk\hapbeat-unity-sdk' },
    @{ Path = 'Packages\com.hapbeat.demo-switch'; Target = 'hapbeat-demos\unity\packages\com.hapbeat.demo-switch' }
)

Write-Output "link-workspace"
Write-Output "  project   : $ProjectPath"
Write-Output "  workspace : $WorkspaceRoot"

$problems = 0
foreach ($link in $links) {
    $path = Join-Path $ProjectPath $link.Path
    $target = Join-Path $WorkspaceRoot $link.Target

    if (-not (Test-Path -LiteralPath $target)) {
        Write-Output "  skip    $($link.Path)  (target not found: $target)"
        continue
    }
    $target = (Resolve-Path -LiteralPath $target).Path

    $item = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    if ($null -ne $item) {
        if ($item.LinkType -eq 'Junction' -or $item.LinkType -eq 'SymbolicLink') {
            $current = @($item.Target)[0]
            if ($current.TrimEnd('\') -ieq $target.TrimEnd('\')) {
                Write-Output "  ok      $($link.Path) -> $target"
            } else {
                Write-Output "  WARN    $($link.Path) already links to $current (expected $target); left unchanged"
                $problems++
            }
        } else {
            Write-Output "  WARN    $($link.Path) exists as a regular $(if ($item.PSIsContainer) { 'directory' } else { 'file' }); move it away and re-run"
            $problems++
        }
        continue
    }

    New-Item -ItemType Junction -Path $path -Target $target | Out-Null
    Write-Output "  created $($link.Path) -> $target"
}

if ($problems -gt 0) {
    Write-Output "link-workspace: $problems link(s) need attention"
    exit 1
}
Write-Output "link-workspace: done (restart the Unity Editor if it was open)"
