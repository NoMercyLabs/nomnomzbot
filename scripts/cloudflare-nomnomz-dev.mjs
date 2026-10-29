// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

// Moves one *.nomnomz.bot hostname between Cloudflare tunnels without showing the key.
//
//   node scripts/cloudflare-nomnomz-dev.mjs <mode> --backup-dir <dir> [--host dev.nomnomz.bot] [--yes]
//
// Modes:
//   backup      save both tunnel configs and the DNS record to <dir> (always runs first)
//   point       add <host> => the local dev server on the Dev tunnel, then point its DNS record there
//   remove-old  drop <host> from the proxmox tunnel (run only after the new route answers)
//   pages-apex  point nomnomz.bot at GitHub Pages: replace the apex CNAME/A/AAAA records with the four
//               GitHub Pages A records, DNS only (GitHub must reach the host to issue its certificate).
//               It touches only the names nomnomz.bot and www.nomnomz.bot, only the types A, AAAA and
//               CNAME (MX and TXT stay), and never dev or vue. It ignores --host.
//   pages-docs  point docs.nomnomz.bot at the NoMercyLabs GitHub Pages site: one CNAME to
//               nomercylabs.github.io, DNS only. It refuses when docs.nomnomz.bot already holds any record
//               of another shape, and ignores --host.
//   cache-read-aloud    one cache rule: Cloudflare keeps each public read-aloud clip of <host> (30 days, as the
//               bot says). The host must be proxied (orange cloud) or Cloudflare never sees the request.
//   protect-read-aloud  one firewall rule: a request to the read-aloud route of <host> is blocked unless its
//               Origin is one of our sites (docs, docs-dev, <host> itself, and any --allow-origin). It runs
//               before the cache, so a cached clip is protected too. --remove takes either rule out again.
//
// Without --yes a mode only prints what it would change. The key comes from the nomercy-tv .env (path in
// CF_ENV_FILE) and is never printed. Every call refuses a hostname outside *.nomnomz.bot, and a write
// fails unless exactly one ingress rule changed.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const ACCOUNT = '430cc0dc9fcf3a1cf3258d165c15abf4';
const PROXMOX_TUNNEL = '3d4e427b-f0ba-45eb-95ae-a410a9fd7ec2';
const DEV_TUNNEL = '57c7b716-39de-49fa-a0fd-6800e71b53ae';
const DEV_ORIGIN = 'http://192.168.2.201:5090';
const ENV_FILE = process.env.CF_ENV_FILE ?? 'C:/Projects/NoMercy/saas/nomercy-tv/.env';

const args = process.argv.slice(2);
const mode = args[0];
const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);
const host = option('--host', 'dev.nomnomz.bot');
const backupDir = option('--backup-dir');
const apply = args.includes('--yes');
// The origin may only be this dev machine; the port picks which local server the hostname shows.
const origin = option('--origin', DEV_ORIGIN);
// Stoney 2026-10-28: "Never pick the tools default port for a static origin, pick something between 5080 and 5090".
const originPort = Number(/^http:\/\/192\.168\.2\.201:(\d+)$/.exec(origin)?.[1]);
if (!(originPort >= 5080 && originPort <= 5090)) {
	console.error(`refused: --origin must be http://192.168.2.201:<5080-5090>, got ${origin}`);
	process.exit(2);
}

if (!['backup', 'point', 'remove-old', 'remove-dev', 'pages-apex', 'pages-docs', 'cache-read-aloud', 'protect-read-aloud'].includes(mode) || !backupDir) {
	console.error('usage: <backup|point|remove-old|remove-dev|pages-apex|pages-docs|cache-read-aloud|protect-read-aloud> --backup-dir <dir> [--host *.nomnomz.bot] [--origin http://192.168.2.201:<port>] [--allow-origin https://site] [--remove] [--yes]');
	process.exit(2);
}
if (mode !== 'pages-apex' && mode !== 'pages-docs' && (!host.endsWith('.nomnomz.bot') || host === 'nomnomz.bot')) {
	console.error(`refused: ${host} is not a *.nomnomz.bot subdomain`);
	process.exit(2);
}

