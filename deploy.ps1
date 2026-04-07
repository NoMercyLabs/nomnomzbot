#Requires -Version 5.1
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) NoMercy Entertainment. All rights reserved.
#
# NomNomzBot — Interactive deployment script for Windows
# Usage: .\deploy.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ─── Colors ───────────────────────────────────────────────────────────────────
function Write-Ok    { param($msg) Write-Host "[✓] $msg" -ForegroundColor Green }
function Write-Info  { param($msg) Write-Host "    $msg" -ForegroundColor Cyan }
function Write-Warn  { param($msg) Write-Host "[⚠] $msg" -ForegroundColor Yellow }
function Write-Err   { param($msg) Write-Host "[✗] $msg" -ForegroundColor Red }
function Write-Dim   { param($msg) Write-Host "    $msg" -ForegroundColor DarkGray }
function Write-Step  { param([int]$n, $msg) Write-Host "  $n.  $msg" -ForegroundColor White }
function Write-Note  { param($msg) Write-Host "       $msg" -ForegroundColor DarkGray }
function Write-Blank { Write-Host "" }

function Write-Rule {
    param([string]$char = "-")
    $w = [Math]::Min(72, $Host.UI.RawUI.WindowSize.Width)
    Write-Host ($char * $w) -ForegroundColor DarkGray
}

function Write-Header {
    param($title)
    Write-Blank
    Write-Rule "-"
    Write-Host "  $title" -ForegroundColor Cyan -NoNewline; Write-Host ""
    Write-Rule "-"
    Write-Blank
}

function Write-Box {
    param([string[]]$lines)
    $max = ($lines | ForEach-Object { $_.Length } | Measure-Object -Maximum).Maximum
    $bar = "-" * ($max + 2)
    Write-Host "+$bar+" -ForegroundColor Cyan
    foreach ($line in $lines) {
        $pad = " " * ($max - $line.Length)
        Write-Host "| " -ForegroundColor Cyan -NoNewline
        Write-Host "$line$pad" -NoNewline
        Write-Host " |" -ForegroundColor Cyan
    }
    Write-Host "+$bar+" -ForegroundColor Cyan
}

function Press-Enter {
    param($msg = "  Press Enter to continue...")
    Write-Blank
    Read-Host $msg | Out-Null
}

function Ask-WithDefault {
    param([string]$prompt, [string]$default)
    $display = if ($default) { " [$default]" } else { "" }
    $answer = Read-Host "  $prompt$display"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $default }
    return $answer.Trim()
}

function Confirm-Question {
    param([string]$msg, [bool]$defaultYes = $true)
    $hint = if ($defaultYes) { "[Y/n]" } else { "[y/N]" }
    $answer = Read-Host "  $msg $hint"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $defaultYes }
    return $answer -match '^[Yy]'
}

function Open-Browser {
    param([string]$url)
    try { Start-Process $url; return $true }
    catch { return $false }
}

function Copy-ToClipboard {
    param([string]$text)
    try {
        Set-Clipboard -Value $text
        return $true
    } catch {
        # Set-Clipboard unavailable on some Server editions — fall back to clip.exe
        try {
            $text | clip.exe
            return $true
        } catch {
            return $false
        }
    }
}

function New-RandomBytes {
    param([int]$count)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $buf = New-Object byte[] $count
    $rng.GetBytes($buf)
    return $buf
}

function To-Hex {
    param([byte[]]$bytes)
    return ($bytes | ForEach-Object { $_.ToString('x2') }) -join ''
}

# Read and prompt a credential with validation and re-prompt
function Get-Credential-Input {
    param(
        [string]$label,
        [string]$what,
        [string]$example,
        [string]$regexPattern
    )

    $value = ""
    while ($true) {
        Write-Blank
        Write-Host "  $label" -ForegroundColor Cyan
        Write-Blank
        Write-Host "  $what" -ForegroundColor White
        if ($example) {
            Write-Host "  Example: $example" -ForegroundColor DarkGray
        }
        Write-Blank

        $value = (Read-Host "  Paste it here").Trim()

        if ([string]::IsNullOrWhiteSpace($value)) {
            Write-Err "This is required. Please paste the value."
            continue
        }

        if ($regexPattern -and $value -notmatch $regexPattern) {
            Write-Blank
            Write-Warn "That doesn't look right."
            Write-Host "  It should look like: $example" -ForegroundColor DarkGray
            Write-Blank
            $retry = Confirm-Question "Try entering it again?" $true
            if ($retry) { continue }
        }

        # Show preview and confirm
        Write-Blank
        $preview = if ($value.Length -gt 20) { "$($value.Substring(0,8))...$($value.Substring($value.Length-4))" } else { $value }
        Write-Host "  You entered:  " -NoNewline
        Write-Host $preview -ForegroundColor Cyan -NoNewline
        Write-Host "  ($($value.Length) chars)" -ForegroundColor DarkGray
        Write-Blank

        $ok = Confirm-Question "Is that correct?" $true
        if ($ok) { break }
    }

    return $value
}

