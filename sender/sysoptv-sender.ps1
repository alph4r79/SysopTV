# SysopTV, Copyright (C) 2026 Christoph Scheel
# Licensed under the GNU General Public License v3.0, see LICENSE.
#
# Sender for SysopTV: reads the Claude usage limits with the login of
# Claude Code and sends them as JSON to the cube (POST /api/usage).
#
# Needs Windows PowerShell 5.1 and a logged in Claude Code, nothing else.
#
# Test run (one query, output in the window):
#   powershell -ExecutionPolicy Bypass -File sysoptv-sender.ps1 -Once
#
# Continuous operation, e.g. from a shortcut in the startup folder:
#   powershell -WindowStyle Hidden -ExecutionPolicy Bypass -File sysoptv-sender.ps1
#
# If an API key is set in the settings of the cube:
#   ... sysoptv-sender.ps1 -ApiKey 0123456789abcdef0123456789abcdef
#
# Log file: %LOCALAPPDATA%\SysopTV\sender.log
#
# The login data of Claude Code is only read, never written, renewed,
# printed or logged.

param(
    [string]$DeviceIp = "sysoptv.local",
    [int]$IntervalSeconds = 120,
    [string]$ApiKey = "",
    [switch]$Once
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
# The web server on the cube does not answer "Expect: 100-continue".
[Net.ServicePointManager]::Expect100Continue = $false

$WorkDir   = Join-Path $env:LOCALAPPDATA "SysopTV"
$LogFile   = Join-Path $WorkDir "sender.log"
$LogLimit  = 200KB
$CredFile  = Join-Path $env:USERPROFILE ".claude\.credentials.json"
$UsageUrl  = "https://api.anthropic.com/api/oauth/usage"
$UserAgent = "claude-cli/2.1.0 (external, cli)"
# Polling every 60 seconds led to permanent HTTP 429.
$MinInterval = 120
# After HTTP 429 wait at least this long, or longer if Retry-After says so.
$MinBlockSeconds = 300

# A failed query does not show "NO CARRIER" at once: the last valid values
# are sent on with the current time, until this many queries in a row failed
# or the values are older than this many minutes.
$MaxFailures = 3
$MaxLastGoodMinutes = 15

$script:BlockedUntil = [DateTime]::MinValue
$script:LastGood = $null
$script:FailCount = 0

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Write-Log([string]$Text) {
    $line = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss") + "  " + $Text
    try {
        if ((Test-Path $LogFile) -and ((Get-Item $LogFile).Length -gt $LogLimit)) {
            Move-Item -Force $LogFile ($LogFile + ".old")
        }
        Add-Content -Path $LogFile -Value $line -Encoding UTF8
    } catch {
    }
    if ($Once) {
        Write-Host $line
    }
}

function Get-UnixNow {
    return [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
}

# Current offset of the Windows time zone to UTC in seconds, including daylight saving time.
function Get-TzOffset {
    return [int][TimeZoneInfo]::Local.GetUtcOffset([DateTime]::UtcNow).TotalSeconds
}

# resets_at is an ISO time in UTC. Windows PowerShell may already have turned it into a DateTime.
function ConvertTo-UnixSeconds($Value) {
    if ($null -eq $Value -or [string]$Value -eq "") {
        return $null
    }
    if ($Value -is [DateTime]) {
        return ([DateTimeOffset]$Value.ToUniversalTime()).ToUnixTimeSeconds()
    }
    $styles = [Globalization.DateTimeStyles]::AssumeUniversal
    return [DateTimeOffset]::Parse([string]$Value, [Globalization.CultureInfo]::InvariantCulture, $styles).ToUnixTimeSeconds()
}

# One limit from the Anthropic answer as the cube expects it, or $null if missing.
function ConvertTo-Limit($Item) {
    if ($null -eq $Item -or $null -eq $Item.utilization) {
        return $null
    }
    $used = [int][math]::Round([double]$Item.utilization)
    if ($used -lt 0) {
        $used = 0
    }
    if ($used -gt 100) {
        $used = 100
    }
    $limit = [ordered]@{ used = $used }
    $reset = ConvertTo-UnixSeconds $Item.resets_at
    if ($null -ne $reset) {
        $limit.resetsAt = $reset
    }
    return $limit
}

function Get-RetryAfter($Response) {
    $wait = $MinBlockSeconds
    try {
        $ra = $Response.Headers["Retry-After"]
        if ($ra -and [int]$ra -gt $wait) {
            $wait = [int]$ra
        }
    } catch {
    }
    return $wait
}

# ---------------------------------------------------------------------------
# Query the limits at Anthropic
# ---------------------------------------------------------------------------

function Get-Usage {
    # Asking again during a 429 block only makes the block longer.
    if ((Get-Date) -lt $script:BlockedUntil) {
        return @{ State = "error"; Attempted = $false; Reason = "429-Pause bis " + $script:BlockedUntil.ToString("HH:mm") }
    }
    if (-not (Test-Path $CredFile)) {
        return @{ State = "login"; Reason = "keine Anmeldedatei" }
    }
    try {
        $oauth = (Get-Content $CredFile -Raw | ConvertFrom-Json).claudeAiOauth
    } catch {
        return @{ State = "login"; Reason = "Anmeldedatei nicht lesbar" }
    }
    if ($null -eq $oauth -or -not $oauth.accessToken) {
        return @{ State = "login"; Reason = "kein Zugangsschlüssel" }
    }
    if ($oauth.expiresAt) {
        $expires = [DateTimeOffset]::FromUnixTimeMilliseconds([int64]$oauth.expiresAt)
        if ($expires -le [DateTimeOffset]::UtcNow) {
            return @{ State = "login"; Reason = "Anmeldung abgelaufen" }
        }
    }

    $headers = @{
        "Authorization"  = "Bearer " + $oauth.accessToken
        "anthropic-beta" = "oauth-2025-04-20"
    }
    try {
        $u = Invoke-RestMethod -Uri $UsageUrl -Headers $headers -UserAgent $UserAgent -TimeoutSec 30
    } catch {
        $code = 0
        if ($_.Exception.Response) {
            $code = [int]$_.Exception.Response.StatusCode
        }
        if ($code -eq 401) {
            return @{ State = "login"; Reason = "Anthropic: 401" }
        }
        if ($code -eq 429) {
            $wait = Get-RetryAfter $_.Exception.Response
            $script:BlockedUntil = (Get-Date).AddSeconds($wait)
            return @{ State = "error"; Attempted = $true; Reason = "Anthropic bremst (429), Pause bis " + $script:BlockedUntil.ToString("HH:mm") }
        }
        if ($code -ne 0) {
            return @{ State = "error"; Attempted = $true; Reason = "Anthropic: HTTP " + $code }
        }
        return @{ State = "error"; Attempted = $true; Reason = "Anthropic nicht erreichbar: " + $_.Exception.Message }
    } finally {
        $headers = $null
        $oauth = $null
    }

    return @{
        State   = "ok"
        Session = ConvertTo-Limit $u.five_hour
        Week    = ConvertTo-Limit $u.seven_day
    }
}

# What goes to the cube for a query result. "ok" and "login" go out as they
# are. After an error the last valid values are sent on, as long as there were
# fewer than $MaxFailures failed queries in a row and the values are not older
# than $MaxLastGoodMinutes. Rounds in a 429 pause are no new failures.
function Resolve-Data([hashtable]$Data, [DateTime]$Now) {
    if ($Data.State -eq "ok") {
        $script:LastGood = @{ Data = $Data; Time = $Now }
        $script:FailCount = 0
        return $Data
    }
    if ($Data.State -eq "login") {
        $script:FailCount = 0
        return $Data
    }
    if ($Data.Attempted) {
        $script:FailCount++
    }
    if ($null -ne $script:LastGood -and $script:FailCount -lt $MaxFailures) {
        $age = ($Now - $script:LastGood.Time).TotalMinutes
        if ($age -le $MaxLastGoodMinutes) {
            $held = $script:LastGood.Data.Clone()
            $held.Held = $true
            $held.Reason = $Data.Reason
            return $held
        }
    }
    return $Data
}

# ---------------------------------------------------------------------------
# Talk to the cube
# ---------------------------------------------------------------------------

function New-Payload([hashtable]$Data) {
    $p = [ordered]@{
        state = $Data.State
        now   = Get-UnixNow
        tz    = Get-TzOffset
    }
    if ($Data.State -eq "ok") {
        if ($null -ne $Data.Session) {
            $p.session = $Data.Session
        }
        if ($null -ne $Data.Week) {
            $p.week = $Data.Week
        }
    }
    return ($p | ConvertTo-Json -Compress -Depth 4)
}

function Get-Summary([hashtable]$Data) {
    if ($Data.State -ne "ok") {
        return $Data.State + " (" + $Data.Reason + ")"
    }
    $parts = @("ok")
    if ($Data.Held) {
        $parts = @("ok mit letzten Werten (" + $Data.Reason + ")")
    }
    if ($null -ne $Data.Session) {
        $parts += "Session " + $Data.Session.used + " % verbraucht"
    } else {
        $parts += "keine Session-Daten"
    }
    if ($null -ne $Data.Week) {
        $parts += "Woche " + $Data.Week.used + " % verbraucht"
    } else {
        $parts += "keine Wochen-Daten"
    }
    return ($parts -join ", ")
}

# Returns $null on success, otherwise an error text.
function Send-Payload([string]$Json) {
    $url = "http://" + $DeviceIp + "/api/usage"
    $body = [Text.Encoding]::UTF8.GetBytes($Json)
    $headers = @{}
    if ($ApiKey) {
        $headers["X-SysopTV-Key"] = $ApiKey
    }
    try {
        Invoke-WebRequest -Uri $url -Method Post -Body $body -Headers $headers -ContentType "application/json" -UseBasicParsing -TimeoutSec 15 | Out-Null
        return $null
    } catch {
        $resp = $_.Exception.Response
        if ($resp) {
            $text = ""
            try {
                $reader = New-Object IO.StreamReader($resp.GetResponseStream())
                $text = $reader.ReadToEnd()
                $reader.Dispose()
            } catch {
            }
            $code = [int]$resp.StatusCode
            if ($code -eq 401) {
                return "HTTP 401, der Würfel verlangt einen API-Schlüssel (Parameter -ApiKey) oder der Schlüssel stimmt nicht"
            }
            return "HTTP " + $code + " " + $text
        }
        return $_.Exception.Message
    }
}

# ---------------------------------------------------------------------------
# Main program
# ---------------------------------------------------------------------------

function Start-Sender {
    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

    $created = $false
    $mutex = [Threading.Mutex]::new($false, "Local\SysopTV-Sender", [ref]$created)
    if (-not $created) {
        Write-Host "Der Sender läuft bereits. Ein zweiter Lauf würde Anthropic doppelt abfragen."
        return
    }

    $interval = $IntervalSeconds
    if ($interval -lt $MinInterval) {
        Write-Log ("Intervall " + $interval + " s ist zu kurz, nehme " + $MinInterval + " s.")
        $interval = $MinInterval
    }
    Write-Log ("Start, Würfel " + $DeviceIp + ", alle " + $interval + " Sekunden")

    $lastSummary = ""
    $lastSendError = ""
    try {
        while ($true) {
            try {
                $data = Resolve-Data (Get-Usage) (Get-Date)
                $summary = Get-Summary $data
                if ($summary -ne $lastSummary -or $Once) {
                    Write-Log ("Daten: " + $summary)
                    $lastSummary = $summary
                }
                $json = New-Payload $data
                if ($Once) {
                    Write-Host ("Sende: " + $json)
                }
                $sendError = Send-Payload $json
                if ($sendError) {
                    if ($sendError -ne $lastSendError -or $Once) {
                        Write-Log ("Würfel nicht erreicht: " + $sendError)
                    }
                } elseif ($lastSendError -or $Once) {
                    Write-Log "Würfel hat die Daten angenommen."
                }
                if ($sendError) {
                    $lastSendError = $sendError
                } else {
                    $lastSendError = ""
                }
            } catch {
                Write-Log ("Fehler: " + $_.Exception.Message)
            }

            if ($Once) {
                break
            }
            Start-Sleep -Seconds $interval
        }
    } finally {
        $mutex.Dispose()
    }
}

Start-Sender