const env = Object.fromEntries(
	readFileSync(ENV_FILE, 'utf8')
		.split(/\r?\n/)
		.map((line) => line.match(/^([A-Z_]+)=(.*)$/))
		.filter(Boolean)
		.map((match) => [match[1], match[2].replace(/^"|"$/g, '')]),
);
const headers = {
	'X-Auth-Email': env.CLOUDFLARE_EMAIL,
	'X-Auth-Key': env.CLOUDFLARE_API_KEY,
	'Content-Type': 'application/json',
};

async function call(method, path, body) {
	const response = await fetch(`https://api.cloudflare.com/client/v4${path}`, {
		method,
		headers,
		body: body ? JSON.stringify(body) : undefined,
	});
	const json = await response.json();
	if (!json.success) throw new Error(`${method} ${path} failed: ${JSON.stringify(json.errors)}`);
	return json.result;
}

const tunnelConfig = (id) => call('GET', `/accounts/${ACCOUNT}/cfd_tunnel/${id}/configurations`);

async function dnsRecord() {
	const zones = await call('GET', '/zones?name=nomnomz.bot');
	const records = await call('GET', `/zones/${zones[0].id}/dns_records?name=${host}`);
	return { zoneId: zones[0].id, record: records[0] };
}

function save(name, data) {
	mkdirSync(backupDir, { recursive: true });
	writeFileSync(join(backupDir, name), JSON.stringify(data, null, 2));
}

function ingressLines(config) {
	return (config.config?.ingress ?? []).map((rule) => JSON.stringify(rule));
}

// Rewrites one tunnel's ingress and proves the result differs by exactly the one rule asked for.
async function rewriteIngress(tunnelId, change) {
	const before = await tunnelConfig(tunnelId);
	const rules = before.config.ingress;
	const next = change(rules);
	const added = next.filter((rule) => !rules.some((old) => JSON.stringify(old) === JSON.stringify(rule)));
	const dropped = rules.filter((old) => !next.some((rule) => JSON.stringify(old) === JSON.stringify(rule)));
	const touched = [...added, ...dropped];
	const hosts = new Set(touched.map((rule) => rule.hostname));
	if (hosts.size !== 1 || touched.length > 2 || !String(touched[0].hostname).endsWith('.nomnomz.bot')) {
		throw new Error(`refused: the change touches ${touched.length} rules`);
	}
	console.log(`${tunnelId}: +${added.length} -${dropped.length}`, JSON.stringify(touched[0]));
	if (!apply) return;

	await call('PUT', `/accounts/${ACCOUNT}/cfd_tunnel/${tunnelId}/configurations`, {
		config: { ...before.config, ingress: next },
	});
	const after = await tunnelConfig(tunnelId);
	const kept = ingressLines(before).filter((line) => ingressLines(after).includes(line));
	console.log(`${tunnelId}: ${kept.length} of ${rules.length} earlier rules unchanged, ${after.config.ingress.length} now`);
}

const catchAll = (rule) => !rule.hostname;

const PAGES_NAMES = ['nomnomz.bot', 'www.nomnomz.bot'];
const PAGES_TYPES = ['A', 'AAAA', 'CNAME'];
const PAGES_IPS = ['185.199.108.153', '185.199.109.153', '185.199.110.153', '185.199.111.153'];

// The A, AAAA and CNAME records at the two public names (the only records pages-apex may touch).
async function pagesRecords() {
	const zones = await call('GET', '/zones?name=nomnomz.bot');
	const zoneId = zones[0].id;
	const lists = await Promise.all(PAGES_NAMES.map((name) => call('GET', `/zones/${zoneId}/dns_records?name=${name}&per_page=100`)));
	const records = lists.flat().filter((record) => PAGES_NAMES.includes(record.name) && PAGES_TYPES.includes(record.type));
	return { zoneId, records };
}

