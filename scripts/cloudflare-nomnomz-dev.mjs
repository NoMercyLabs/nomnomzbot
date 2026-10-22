// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Moves one *.nomnomz.bot hostname between Cloudflare tunnels without showing the key.
//
//   node scripts/cloudflare-nomnomz-dev.mjs <mode> --backup-dir <dir> [--host dev.nomnomz.bot] [--yes]
//
// Modes:
//   backup      save both tunnel configs and the DNS record to <dir> (always runs first)
//   point       add <host> => the local dev server on the Dev tunnel, then point its DNS record there
//   remove-old  drop <host> from the proxmox tunnel (run only after the new route answers)
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

if (!['backup', 'point', 'remove-old'].includes(mode) || !backupDir) {
	console.error('usage: <backup|point|remove-old> --backup-dir <dir> [--host *.nomnomz.bot] [--yes]');
	process.exit(2);
}
if (!host.endsWith('.nomnomz.bot') || host === 'nomnomz.bot') {
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
	if (touched.length !== 1 || !String(touched[0].hostname).endsWith('.nomnomz.bot')) {
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

if (mode === 'backup' || apply) {
	save('proxmox-tunnel.json', await tunnelConfig(PROXMOX_TUNNEL));
	save('dev-tunnel.json', await tunnelConfig(DEV_TUNNEL));
	save('dns-record.json', (await dnsRecord()).record);
	console.log(`backup written to ${backupDir}`);
}

if (mode === 'point') {
	await rewriteIngress(DEV_TUNNEL, (rules) => {
		if (rules.some((rule) => rule.hostname === host)) return rules;
		const last = rules.findIndex(catchAll);
		return [...rules.slice(0, last), { hostname: host, service: DEV_ORIGIN }, ...rules.slice(last)];
	});
	const { zoneId, record } = await dnsRecord();
	const content = `${DEV_TUNNEL}.cfargotunnel.com`;
	console.log(`dns ${record.name}: ${record.content} -> ${content}${apply ? '' : ' (dry run)'}`);
	if (apply && record.content !== content) {
		await call('PATCH', `/zones/${zoneId}/dns_records/${record.id}`, { content });
	}
}

if (mode === 'remove-old') {
	await rewriteIngress(PROXMOX_TUNNEL, (rules) => rules.filter((rule) => rule.hostname !== host));
}