function Set-EnvVar {
    param([string]$key, [string]$value, [string]$file = ".env")
    $content = Get-Content $file -Raw -ErrorAction SilentlyContinue
    if ($null -eq $content) { $content = "" }
    if ($content -match "(?m)^${key}=") {
        $content = $content -replace "(?m)^${key}=.*", "${key}=${value}"
    } else {
        $content = $content.TrimEnd() + "`n${key}=${value}`n"
    }
    [System.IO.File]::WriteAllText((Resolve-Path $file).Path, $content)
}

# ══════════════════════════════════════════════════════════════════════════════
# WELCOME
# ══════════════════════════════════════════════════════════════════════════════

Clear-Host 2>$null
Write-Blank
Write-Host "  #     #                #     #                         ######" -ForegroundColor Magenta
Write-Host "  ##    #  ####  #    #  ##    #  ####  #    # ###### ######  #" -ForegroundColor Magenta
Write-Host "  # #   # #    # ##  ##  # #   # #    # ##  ## #    #     #  #" -ForegroundColor Magenta
Write-Host "  #  #  # #    # # ## #  #  #  # #    # # ## # #    #    #  #" -ForegroundColor Magenta
Write-Host "  #   # # #    # #    #  #   # # #    # #    # #    #   #  #" -ForegroundColor Magenta
Write-Host "  #    ## #    # #    #  #    ## #    # #    # #    #  #  #" -ForegroundColor Magenta
Write-Host "  #     #  ####  #    #  #     #  ####  #    # ###### ######" -ForegroundColor Magenta
Write-Blank
Write-Rule "="
Write-Host "  Welcome to NomNomzBot Setup!" -ForegroundColor White
Write-Blank
Write-Host "  This will get your bot running in about 5-10 minutes." -ForegroundColor White
Write-Host "  You'll need a Twitch account. That's it." -ForegroundColor DarkGray
Write-Rule "="
Write-Blank
Write-Host "  This script will:" -ForegroundColor DarkGray
Write-Host "  -> Check Docker Desktop is installed and running"
Write-Host "  -> Walk you through creating a Twitch application"
Write-Host "  -> Generate all security keys automatically"
Write-Host "  -> Start NomNomzBot with docker compose"
Write-Blank

Press-Enter "  Press Enter to begin..."

# ══════════════════════════════════════════════════════════════════════════════
# STEP 1 — Docker
# ══════════════════════════════════════════════════════════════════════════════

Write-Header "Step 1 of 6 -- Docker"

Write-Host "  Docker Desktop is the only thing you need installed on Windows."
Write-Host "  It runs the bot, database, and everything else in containers." -ForegroundColor DarkGray
Write-Blank

# Check docker
$dockerFound = $false
try {
    $dockerVer = docker --version 2>&1
    if ($LASTEXITCODE -eq 0) { $dockerFound = $true }
} catch { }

if (-not $dockerFound) {
    Write-Warn "Docker Desktop is not installed."
    Write-Blank
    Write-Host "  Download and install Docker Desktop, then re-run this script:"
    Write-Host "  https://www.docker.com/products/docker-desktop/" -ForegroundColor Cyan
    Write-Blank
    Write-Host "  Tips after installing:" -ForegroundColor DarkGray
    Write-Host "    - Make sure Docker Desktop is running (whale icon in the taskbar)" -ForegroundColor DarkGray
    Write-Host "    - Enable 'Use WSL 2 based engine' in Docker Desktop settings" -ForegroundColor DarkGray
    Write-Blank
    $open = Confirm-Question "Open the Docker Desktop download page now?" $true
    if ($open) { Open-Browser "https://www.docker.com/products/docker-desktop/" | Out-Null }
    Write-Blank
    Write-Err "Docker is required. Install it and re-run .\deploy.ps1"
    exit 1
} else {
    Write-Ok "Docker found: $dockerVer"
}

# Check docker is running
$dockerRunning = $false
try {
    docker info --format "ok" 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { $dockerRunning = $true }
} catch { }