const recordLine = (record) => `${record.type} ${record.name} ${record.content} proxied=${record.proxied}`;

// Replaces the apex records with the four GitHub Pages addresses and proves nothing else moved.
async function pointApexAtPages() {
	const { zoneId, records } = await pagesRecords();
	save('pages-apex-before.json', records);
	const apexOld = records.filter((record) => record.name === 'nomnomz.bot');
	const keep = (record) => record.type === 'A' && PAGES_IPS.includes(record.content) && record.proxied === false;
	const stale = apexOld.filter((record) => !keep(record));
	const missing = PAGES_IPS.filter((ip) => !apexOld.some((record) => keep(record) && record.content === ip));

	for (const record of stale) console.log(`delete ${recordLine(record)}${apply ? '' : ' (dry run)'}`);
	for (const ip of missing) console.log(`create A nomnomz.bot ${ip} proxied=false${apply ? '' : ' (dry run)'}`);
	if (!apply) return;

	for (const record of stale) await call('DELETE', `/zones/${zoneId}/dns_records/${record.id}`);
	for (const ip of missing) {
		await call('POST', `/zones/${zoneId}/dns_records`, { type: 'A', name: 'nomnomz.bot', content: ip, proxied: false, ttl: 1 });
	}

	const after = (await pagesRecords()).records;
	save('pages-apex-after.json', after);
	const wwwBefore = records.filter((record) => record.name === 'www.nomnomz.bot').map(recordLine).sort();
	const wwwAfter = after.filter((record) => record.name === 'www.nomnomz.bot').map(recordLine).sort();
	const apexAfter = after.filter((record) => record.name === 'nomnomz.bot').map(recordLine).sort();
	const wanted = PAGES_IPS.map((ip) => `A nomnomz.bot ${ip} proxied=false`).sort();
	if (JSON.stringify(apexAfter) !== JSON.stringify(wanted) || JSON.stringify(wwwBefore) !== JSON.stringify(wwwAfter)) {
		throw new Error('the records after the change are not exactly the apex change');
	}
	console.log(`apex now: ${apexAfter.length} A records, DNS only; www unchanged (${wwwAfter.length} record)`);
}

const DOCS_NAME = 'docs.nomnomz.bot';
const DOCS_TARGET = 'nomercylabs.github.io';

// Creates the one docs CNAME and proves it is the only record at that name.
async function pointDocsAtPages() {
	const zones = await call('GET', '/zones?name=nomnomz.bot');
	const zoneId = zones[0].id;
	const existing = await call('GET', `/zones/${zoneId}/dns_records?name=${DOCS_NAME}`);
	save('pages-docs-before.json', existing);
	const done = existing.length === 1 && existing[0].type === 'CNAME' && existing[0].content === DOCS_TARGET;
	if (done) return console.log(`already: CNAME ${DOCS_NAME} ${DOCS_TARGET}`);
	if (existing.length > 0) throw new Error(`refused: ${DOCS_NAME} already holds ${existing.map(recordLine).join(', ')}`);
	console.log(`create CNAME ${DOCS_NAME} ${DOCS_TARGET} proxied=false${apply ? '' : ' (dry run)'}`);
	if (!apply) return;

	await call('POST', `/zones/${zoneId}/dns_records`, { type: 'CNAME', name: DOCS_NAME, content: DOCS_TARGET, proxied: false, ttl: 1 });
	const after = await call('GET', `/zones/${zoneId}/dns_records?name=${DOCS_NAME}`);
	save('pages-docs-after.json', after);
	console.log(`docs now: ${after.map(recordLine).join(', ')}`);
}

