#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) NoMercy Entertainment. All rights reserved.
#
# NomNomzBot — Interactive deployment script for Linux / macOS
# Usage: chmod +x deploy.sh && ./deploy.sh

set -uo pipefail

# ─── Colors ───────────────────────────────────────────────────────────────────
R=$'\033[0m'
BOLD=$'\033[1m'
DIM=$'\033[2m'
RED=$'\033[31m'
GREEN=$'\033[32m'
YELLOW=$'\033[33m'
CYAN=$'\033[36m'
WHITE=$'\033[37m'

OK="${GREEN}✓${R}"
FAIL="${RED}✗${R}"
WARN="${YELLOW}⚠${R}"
ARR="${CYAN}→${R}"

# ─── Helpers ──────────────────────────────────────────────────────────────────

nl() { echo ""; }

info()  { echo -e "${GREEN}${BOLD}[✓]${R} $*"; }
warn()  { echo -e "${YELLOW}${BOLD}[⚠]${R} $*"; }
error() { echo -e "${RED}${BOLD}[✗]${R} $*" >&2; }
dim()   { echo -e "${DIM}$*${R}"; }
step()  { echo -e "  ${CYAN}${BOLD}$1.${R}  $2"; }
note()  { echo -e "     ${DIM}$*${R}"; }

rule() {
  local char="${1:--}"
  local cols="${COLUMNS:-72}"
  local width=$(( cols < 72 ? cols : 72 ))
  printf "${DIM}%${width}s${R}\n" | tr ' ' "$char"
}

header() {
  nl
  rule "─"
  echo -e "${CYAN}${BOLD}  $1${R}"
  rule "─"
  nl
}