if (-not $dockerRunning) {
    Write-Warn "Docker is installed but not running."
    Write-Blank
    Write-Host "  Start Docker Desktop from the Start menu." -ForegroundColor White
    Write-Host "  Wait until the whale icon in your taskbar stops animating," -ForegroundColor DarkGray
    Write-Host "  then press Enter to continue." -ForegroundColor DarkGray
    Press-Enter "  Press Enter once Docker Desktop is running..."
}

# Check docker compose v2
$composeFound = $false
try {
    $composeVer = docker compose version 2>&1
    if ($LASTEXITCODE -eq 0) { $composeFound = $true }
} catch { }

if (-not $composeFound) {
    Write-Err "Docker Compose v2 is not available."
    Write-Host "  Update Docker Desktop to the latest version and try again." -ForegroundColor White
    exit 1
} else {
    Write-Ok "Docker Compose found: $composeVer"
}

Write-Blank
Write-Ok "Docker is ready."

# ══════════════════════════════════════════════════════════════════════════════
# STEP 2 — API Base URL
# ══════════════════════════════════════════════════════════════════════════════

Write-Header "Step 2 of 6 -- Your Server URL"

Write-Box @(
    "  What is the public URL for your bot's API?",
    "",
    "  This is the URL your server is reachable at from the internet.",
    "  Twitch needs this to send login callbacks back to your bot.",
    "",
    "  Examples:",
    "    https://bot-dev-api.nomercy.tv   <- shared dev tunnel (works now)",
    "    https://api.yourdomain.com       <- your own domain",
    "",
    "  Note: Twitch OAuth requires HTTPS for real logins.",
    "  http://localhost works for local testing only."
)

Write-Blank
$API_BASE_URL = Ask-WithDefault "Your server URL" "https://bot-dev-api.nomercy.tv"
$API_BASE_URL = $API_BASE_URL.TrimEnd("/")

Write-Blank
if ($API_BASE_URL -like "http://localhost*") {
    Write-Warn "You entered a localhost URL."
    Write-Host "  Twitch OAuth requires HTTPS for real logins. localhost works for" -ForegroundColor Yellow
    Write-Host "  local testing but not for connecting a real Twitch account." -ForegroundColor Yellow
    Write-Host "  That's fine for now -- continue." -ForegroundColor DarkGray
    Write-Blank
    $cont = Confirm-Question "Continue with localhost?" $true
    if (-not $cont) { Write-Err "Aborted."; exit 1 }
}

Write-Ok "API URL set to: $API_BASE_URL"

# Compute redirect URIs
$URI_LOGIN   = "$API_BASE_URL/api/v1/auth/twitch/callback"
$URI_BOT     = "$API_BASE_URL/api/v1/auth/twitch/bot/callback"
$URI_CHANNEL = "$API_BASE_URL/api/v1/channels/callback/bot"

# ══════════════════════════════════════════════════════════════════════════════
# STEP 3 — Show redirect URIs
# ══════════════════════════════════════════════════════════════════════════════

Write-Header "Step 3 of 6 -- Twitch Redirect URIs"

Write-Host "  You'll need to add these 3 URLs to your Twitch application in the next step."
Write-Host "  Twitch uses them to send users back to your bot after they log in." -ForegroundColor DarkGray
Write-Blank

Write-Box @(
    "  Your Twitch Redirect URIs:",
    "",
    "  1. $URI_LOGIN",
    "  2. $URI_BOT",
    "  3. $URI_CHANNEL"
)

Write-Blank
$clipText = "$URI_LOGIN`n$URI_BOT`n$URI_CHANNEL"
$copied = Copy-ToClipboard $clipText
if ($copied) {
    Write-Ok "All 3 URLs copied to your clipboard!"
    Write-Dim "You can paste them into the Twitch form in the next step."
} else {
    Write-Warn "Couldn't copy automatically. Copy the URLs above manually."
}

Press-Enter "  Keep these handy -- press Enter to open the Twitch setup..."

# ══════════════════════════════════════════════════════════════════════════════
# STEP 4 — Twitch Application
# ══════════════════════════════════════════════════════════════════════════════

Write-Header "Step 4 of 6 -- Create Your Twitch Application"

Write-Box @(
    "  What is a Twitch Application?",
    "",
    "  NomNomzBot needs to be registered with Twitch so that Twitch",
    "  knows your bot is legitimate and has permission to connect.",
    "",
    "  Think of it like getting an ID badge for your bot at Twitch HQ.",
    "",
    "  You'll get two codes:",
    "    Client ID      -- like a username for your bot",
    "    Client Secret  -- like a password for your bot",
    "",
    "  Takes about 3-5 minutes. It's free."
)