const CACHE_PHASE = 'http_request_cache_settings';
const FIREWALL_PHASE = 'http_request_firewall_custom';
const READ_ALOUD_PATH = '/api/v1/public/read-aloud';
const CACHE_RULE_REF = 'nnz-read-aloud-cache';
const PROTECT_RULE_REF = 'nnz-read-aloud-origin';
// The sites that may use the voice from a browser; the host's own site is always added. Same sites as the bot's
// ReadAloud:Origins default (ReadAloudCors.cs), and --allow-origin adds more.
const DEFAULT_ORIGINS = ['https://docs.nomnomz.bot', 'https://docs-dev.nomnomz.bot'];

// Adds, replaces or removes ONE rule of a zone ruleset phase, keyed by its ref, and proves that every other rule
// is still there afterwards. Without --yes it only prints the rule it would write.
async function changeOneRule(phase, ref, rule, label) {
	const zones = await call('GET', '/zones?name=nomnomz.bot');
	const zoneId = zones[0].id;
	const path = `/zones/${zoneId}/rulesets/phases/${phase}/entrypoint`;
	let rules = [];
	try {
		rules = (await call('GET', path)).rules ?? [];
	} catch (error) {
		if (!String(error.message).includes('10003')) throw error;
	}
	save(`${label}-rules-before.json`, rules);

	const keep = rules.filter((existing) => existing.ref !== ref);
	const had = rules.length - keep.length;
	const remove = args.includes('--remove');
	console.log(`${label} rules now: ${rules.length}; ${remove ? 'remove' : had ? 'replace' : 'add'} ${ref} for ${host}${READ_ALOUD_PATH}${apply ? '' : ' (dry run)'}`);
	if (!remove) console.log(`expression: ${rule.expression}`);
	if (!apply) return;

	const clean = ({ id, ref, description, expression, action, action_parameters, enabled }) => ({ id, ref, description, expression, action, action_parameters, enabled });
	await call('PUT', path, { rules: [...keep.map(clean), ...(remove ? [] : [rule])] });
	const after = (await call('GET', path)).rules ?? [];
	save(`${label}-rules-after.json`, after);
	const keptIds = keep.every((old) => after.some((now) => now.id === old.id));
	const ours = after.filter((now) => now.ref === ref);
	if (!keptIds || ours.length !== (remove ? 0 : 1) || after.length !== keep.length + (remove ? 0 : 1)) throw new Error(`the rules after the change are not exactly the one ${label} rule change`);
	console.log(`${label} rules after: ${after.length}; ${keep.length} earlier rule(s) unchanged; ours ${remove ? 'removed' : `enabled=${ours[0].enabled}`}`);
}

// The cache rule for the public read-aloud clips of one host. It follows the origin's Cache-Control (the bot sends
// a 30 day s-maxage on a clip and `no-store` on every refusal and every empty clip), so an error answer is never
// stored. The default cache key keeps the query string, which holds the text and the language; a custom key
// (query string list, Origin header) needs an Enterprise plan, so the rule does not use one.
function cacheReadAloud() {
	return changeOneRule(
		CACHE_PHASE,
		CACHE_RULE_REF,
		{
			ref: CACHE_RULE_REF,
			description: 'NomNomzBot: cache the public read-aloud clips',
			expression: `(http.host eq "${host}" and http.request.method eq "GET" and http.request.uri.path eq "${READ_ALOUD_PATH}")`,
			action: 'set_cache_settings',
			action_parameters: { cache: true, edge_ttl: { mode: 'respect_origin' }, browser_ttl: { mode: 'respect_origin' } },
			enabled: true,
		},
		'cache',
	);
}

