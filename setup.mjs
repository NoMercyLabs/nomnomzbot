#!/usr/bin/env node
/**
 * NomNomzBot Interactive Setup Helper
 *
 * Guides non-technical users through the complete setup process.
 * Zero external dependencies — Node.js built-in modules only.
 *
 * Usage:  node setup.mjs
 *         npm run setup
 */

import readline from 'readline';
import { execSync, spawn } from 'child_process';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import crypto from 'crypto';

const __filename = fileURLToPath(import.meta.url);
const ROOT         = path.dirname(__filename);
const BACKEND_DIR  = path.join(ROOT, 'server');
const FRONTEND_DIR = path.join(ROOT, 'app');
const APPSETTINGS_DEV = path.join(BACKEND_DIR, 'src', 'NomNomzBot.Api', 'appsettings.Development.json');
const BACKEND_ENV  = path.join(BACKEND_DIR, '.env');
const FRONTEND_ENV = path.join(FRONTEND_DIR, '.env.development');
const PROGRESS_FILE = path.join(ROOT, 'setup.progress.json');

const IS_WIN = process.platform === 'win32';
const IS_MAC = process.platform === 'darwin';

// ─── ANSI colors ──────────────────────────────────────────────────────────────
const RESET   = '\x1b[0m';
const BOLD    = '\x1b[1m';
const DIM     = '\x1b[2m';
const RED     = '\x1b[31m';
const GREEN   = '\x1b[32m';
const YELLOW  = '\x1b[33m';
const BLUE    = '\x1b[34m';
const MAGENTA = '\x1b[35m';
const CYAN    = '\x1b[36m';
const WHITE   = '\x1b[37m';

const bold    = s => `${BOLD}${s}${RESET}`;
const dim     = s => `${DIM}${s}${RESET}`;
const red     = s => `${RED}${s}${RESET}`;
const green   = s => `${GREEN}${s}${RESET}`;
const yellow  = s => `${YELLOW}${s}${RESET}`;
const cyan    = s => `${CYAN}${s}${RESET}`;
const magenta = s => `${MAGENTA}${s}${RESET}`;

function stripAnsi(str) {
  return str.replace(/\x1b\[[0-9;]*m/g, '');
}

const OK   = green('✓');
const FAIL = red('✗');
const INFO = cyan('ℹ');
const WARN = yellow('⚠');
const ARR  = cyan('→');

// ─── Readline ─────────────────────────────────────────────────────────────────
let rl;

function createRl() {
  rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout,
    terminal: true,
  });
}

function ask(question) {
  return new Promise(resolve => rl.question(question, a => resolve(a.trim())));
}

async function confirm(message, defaultYes = true) {
  const hint = dim(defaultYes ? '[Y/n]' : '[y/N]');
  const answer = await ask(`${message} ${hint} `);
  if (!answer) return defaultYes;
  return /^y/i.test(answer);
}

async function pressEnter(message = '') {
  const msg = message || dim('  Press Enter to continue...');
  await ask(`\n${msg} `);
}

// ─── Spinner ──────────────────────────────────────────────────────────────────
const FRAMES = IS_WIN ? ['-', '\\', '|', '/'] : ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

class Spinner {
  constructor(msg) { this.msg = msg; this.idx = 0; this.t = null; }
  start() {
    process.stdout.write('\x1b[?25l');
    this.t = setInterval(() => {
      process.stdout.write(`\r${CYAN}${FRAMES[this.idx++ % FRAMES.length]}${RESET} ${this.msg}`);
    }, 80);
    return this;
  }
  stop(line = '') {
    clearInterval(this.t);
    process.stdout.write('\r\x1b[K\x1b[?25h');
    if (line) console.log(line);
  }
  succeed(m) { this.stop(`${OK} ${m || this.msg}`); }
  fail(m)    { this.stop(`${FAIL} ${m || this.msg}`); }
  info(m)    { this.stop(`${INFO} ${m || this.msg}`); }
}

const spin = m => new Spinner(m);

// ─── Visual helpers ────────────────────────────────────────────────────────────
function nl() { console.log(''); }

function rule(char = '─', color = DIM) {
  const width = Math.min(process.stdout.columns || 72, 72);
  console.log(`${color}${char.repeat(width)}${RESET}`);
}

function header(text, color = CYAN) {
  nl();
  rule('─', DIM);
  console.log(`${color}${BOLD}  ${text}${RESET}`);
  rule('─', DIM);
  nl();
}

function subheader(text) {
  nl();
  console.log(`${YELLOW}${BOLD}  ${text}${RESET}`);
  nl();
}

function step(num, text) {
  console.log(`  ${CYAN}${BOLD}${num}.${RESET}  ${text}`);
}

function note(text) {
  console.log(`     ${DIM}${text}${RESET}`);
}

function indent(text, spaces = 5) {
  console.log(`${' '.repeat(spaces)}${text}`);
}

function printBox(lines, borderColor = CYAN) {
  const innerW = Math.max(...lines.map(l => stripAnsi(l).length));
  const bar    = '─'.repeat(innerW + 2);
  console.log(`${borderColor}┌${bar}┐${RESET}`);
  for (const line of lines) {
    const pad = ' '.repeat(innerW - stripAnsi(line).length);
    console.log(`${borderColor}│${RESET} ${line}${pad} ${borderColor}│${RESET}`);
  }
  console.log(`${borderColor}└${bar}┘${RESET}`);
}

function sectionBanner(num, total, title) {
  nl();
  const tag = ` Step ${num}/${total} `;
  const body = ` ${title} `;
  console.log(`${MAGENTA}${BOLD}╔${'═'.repeat(tag.length + body.length + 2)}╗${RESET}`);
  console.log(`${MAGENTA}${BOLD}║${CYAN}${tag}${MAGENTA}·${WHITE}${body}${MAGENTA}${BOLD} ║${RESET}`);
  console.log(`${MAGENTA}${BOLD}╚${'═'.repeat(tag.length + body.length + 2)}╝${RESET}`);
  nl();
}

// ─── Credential input ──────────────────────────────────────────────────────────
// Shows what: what this credential IS in plain English
// whereToFind: step-by-step where to get it (multiline string, printed verbatim)
// looksLike: example value for format hint
// validate: fn(val) -> bool
async function getCredential({ label, what, whereToFind, looksLike, validate, existing, required = true }) {
  let value = existing || '';

  for (;;) {
    nl();
    console.log(`${BOLD}${CYAN}  ${label}${RESET}`);
    nl();
    console.log(`  ${what}`);
    if (looksLike) {
      console.log(`  ${DIM}Example: ${looksLike}${RESET}`);
    }
    nl();

    const display = value ? dim(` (current: ${value.slice(0, 6)}...${value.slice(-4)})`) : '';
    value = (await ask(`  ${GREEN}Paste it here${RESET}${display}: `)).trim();

    if (!value && required) {
      console.log(`\n  ${FAIL} ${red('This is required — please paste the value above.')}`);
      continue;
    }
    if (!value && !required) {
      return '';
    }

    if (validate && !validate(value)) {
      nl();
      console.log(`  ${WARN} ${yellow("That doesn't look quite right.")}`);
      if (looksLike) {
        console.log(`  ${DIM}It should look something like: ${looksLike}${RESET}`);
      }
      const tryAgain = await confirm(`  Would you like to try entering it again?`, true);
      if (tryAgain) continue;
    }

    // Confirm what they entered
    nl();
    const preview = value.length > 20
      ? `${value.slice(0, 8)}...${value.slice(-6)}`
      : value;
    console.log(`  ${DIM}You entered:${RESET}  ${CYAN}${BOLD}${preview}${RESET}  ${DIM}(${value.length} characters)${RESET}`);
    nl();
    const correct = await confirm(`  Is that correct?`, true);
    if (!correct) {
      console.log(`\n  ${DIM}No problem — let's try again.${RESET}`);
      value = '';
      continue;
    }

    return value;
  }
}

// ─── Progress ─────────────────────────────────────────────────────────────────
function loadProgress() {
  try {
    if (fs.existsSync(PROGRESS_FILE))
      return JSON.parse(fs.readFileSync(PROGRESS_FILE, 'utf8'));
  } catch { /* ignore */ }
  return { completedSteps: [], config: {} };
}