Write-Blank
$skipTwitch = -not (Confirm-Question "Set up Twitch now? (recommended)" $true)

$TWITCH_CLIENT_ID     = ""
$TWITCH_CLIENT_SECRET = ""

if (-not $skipTwitch) {

    # Part 1
    Write-Header "Part 1 of 3 -- Sign In to Twitch Developer Console"

    Write-Step 1 "We're opening the Twitch Developer Console in your browser:"
    Write-Blank
    Write-Host "     https://dev.twitch.tv/console/apps" -ForegroundColor Cyan
    Write-Blank

    Open-Browser "https://dev.twitch.tv/console/apps" | Out-Null
    Write-Ok "Browser opened."
    Write-Blank

    Write-Step 2 "Sign in with your streamer Twitch account if prompted."
    Write-Note "This is your personal streaming account -- NOT the bot account."
    Write-Blank
    Write-Step 3 "You should see a page titled `"Applications`" with a"
    Write-Host '       "Register Your Application" button.' -ForegroundColor White

    Press-Enter '  Press Enter once you can see the Applications page...'

    # Part 2
    Write-Header "Part 2 of 3 -- Create the Application"

    Write-Step 1 'Click "Register Your Application".'
    Write-Blank
    Write-Step 2 "Fill in the form:"
    Write-Blank

    Write-Box @(
        "  Name:      Anything you like -- e.g. `"My NomNomzBot`"",
        "             (Just a label for your own reference)",
        "",
        "  Category:  Select `"Chat Bot`" from the dropdown"
    )

    Write-Blank
    Write-Step 3 "OAuth Redirect URLs -- add 3 URLs, one at a time."
    Write-Blank

    $uriLabels = @("Main login callback", "Bot account callback", "Per-channel bot callback")
    $allUris   = @($URI_LOGIN, $URI_BOT, $URI_CHANNEL)

    for ($i = 0; $i -lt 3; $i++) {
        $uri   = $allUris[$i]
        $label = $uriLabels[$i]
        $num   = $i + 1

        Write-Rule "."
        Write-Blank
        Write-Host "  Redirect URL $num of 3:  " -ForegroundColor Cyan -NoNewline
        Write-Host $label -ForegroundColor DarkGray
        Write-Blank

        $c = Copy-ToClipboard $uri
        if ($c) {
            Write-Ok "Copied to clipboard!"
        } else {
            Write-Warn "Couldn't copy automatically. Copy it manually:"
        }

        Write-Blank
        Write-Box @("  $uri  ")
        Write-Blank

        if ($num -lt 3) {
            Write-Step 1 "Paste the URL into the `"OAuth Redirect URLs`" field."
            Write-Step 2 'Click "Add" to save it before moving to the next URL.'
            Press-Enter "  Press Enter when URL $num is added..."
        } else {
            Write-Step 1 'Paste the URL into the field and click "Add".'
        }
    }

    Write-Rule "."
    Write-Blank
    Write-Step 4 'Check the "I''m not a robot" box if it appears.'
    Write-Blank
    Write-Step 5 'Click the "Create" button at the bottom.'

    Press-Enter '  Press Enter once you have clicked "Create"...'

    # Part 3
    Write-Header "Part 3 of 3 -- Copy Your Credentials"

    Write-Step 1 'Find your new app in the list and click "Manage".'
    Write-Blank
    Write-Step 2 "You'll land on your app's settings page."

    Press-Enter "  Press Enter once you're on your app's detail page..."

    # Client ID
    Write-Header "Getting Your Client ID"

    Write-Host '  Look for "Client ID" on the app settings page.'
    Write-Host "  You'll see a long string of letters and numbers."
    Write-Blank
    Write-Host "  It looks like:" -ForegroundColor DarkGray
    Write-Host "     abc123def456ghi789jkl012mnop34" -ForegroundColor DarkGray
    Write-Host "  (about 30 lowercase letters and numbers)" -ForegroundColor DarkGray

    $TWITCH_CLIENT_ID = Get-Credential-Input `
        -label  "Twitch Client ID" `
        -what   "This identifies your bot application to Twitch." `
        -example "abc123def456ghi789jkl012mnop34" `
        -regexPattern "^[a-zA-Z0-9]{15,}$"

    # Client Secret
    Write-Header "Getting Your Client Secret"

    Write-Host "  The Client Secret is not visible by default -- you need to generate it."
    Write-Blank
    Write-Step 1 'On the same page, scroll down to "Client Secret".'
    Write-Step 2 'Click "New Secret".'
    Write-Step 3 'A confirmation may appear -- click "OK".'
    Write-Step 4 "Copy the secret immediately -- Twitch only shows it once."
    Write-Note "(If you forget, click `"New Secret`" again -- it creates a new one.)"
    Write-Blank
    Write-Host "  It looks similar to the Client ID:" -ForegroundColor DarkGray
    Write-Host "     abc123def456ghi789jkl012mnop34" -ForegroundColor DarkGray

    $TWITCH_CLIENT_SECRET = Get-Credential-Input `
        -label  "Twitch Client Secret" `
        -what   "Like a password for your app. Keep it private!" `
        -example "abc123def456ghi789jkl012mnop34" `
        -regexPattern "^[a-zA-Z0-9]{15,}$"

    Write-Ok "Twitch credentials saved!"

} else {
    Write-Dim "Skipped. Set TWITCH_CLIENT_ID and TWITCH_CLIENT_SECRET in .env later."
}

# Bot account
Write-Blank
Write-Header "Bot Account (Optional)"

Write-Box @(
    "  What is the Bot Account?",
    "",
    "  NomNomzBot can use a separate Twitch account as the `"bot`".",
    "  This is the account that appears in chat -- e.g. NomNomzBot: Hello!",
    "",
    "  Why separate? So viewers can tell apart your messages from bot messages.",
    "",
    "  You can skip this and set it up later in the app Settings."
)