print_box() {
  # Usage: print_box "line1" "line2" ...
  local lines=("$@")
  local max=0
  for l in "${lines[@]}"; do
    local plain
    plain=$(echo -e "$l" | sed 's/\x1b\[[0-9;]*m//g')
    (( ${#plain} > max )) && max=${#plain}
  done
  local bar
  bar=$(printf '─%.0s' $(seq 1 $((max + 2))))
  echo -e "${CYAN}┌${bar}┐${R}"
  for l in "${lines[@]}"; do
    local plain
    plain=$(echo -e "$l" | sed 's/\x1b\[[0-9;]*m//g')
    local pad
    pad=$(printf '%*s' $((max - ${#plain})) "")
    echo -e "${CYAN}│${R} ${l}${pad} ${CYAN}│${R}"
  done
  echo -e "${CYAN}└${bar}┘${R}"
}

press_enter() {
  local msg="${1:-  ${DIM}Press Enter to continue...${R}}"
  echo ""
  read -rp "$(echo -e "$msg") " _
}

ask_with_default() {
  # ask_with_default VARNAME "prompt" "default"
  local __var="$1"
  local prompt="$2"
  local default="$3"
  local answer
  read -rp "$(echo -e "  ${GREEN}${prompt}${R} ${DIM}[${default}]${R}: ")" answer
  printf -v "$__var" '%s' "${answer:-$default}"
}

ask_required() {
  # ask_required VARNAME "prompt"
  local __var="$1"
  local prompt="$2"
  local answer=""
  while [[ -z "$answer" ]]; do
    read -rp "$(echo -e "  ${GREEN}${prompt}${R}: ")" answer
    if [[ -z "$answer" ]]; then
      echo -e "  ${FAIL} ${RED}This is required. Please enter a value.${R}"
    fi
  done
  printf -v "$__var" '%s' "$answer"
}

confirm() {
  # confirm "message" [default_yes=1]
  local msg="$1"
  local default="${2:-1}"
  local hint
  hint=$( [[ $default -eq 1 ]] && echo "${DIM}[Y/n]${R}" || echo "${DIM}[y/N]${R}" )
  local answer
  read -rp "$(echo -e "  $msg $hint ")" answer
  if [[ -z "$answer" ]]; then
    return $(( 1 - default ))
  fi
  [[ "$answer" =~ ^[Yy] ]] && return 0 || return 1
}

open_browser() {
  local url="$1"
  if command -v xdg-open &>/dev/null; then
    xdg-open "$url" &>/dev/null & disown 2>/dev/null || true
    return 0
  elif command -v open &>/dev/null; then
    open "$url" &>/dev/null & disown 2>/dev/null || true
    return 0
  fi
  return 1
}

copy_to_clipboard() {
  local text="$1"
  if command -v pbcopy &>/dev/null; then
    printf '%s' "$text" | pbcopy && return 0
  elif command -v xclip &>/dev/null; then
    printf '%s' "$text" | xclip -selection clipboard && return 0
  elif command -v xsel &>/dev/null; then
    printf '%s' "$text" | xsel --clipboard --input && return 0
  fi
  return 1
}

# Validate and re-prompt a credential
# get_credential VARNAME "label" "what it is" "example" "regex"
get_credential() {
  local __var="$1"
  local label="$2"
  local what="$3"
  local example="$4"
  local regex="$5"
  local value=""

  for (( ; ; )); do
    nl
    echo -e "  ${BOLD}${CYAN}${label}${R}"
    nl
    echo -e "  ${what}"
    if [[ -n "$example" ]]; then
      echo -e "  ${DIM}Example: ${example}${R}"
    fi
    nl
    read -rp "$(echo -e "  ${GREEN}Paste it here${R}: ")" value
    value="${value// /}"   # strip accidental spaces

    if [[ -z "$value" ]]; then
      echo -e "\n  ${FAIL} ${RED}This is required.${R}"
      continue
    fi

    if [[ -n "$regex" ]] && ! [[ "$value" =~ $regex ]]; then
      nl
      echo -e "  ${WARN} ${YELLOW}That doesn't look right.${R}"
      echo -e "  ${DIM}It should look like: ${example}${R}"
      nl
      if confirm "Try entering it again?" 1; then
        continue
      fi
    fi

    # Show preview and confirm
    nl
    local preview="${value:0:8}...${value: -4}"
    echo -e "  ${DIM}You entered:${R}  ${CYAN}${BOLD}${preview}${R}  ${DIM}(${#value} chars)${R}"
    nl
    if confirm "Is that correct?" 1; then
      break
    fi
  done

  printf -v "$__var" '%s' "$value"
}

set_env_var() {
  local key="$1"
  local val="$2"
  local file="${3:-.env}"
  # escape for sed: & \ / in value
  local escaped
  escaped=$(printf '%s\n' "$val" | sed 's/[&/\]/\\&/g')
  if grep -q "^${key}=" "$file" 2>/dev/null; then
    sed -i "s|^${key}=.*|${key}=${escaped}|" "$file"
  else
    echo "${key}=${val}" >> "$file"
  fi
}

DOCKER="docker"

# ══════════════════════════════════════════════════════════════════════════════
# WELCOME
# ══════════════════════════════════════════════════════════════════════════════

clear 2>/dev/null || true
nl
echo -e "${MAGENTA:-$CYAN}${BOLD}"
echo "  ███╗   ██╗ ██████╗ ███╗   ███╗███╗   ██╗ ██████╗ ███╗   ███╗███████╗"
echo "  ████╗  ██║██╔═══██╗████╗ ████║████╗  ██║██╔═══██╗████╗ ████║╚══███╔╝"
echo "  ██╔██╗ ██║██║   ██║██╔████╔██║██╔██╗ ██║██║   ██║██╔████╔██║  ███╔╝ "
echo "  ██║╚██╗██║██║   ██║██║╚██╔╝██║██║╚██╗██║██║   ██║██║╚██╔╝██║ ███╔╝  "
echo "  ██║ ╚████║╚██████╔╝██║ ╚═╝ ██║██║ ╚████║╚██████╔╝██║ ╚═╝ ██║███████╗"
echo "  ╚═╝  ╚═══╝ ╚═════╝ ╚═╝     ╚═╝╚═╝  ╚═══╝ ╚═════╝ ╚═╝     ╚═╝╚══════╝"
echo -e "${R}"
rule "═"
echo -e "  ${BOLD}Welcome to NomNomzBot Setup!${R}"
echo ""
echo -e "  This will get your bot running in about 5–10 minutes."
echo -e "  ${DIM}You'll need a Twitch account. That's it.${R}"
rule "═"
nl
echo -e "  ${DIM}This script will:${R}"
echo -e "  ${ARR} Check (and optionally install) Docker"
echo -e "  ${ARR} Walk you through creating a Twitch application"
echo -e "  ${ARR} Generate all security keys automatically"
echo -e "  ${ARR} Start NomNomzBot with docker compose"
nl

press_enter "  Press Enter to begin..."

# ══════════════════════════════════════════════════════════════════════════════
# STEP 1 — Docker
# ══════════════════════════════════════════════════════════════════════════════

header "Step 1 of 6 — Docker"

echo -e "  Docker is the only thing you need installed on this server."
echo -e "  ${DIM}It runs the bot, database, and everything else in isolated containers.${R}"
nl

# Check docker
if ! command -v docker &>/dev/null; then
  warn "Docker is not installed."
  nl
  if confirm "Install Docker now? (recommended — runs curl | sh from get.docker.com)" 1; then
    nl
    info "Installing Docker..."
    curl -fsSL https://get.docker.com | sh
    nl
    if ! groups "$USER" 2>/dev/null | grep -q '\bdocker\b'; then
      sudo usermod -aG docker "$USER" 2>/dev/null || true
      warn "Added ${USER} to the docker group."
      warn "You may need to run: ${BOLD}newgrp docker${R}${YELLOW} — or log out and back in."
      warn "For this session, commands will run with sudo if needed."
      DOCKER="sudo docker"
    fi
    info "Docker installed."
  else
    nl
    error "Docker is required to run NomNomzBot."
    echo -e "  Install it from: ${CYAN}https://docs.docker.com/get-docker/${R}"
    exit 1
  fi
else
  DOCKER_VER=$($DOCKER --version 2>/dev/null | head -1)
  info "Docker found: ${CYAN}${DOCKER_VER}${R}"
fi

# Check docker compose v2
if ! $DOCKER compose version &>/dev/null; then
  warn "Docker Compose v2 plugin not found."
  nl
  if confirm "Install docker-compose-plugin now?" 1; then
    if command -v apt-get &>/dev/null; then
      sudo apt-get update -qq && sudo apt-get install -y docker-compose-plugin
    elif command -v yum &>/dev/null; then
      sudo yum install -y docker-compose-plugin
    elif command -v dnf &>/dev/null; then
      sudo dnf install -y docker-compose-plugin
    else
      error "Cannot auto-install on this system."
      echo -e "  Install manually: ${CYAN}https://docs.docker.com/compose/install/${R}"
      exit 1
    fi
    info "Docker Compose v2 installed."
  else
    error "Docker Compose v2 is required."
    exit 1
  fi
else
  COMPOSE_VER=$($DOCKER compose version 2>/dev/null | head -1)
  info "Docker Compose found: ${CYAN}${COMPOSE_VER}${R}"
fi

nl
info "Docker is ready."

# ══════════════════════════════════════════════════════════════════════════════
# STEP 2 — API Base URL
# ══════════════════════════════════════════════════════════════════════════════

header "Step 2 of 6 — Your Server URL"

print_box \
  "  What is the public URL for your bot's API?" \
  "" \
  "  This is the URL your server is reachable at from the internet." \
  "  Twitch needs this to send login callbacks back to your bot." \
  "" \
  "  ${DIM}Examples:${R}" \
  "    ${CYAN}https://bot-dev-api.nomercy.tv${R}  ← shared dev tunnel (already works)" \
  "    ${CYAN}https://api.yourdomain.com${R}     ← your own domain" \
  "" \
  "  ${DIM}Note: Twitch OAuth requires HTTPS for real logins.${R}" \
  "  ${DIM}http://localhost works for local testing only.${R}"

nl
ask_with_default API_BASE_URL "Your server URL" "https://bot-dev-api.nomercy.tv"
API_BASE_URL="${API_BASE_URL%/}"   # strip trailing slash

nl
if [[ "$API_BASE_URL" == http://localhost* ]]; then
  warn "You entered a localhost URL."
  echo -e "  ${YELLOW}Twitch OAuth requires HTTPS for real logins. This will work for local"
  echo -e "  testing, but you won't be able to log in with a real Twitch account until"
  echo -e "  you have a public HTTPS domain. That's fine — continue for now.${R}"
  nl
  confirm "Continue with localhost?" 1 || { error "Aborted."; exit 1; }
fi

info "API URL set to: ${CYAN}${API_BASE_URL}${R}"

# Compute redirect URIs
URI_LOGIN="${API_BASE_URL}/api/v1/auth/twitch/callback"
URI_BOT="${API_BASE_URL}/api/v1/auth/twitch/bot/callback"
URI_CHANNEL="${API_BASE_URL}/api/v1/channels/callback/bot"

# ══════════════════════════════════════════════════════════════════════════════
# STEP 3 — Show redirect URIs + clipboard
# ══════════════════════════════════════════════════════════════════════════════

header "Step 3 of 6 — Twitch Redirect URIs"

echo -e "  You'll need to add these 3 URLs to your Twitch application in the next step."
echo -e "  ${DIM}Twitch uses them to send users back to your bot after they log in.${R}"
nl

print_box \
  "  ${BOLD}Your Twitch Redirect URIs:${R}" \
  "" \
  "  1. ${CYAN}${URI_LOGIN}${R}" \
  "  2. ${CYAN}${URI_BOT}${R}" \
  "  3. ${CYAN}${URI_CHANNEL}${R}"

nl

ALL_URIS="${URI_LOGIN}
${URI_BOT}
${URI_CHANNEL}"

if copy_to_clipboard "$ALL_URIS" 2>/dev/null; then
  info "${BOLD}${GREEN}All 3 URLs copied to your clipboard!${R}"
  echo -e "  ${DIM}You can paste them into the Twitch form in the next step.${R}"
else
  warn "Couldn't copy automatically (no clipboard tool found)."
  echo -e "  ${DIM}Copy the URLs above manually.${R}"
fi

press_enter "  Keep these handy — press Enter to open the Twitch setup..."

# ══════════════════════════════════════════════════════════════════════════════
# STEP 4 — Twitch Application
# ══════════════════════════════════════════════════════════════════════════════

header "Step 4 of 6 — Create Your Twitch Application"

print_box \
  "  ${BOLD}What is a Twitch Application?${R}" \
  "" \
  "  NomNomzBot needs to be registered with Twitch so that Twitch" \
  "  knows your bot is legitimate and has permission to access your channel." \
  "" \
  "  Think of it like getting an ID badge for your bot at Twitch HQ." \
  "" \
  "  You'll get two codes:" \
  "    ${BOLD}Client ID${R}      — like a username for your bot" \
  "    ${BOLD}Client Secret${R}  — like a password for your bot" \
  "" \
  "  ${DIM}Takes about 3–5 minutes. It's free.${R}"

nl
if ! confirm "Skip Twitch setup for now? (you can add credentials later in .env)" 0; then

  # ── Part 1: Open the console ────────────────────────────────────────────────
  header "Part 1 of 3 — Sign In to Twitch Developer Console"

  step 1 "We're opening the Twitch Developer Console in your browser:"
  nl
  echo -e "     ${CYAN}${BOLD}https://dev.twitch.tv/console/apps${R}"
  nl

  if open_browser "https://dev.twitch.tv/console/apps" 2>/dev/null; then
    info "Browser opened."
  else
    warn "Couldn't open a browser (headless server?)."
    echo -e "  ${DIM}Open this URL on your local machine:${R}"
    echo -e "  ${CYAN}https://dev.twitch.tv/console/apps${R}"
  fi

  nl
  step 2 "Sign in with your ${BOLD}streamer${R} Twitch account if prompted."
  note "This is your personal streaming account — NOT the bot account."
  nl
  step 3 "You should see a page titled ${BOLD}\"Applications\"${R} with a"
  echo -e "       ${BOLD}\"Register Your Application\"${R} button."

  press_enter "  Press Enter once you can see the Applications page..."

  # ── Part 2: Fill the form ────────────────────────────────────────────────────
  header "Part 2 of 3 — Create the Application"

  step 1 "Click ${BOLD}${CYAN}\"Register Your Application\"${R}."
  nl
  step 2 "Fill in the form:"
  nl

  print_box \
    "  ${BOLD}Name:${R}      Anything you like — e.g. ${CYAN}\"My NomNomzBot\"${R}" \
    "             ${DIM}(Just a label for your own reference)${R}" \
    "" \
    "  ${BOLD}Category:${R}  Select ${BOLD}\"Chat Bot\"${R} from the dropdown"

  nl
  step 3 "${BOLD}OAuth Redirect URLs${R} — you need to add ${BOLD}3 URLs${R}, one at a time."
  nl

  # Walk through each URI
  URI_LABELS=("Main login callback" "Bot account callback" "Per-channel bot callback")
  ALL_URIS_ARR=("$URI_LOGIN" "$URI_BOT" "$URI_CHANNEL")

  for i in 0 1 2; do
    uri="${ALL_URIS_ARR[$i]}"
    label="${URI_LABELS[$i]}"
    num=$(( i + 1 ))

    rule "·"
    nl
    echo -e "  ${CYAN}${BOLD}Redirect URL ${num} of 3:${R}  ${DIM}${label}${R}"
    nl

    if copy_to_clipboard "$uri" 2>/dev/null; then
      info "${GREEN}${BOLD}Copied to clipboard!${R}"
    else
      warn "Couldn't copy automatically. Copy it manually:"
    fi

    nl
    print_box "  ${uri}  "
    nl

    if [[ $num -lt 3 ]]; then
      step 1 "Paste the URL above into the ${BOLD}\"OAuth Redirect URLs\"${R} field."
      step 2 "Click ${BOLD}\"Add\"${R} to save it before moving to the next URL."
      press_enter "  Press Enter when URL ${num} is added and you're ready for the next..."
    else
      step 1 "Paste the URL above into the field and click ${BOLD}\"Add\"${R}."
    fi
  done

  rule "·"
  nl
  step 4 "Check the ${BOLD}\"I'm not a robot\"${R} box if it appears."
  nl
  step 5 "Click the ${BOLD}${CYAN}\"Create\"${R} button at the bottom."

  press_enter "  Press Enter once you've clicked \"Create\"..."

  # ── Part 3: Get credentials ──────────────────────────────────────────────────
  header "Part 3 of 3 — Copy Your Credentials"

  step 1 "Find your new app in the list and click ${BOLD}${CYAN}\"Manage\"${R}."
  nl
  step 2 "You'll land on your app's settings page."

  press_enter "  Press Enter once you're on your app's detail page..."

  # Client ID
  header "Getting Your Client ID"

  echo -e "  Look for ${BOLD}\"Client ID\"${R} on the app settings page."
  echo -e "  You'll see a long string of letters and numbers."
  nl
  echo -e "  ${DIM}It looks like:${R}"
  echo -e "     ${DIM}abc123def456ghi789jkl012mnop34${R}"
  echo -e "  ${DIM}(about 30 lowercase letters and numbers)${R}"

  get_credential TWITCH_CLIENT_ID \
    "Twitch Client ID" \
    "This identifies your bot application to Twitch." \
    "abc123def456ghi789jkl012mnop34" \
    "^[a-zA-Z0-9]{15,}$"

  # Client Secret
  header "Getting Your Client Secret"

  echo -e "  The Client Secret is ${BOLD}not visible by default${R} — you need to generate it."
  nl
  step 1 "On the same page, scroll down to ${BOLD}\"Client Secret\"${R}."
  step 2 "Click ${BOLD}${CYAN}\"New Secret\"${R}."
  step 3 "A confirmation may appear — click ${BOLD}\"OK\"${R}."
  step 4 "${BOLD}${RED}Copy the secret immediately${R} — Twitch only shows it once."
  note "(If you forget, click \"New Secret\" again — it just creates a new one.)"
  nl
  echo -e "  ${DIM}It looks similar to the Client ID:${R}"
  echo -e "     ${DIM}abc123def456ghi789jkl012mnop34${R}"

  get_credential TWITCH_CLIENT_SECRET \
    "Twitch Client Secret" \
    "Like a password for your app. Keep it private — never share it." \
    "abc123def456ghi789jkl012mnop34" \
    "^[a-zA-Z0-9]{15,}$"

  info "Twitch credentials saved!"

else
  nl
  dim "  Skipped. Set TWITCH_CLIENT_ID and TWITCH_CLIENT_SECRET in .env later."
  TWITCH_CLIENT_ID=""
  TWITCH_CLIENT_SECRET=""
fi

# ── Bot account ─────────────────────────────────────────────────────────────
nl
header "Bot Account (Optional)"

print_box \
  "  ${BOLD}What is the Bot Account?${R}" \
  "" \
  "  NomNomzBot can use a ${BOLD}separate Twitch account${R} as the \"bot\"." \
  "  This is the account that appears in your chat — e.g. ${CYAN}NomNomzBot${R}: Hello!" \
  "" \
  "  ${BOLD}Why separate?${R} So viewers can tell apart your messages from bot messages." \
  "" \
  "  ${DIM}You can skip this and set it up later in the app Settings.${R}"

nl
TWITCH_BOT_USERNAME="NomNomzBot"
if confirm "Do you have a separate bot Twitch account?" 0; then
  nl
  echo -e "  ${DIM}Enter the exact Twitch username of the bot account.${R}"
  echo -e "  ${DIM}Example: NomNomzBot  or  MyChannelBot  or  StreamerBot_${R}"

  for (( ; ; )); do
    nl
    ask_required TWITCH_BOT_USERNAME "Bot account username"

    if [[ "$TWITCH_BOT_USERNAME" =~ [[:space:]] ]]; then
      warn "Twitch usernames can't contain spaces. Try again."
      continue
    fi

    nl
    echo -e "  ${DIM}You entered:${R}  ${CYAN}${BOLD}${TWITCH_BOT_USERNAME}${R}"
    if confirm "Is that correct?" 1; then break; fi
  done
  info "Bot username: ${CYAN}${TWITCH_BOT_USERNAME}${R}"
else
  dim "  Bot account skipped. Using NomNomzBot as default."
fi

# ══════════════════════════════════════════════════════════════════════════════
# STEP 5 — Optional Integrations
# ══════════════════════════════════════════════════════════════════════════════

header "Step 5 of 6 — Optional Integrations"

SPOTIFY_CLIENT_ID=""
SPOTIFY_CLIENT_SECRET=""
DISCORD_CLIENT_ID=""
DISCORD_CLIENT_SECRET=""
CLOUDFLARE_TUNNEL_TOKEN=""

# ── Spotify ──────────────────────────────────────────────────────────────────
print_box \
  "  ${BOLD}Spotify${R}  ${DIM}(optional)${R}" \
  "" \
  "  Lets viewers see what song is playing and request songs with !sr." \
  "  You need a free or premium Spotify account."

nl
if confirm "Set up Spotify integration?" 0; then
  nl
  step 1 "Open the Spotify Developer Dashboard:"
  nl
  echo -e "     ${CYAN}${BOLD}https://developer.spotify.com/dashboard${R}"
  nl

  if confirm "  Open it now?" 1; then
    open_browser "https://developer.spotify.com/dashboard" || true
  fi

  nl
  step 2 "Sign in and click ${BOLD}${CYAN}\"Create App\"${R}."
  nl
  step 3 "Fill in the form:"
  nl

  print_box \
    "  ${BOLD}App name:${R}         Anything, e.g. ${CYAN}\"NomNomzBot\"${R}" \
    "  ${BOLD}Redirect URI:${R}     ${CYAN}${API_BASE_URL}/api/v1/auth/spotify/callback${R}" \
    "  ${BOLD}APIs used:${R}        Check ${BOLD}\"Web API\"${R}"

  nl
  step 4 "Check Terms of Service and click ${BOLD}${CYAN}\"Save\"${R}."
  step 5 "Click ${BOLD}\"Settings\"${R} → copy your ${BOLD}Client ID${R}."

  press_enter "  Press Enter once you've created your Spotify app..."

  get_credential SPOTIFY_CLIENT_ID \
    "Spotify Client ID" \
    "Identifies your Spotify app to NomNomzBot." \
    "4c01e10681b24fc8b18a2f9a1f7bdbfb" \
    "^[a-zA-Z0-9]{20,}$"

  nl
  echo -e "  Click ${BOLD}${CYAN}\"View client secret\"${R} to reveal your secret."

  get_credential SPOTIFY_CLIENT_SECRET \
    "Spotify Client Secret" \
    "Like a password — keep it private." \
    "6ee29fb8093046aeaecebaa6f4ba3d3b" \
    "^[a-zA-Z0-9]{20,}$"

  info "Spotify credentials saved!"
else
  dim "  Skipped. Enable Spotify later in the app Settings."
fi

nl

# ── Discord ───────────────────────────────────────────────────────────────────
print_box \
  "  ${BOLD}Discord${R}  ${DIM}(optional)${R}" \
  "" \
  "  Posts stream-start announcements to your Discord server." \
  "  You need a Discord account and a server."

nl
if confirm "Set up Discord integration?" 0; then
  nl
  step 1 "Open the Discord Developer Portal:"
  nl
  echo -e "     ${CYAN}${BOLD}https://discord.com/developers/applications${R}"
  nl

  if confirm "  Open it now?" 1; then
    open_browser "https://discord.com/developers/applications" || true
  fi

  nl
  step 2 "Click ${BOLD}${CYAN}\"New Application\"${R}, give it a name, click ${BOLD}\"Create\"${R}."
  step 3 "In the left sidebar, click ${BOLD}\"OAuth2\"${R}."
  step 4 "Under ${BOLD}\"Redirects\"${R}, add this URL:"
  nl
  print_box "  ${CYAN}${API_BASE_URL}/api/v1/auth/discord/callback${R}"
  nl
  step 5 "Click ${BOLD}${CYAN}\"Save Changes\"${R}."

  press_enter "  Press Enter once you've saved your Discord app..."

  nl
  echo -e "  Your ${BOLD}Client ID${R} is shown near the top of the OAuth2 page."
  echo -e "  ${DIM}It looks like a long number: 952230846465728553${R}"

  get_credential DISCORD_CLIENT_ID \
    "Discord Client ID" \
    "A number that identifies your Discord application." \
    "952230846465728553" \
    "^[0-9]{15,}$"

  nl
  step 1 "Click ${BOLD}${CYAN}\"Reset Secret\"${R} then copy the secret that appears."
  warn "${YELLOW}The Client Secret is on the OAuth2 page — NOT the Bot page.${R}"

  get_credential DISCORD_CLIENT_SECRET \
    "Discord Client Secret" \
    "OAuth2 secret — NOT the bot token. Keep it private." \
    "YsV4pbm379BG2HZPUG2qln1By0J_DLTy" \
    ".{20,}"

  info "Discord credentials saved!"
else
  dim "  Skipped. Enable Discord later in the app Settings."
fi

nl

# ── Cloudflare Tunnel ─────────────────────────────────────────────────────────
print_box \
  "  ${BOLD}Cloudflare Tunnel Token${R}  ${DIM}(optional)${R}" \
  "" \
  "  Needed if your server doesn't have a domain pointed at it yet," \
  "  or if you want secure HTTPS without configuring nginx/Caddy." \
  "" \
  "  ${DIM}Skip if you already have a domain or are using the shared dev tunnel.${R}"

nl
if confirm "Do you have a Cloudflare Tunnel token?" 0; then
  nl
  step 1 "Go to: ${CYAN}https://one.dash.cloudflare.com/${R} → ${BOLD}Networks → Tunnels${R}"
  step 2 "Click ${BOLD}${CYAN}\"Create a tunnel\"${R} → choose ${BOLD}\"Cloudflared\"${R}"
  step 3 "Give it a name and copy the token from ${BOLD}\"Install and run a connector\"${R}"
  note   "It looks like: eyJhIjoiYWJj...(very long base64 string)"

  get_credential CLOUDFLARE_TUNNEL_TOKEN \
    "Cloudflare Tunnel Token" \
    "Lets your bot be reachable via a secure HTTPS tunnel." \
    "eyJhIjoiYWJj...(long string)" \
    ".{50,}"

  info "Cloudflare Tunnel token saved!"
else
  dim "  Skipped. Add CLOUDFLARE_TUNNEL_TOKEN to .env later if needed."
fi

# ══════════════════════════════════════════════════════════════════════════════
# STEP 6 — Generate keys + write .env
# ══════════════════════════════════════════════════════════════════════════════

header "Step 6 of 6 — Generating Security Keys & Writing Configuration"

echo -e "  Generating cryptographically random security keys..."
nl

JWT_SECRET=$(openssl rand -base64 64)
info "JWT Secret generated           ${DIM}(64 bytes, base64)${R}"

ENCRYPTION_KEY=$(openssl rand -base64 32)
info "Encryption Key generated       ${DIM}(32 bytes, AES-256)${R}"

POSTGRES_PASSWORD=$(openssl rand -hex 32)
info "PostgreSQL password generated"

REDIS_PASSWORD=$(openssl rand -hex 32)
info "Redis password generated"

nl

# Handle existing .env
if [[ -f .env ]]; then
  nl
  warn ".env already exists from a previous setup."
  nl
  echo -e "  ${BOLD}m${R} = Merge    (keep existing secrets, update credentials only)"
  echo -e "  ${BOLD}o${R} = Overwrite (generate fresh secrets, replace everything)"
  echo -e "  ${BOLD}s${R} = Skip     (leave .env exactly as-is)"
  nl
  read -rp "$(echo -e "  ${GREEN}Your choice${R} ${DIM}[m]${R}: ")" env_choice
  env_choice="${env_choice:-m}"

  case "${env_choice,,}" in
    s)
      dim "  .env left unchanged."
      SKIP_ENV=1
      ;;
    m)
      # Preserve existing security keys
      EXISTING_JWT=$(grep "^JWT_SECRET=" .env 2>/dev/null | cut -d= -f2- || true)
      EXISTING_ENC=$(grep "^ENCRYPTION_KEY=" .env 2>/dev/null | cut -d= -f2- || true)
      EXISTING_PG=$(grep "^POSTGRES_PASSWORD=" .env 2>/dev/null | cut -d= -f2- || true)
      EXISTING_REDIS=$(grep "^REDIS_PASSWORD=" .env 2>/dev/null | cut -d= -f2- || true)
      [[ -n "$EXISTING_JWT" ]]   && JWT_SECRET="$EXISTING_JWT"
      [[ -n "$EXISTING_ENC" ]]   && ENCRYPTION_KEY="$EXISTING_ENC"
      [[ -n "$EXISTING_PG" ]]    && POSTGRES_PASSWORD="$EXISTING_PG"
      [[ -n "$EXISTING_REDIS" ]] && REDIS_PASSWORD="$EXISTING_REDIS"
      info "Existing security keys preserved."
      SKIP_ENV=0
      ;;
    *)
      SKIP_ENV=0
      ;;
  esac
else
  SKIP_ENV=0
fi

if [[ "${SKIP_ENV:-0}" -eq 0 ]]; then
  # Write .env
  cat > .env <<ENVFILE
# NomNomzBot Environment Variables
# Generated by deploy.sh on $(date)
# Do NOT commit this file — it contains your secrets.

# ── Security Keys (auto-generated — do not share) ────────────────────────────
JWT_SECRET=${JWT_SECRET}
ENCRYPTION_KEY=${ENCRYPTION_KEY}

# ── Database ──────────────────────────────────────────────────────────────────
POSTGRES_USER=nomnomzbot
POSTGRES_PASSWORD=${POSTGRES_PASSWORD}
POSTGRES_DB=nomnomzbot

# ── Redis ─────────────────────────────────────────────────────────────────────
REDIS_PASSWORD=${REDIS_PASSWORD}

# ── Twitch ────────────────────────────────────────────────────────────────────
TWITCH_CLIENT_ID=${TWITCH_CLIENT_ID}
TWITCH_CLIENT_SECRET=${TWITCH_CLIENT_SECRET}
TWITCH_BOT_USERNAME=${TWITCH_BOT_USERNAME}

# ── URLs ──────────────────────────────────────────────────────────────────────
API_BASE_URL=${API_BASE_URL}
FRONTEND_URL=${API_BASE_URL/api./}

# ── Deployment ────────────────────────────────────────────────────────────────
DEPLOYMENT_MODE=self-hosted
ASPNETCORE_ENVIRONMENT=Production

# ── Cloudflare Tunnel ─────────────────────────────────────────────────────────
CLOUDFLARE_TUNNEL_TOKEN=${CLOUDFLARE_TUNNEL_TOKEN}

# ── Optional Integrations ─────────────────────────────────────────────────────
SPOTIFY_CLIENT_ID=${SPOTIFY_CLIENT_ID}
SPOTIFY_CLIENT_SECRET=${SPOTIFY_CLIENT_SECRET}
DISCORD_CLIENT_ID=${DISCORD_CLIENT_ID}
DISCORD_CLIENT_SECRET=${DISCORD_CLIENT_SECRET}
YOUTUBE_CLIENT_ID=
YOUTUBE_CLIENT_SECRET=

# ── Optional TTS ──────────────────────────────────────────────────────────────
AZURE_TTS_API_KEY=
AZURE_TTS_REGION=westeurope
ELEVENLABS_API_KEY=
ENVFILE

  nl
  print_box \
    "  ${OK} ${BOLD}${GREEN}.env written successfully!${R}" \
    "  ${DIM}All keys are cryptographically unique to this installation.${R}" \
    "  ${DIM}They're saved to .env — never share that file.${R}"
fi

nl

# ══════════════════════════════════════════════════════════════════════════════
# BUILD AND START
# ══════════════════════════════════════════════════════════════════════════════

header "Starting NomNomzBot"

echo -e "  Building and starting containers..."
echo -e "  ${DIM}This takes about 5 minutes the first time (downloading Docker images).${R}"
echo -e "  ${DIM}Grab a coffee — we'll wait here.${R}"
nl

$DOCKER compose up -d --build

nl
info "Containers started. Waiting for the API to become healthy..."
echo -e "  ${DIM}(Migrations run automatically — the database is being initialised.)${R}"

API_PORT=$(grep "^API_HTTP_PORT=" .env 2>/dev/null | cut -d= -f2 || echo "5080")
API_PORT="${API_PORT:-5080}"

MAX_WAIT=180
ELAPSED=0
while true; do
  if curl -sf "http://localhost:${API_PORT}/health/live" &>/dev/null; then
    break
  fi
  if [[ $ELAPSED -ge $MAX_WAIT ]]; then
    error "API did not become healthy within ${MAX_WAIT}s."
    nl
    echo -e "  Check what went wrong:"
    echo -e "  ${CYAN}docker compose logs api${R}"
    exit 1
  fi
  printf "."
  sleep 5
  ELAPSED=$(( ELAPSED + 5 ))
done
echo ""

# ══════════════════════════════════════════════════════════════════════════════
# SUCCESS
# ══════════════════════════════════════════════════════════════════════════════

nl
rule "═"
echo -e "${GREEN}${BOLD}"
echo "    ✓  NomNomzBot is running!"
echo -e "${R}"
rule "═"
nl
echo -e "  ${BOLD}Web app:${R}  ${CYAN}${API_BASE_URL/api./}${R}"
echo -e "  ${BOLD}API:${R}      ${CYAN}http://localhost:${API_PORT}${R}"
echo -e "  ${BOLD}API docs:${R} ${CYAN}http://localhost:${API_PORT}/scalar${R}"
echo -e "  ${BOLD}Health:${R}   ${CYAN}http://localhost:${API_PORT}/health${R}"
nl
rule "─"
echo -e "  ${BOLD}Register these 3 Redirect URIs in your Twitch Developer Console:${R}"
echo -e "  ${DIM}(dev.twitch.tv → your app → Manage)${R}"
nl
echo -e "  ${GREEN}${URI_LOGIN}${R}"
echo -e "  ${GREEN}${URI_BOT}${R}"
echo -e "  ${GREEN}${URI_CHANNEL}${R}"
nl
if copy_to_clipboard "${URI_LOGIN}
${URI_BOT}
${URI_CHANNEL}" 2>/dev/null; then
  info "Redirect URIs copied to clipboard."
fi
rule "─"
nl
echo -e "  ${DIM}To stop:  docker compose down${R}"
echo -e "  ${DIM}To update: git pull --recurse-submodules && docker compose up -d --build${R}"
nl