function saveProgress(state) {
  try {
    fs.writeFileSync(PROGRESS_FILE, JSON.stringify({ ...state, savedAt: new Date().toISOString() }, null, 2));
  } catch (err) {
    console.warn(`${WARN} Could not save progress: ${err.message}`);
  }
}

// ─── Shell helpers ─────────────────────────────────────────────────────────────
function exec(cmd, opts = {}) {
  try { return execSync(cmd, { stdio: 'pipe', ...opts }).toString().trim(); }
  catch { return null; }
}

function openBrowser(url) {
  try {
    if (IS_WIN) exec(`cmd /c start "" "${url}"`);
    else if (IS_MAC) exec(`open "${url}"`);
    else exec(`xdg-open "${url}"`);
  } catch { /* silent */ }
}

function launchInTerminal(cmd, title) {
  try {
    if (IS_WIN) {
      spawn('cmd', ['/c', 'start', title, 'cmd', '/k', cmd], { detached: true, stdio: 'ignore' }).unref();
    } else if (IS_MAC) {
      spawn('osascript', ['-e', `tell application "Terminal" to do script "${cmd.replace(/"/g, '\\"')}"`],
        { detached: true, stdio: 'ignore' }).unref();
    } else {
      for (const term of ['gnome-terminal', 'xterm', 'konsole', 'xfce4-terminal', 'lxterminal']) {
        if (exec(`which ${term} 2>/dev/null`)) {
          const args = term === 'gnome-terminal'
            ? ['--', 'bash', '-c', `${cmd}; read -p "Press Enter to close..."`]
            : ['-e', `bash -c "${cmd.replace(/"/g, '\\"')}; read -p 'Press Enter to close...'"`];
          spawn(term, args, { detached: true, stdio: 'ignore' }).unref();
          return;
        }
      }
      console.log(`${WARN} ${yellow("Couldn't open a terminal automatically.")}`);
      console.log(dim(`  Run manually: ${cmd}`));
    }
  } catch (err) {
    console.log(`${WARN} ${yellow(`Couldn't open a new terminal: ${err.message}`)}`);
    console.log(dim(`  Run manually: ${cmd}`));
  }
}

function trimTrailingSlash(url) {
  return (url || '').trim().replace(/\/+$/, '');
}

function deriveFrontendUrl(apiBaseUrl) {
  const normalized = trimTrailingSlash(apiBaseUrl) || 'http://localhost:5080';

  try {
    const url = new URL(normalized);
    const host = url.hostname.toLowerCase();

    if (host === 'localhost' || host === '127.0.0.1') {
      url.port = '8081';
      return `${url.protocol}//${url.hostname}:${url.port}`;
    }

    if (host.startsWith('api.')) {
      url.hostname = host.slice(4);
      url.port = '';
      return `${url.protocol}//${url.hostname}`;
    }

    if (host.includes('-api.')) {
      url.hostname = host.replace('-api.', '.');
      url.port = '';
      return `${url.protocol}//${url.hostname}`;
    }

    return `${url.protocol}//${url.host}`;
  } catch {
    return normalized;
  }
}

function setEnvVarInContent(content, key, value) {
  const escapedKey = key.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const re = new RegExp(`^${escapedKey}=.*$`, 'm');
  const next = re.test(content)
    ? content.replace(re, `${key}=${value}`)
    : `${content.trimEnd()}\n${key}=${value}\n`;

  return next.endsWith('\n') ? next : `${next}\n`;
}

// ─── Config writers ────────────────────────────────────────────────────────────
function readEnvPairs(filePath) {
  const out = {};
  try {
    for (const line of fs.readFileSync(filePath, 'utf8').split('\n')) {
      const t = line.trim();
      if (!t || t.startsWith('#')) continue;
      const eq = t.indexOf('=');
      if (eq === -1) continue;
      out[t.slice(0, eq)] = t.slice(eq + 1);
    }
  } catch { /* file missing */ }
  return out;
}

function writeEnvFile(filePath, vars) {
  const lines = [
    '# NomNomzBot Environment Variables',
    `# Generated by setup.mjs on ${new Date().toLocaleDateString()}`,
    '# Do NOT commit this file to source control — it contains secrets.',
    '',
  ];
  for (const [k, v] of Object.entries(vars)) lines.push(`${k}=${v}`);
  fs.writeFileSync(filePath, lines.join('\n') + '\n');
}

function mergeEnvFile(filePath, updates) {
  let content = '';
  try { content = fs.readFileSync(filePath, 'utf8'); } catch { content = ''; }
  for (const [key, val] of Object.entries(updates)) {
    if (val === undefined || val === null || val === '') continue;
    const re = new RegExp(`^(${key}=).*$`, 'm');
    content = re.test(content)
      ? content.replace(re, `$1${val}`)
      : content.trimEnd() + `\n${key}=${val}\n`;
  }
  fs.writeFileSync(filePath, content);
}

function deepMerge(target, source) {
  const out = { ...target };
  for (const [k, v] of Object.entries(source)) {
    out[k] = (v && typeof v === 'object' && !Array.isArray(v))
      ? deepMerge(out[k] || {}, v) : v;
  }
  return out;
}

function mergeJsonFile(filePath, patch) {
  let existing = {};
  try { if (fs.existsSync(filePath)) existing = JSON.parse(fs.readFileSync(filePath, 'utf8')); }
  catch { /* start fresh */ }
  fs.writeFileSync(filePath, JSON.stringify(deepMerge(existing, patch), null, 2) + '\n');
}

function generateSecret(bytes = 32) {
  return crypto.randomBytes(bytes).toString('base64');
}

// ─── Security key generation ──────────────────────────────────────────────────
function generateSecurityKeys() {
  return {
    jwtSecret:     crypto.randomBytes(64).toString('base64'),   // 64 bytes, base64
    encryptionKey: crypto.randomBytes(32).toString('base64'),   // 32 bytes, AES-256
    pgPassword:    crypto.randomBytes(32).toString('hex'),      // 64 hex chars, no special chars
    redisPassword: crypto.randomBytes(32).toString('hex'),      // 64 hex chars
  };
}

// Read existing security keys from a .env file so we can preserve them on re-run
function readExistingSecrets(filePath) {
  const pairs = {};
  try {
    for (const line of fs.readFileSync(filePath, 'utf8').split('\n')) {
      const t = line.trim();
      if (!t || t.startsWith('#')) continue;
      const eq = t.indexOf('=');
      if (eq === -1) continue;
      pairs[t.slice(0, eq)] = t.slice(eq + 1);
    }
  } catch { /* file missing */ }
  return {
    jwtSecret:     pairs['JWT_SECRET']      || null,
    encryptionKey: pairs['ENCRYPTION_KEY']  || null,
    pgPassword:    pairs['POSTGRES_PASSWORD'] || null,
    redisPassword: pairs['REDIS_PASSWORD']  || null,
  };
}

// ─── Clipboard ────────────────────────────────────────────────────────────────
async function copyToClipboard(text) {
  try {
    if (IS_WIN) {
      // PowerShell is more reliable than clip for special chars
      execSync(`powershell -command "Set-Clipboard -Value '${text.replace(/'/g, "''")}'"`, { stdio: 'pipe' });
      return true;
    } else if (IS_MAC) {
      execSync(`printf '%s' '${text.replace(/'/g, "'\\''")}' | pbcopy`, { stdio: 'pipe', shell: true });
      return true;
    } else {
      if (exec('which xclip')) {
        execSync(`printf '%s' '${text.replace(/'/g, "'\\''")}' | xclip -selection clipboard`, { stdio: 'pipe', shell: true });
        return true;
      }
      if (exec('which xsel')) {
        execSync(`printf '%s' '${text.replace(/'/g, "'\\''")}' | xsel --clipboard --input`, { stdio: 'pipe', shell: true });
        return true;
      }
      return false; // no clipboard tool available
    }
  } catch {
    return false;
  }
}

// ══════════════════════════════════════════════════════════════════════════════
// STEP 1 — Prerequisites
// ══════════════════════════════════════════════════════════════════════════════
async function stepPrerequisites() {
  console.log('Before we start, let\'s make sure your computer has everything installed.');
  console.log('This will only take a moment.');
  nl();

  const checks = [
    {
      name: 'Docker Desktop',
      versionCmd: 'docker --version',
      runningCmd: 'docker info --format "ok"',
      minMajor: null,
      url: 'https://docs.docker.com/get-docker/',
      required: true,
      notRunningMsg: [
        `Docker is installed but ${bold('not running')}.`,
        `Please open Docker Desktop and wait for the whale icon to stop animating,`,
        `then run this setup script again.`,
      ].join('\n  '),
    },
    {
      name: '.NET 10 SDK',
      versionCmd: 'dotnet --version',
      minMajor: 10,
      url: 'https://dotnet.microsoft.com/download/dotnet/10.0',
      required: true,
    },
    {
      name: 'Node.js 20+',
      versionCmd: 'node --version',
      minMajor: 20,
      url: 'https://nodejs.org/',
      required: true,
    },
    {
      name: 'Yarn',
      versionCmd: 'yarn --version',
      fallbackName: 'npm',
      fallbackCmd: 'npm --version',
      url: 'https://classic.yarnpkg.com/lang/en/docs/install/',
      required: false,
    },
  ];

  let allOk = true;
  let pkgMgr = 'npm';
  const missing = [];

  for (const chk of checks) {
    const s = spin(`Checking ${chk.name}...`).start();
    const ver = exec(chk.versionCmd);

    if (!ver) {
      if (chk.fallbackCmd) {
        const fb = exec(chk.fallbackCmd);
        if (fb) {
          s.succeed(`${chk.fallbackName} ${fb}  ${dim('(Yarn not found — npm will be used instead)')}`);
          pkgMgr = chk.fallbackName;
          continue;
        }
      }
      s.fail(`${chk.name} — ${red('NOT FOUND')}`);
      missing.push(chk);
      if (chk.required) allOk = false;
      continue;
    }

    if (chk.runningCmd && !exec(chk.runningCmd)) {
      s.fail(`Docker is installed but ${red('not running')}`);
      missing.push({ ...chk, reason: 'not-running' });
      if (chk.required) allOk = false;
      continue;
    }

    if (chk.minMajor) {
      const major = parseInt(ver.replace(/^v/, '').split('.')[0], 10);
      if (major < chk.minMajor) {
        s.fail(`${chk.name}: ${ver} — ${red(`version ${chk.minMajor}+ required`)}`);
        missing.push(chk);
        if (chk.required) allOk = false;
        continue;
      }
    }

    if (chk.name === 'Yarn') pkgMgr = 'yarn';
    s.succeed(`${chk.name}: ${green(ver)}`);
  }

  if (!allOk) {
    nl();
    rule('─', RED);
    console.log(`${FAIL} ${red(bold('Some required tools are missing.'))}`);
    rule('─', RED);
    nl();

    for (const chk of missing) {
      if (chk.reason === 'not-running') {
        console.log(`  ${WARN} ${bold('Docker Desktop')} is installed but not running.`);
        console.log(`     Open Docker Desktop from your applications menu.`);
        console.log(`     Wait until the whale icon in your system tray stops moving.`);
      } else {
        console.log(`  ${FAIL} ${bold(chk.name)} needs to be installed.`);
        console.log(`     Download it from: ${cyan(bold(chk.url))}`);
      }
      nl();
    }

    const cont = await confirm('Continue anyway? (things may not work)', false);
    if (!cont) {
      nl();
      console.log(`Install the missing tools above, then run ${bold('node setup.mjs')} again.`);
      nl();
      process.exit(1);
    }
  } else {
    nl();
    console.log(`${OK} ${bold(green('All tools are installed and ready.'))}`);
  }

  return { pkgMgr };
}

// ══════════════════════════════════════════════════════════════════════════════
// STEP 2 — Twitch Application
// ══════════════════════════════════════════════════════════════════════════════
async function stepTwitch(config) {

  // ── Intro ──────────────────────────────────────────────────────────────────
  printBox([
    bold('  What is a Twitch Application?  '),
    '',
    `  NomNomzBot needs to be registered with Twitch so that Twitch`,
    `  knows your bot is legitimate and has permission to connect to`,
    `  your channel.`,
    '',
    `  Think of it like getting an ID badge for your bot at Twitch HQ.`,
    '',
    `  You will create a free "Application" in the Twitch Developer`,
    `  Console and get two secret codes:`,
    '',
    `    ${bold('Client ID')}      — like a username for your bot`,
    `    ${bold('Client Secret')}  — like a password for your bot`,
    '',
    `  ${DIM}This takes about 3–5 minutes the first time.${RESET}`,
  ], CYAN);

  nl();
  const skip = await confirm('Skip Twitch setup for now?', false);
  if (skip) {
    console.log(dim('\n  Skipped. Set credentials manually in server/.env later.'));
    return { skipped: true };
  }

  // ── Ask for base URL ───────────────────────────────────────────────────────
  header('Quick question before we start', YELLOW);

  console.log(`  The bot needs to know what URL it\'s running at so Twitch`);
  console.log(`  knows where to send login callbacks.`);
  nl();
  console.log(`  ${bold('For most people:')} use the shared dev tunnel (already works with the pre-filled credentials):`);
  nl();
  indent(`${CYAN}https://bot-dev-api.nomercy.tv${RESET}`, 5);
  nl();
  console.log(`  ${DIM}Or use localhost:5080 if you have your own Cloudflare tunnel pointing there.`);
  console.log(`  When api.nomnomz.bot is fully configured it will replace bot-dev-api.nomercy.tv.${RESET}`);
  nl();

  let baseUrl = config.baseUrl || 'https://bot-dev-api.nomercy.tv';
  const baseAnswer = (await ask(
    `  ${GREEN}Your API base URL${RESET} ${dim(`(default: ${baseUrl})`)} `,
  )).trim();
  if (baseAnswer) baseUrl = baseAnswer.replace(/\/+$/, ''); // strip trailing slash

  // All Twitch OAuth flows now share the same callback URL.
  const redirectUri = `${baseUrl}/api/v1/auth/twitch/callback`;

  nl();
  console.log(`${OK} Using base URL: ${cyan(bold(baseUrl))}`);
  console.log(`${INFO} Register this callback URL in Twitch: ${cyan(redirectUri)}`);

  // ── Part 1: Open the Twitch Dev Console ───────────────────────────────────
  header('Part 1 of 3 — Sign in to the Twitch Developer Console', YELLOW);

  step(1, 'We\'re opening the Twitch Developer Console in your browser.');
  nl();
  indent(`${CYAN}${BOLD}  https://dev.twitch.tv/console/apps${RESET}`, 5);
  nl();

  openBrowser('https://dev.twitch.tv/console/apps');
  console.log(`  ${OK} Browser opened.`);
  nl();

  step(2, 'Sign in with your regular Twitch account if prompted.');
  note('This is your streamer account — NOT the bot account.');
  nl();
  step(3, `You should see a page titled ${bold('"Applications"')} with a`);
  indent(`${bold('"Register Your Application"')} button.`);

  await pressEnter('\n  Press Enter once you can see the Applications page...');

  // ── Part 2: Fill in the form ───────────────────────────────────────────────
  header('Part 2 of 3 — Create the Application', YELLOW);

  step(1, `Click ${bold(cyan('"Register Your Application"'))}.`);
  nl();
  step(2, `Fill in the form:`);
  nl();

  printBox([
    `  ${bold('Name:')}      Anything you like — e.g. ${cyan('"My NomNomzBot"')}`,
    `              ${DIM}(Just a label for your own reference)${RESET}`,
    '',
    `  ${bold('Category:')}  Select ${bold('"Chat Bot"')} from the dropdown`,
  ], CYAN);

  nl();
  step(3, `${bold('OAuth Redirect URL')} — you only need ${bold('1 URL')} now.`);
  nl();
  console.log(`  ${DIM}Streamer login, bot login, and per-channel bot auth all use the same callback now.${RESET}`);
  console.log(`  ${DIM}We\'ll copy it to your clipboard for you.${RESET}`);
  nl();

  const copied = await copyToClipboard(redirectUri);

  if (copied) {
    console.log(`  ${OK} ${bold(green('Copied to your clipboard!'))}`);
  } else {
    console.log(`  ${WARN} ${yellow("Couldn\'t copy automatically.")} Please copy it manually:`);
  }

  nl();
  printBox([`  ${redirectUri}  `], CYAN);
  nl();
  console.log(`  ${dim('In the Twitch form:')}`);
  step(4, `${copied ? 'Paste' : 'Type'} the URL above into the ${bold('"OAuth Redirect URLs"')} field.`);
  step(5, `Click ${bold('"Add"')} if Twitch shows an add button, then continue with the form.`);
  nl();
  rule('·', DIM);
  nl();
  step(6, `Check the ${bold('"I\'m not a robot"')} box if it appears.`);
  nl();
  step(7, `Click the ${bold(cyan('"Create"'))} button at the bottom.`);

  await pressEnter('\n  Press Enter once you have clicked "Create"...');

  // ── Part 3: Get credentials ────────────────────────────────────────────────
  header('Part 3 of 3 — Copy Your Credentials', YELLOW);

  step(1, `Find your new app in the list and click ${bold(cyan('"Manage"'))}.`);
  nl();
  step(2, `You\'ll land on your app\'s settings page.`);

  await pressEnter('\n  Press Enter once you are on your app\'s detail page...');

  // ── Get Client ID ──────────────────────────────────────────────────────────
  header('Getting Your Client ID', CYAN);

  console.log(`  On the app settings page, look for ${bold('"Client ID"')}.`);
  console.log(`  You\'ll see a long string of letters and numbers.`);
  nl();
  console.log(`  ${DIM}Example of what it looks like:${RESET}`);
  indent(`${DIM}abc123def456ghi789jkl012mnop34${RESET}`);
  console.log(`  ${DIM}(about 30 lowercase letters and numbers)${RESET}`);

  const clientId = await getCredential({
    label: 'Twitch Client ID',
    what: 'The Client ID identifies your bot application to Twitch.',
    looksLike: 'abc123def456ghi789jkl012mnop34',
    validate: v => /^[a-z0-9]{15,}$/i.test(v),
    existing: config.twitch?.clientId,
  });

  // ── Get Client Secret ──────────────────────────────────────────────────────
  header('Getting Your Client Secret', CYAN);

  console.log(`  The Client Secret is ${bold('not visible by default')} — you must generate it.`);
  nl();
  step(1, `On the same page, scroll down to find ${bold('"Client Secret"')}.`);
  step(2, `Click ${bold(cyan('"New Secret"'))}.`);
  step(3, `A confirmation may appear — click ${bold('"OK"')} or ${bold('"Continue"')}.`);
  step(4, `${bold(red('Copy the secret immediately'))} — Twitch only shows it once.`);
  note(`(If you forget to copy it, you can always click "New Secret" again.)`);
  nl();
  console.log(`  ${DIM}It looks like:${RESET}`);
  indent(`${DIM}abc123def456ghi789jkl012mnop34${RESET}`);
  console.log(`  ${DIM}(similar format to the Client ID)${RESET}`);

  const clientSecret = await getCredential({
    label: 'Twitch Client Secret',
    what: 'The Client Secret is like a password for your app. Keep it private!',
    looksLike: 'abc123def456ghi789jkl012mnop34',
    validate: v => /^[a-z0-9]{15,}$/i.test(v),
    existing: config.twitch?.clientSecret,
  });

  nl();
  console.log(`${OK} ${bold(green('Twitch credentials saved!'))}`);
  return { clientId, clientSecret, baseUrl, redirectUri };
}

// ══════════════════════════════════════════════════════════════════════════════
// STEP 3 — Bot Account
// ══════════════════════════════════════════════════════════════════════════════
async function stepBotAccount(config) {

  printBox([
    bold('  What is the Bot Account?  '),
    '',
    `  NomNomzBot can use a ${bold('separate Twitch account')} as the "bot".`,
    `  This is the account that will appear in your chat and send`,
    `  messages — like "${cyan('MyChannelBot')}: Hello everyone!"`,
    '',
    `  ${bold('Why a separate account?')}`,
    `  If you use your main account, viewers can\'t tell apart your`,
    `  personal messages from automated bot messages. A separate`,
    `  account makes everything look clean and professional.`,
    '',
    `  ${bold('Don\'t have one yet?')} No problem — you can:`,
    `    • Skip this and use your main account for now`,
    `    • Create a free Twitch account at twitch.tv later`,
    `    • Set it up anytime in the app Settings page`,
  ], CYAN);

  nl();
  const has = await confirm('Do you have a separate bot Twitch account set up?', true);

  if (!has) {
    nl();
    console.log(`  ${INFO} No problem! You can set this up later.`);
    console.log(`  ${DIM}After first login, go to Settings → Bot Account to connect one.${RESET}`);
    return { skipped: true };
  }

  nl();
  console.log(`  ${bold('What is your bot account\'s Twitch username?')}`);
  console.log(`  ${DIM}This is the exact username of the bot\'s Twitch account.${RESET}`);
  console.log(`  ${DIM}Example: MyChannelBot  or  StreamerBot_  or  NomNomzBot_${RESET}`);

  let username = '';
  for (;;) {
    nl();
    username = (await ask(`  ${GREEN}Bot username${RESET}${config.botAccount?.username ? dim(` (current: ${config.botAccount.username})`) : ''}: `)).trim()
      || config.botAccount?.username || '';

    if (!username) {
      console.log(`\n  ${DIM}No username entered — skipping bot account setup.${RESET}`);
      return { skipped: true };
    }

    if (/\s/.test(username)) {
      console.log(`  ${WARN} ${yellow("Twitch usernames can't contain spaces.")}`);
      continue;
    }

    nl();
    console.log(`  ${DIM}You entered:${RESET}  ${CYAN}${BOLD}${username}${RESET}`);
    const ok = await confirm('  Is that the correct username?', true);
    if (ok) break;
  }

  nl();
  console.log(`${OK} ${bold(green(`Bot account username set to: ${cyan(username)}`))} `);
  return { username };
}

// ══════════════════════════════════════════════════════════════════════════════
// STEP 4 — Spotify
// ══════════════════════════════════════════════════════════════════════════════
async function stepSpotify(config) {

  printBox([
    bold('  Spotify Integration  ') + DIM + '(optional)' + RESET,
    '',
    `  Connecting Spotify allows your viewers to:`,
    `    • See what song is currently playing on stream`,
    `    • Request songs with the ${cyan('!sr')} or ${cyan('!songrequest')} command`,
    `    • Control your music with chat commands`,
    `    • See a "Now Playing" widget in your overlays`,
    '',
    `  ${DIM}You need a free or premium Spotify account to use this.${RESET}`,
  ], CYAN);

  nl();
  const want = await confirm('Would you like to set up Spotify integration?', false);
  if (!want) {
    console.log(dim('\n  Skipped. You can enable Spotify later in the app Settings.'));
    return { enabled: false };
  }

  const baseUrl = trimTrailingSlash(config?.twitch?.baseUrl || 'http://localhost:5080');
  const spotifyRedirectUri = `${baseUrl}/api/v1/integrations/spotify/callback`;

  // ── Part 1: Create the Spotify app ────────────────────────────────────────
  header('Part 1 of 2 — Create a Spotify App', YELLOW);

  step(1, 'Open the Spotify Developer Dashboard:');
  nl();
  indent(`${CYAN}${BOLD}  https://developer.spotify.com/dashboard${RESET}`, 5);
  nl();

  const opened = await confirm('  Open it now?', true);
  if (opened) openBrowser('https://developer.spotify.com/dashboard');

  nl();
  step(2, 'Sign in with your Spotify account.');
  nl();
  step(3, `Click ${bold(cyan('"Create App"'))} (top right corner).`);
  nl();
  step(4, `Fill in the form:`);
  nl();

  printBox([
    `  ${bold('App name:')}        Anything you like, e.g. ${cyan('"NomNomzBot"')}`,
    `  ${bold('App description:')} "Twitch bot music integration" or similar`,
    `  ${bold('Redirect URI:')}    Click ${bold('"Add"')} and paste this exact URL:`,
    '',
    `    ${green('→')}  ${spotifyRedirectUri}`,
    '',
    `  ${bold('APIs used:')}       Check ${bold('"Web API"')}`,
  ], CYAN);

  nl();
  step(5, `Check the Terms of Service box and click ${bold(cyan('"Save"'))}.`);

  await pressEnter('\n  Press Enter once you have created your Spotify app...');

  // ── Part 2: Get credentials ────────────────────────────────────────────────
  header('Part 2 of 2 — Copy Your Spotify Credentials', YELLOW);

  step(1, `On your app\'s page, click ${bold('"Settings"')} (top right).`);
  nl();
  step(2, `You will see your ${bold('Client ID')} near the top.`);
  nl();
  console.log(`  ${DIM}The Client ID looks like:${RESET}`);
  indent(`${DIM}4c01e10681b24fc8b18a2f9a1f7bdbfb${RESET}`);
  console.log(`  ${DIM}(32 lowercase letters and numbers)${RESET}`);

  const clientId = await getCredential({
    label: 'Spotify Client ID',
    what: 'This tells NomNomzBot which Spotify app to connect to.',
    looksLike: '4c01e10681b24fc8b18a2f9a1f7bdbfb',
    validate: v => /^[a-z0-9]{20,}$/i.test(v),
    existing: config.spotify?.clientId,
  });

  nl();
  step(3, `Click ${bold(cyan('"View client secret"'))} to reveal your Client Secret.`);
  nl();
  console.log(`  ${DIM}The Client Secret looks like:${RESET}`);
  indent(`${DIM}6ee29fb8093046aeaecebaa6f4ba3d3b${RESET}`);
  console.log(`  ${DIM}(same format as the Client ID)${RESET}`);

  const clientSecret = await getCredential({
    label: 'Spotify Client Secret',
    what: 'Like a password — keep this private and don\'t share it.',
    looksLike: '6ee29fb8093046aeaecebaa6f4ba3d3b',
    validate: v => /^[a-z0-9]{20,}$/i.test(v),
    existing: config.spotify?.clientSecret,
  });

  nl();
  console.log(`${OK} ${bold(green('Spotify credentials saved!'))}`);
  return { enabled: true, clientId, clientSecret };
}

// ══════════════════════════════════════════════════════════════════════════════
// STEP 5 — Discord
// ══════════════════════════════════════════════════════════════════════════════
async function stepDiscord(config) {

  printBox([
    bold('  Discord Integration  ') + DIM + '(optional)' + RESET,
    '',
    `  Connecting Discord allows:`,
    `    • Posting stream-start announcements to your Discord server`,
    `    • Viewers logging in to the dashboard with their Discord account`,
    `    • Cross-posting highlight clips to a Discord channel`,
    '',
    `  ${DIM}You need a Discord account and a server to use this.${RESET}`,
  ], CYAN);

  nl();
  const want = await confirm('Would you like to set up Discord integration?', false);
  if (!want) {
    console.log(dim('\n  Skipped. You can enable Discord later in the app Settings.'));
    return { enabled: false };
  }

  const baseUrl = trimTrailingSlash(config?.twitch?.baseUrl || 'http://localhost:5080');
  const discordRedirectUri = `${baseUrl}/api/v1/integrations/discord/callback`;

  // ── Part 1: Create Discord application ────────────────────────────────────
  header('Part 1 of 2 — Create a Discord Application', YELLOW);

  step(1, 'Open the Discord Developer Portal:');
  nl();
  indent(`${CYAN}${BOLD}  https://discord.com/developers/applications${RESET}`, 5);
  nl();

  const opened = await confirm('  Open it now?', true);
  if (opened) openBrowser('https://discord.com/developers/applications');

  nl();
  step(2, 'Log in with your Discord account if prompted.');
  nl();
  step(3, `Click ${bold(cyan('"New Application"'))} in the top right corner.`);
  nl();
  step(4, `Give it any name (e.g. ${cyan('"NomNomzBot"')}) and click ${bold('"Create"')}.`);
  nl();
  step(5, `In the left sidebar, click ${bold('"OAuth2"')}.`);
  nl();
  step(6, `Under ${bold('"Redirects"')}, click ${bold('"Add Redirect"')} and paste this URL:`);
  nl();

  printBox([
    `  ${green('→')}  ${discordRedirectUri}`,
  ], CYAN);

  nl();
  step(7, `Click ${bold(cyan('"Save Changes"'))} at the bottom.`);

  await pressEnter('\n  Press Enter once you have saved your Discord app...');

  // ── Part 2: Get credentials ────────────────────────────────────────────────
  header('Part 2 of 2 — Copy Your Discord Credentials', YELLOW);

  console.log(`  You should still be on the ${bold('"OAuth2"')} page.`);
  nl();
  step(1, `Your ${bold('Client ID')} is shown near the top of the OAuth2 page.`);
  nl();
  console.log(`  ${DIM}It looks like a long number:${RESET}`);
  indent(`${DIM}952230846465728553${RESET}`);
  console.log(`  ${DIM}(17–19 digits, no letters)${RESET}`);

  const clientId = await getCredential({
    label: 'Discord Client ID',
    what: 'This is a number that identifies your Discord application.',
    looksLike: '952230846465728553',
    validate: v => /^\d{15,}$/.test(v),
    existing: config.discord?.clientId,
  });

  nl();
  step(2, `Click ${bold(cyan('"Reset Secret"'))} (or ${bold('"Copy"')} if a secret is already shown).`);
  step(3, `If asked to confirm, click ${bold('"Yes, do it!"')}.`);
  step(4, `Copy the secret that appears.`);
  nl();
  console.log(`  ${WARN} ${yellow('Important:')} The Client Secret is different from a "Bot Token".`);
  console.log(`  ${DIM}Make sure you\'re on the OAuth2 page, not the Bot page.${RESET}`);
  nl();
  console.log(`  ${DIM}It looks like:${RESET}`);
  indent(`${DIM}YsV4pbm379BG2HZPUG2qln1By0J_DLTy${RESET}`);
  console.log(`  ${DIM}(32 letters, numbers, and underscores/dashes)${RESET}`);

  const clientSecret = await getCredential({
    label: 'Discord Client Secret',
    what: 'This is the OAuth2 secret (NOT the bot token). Keep it private!',
    looksLike: 'YsV4pbm379BG2HZPUG2qln1By0J_DLTy',
    validate: v => v.length >= 20,
    existing: config.discord?.clientSecret,
  });

  nl();
  console.log(`${OK} ${bold(green('Discord credentials saved!'))}`);
  return { enabled: true, clientId, clientSecret };
}

// ══════════════════════════════════════════════════════════════════════════════
// STEP 6 — Cloudflare Tunnel (optional)
// ══════════════════════════════════════════════════════════════════════════════
async function stepCloudflare(config) {
  printBox([
    bold('  Cloudflare Tunnel Token  ') + DIM + '(optional)' + RESET,
    '',
    `  A Cloudflare Tunnel lets your bot be reachable from the internet`,
    `  with a real HTTPS URL — required for Twitch OAuth to work with`,
    `  your own credentials (not the shared dev tunnel).`,
    '',
    `  ${bold('When do you need this?')}`,
    `    • You created your own Twitch app with custom redirect URLs`,
    `    • You want your bot accessible outside your home network`,
    `    • You\'re running this on a server without a domain name yet`,
    '',
    `  ${bold('When can you skip this?')}`,
    `    • You\'re using the pre-filled dev credentials (they use a`,
    `      shared tunnel at bot-dev-api.nomercy.tv that already works)`,
    `    • You already have a domain name pointing at your server`,
    '',
    `  ${DIM}You can always add a token later by editing server/.env${RESET}`,
  ], CYAN);

  nl();
  const want = await confirm('Do you have a Cloudflare Tunnel token to set up?', false);
  if (!want) {
    console.log(dim('\n  Skipped. Add CLOUDFLARE_TUNNEL_TOKEN to your .env file later if needed.'));
    return { enabled: false };
  }

  nl();
  console.log(`  ${bold('How to get a Cloudflare Tunnel token:')}`);
  nl();
  step(1, `Go to: ${cyan(bold('https://one.dash.cloudflare.com/'))} → ${bold('Networks → Tunnels')}`);
  step(2, `Click ${bold(cyan('"Create a tunnel"'))}, choose ${bold('"Cloudflared"')}`);
  step(3, `Give it a name (e.g. ${cyan('"nomnomzbot-local"')}) and click ${bold('"Save tunnel"')}`);
  step(4, `Copy the token shown in the ${bold('"Install and run a connector"')} section`);
  note('It looks like: eyJhIjoiYWJ...(very long base64 string)');

  const token = await getCredential({
    label: 'Cloudflare Tunnel Token',
    what: 'This lets your bot be reachable via a secure HTTPS tunnel.',
    looksLike: 'eyJhIjoiYWJj...(long base64 string)',
    validate: v => v.length >= 50,
    existing: config.cloudflare?.token,
    required: false,
  });

  if (!token) {
    console.log(dim('\n  No token entered — Cloudflare skipped.'));
    return { enabled: false };
  }

  nl();
  console.log(`${OK} ${bold(green('Cloudflare Tunnel token saved!'))}`);
  return { enabled: true, token };
}

// ══════════════════════════════════════════════════════════════════════════════
// STEP 7 — Generate config files
// ══════════════════════════════════════════════════════════════════════════════
async function stepGenerateConfigs(config) {
  console.log('Writing your credentials into the configuration files...');
  nl();

  await writeBackendDotEnv(config);
  writeAppsettingsDev(config);
  writeFrontendEnv(config);
}

async function writeBackendDotEnv(config) {
  const { twitch, botAccount, spotify, discord, cloudflare } = config;
  const base = (twitch?.baseUrl || 'http://localhost:5080').replace(/\/+$/, '');
  const exists = fs.existsSync(BACKEND_ENV);

  // ── Existing file: ask what to do ─────────────────────────────────────────
  if (exists) {
    nl();
    console.log(`  ${WARN} ${bold('server/.env')} already exists from a previous setup.`);
    const choice = await ask(
      `  ${DIM}What would you like to do?${RESET}\n` +
      `    ${bold('m')} = Merge  (keep your existing secrets, update credentials only)\n` +
      `    ${bold('o')} = Overwrite  (generate fresh secrets, replace everything)\n` +
      `    ${bold('s')} = Skip  (leave the file exactly as-is)\n` +
      `\n  Your choice ${dim('[m]')}: `
    );
    const c = choice.toLowerCase() || 'm';

    if (c === 's') {
      console.log(dim('  Skipped — .env left unchanged.'));
      return;
    }

    if (c === 'm') {
      // Preserve existing secrets — only update user-provided credentials
      mergeEnvFile(BACKEND_ENV, {
        API_BASE_URL:                    base,
        TWITCH_CLIENT_ID:                twitch?.clientId        || undefined,
        TWITCH_CLIENT_SECRET:            twitch?.clientSecret    || undefined,
        TWITCH_BOT_USERNAME:             botAccount?.username    || undefined,
        SPOTIFY_CLIENT_ID:               spotify?.clientId       || undefined,
        SPOTIFY_CLIENT_SECRET:           spotify?.clientSecret   || undefined,
        DISCORD_CLIENT_ID:               discord?.clientId       || undefined,
        DISCORD_CLIENT_SECRET:           discord?.clientSecret   || undefined,
        CLOUDFLARE_TUNNEL_TOKEN:         cloudflare?.token       || undefined,
      });
      console.log(`  ${OK} server/.env — credentials merged in (existing secrets preserved)`);
      return;
    }
    // fall through → overwrite
  }

  // ── Generate fresh security keys ──────────────────────────────────────────
  nl();
  console.log(`${BOLD}${CYAN}  🔐 Generating security keys...${RESET}`);
  nl();

  const s1 = spin('Generating JWT Secret (64 bytes)...').start();
  const jwtSecret = crypto.randomBytes(64).toString('base64');
  await new Promise(r => setTimeout(r, 120)); // tiny pause so users can read it
  s1.succeed('JWT Secret generated  (64 bytes, base64)');

  const s2 = spin('Generating Encryption Key (32 bytes, AES-256)...').start();
  const encryptionKey = crypto.randomBytes(32).toString('base64');
  await new Promise(r => setTimeout(r, 120));
  s2.succeed('Encryption Key generated  (32 bytes, AES-256)');

  const s3 = spin('Generating PostgreSQL password...').start();
  const pgPassword = crypto.randomBytes(32).toString('hex');
  await new Promise(r => setTimeout(r, 120));
  s3.succeed('PostgreSQL password generated');

  const s4 = spin('Generating Redis password...').start();
  const redisPassword = crypto.randomBytes(32).toString('hex');
  await new Promise(r => setTimeout(r, 120));
  s4.succeed('Redis password generated');

  nl();
  printBox([
    `  ${OK} ${bold('All keys are cryptographically random and unique to this installation.')}`,
    `  ${DIM}They\'ve been saved to your .env file. Never share them.${RESET}`,
  ], GREEN);

  // ── Write structured .env ─────────────────────────────────────────────────
  const dbUrl = `Host=postgres;Port=5432;Database=nomnomzbot;Username=nomnomzbot;Password=${pgPassword}`;
  const redisUrl = `redis://:${redisPassword}@redis:6379`;

  const sections = [
    {
      comment: '─── Auto-generated security keys (DO NOT SHARE) ──────────────────────────────',
      vars: {
        JWT_SECRET:       jwtSecret,
        ENCRYPTION_KEY:   encryptionKey,
        POSTGRES_PASSWORD: pgPassword,
        REDIS_PASSWORD:   redisPassword,
      },
    },
    {
      comment: '─── Twitch (required) ────────────────────────────────────────────────────────',
      vars: {
        TWITCH_CLIENT_ID:      twitch?.clientId      || '',
        TWITCH_CLIENT_SECRET:  twitch?.clientSecret  || '',
        TWITCH_BOT_USERNAME:   botAccount?.username  || '',
      },
    },
    {
      comment: '─── Database (auto-configured) ───────────────────────────────────────────────',
      vars: {
        POSTGRES_USER: 'nomnomzbot',
        POSTGRES_DB:   'nomnomzbot',
        DATABASE_URL:  dbUrl,
      },
    },
    {
      comment: '─── Redis (auto-configured) ──────────────────────────────────────────────────',
      vars: {
        REDIS_URL: redisUrl,
        REDIS_CONNECTION_STRING: `redis:6379`,  // used by Docker service name resolution
      },
    },
    {
      comment: '─── API URLs ─────────────────────────────────────────────────────────────────',
      vars: {
        API_BASE_URL:   base,
        FRONTEND_URL:   'http://localhost:8081',
        JWT_ISSUER:     'nomnomzbot',
        JWT_AUDIENCE:   'nomnomzbot',
      },
    },
    {
      comment: '─── Optional integrations ────────────────────────────────────────────────────',
      vars: {
        SPOTIFY_CLIENT_ID:     spotify?.clientId      || '',
        SPOTIFY_CLIENT_SECRET: spotify?.clientSecret  || '',
        DISCORD_CLIENT_ID:     discord?.clientId      || '',
        DISCORD_CLIENT_SECRET: discord?.clientSecret  || '',
      },
    },
    {
      comment: '─── Optional: Cloudflare Tunnel ──────────────────────────────────────────────',
      vars: {
        CLOUDFLARE_TUNNEL_TOKEN: cloudflare?.token || '',
      },
    },
    {
      comment: '─── Deployment ───────────────────────────────────────────────────────────────',
      vars: {
        DEPLOYMENT_MODE: 'self-hosted',
      },
    },
  ];

  const lines = [
    '# NomNomzBot Environment Variables',
    `# Generated by setup.mjs on ${new Date().toLocaleDateString()}`,
    '# Do NOT commit this file to source control — it contains your secrets.',
    '',
  ];
  for (const section of sections) {
    lines.push(`# ${section.comment}`);
    for (const [k, v] of Object.entries(section.vars)) {
      lines.push(`${k}=${v}`);
    }
    lines.push('');
  }
  fs.writeFileSync(BACKEND_ENV, lines.join('\n'));

  nl();
  console.log(`  ${OK} server/.env — ${exists ? 'overwritten with fresh secrets' : 'created'}`);
}

function writeAppsettingsDev(config) {
  const { twitch, botAccount } = config;
  const baseUrl = (twitch?.baseUrl || 'http://localhost:5080').replace(/\/+$/, '');
  const patch = {};

  if (twitch?.clientId || twitch?.clientSecret || botAccount?.username) {
    patch.Twitch = {};
    if (twitch?.clientId)      patch.Twitch.ClientId     = twitch.clientId;
    if (twitch?.clientSecret)  patch.Twitch.ClientSecret = twitch.clientSecret;
    if (botAccount?.username)  patch.Twitch.BotUsername  = botAccount.username;
  }

  patch.App = { BaseUrl: baseUrl };

  if (Object.keys(patch).length <= 1) {
    console.log(dim('  appsettings.Development.json — no credential changes needed'));
    return;
  }

  mergeJsonFile(APPSETTINGS_DEV, patch);
  console.log(`  ${OK} server/src/NomNomzBot.Api/appsettings.Development.json — updated`);
}

function writeFrontendEnv(config) {
  const base = trimTrailingSlash(config?.twitch?.baseUrl || 'http://localhost:5080') || 'http://localhost:5080';

  let content = '';
  try {
    if (fs.existsSync(FRONTEND_ENV))
      content = fs.readFileSync(FRONTEND_ENV, 'utf8');
  } catch {
    content = '';
  }

  content = setEnvVarInContent(content, 'EXPO_PUBLIC_API_URL', base);

  if (!/^EXPO_PUBLIC_PROJECT_ID=/m.test(content))
    content = setEnvVarInContent(content, 'EXPO_PUBLIC_PROJECT_ID', '');

  fs.writeFileSync(FRONTEND_ENV, content);
  console.log(`  ${OK} app/.env.development — API URL set to ${base}`);
}

// ══════════════════════════════════════════════════════════════════════════════
// STEP 7 — Start services
// ══════════════════════════════════════════════════════════════════════════════
async function stepStartServices(pkgMgr) {

  printBox([
    bold('  What are these services?  '),
    '',
    `  NomNomzBot needs two background services running:`,
    '',
    `  ${bold('PostgreSQL')} — the database that stores all your bot\'s data`,
    `  ${bold('Redis')}      — a fast memory cache for real-time features`,
    `  ${bold('Adminer')}    — a web interface to view your database (optional)`,
    '',
    `  These run in ${bold('Docker')} — they start automatically and you don\'t`,
    `  need to think about them again.`,
  ], CYAN);

  nl();
  const wantDocker = await confirm('Start the database and cache services now?', true);

  let dockerStarted = false;

  if (!wantDocker) {
    console.log(dim('\n  Skipped. Run manually when ready:'));
    console.log(dim('    cd server && docker compose up -d postgres redis adminer'));
  } else if (!fs.existsSync(path.join(BACKEND_DIR, 'docker-compose.yml'))) {
    console.log(`\n  ${FAIL} ${red('docker-compose.yml not found.')} Is the server folder missing?`);
  } else {
    nl();
    const s = spin('Starting Docker services (this may take a minute the first time)...').start();
    try {
      execSync('docker compose up -d postgres redis adminer', { cwd: BACKEND_DIR, stdio: 'pipe' });
      s.succeed('Docker services are starting up');
      dockerStarted = true;
    } catch (err) {
      s.fail('Could not start Docker services');
      console.log(`  ${WARN} Error: ${dim(err.message)}`);
      console.log(dim('  Try manually: cd server && docker compose up -d postgres redis adminer'));
    }

    if (dockerStarted) {
      nl();
      const s2 = spin('Waiting for database to be ready (up to 2 minutes)...').start();
      let healthy = false;
      for (let i = 0; i < 24 && !healthy; i++) {
        await new Promise(r => setTimeout(r, 5000));
        try {
          const out = execSync('docker compose ps', { cwd: BACKEND_DIR, stdio: 'pipe' }).toString();
          healthy = out.includes('postgres') && out.includes('healthy')
                 && out.includes('redis')    && out.includes('healthy');
        } catch { /* keep waiting */ }
      }
      healthy
        ? s2.succeed('Database and cache are ready!')
        : s2.info('Services are still starting — they should be ready within a minute.');
    }
  }

  // dotnet-ef
  nl();
  if (!exec('dotnet ef --version')) {
    const s = spin('Installing Entity Framework tools (needed to run database migrations)...').start();
    try {
      execSync('dotnet tool install --global dotnet-ef', { stdio: 'pipe' });
      s.succeed('Entity Framework tools installed');
    } catch {
      s.info('dotnet-ef install skipped — may already be installed');
    }
  }

  // Backend
  nl();
  rule();
  nl();
  console.log(`${bold('Backend API')}  ${DIM}→  http://localhost:5080${RESET}`);
  nl();
  console.log(`  ${INFO} On first run, the API will:`);
  console.log(`    1. Connect to the database`);
  console.log(`    2. Create all the tables (migrations) — takes ~15 seconds`);
  console.log(`    3. Seed initial data (voices, presets, etc.)`);
  console.log(`    4. Start listening at http://localhost:5080`);
  nl();

  const startBackend = await confirm('Start the backend API now?', true);
  if (startBackend) {
    const apiDir = path.join(BACKEND_DIR, 'src', 'NomNomzBot.Api');
    launchInTerminal(`cd "${apiDir}" && dotnet run`, 'NomNomzBot API');
    console.log(`\n  ${OK} API is starting in a new terminal window.`);
    console.log(`  ${DIM}Wait until you see "Now listening on: http://localhost:5080"${RESET}`);
  } else {
    console.log(dim('  Run manually: cd server/src/NomNomzBot.Api && dotnet run'));
  }

  // Frontend
  nl();
  rule();
  nl();
  console.log(`${bold('Frontend Dashboard')}  ${DIM}→  http://localhost:8081${RESET}`);
  nl();
  console.log(`  ${INFO} The dashboard is a web app that opens in your browser.`);
  console.log(`  ${DIM}Make sure the API is running first before using the dashboard.${RESET}`);
  nl();

  const installCmd = pkgMgr === 'yarn' ? 'yarn install' : 'npm install';
  const nodeModulesPath = path.join(FRONTEND_DIR, 'node_modules');

  if (!fs.existsSync(nodeModulesPath)) {
    const installDeps = await confirm('Install frontend packages now? (first run only)', true);
    if (installDeps) {
      const s3 = spin(`Installing frontend packages with ${pkgMgr}...`).start();
      try {
        execSync(installCmd, { cwd: FRONTEND_DIR, stdio: 'ignore' });
        s3.succeed('Frontend packages installed');
      } catch (err) {
        s3.fail('Could not install frontend packages automatically');
        console.log(`  ${WARN} Error: ${dim(err.message)}`);
        console.log(dim(`  Run manually: cd app && ${installCmd}`));
      }
    } else {
      console.log(dim(`  Skipped package install. Run manually first: cd app && ${installCmd}`));
    }
    nl();
  }

  const startFrontend = await confirm('Start the frontend dashboard now?', true);
  if (startFrontend) {
    const cmd = pkgMgr === 'yarn' ? 'yarn web' : 'npm run web';
    launchInTerminal(`cd "${FRONTEND_DIR}" && ${cmd}`, 'NomNomzBot Dashboard');
    console.log(`\n  ${OK} Dashboard is starting in a new terminal window.`);
    console.log(`  ${DIM}It will open http://localhost:8081 in your browser automatically.${RESET}`);
  } else {
    const cmd = pkgMgr === 'yarn' ? 'yarn web' : 'npm run web';
    console.log(dim(`  Run manually: cd app && ${cmd}`));
  }

  return { dockerStarted, startedBackend: startBackend, startedFrontend: startFrontend };
}

// ══════════════════════════════════════════════════════════════════════════════
// STEP 8 — First run guide
// ══════════════════════════════════════════════════════════════════════════════
async function stepFirstRunGuide(services) {
  nl();

  printBox([
    `  ${GREEN}${BOLD}  You\'re all set!  ${RESET}`,
    '',
    bold('  Your URLs:'),
    '',
    `  ${ARR}  Dashboard   →  ${cyan('http://localhost:8081')}`,
    `  ${ARR}  API         →  ${cyan('http://localhost:5080')}`,
    `  ${ARR}  API Docs    →  ${cyan('http://localhost:5080/scalar')}`,
    `  ${ARR}  Health      →  ${cyan('http://localhost:5080/health')}`,
    `  ${ARR}  DB Browser  →  ${cyan('http://localhost:8082')}  ${DIM}(Adminer)${RESET}`,
  ], GREEN);

  nl();

  printBox([
    bold('  What happens when you open the dashboard for the first time:  '),
    '',
    `  The app will detect that no account is configured and take you`,
    `  to a ${bold('Setup Wizard')} automatically. It will walk you through:`,
    '',
    `  ${bold('Step 1:')} Connect your ${bold('Twitch streamer account')}`,
    `          ${DIM}(Click "Connect with Twitch" and log in — this is YOU, the streamer)${RESET}`,
    '',
    `  ${bold('Step 2:')} Connect your ${bold('Twitch bot account')}`,
    `          ${DIM}(Log in as the bot account that will chat in your channel)${RESET}`,
    '',
    `  ${bold('Step 3:')} Configure basics`,
    `          ${DIM}(Bot command prefix like "!", your language, timezone)${RESET}`,
    '',
    `  ${bold('Step 4:')} Enable optional integrations`,
    `          ${DIM}(Spotify, Discord, TTS — you can skip these and do them later)${RESET}`,
    '',
    `  After setup you will land on your main ${bold('Dashboard')} where you`,
    `  can manage commands, events, overlays, and more.`,
  ], CYAN);

  nl();

  printBox([
    bold('  Twitch Login Note  '),
    '',
    `  Twitch OAuth requires an HTTPS redirect URL.`,
    '',
    `  ${bold('Option A (easiest):')} The pre-filled dev credentials use a shared`,
    `  Cloudflare tunnel at ${DIM}bot-dev-api.nomercy.tv${RESET} — OAuth just works. Production domain: nomnomz.bot.`,
    '',
    `  ${bold('Option B:')} If you used your own Twitch app credentials, you will`,
    `  need a Cloudflare tunnel or HTTPS proxy. See the README for`,
    `  instructions on setting up ${cyan('cloudflared')}.`,
  ], YELLOW);

  nl();

  if (services?.startedBackend || services?.startedFrontend) {
    console.log(`  ${INFO} Tip: The API takes about ${bold('30 seconds')} on first boot to run`);
    console.log(`  database migrations. Wait until you see ${dim('"Now listening on:"')} in`);
    console.log(`  the API terminal window before opening the dashboard.`);
    nl();
  }

  const openDash = await confirm(
    'Open the dashboard in your browser now?',
    !!(services?.startedFrontend),
  );
  if (openDash) openBrowser('http://localhost:8081');
}

// ══════════════════════════════════════════════════════════════════════════════
// Banner
// ══════════════════════════════════════════════════════════════════════════════
function printBanner() {
  if (process.stdout.isTTY) process.stdout.write('\x1b[2J\x1b[H');
  nl();
  console.log(`${MAGENTA}${BOLD}  ╔══════════════════════════════════════════════════════════╗${RESET}`);
  console.log(`${MAGENTA}${BOLD}  ║                                                          ║${RESET}`);
  console.log(`${MAGENTA}${BOLD}  ║  ${CYAN}${BOLD}N O M E R C Y B O T${RESET}${MAGENTA}${BOLD}  ·  ${WHITE}${BOLD}Interactive Setup Wizard${RESET}${MAGENTA}${BOLD}   ║${RESET}`);
  console.log(`${MAGENTA}${BOLD}  ║                                                          ║${RESET}`);
  console.log(`${MAGENTA}${BOLD}  ╚══════════════════════════════════════════════════════════╝${RESET}`);
  nl();
  console.log(dim('  This wizard will guide you through setting up NomNomzBot step by step.'));
  console.log(dim('  You can press Ctrl+C at any time — your progress will be saved.'));
  nl();
}

// ══════════════════════════════════════════════════════════════════════════════
// Main
// ══════════════════════════════════════════════════════════════════════════════
async function main() {
  printBanner();
  createRl();

  process.on('SIGINT', () => {
    nl();
    console.log(`\n${WARN} Setup interrupted. Your progress has been saved.`);
    console.log(dim('  Run "node setup.mjs" again to pick up where you left off.'));
    nl();
    rl?.close();
    process.exit(0);
  });

  // Resume from saved progress
  const saved  = loadProgress();
  const state  = { completedSteps: saved.completedSteps || [], config: saved.config || {} };

  if (state.completedSteps.length > 0) {
    const when = saved.savedAt ? new Date(saved.savedAt).toLocaleString() : 'a previous session';
    console.log(`${INFO} Found saved progress from ${dim(when)}.`);
    console.log(dim(`  Steps completed so far: ${state.completedSteps.join(', ')}`));
    nl();
    const resume = await confirm('Resume from where you left off?', true);
    if (!resume) { state.completedSteps = []; state.config = {}; }
    nl();
  }

  const TOTAL = 9;
  let pkgMgr  = state.config.pkgMgr || 'npm';

  // ── Step 1: Prerequisites ──────────────────────────────────────────────────
  if (!state.completedSteps.includes(1)) {
    sectionBanner(1, TOTAL, 'Prerequisites Check');
    const r = await stepPrerequisites();
    pkgMgr = r.pkgMgr;
    state.config.pkgMgr = pkgMgr;
    state.completedSteps.push(1);
    saveProgress(state);
  } else {
    console.log(dim(`  [Step 1/${TOTAL}: Prerequisites — already done ✓]`));
  }

  // ── Step 2: Twitch ─────────────────────────────────────────────────────────
  if (!state.completedSteps.includes(2)) {
    sectionBanner(2, TOTAL, 'Twitch Application Setup');
    const r = await stepTwitch(state.config);
    if (!r.skipped) state.config.twitch = r;
    state.completedSteps.push(2);
    saveProgress(state);
  } else {
    console.log(dim(`  [Step 2/${TOTAL}: Twitch — already done ✓]`));
  }

  // ── Step 3: Bot account ────────────────────────────────────────────────────
  if (!state.completedSteps.includes(3)) {
    sectionBanner(3, TOTAL, 'Bot Twitch Account');
    const r = await stepBotAccount(state.config);
    if (!r.skipped) state.config.botAccount = r;
    state.completedSteps.push(3);
    saveProgress(state);
  } else {
    console.log(dim(`  [Step 3/${TOTAL}: Bot Account — already done ✓]`));
  }

  // ── Step 4: Spotify ────────────────────────────────────────────────────────
  if (!state.completedSteps.includes(4)) {
    sectionBanner(4, TOTAL, 'Spotify Integration  (optional)');
    state.config.spotify = await stepSpotify(state.config);
    state.completedSteps.push(4);
    saveProgress(state);
  } else {
    console.log(dim(`  [Step 4/${TOTAL}: Spotify — already done ✓]`));
  }

  // ── Step 5: Discord ────────────────────────────────────────────────────────
  if (!state.completedSteps.includes(5)) {
    sectionBanner(5, TOTAL, 'Discord Integration  (optional)');
    state.config.discord = await stepDiscord(state.config);
    state.completedSteps.push(5);
    saveProgress(state);
  } else {
    console.log(dim(`  [Step 5/${TOTAL}: Discord — already done ✓]`));
  }

  // ── Step 6: Cloudflare Tunnel ──────────────────────────────────────────────
  if (!state.completedSteps.includes(6)) {
    sectionBanner(6, TOTAL, 'Cloudflare Tunnel  (optional)');
    state.config.cloudflare = await stepCloudflare(state.config);
    state.completedSteps.push(6);
    saveProgress(state);
  } else {
    console.log(dim(`  [Step 6/${TOTAL}: Cloudflare — already done ✓]`));
  }

  // ── Step 7: Generate configs ───────────────────────────────────────────────
  if (!state.completedSteps.includes(7)) {
    sectionBanner(7, TOTAL, 'Writing Configuration Files');
    await stepGenerateConfigs(state.config);
    nl();
    console.log(`${OK} ${bold(green('All configuration files written.'))}`);
    state.completedSteps.push(7);
    saveProgress(state);
  } else {
    console.log(dim(`  [Step 7/${TOTAL}: Config files — already done ✓]`));
  }

  // ── Step 8: Start services ─────────────────────────────────────────────────
  let services = state.config.services || {};
  if (!state.completedSteps.includes(8)) {
    sectionBanner(8, TOTAL, 'Starting Services');
    services = await stepStartServices(pkgMgr);
    state.config.services = services;
    state.completedSteps.push(8);
    saveProgress(state);
  } else {
    console.log(dim(`  [Step 8/${TOTAL}: Start Services — already done ✓]`));
  }

  // ── Step 9: First run guide ────────────────────────────────────────────────
  if (!state.completedSteps.includes(9)) {
    sectionBanner(9, TOTAL, 'First Run Guide');
    await stepFirstRunGuide(services);
    state.config.setupComplete = true;
    state.completedSteps.push(9);
    saveProgress(state);
  }

  nl();
  rule('═', GREEN);
  console.log(`${OK} ${bold(green('Setup complete! Enjoy NomNomzBot.'))}`);
  rule('═', GREEN);
  nl();

  rl?.close();
}

main().catch(err => {
  console.error(`\n${FAIL} ${red(`Unexpected error: ${err.message}`)}`);
  console.error(dim(err.stack));
  rl?.close();
  process.exit(1);
});