Write-Blank
$TWITCH_BOT_USERNAME = "NomNomzBot"
$hasBotAccount = Confirm-Question "Do you have a separate bot Twitch account?" $false
if ($hasBotAccount) {
    Write-Blank
    Write-Host "  Enter the exact Twitch username of the bot account." -ForegroundColor DarkGray
    Write-Host "  Example: NomNomzBot  or  MyChannelBot  or  StreamerBot_" -ForegroundColor DarkGray

    while ($true) {
        Write-Blank
        $TWITCH_BOT_USERNAME = (Read-Host "  Bot account username").Trim()

        if ([string]::IsNullOrWhiteSpace($TWITCH_BOT_USERNAME)) {
            Write-Dim "No username entered -- skipping bot account."
            $TWITCH_BOT_USERNAME = "NomNomzBot"
            break
        }

        if ($TWITCH_BOT_USERNAME -match '\s') {
            Write-Warn "Twitch usernames can't contain spaces. Try again."
            continue
        }

        Write-Blank
        Write-Host "  You entered:  " -NoNewline
        Write-Host $TWITCH_BOT_USERNAME -ForegroundColor Cyan
        $ok = Confirm-Question "Is that correct?" $true
        if ($ok) { break }
    }

    Write-Ok "Bot username: $TWITCH_BOT_USERNAME"
} else {
    Write-Dim "Bot account skipped. Using NomNomzBot as default."
}

# ══════════════════════════════════════════════════════════════════════════════
# STEP 5 — Optional Integrations
# ══════════════════════════════════════════════════════════════════════════════

Write-Header "Step 5 of 6 -- Optional Integrations"

$SPOTIFY_CLIENT_ID     = ""
$SPOTIFY_CLIENT_SECRET = ""
$DISCORD_CLIENT_ID     = ""
$DISCORD_CLIENT_SECRET = ""
$CLOUDFLARE_TUNNEL_TOKEN = ""

# Spotify
Write-Box @(
    "  Spotify  (optional)",
    "",
    "  Lets viewers see what song is playing and request songs with !sr.",
    "  You need a free or premium Spotify account."
)
Write-Blank

