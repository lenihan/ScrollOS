# ScrollOS SDK
#
# Widget builders that apps use in Show-App, and shell commands for working with the timeline.
# Widgets are plain ordered hashtables; the ScrollOS core lays them out and draws them.
#
# App contract (functions an app module exports):
#   Start-App                      initialize fresh state                  (optional)
#   Restore-AppState -State <ht>   initialize from saved state on resume   (optional)
#   Show-App                       return the widget tree                  (required)
#   Invoke-AppInput <event>        handle an event; output 'exit' to close (optional)
#   Save-AppState                  return state to persist                 (optional)
#
# Events are hashtables: @{ type = 'click'|'select'|'submit'|'key'|'tick'; target; index; value; key }
#
# Background work: call Set-ScrollTimer to receive 'tick' events, which keep arriving while the app is
# suspended (Ctrl+Z). Send-ScrollNotification adds an entry to the timeline; clicking it opens the app.

function ConvertTo-WidgetList($Content) {
    if ($Content -is [scriptblock]) { return @(& $Content) }
    return @($Content)
}

function New-Panel {
    param(
        [Parameter(Position = 0)][string]$Title,
        [Parameter(Position = 1)]$Content,
        [string]$Fg
    )
    [ordered]@{ type = 'panel'; title = $Title; fg = $Fg; children = (ConvertTo-WidgetList $Content) }
}

function New-Column {
    param([Parameter(Position = 0)]$Content)
    [ordered]@{ type = 'column'; children = (ConvertTo-WidgetList $Content) }
}

function New-Row {
    param([Parameter(Position = 0)]$Content)
    [ordered]@{ type = 'row'; children = (ConvertTo-WidgetList $Content) }
}

function New-Text {
    param(
        [Parameter(Position = 0)][string]$Text,
        [string]$Fg,
        [string]$Bg,
        [switch]$Bold,
        [switch]$Dim
    )
    [ordered]@{ type = 'text'; text = $Text; fg = $Fg; bg = $Bg; bold = [bool]$Bold; dim = [bool]$Dim }
}

function New-Button {
    param(
        [Parameter(Position = 0, Mandatory)][string]$Text,
        [string]$Id
    )
    [ordered]@{ type = 'button'; text = $Text; id = $(if ($Id) { $Id } else { $Text }) }
}

function New-List {
    param(
        [string]$Id = 'list',
        [object[]]$Items = @(),
        [int]$Selected = -1,
        [string]$Placeholder
    )
    [ordered]@{
        type        = 'list'
        id          = $Id
        items       = [string[]]@($Items | ForEach-Object { "$_" })
        selected    = $Selected
        placeholder = $Placeholder
    }
}

function New-Input {
    param(
        [string]$Id = 'input',
        [string]$Value,
        [string]$Placeholder
    )
    [ordered]@{ type = 'input'; id = $Id; value = $Value; placeholder = $Placeholder }
}

function New-Divider {
    [ordered]@{ type = 'divider' }
}

# ---------------------------------------------------------------------------- shell commands

function Assert-ScrollShell {
    if (-not $global:ScrollOS -or -not $global:ScrollOS.IsShell) { throw 'This command only works at the ScrollOS prompt.' }
}

function Send-ScrollNotification {
    <# Adds a notification to the timeline. Sent from an app, clicking it opens that app. #>
    param([Parameter(Mandatory, Position = 0)][string]$Text)
    if (-not $global:ScrollOS) { throw 'Not running inside ScrollOS.' }
    $global:ScrollOS.Notify($Text)
}

function Set-ScrollTimer {
    <# Delivers a 'tick' event to Invoke-AppInput every N milliseconds, even in the background. 0 stops it. #>
    param([Parameter(Mandatory, Position = 0)][int]$Milliseconds)
    if (-not $global:ScrollOS -or $global:ScrollOS.IsShell) { throw 'Set-ScrollTimer only works inside an app.' }
    $global:ScrollOS.SetTimer($Milliseconds)
}

function Get-ScrollApp {
    <# Lists installed ScrollOS apps (folders under $env:SCROLLOS_APPS containing <Name>/<Name>.psm1). #>
    if (-not $env:SCROLLOS_APPS -or -not (Test-Path -LiteralPath $env:SCROLLOS_APPS)) { return }
    Get-ChildItem -LiteralPath $env:SCROLLOS_APPS -Directory | ForEach-Object {
        $module = Join-Path $_.FullName "$($_.Name).psm1"
        if (Test-Path -LiteralPath $module) {
            [pscustomobject]@{ Name = ($_.Name -replace 'PS$', ''); Module = $_.Name; Path = $module }
        }
    }
}

function Start-ScrollApp {
    <# Launches an app as a new live entry at the bottom of the timeline. #>
    param([Parameter(Mandatory, Position = 0)][string]$Name)
    Assert-ScrollShell
    $app = Get-ScrollApp | Where-Object { $_.Name -eq $Name -or $_.Module -eq $Name } | Select-Object -First 1
    if (-not $app) { throw "No ScrollOS app named '$Name'. Run Get-ScrollApp to see what's installed." }
    $global:ScrollOS.Launch($app.Name, $app.Path)
}

function Resume-App {
    <# Resumes a closed app artifact from history as a new live entry. #>
    param([Parameter(Mandatory, Position = 0)][long]$Id)
    Assert-ScrollShell
    $global:ScrollOS.Resume($Id)
}

function Get-Timeline {
    <# Returns timeline entries as objects, oldest first. #>
    param([int]$Last = 20, [string]$Kind)
    $index = Join-Path $env:SCROLLOS_HOME 'timeline/index.jsonl'
    if (-not (Test-Path -LiteralPath $index)) { return }

    # The index is append-only; the last line for each id is its current state.
    $latest = [ordered]@{}
    foreach ($line in Get-Content -LiteralPath $index) {
        if ($line) { $e = $line | ConvertFrom-Json; $latest["$($e.id)"] = $e }
    }
    $latest.Values |
        Where-Object { -not $Kind -or $_.kind -eq $Kind } |
        Select-Object -Last $Last |
        ForEach-Object {
            [pscustomobject]@{
                Id     = $_.id
                Time   = ([datetime]$_.time).ToString('MM-dd HH:mm')
                Kind   = $_.kind
                Status = $_.status
                Title  = $_.title
            }
        }
}

function Register-ScrollApps {
    <# Defines a shortcut function per app (e.g. 'notes' runs Start-ScrollApp Notes). #>
    foreach ($app in Get-ScrollApp) {
        Set-Item -Path "function:global:$($app.Name)" -Value ([scriptblock]::Create("Start-ScrollApp '$($app.Name)'"))
    }
}

Export-ModuleMember -Function New-Panel, New-Column, New-Row, New-Text, New-Button, New-List, New-Input, New-Divider,
    Get-ScrollApp, Start-ScrollApp, Resume-App, Get-Timeline, Register-ScrollApps, Send-ScrollNotification, Set-ScrollTimer