// The Origin rule for the same path. A cached clip is answered by Cloudflare without asking the bot, so the
// bot's own origin check never sees it; this rule runs before the cache and refuses every browser request whose
// Origin is not one of the allowed sites. A request with no Origin (curl, a script) is refused too.
function protectReadAloud() {
	const allowed = [...DEFAULT_ORIGINS, `https://${host}`, ...args.flatMap((arg, i) => (arg === '--allow-origin' ? [args[i + 1]] : []))];
	const list = [...new Set(allowed)].map((origin) => `"${origin}"`).join(' ');
	return changeOneRule(
		FIREWALL_PHASE,
		PROTECT_RULE_REF,
		{
			ref: PROTECT_RULE_REF,
			description: 'NomNomzBot: only our own sites may use the public read-aloud voice',
			expression: `(http.host eq "${host}" and http.request.uri.path eq "${READ_ALOUD_PATH}" and not any(http.request.headers["origin"][*] in {${list}}))`,
			action: 'block',
			enabled: true,
		},
		'protect',
	);
}

const zoneOnlyMode = mode === 'pages-apex' || mode === 'pages-docs' || mode === 'cache-read-aloud' || mode === 'protect-read-aloud';
const pagesMode = zoneOnlyMode;
if (mode === 'cache-read-aloud') await cacheReadAloud();
if (mode === 'protect-read-aloud') await protectReadAloud();
if (mode === 'backup' || (apply && !pagesMode)) {
	save('proxmox-tunnel.json', await tunnelConfig(PROXMOX_TUNNEL));
	save('dev-tunnel.json', await tunnelConfig(DEV_TUNNEL));
	save('dns-record.json', (await dnsRecord()).record ?? null);
	console.log(`backup written to ${backupDir}`);
}

if (mode === 'pages-apex') await pointApexAtPages();
if (mode === 'pages-docs') await pointDocsAtPages();

if (mode === 'point') {
	await rewriteIngress(DEV_TUNNEL, (rules) => {
		if (rules.some((rule) => rule.hostname === host)) {
			return rules.map((rule) => (rule.hostname === host ? { ...rule, service: origin } : rule));
		}
		const last = rules.findIndex(catchAll);
		return [...rules.slice(0, last), { hostname: host, service: origin }, ...rules.slice(last)];
	});
	const { zoneId, record } = await dnsRecord();
	const content = `${DEV_TUNNEL}.cfargotunnel.com`;
	if (!record) {
		console.log(`dns ${host}: (none) -> ${content} proxied${apply ? '' : ' (dry run)'}`);
		if (apply) {
			await call('POST', `/zones/${zoneId}/dns_records`, { type: 'CNAME', name: host, content, proxied: true });
		}
		process.exit(0);
	}
	console.log(`dns ${record.name}: ${record.content} -> ${content}${apply ? '' : ' (dry run)'}`);
	if (apply && record.content !== content) {
		await call('PATCH', `/zones/${zoneId}/dns_records/${record.id}`, { content });
	}
}

if (mode === 'remove-old') {
	await rewriteIngress(PROXMOX_TUNNEL, (rules) => rules.filter((rule) => rule.hostname !== host));
}

// Takes a hostname down: drops its route from the Dev tunnel, then deletes its DNS record when that record
// points at the Dev tunnel. A record that points anywhere else stays and is reported.
if (mode === 'remove-dev') {
	await rewriteIngress(DEV_TUNNEL, (rules) => rules.filter((rule) => rule.hostname !== host));
	const { zoneId, record } = await dnsRecord();
	const content = `${DEV_TUNNEL}.cfargotunnel.com`;
	if (!record) {
		console.log(`dns ${host}: no record`);
	} else if (record.content !== content) {
		console.log(`dns ${record.name}: points at ${record.content}, not the Dev tunnel; left as it is`);
	} else {
		console.log(`dns ${record.name}: delete ${record.type} -> ${record.content}${apply ? '' : ' (dry run)'}`);
		if (apply) {
			await call('DELETE', `/zones/${zoneId}/dns_records/${record.id}`);
			const left = await call('GET', `/zones/${zoneId}/dns_records?name=${host}`);
			console.log(`dns ${host}: ${left.length} record(s) now`);
		}
	}
}