$wantSpotify = Confirm-Question "Set up Spotify integration?" $false
if ($wantSpotify) {
    Write-Blank
    Write-Step 1 "Open the Spotify Developer Dashboard:"
    Write-Blank
    Write-Host "     https://developer.spotify.com/dashboard" -ForegroundColor Cyan
    Write-Blank
    $openSpotify = Confirm-Question "  Open it now?" $true
    if ($openSpotify) { Open-Browser "https://developer.spotify.com/dashboard" | Out-Null }
    Write-Blank
    Write-Step 2 'Sign in and click "Create App".'
    Write-Step 3 "Fill in the form:"
    Write-Blank
    Write-Box @(
        "  App name:         Anything, e.g. `"NomNomzBot`"",
        "  Redirect URI:     $API_BASE_URL/api/v1/auth/spotify/callback",
        "  APIs used:        Check `"Web API`""
    )
    Write-Blank
    Write-Step 4 'Check Terms of Service and click "Save".'
    Write-Step 5 'Click "Settings" -> copy your Client ID.'
    Press-Enter "  Press Enter once you've created your Spotify app..."

    $SPOTIFY_CLIENT_ID = Get-Credential-Input `
        -label "Spotify Client ID" `
        -what  "Identifies your Spotify app to NomNomzBot." `
        -example "4c01e10681b24fc8b18a2f9a1f7bdbfb" `
        -regexPattern "^[a-zA-Z0-9]{20,}$"

    Write-Blank
    Write-Host '  Click "View client secret" to reveal your secret.' -ForegroundColor White

    $SPOTIFY_CLIENT_SECRET = Get-Credential-Input `
        -label "Spotify Client Secret" `
        -what  "Like a password -- keep it private." `
        -example "6ee29fb8093046aeaecebaa6f4ba3d3b" `
        -regexPattern "^[a-zA-Z0-9]{20,}$"

    Write-Ok "Spotify credentials saved!"
} else {
    Write-Dim "Skipped. Enable Spotify later in the app Settings."
}

Write-Blank

# Discord
Write-Box @(
    "  Discord  (optional)",
    "",
    "  Posts stream-start announcements to your Discord server.",
    "  You need a Discord account and a server."
)
Write-Blank

$wantDiscord = Confirm-Question "Set up Discord integration?" $false
if ($wantDiscord) {
    Write-Blank
    Write-Step 1 "Open the Discord Developer Portal:"
    Write-Blank
    Write-Host "     https://discord.com/developers/applications" -ForegroundColor Cyan
    Write-Blank
    $openDiscord = Confirm-Question "  Open it now?" $true
    if ($openDiscord) { Open-Browser "https://discord.com/developers/applications" | Out-Null }
    Write-Blank
    Write-Step 2 'Click "New Application", give it a name, click "Create".'
    Write-Step 3 'In the left sidebar, click "OAuth2".'
    Write-Step 4 'Under "Redirects", add this URL:'
    Write-Blank
    Write-Box @("  $API_BASE_URL/api/v1/auth/discord/callback")
    Write-Blank
    Write-Step 5 'Click "Save Changes".'
    Press-Enter "  Press Enter once you've saved your Discord app..."

    Write-Blank
    Write-Host "  Your Client ID is shown near the top of the OAuth2 page." -ForegroundColor White
    Write-Host "  It looks like a long number: 952230846465728553" -ForegroundColor DarkGray

    $DISCORD_CLIENT_ID = Get-Credential-Input `
        -label "Discord Client ID" `
        -what  "A number that identifies your Discord application." `
        -example "952230846465728553" `
        -regexPattern "^\d{15,}$"

    Write-Blank
    Write-Step 1 'Click "Reset Secret" then copy the secret that appears.'
    Write-Warn "The Client Secret is on the OAuth2 page -- NOT the Bot page."

    $DISCORD_CLIENT_SECRET = Get-Credential-Input `
        -label "Discord Client Secret" `
        -what  "OAuth2 secret -- NOT the bot token. Keep it private." `
        -example "YsV4pbm379BG2HZPUG2qln1By0J_DLTy" `
        -regexPattern ".{20,}"

    Write-Ok "Discord credentials saved!"
} else {
    Write-Dim "Skipped. Enable Discord later in the app Settings."
}

Write-Blank

# Cloudflare
Write-Box @(
    "  Cloudflare Tunnel Token  (optional)",
    "",
    "  Needed if your server doesn't have a domain pointed at it yet.",
    "  Gives you a secure HTTPS URL without configuring nginx/Caddy.",
    "",
    "  Skip if you already have a domain or are using the shared dev tunnel."
)
Write-Blank

$wantCf = Confirm-Question "Do you have a Cloudflare Tunnel token?" $false
if ($wantCf) {
    Write-Blank
    Write-Step 1 "Go to: https://one.dash.cloudflare.com/ -> Networks -> Tunnels"
    Write-Step 2 'Click "Create a tunnel" -> choose "Cloudflared"'
    Write-Step 3 'Give it a name and copy the token from "Install and run a connector"'
    Write-Note "It looks like: eyJhIjoiYWJj...(very long base64 string)"

    $CLOUDFLARE_TUNNEL_TOKEN = Get-Credential-Input `
        -label "Cloudflare Tunnel Token" `
        -what  "Lets your bot be reachable via a secure HTTPS tunnel." `
        -example "eyJhIjoiYWJj...(long string)" `
        -regexPattern ".{50,}"

    Write-Ok "Cloudflare Tunnel token saved!"
} else {
    Write-Dim "Skipped. Add CLOUDFLARE_TUNNEL_TOKEN to .env later if needed."
}

# ══════════════════════════════════════════════════════════════════════════════
# STEP 6 — Generate keys + write .env
# ══════════════════════════════════════════════════════════════════════════════

Write-Header "Step 6 of 6 -- Generating Security Keys and Writing Configuration"

Write-Host "  Generating cryptographically random security keys..."
Write-Blank

$JWT_SECRET       = [Convert]::ToBase64String((New-RandomBytes 64))
Write-Ok "JWT Secret generated           (64 bytes, base64)"

$ENCRYPTION_KEY   = [Convert]::ToBase64String((New-RandomBytes 32))
Write-Ok "Encryption Key generated       (32 bytes, AES-256)"

$POSTGRES_PASSWORD = To-Hex (New-RandomBytes 32)
Write-Ok "PostgreSQL password generated"

$REDIS_PASSWORD   = To-Hex (New-RandomBytes 32)
Write-Ok "Redis password generated"

Write-Blank

# Handle existing .env
$SKIP_ENV = $false
if (Test-Path .env) {
    Write-Blank
    Write-Warn ".env already exists from a previous setup."
    Write-Blank
    Write-Host "  m = Merge    (keep existing secrets, update credentials only)" -ForegroundColor White
    Write-Host "  o = Overwrite (generate fresh secrets, replace everything)" -ForegroundColor White
    Write-Host "  s = Skip     (leave .env exactly as-is)" -ForegroundColor White
    Write-Blank
    $envChoice = (Read-Host "  Your choice [m]").Trim().ToLower()
    if ([string]::IsNullOrWhiteSpace($envChoice)) { $envChoice = "m" }

    switch ($envChoice) {
        "s" {
            Write-Dim ".env left unchanged."
            $SKIP_ENV = $true
        }
        "m" {
            # Preserve existing security keys
            $existing = Get-Content .env -ErrorAction SilentlyContinue
            if ($existing) {
                foreach ($line in $existing) {
                    if ($line -match '^JWT_SECRET=(.+)$')        { $JWT_SECRET        = $Matches[1] }
                    if ($line -match '^ENCRYPTION_KEY=(.+)$')    { $ENCRYPTION_KEY    = $Matches[1] }
                    if ($line -match '^POSTGRES_PASSWORD=(.+)$') { $POSTGRES_PASSWORD = $Matches[1] }
                    if ($line -match '^REDIS_PASSWORD=(.+)$')    { $REDIS_PASSWORD    = $Matches[1] }
                }
            }
            Write-Ok "Existing security keys preserved."
            $SKIP_ENV = $false
        }
        default {
            $SKIP_ENV = $false
        }
    }
}

if (-not $SKIP_ENV) {
    $envContent = @"
# NomNomzBot Environment Variables
# Generated by deploy.ps1 on $(Get-Date -Format "yyyy-MM-dd HH:mm")
# Do NOT commit this file -- it contains your secrets.

# -- Security Keys (auto-generated -- do not share) ---------------------------
JWT_SECRET=$JWT_SECRET
ENCRYPTION_KEY=$ENCRYPTION_KEY

# -- Database -----------------------------------------------------------------
POSTGRES_USER=nomnomzbot
POSTGRES_PASSWORD=$POSTGRES_PASSWORD
POSTGRES_DB=nomnomzbot

# -- Redis --------------------------------------------------------------------
REDIS_PASSWORD=$REDIS_PASSWORD

# -- Twitch -------------------------------------------------------------------
TWITCH_CLIENT_ID=$TWITCH_CLIENT_ID
TWITCH_CLIENT_SECRET=$TWITCH_CLIENT_SECRET
TWITCH_BOT_USERNAME=$TWITCH_BOT_USERNAME

# -- URLs ---------------------------------------------------------------------
API_BASE_URL=$API_BASE_URL
FRONTEND_URL=$($API_BASE_URL -replace 'api\.', '')

# -- Deployment ---------------------------------------------------------------
DEPLOYMENT_MODE=self-hosted
ASPNETCORE_ENVIRONMENT=Production

# -- Cloudflare Tunnel --------------------------------------------------------
CLOUDFLARE_TUNNEL_TOKEN=$CLOUDFLARE_TUNNEL_TOKEN

# -- Optional Integrations ----------------------------------------------------
SPOTIFY_CLIENT_ID=$SPOTIFY_CLIENT_ID
SPOTIFY_CLIENT_SECRET=$SPOTIFY_CLIENT_SECRET
DISCORD_CLIENT_ID=$DISCORD_CLIENT_ID
DISCORD_CLIENT_SECRET=$DISCORD_CLIENT_SECRET
YOUTUBE_CLIENT_ID=
YOUTUBE_CLIENT_SECRET=

# -- Optional TTS -------------------------------------------------------------
AZURE_TTS_API_KEY=
AZURE_TTS_REGION=westeurope
ELEVENLABS_API_KEY=
"@

    [System.IO.File]::WriteAllText("$PWD\.env", $envContent)

    Write-Blank
    Write-Box @(
        "  [OK] .env written successfully!",
        "  All keys are cryptographically unique to this installation.",
        "  They're saved to .env -- never share that file."
    )
}

Write-Blank

# ══════════════════════════════════════════════════════════════════════════════
# BUILD AND START
# ══════════════════════════════════════════════════════════════════════════════

Write-Header "Starting NomNomzBot"

Write-Host "  Building and starting containers..." -ForegroundColor White
Write-Host "  This takes about 5 minutes the first time (downloading Docker images)." -ForegroundColor DarkGray
Write-Host "  Grab a coffee -- we'll wait here." -ForegroundColor DarkGray
Write-Blank

docker compose up -d --build
if ($LASTEXITCODE -ne 0) {
    Write-Err "docker compose up failed. Check the output above."
    exit 1
}

Write-Blank
Write-Ok "Containers started. Waiting for the API to become healthy..."
Write-Dim "(Migrations run automatically -- the database is being initialised.)"

# Read port from .env
$API_PORT = "5080"
if (Test-Path .env) {
    $portLine = Select-String -Path .env -Pattern "^API_HTTP_PORT=(.+)" -ErrorAction SilentlyContinue
    if ($portLine -and $portLine.Matches.Count -gt 0) {
        $API_PORT = $portLine.Matches[0].Groups[1].Value.Trim()
    }
}

$maxWait = 180
$elapsed = 0
$healthy = $false

while ($elapsed -lt $maxWait) {
    try {
        $resp = Invoke-WebRequest -Uri "http://localhost:$API_PORT/health/live" `
            -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
        if ($resp.StatusCode -eq 200) { $healthy = $true; break }
    } catch { }
    Write-Host "." -NoNewline
    Start-Sleep 5
    $elapsed += 5
}
Write-Host ""

if (-not $healthy) {
    Write-Err "API did not become healthy within ${maxWait}s."
    Write-Blank
    Write-Host "  Check what went wrong:" -ForegroundColor White
    Write-Host "  docker compose logs api" -ForegroundColor Cyan
    exit 1
}

# ══════════════════════════════════════════════════════════════════════════════
# SUCCESS
# ══════════════════════════════════════════════════════════════════════════════

Write-Blank
Write-Rule "="
Write-Host ""
Write-Host "    [OK]  NomNomzBot is running!" -ForegroundColor Green
Write-Host ""
Write-Rule "="
Write-Blank

$frontendUrl = $API_BASE_URL -replace 'api\.', ''
Write-Host "  Web app:  $frontendUrl" -ForegroundColor White
Write-Host "  API:      http://localhost:$API_PORT" -ForegroundColor White
Write-Host "  API docs: http://localhost:$API_PORT/scalar" -ForegroundColor White
Write-Host "  Health:   http://localhost:$API_PORT/health" -ForegroundColor White
Write-Blank
Write-Rule "-"
Write-Host "  Register these 3 Redirect URIs in your Twitch Developer Console:" -ForegroundColor White
Write-Host "  (dev.twitch.tv -> your app -> Manage)" -ForegroundColor DarkGray
Write-Blank
Write-Host "  $URI_LOGIN"   -ForegroundColor Cyan
Write-Host "  $URI_BOT"     -ForegroundColor Cyan
Write-Host "  $URI_CHANNEL" -ForegroundColor Cyan
Write-Blank

$finalCopy = Copy-ToClipboard "$URI_LOGIN`n$URI_BOT`n$URI_CHANNEL"
if ($finalCopy) { Write-Ok "Redirect URIs copied to clipboard." }

Write-Rule "-"
Write-Blank
Write-Dim "To stop:   docker compose down"
Write-Dim "To update: git pull --recurse-submodules && docker compose up -d --build"
Write-Blank
