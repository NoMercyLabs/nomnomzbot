// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Pre-defined API errors, after the PlayerError of nomercy-player-core: every failure has a code, an origin
// (the API module it came from), a message key for the screen, and the details the server sent. The server
// answers with Microsoft ProblemDetails (RFC 9457) and ValidationProblemDetails; `parseProblem` reads them.

import { merge } from '@/lib/array';
import type { JsonObject } from '@/lib/json';

// The API module an error comes from. It is the first part of the code.
export type ApiOrigin =
  | 'access' |
  'actionRequired' |
  'admin' |
  'analytics' |
  'chat' |
  'codeScripts' |
  'commands' |
  'community' |
  'discord' |
  'economy' |
  'engagement' |
  'favoriteEmotes' |
  'games' |
  'home' |
  'liveOps' |
  'moderation' |
  'modTools' |
  'music' |
  'obs' |
  'vts' |
  'supporters' |
  'people' |
  'pipelineRuns' |
  'pipelines' |
  'rewards' |
  'shield' |
  'settings' |
  'shoutouts' |
  'stream' |
  'tts' |
  'viewer' |
  'widgets';

// The catalog: every operation that can fail, and the module it belongs to. A new operation is added here
// first; the type of `failed` and `refusal` then accepts it.
export const apiOperations = {
  'channel access': 'access',
  'action-required-dismiss': 'actionRequired',
  'admin tenants': 'admin',
  'admin tenant': 'admin',
  'admin stats': 'admin',
  'admin users': 'admin',
  'support people': 'admin',
  'support person': 'admin',
  'support history': 'admin',
  'support commands': 'admin',
  'support pipelines': 'admin',
  'billing tiers': 'admin',
  'billing invites': 'admin',
  'billing priced units': 'admin',
  'billing invoices': 'admin',
  'billing grants': 'admin',
  'billing tier preview': 'admin',
  'billing grant preview': 'admin',
  'billing tier save': 'admin',
  'billing tier create': 'admin',
  'billing invite create': 'admin',
  'billing invite revoke': 'admin',
  'billing tier grant': 'admin',
  'billing founder grant': 'admin',
  'billing refund': 'admin',
  'billing grant issue': 'admin',
  'billing priced unit': 'admin',
  'admin spam defaults': 'admin',
  'admin eventsub health': 'admin',
  'admin webhook deliveries': 'admin',
  'admin webhook replay': 'admin',
  'admin jobs': 'admin',
  'admin job retry': 'admin',
  'platform defaults actions': 'admin',
  'platform defaults replies': 'admin',
  'platform defaults events': 'admin',
  'platform defaults voice': 'admin',
  'admin usage': 'admin',
  'admin error budget': 'admin',
  'access principals': 'admin',
  'access roles': 'admin',
  'access assign': 'admin',
  'access revoke': 'admin',
  'access deactivate': 'admin',
  'access reactivate': 'admin',
  'access end': 'admin',
  'access impersonate': 'admin',
  'data requests list': 'admin',
  'data requests summary': 'admin',
  'data requests preview': 'admin',
  'data requests retry': 'admin',
  'event replay projections': 'admin',
  'event replay history': 'admin',
  'event replay preview': 'admin',
  'event replay execute': 'admin',
  'admin system': 'admin',
  'admin key rotation': 'admin',
  'platform bot': 'admin',
  'platform bot preview': 'admin',
  'platform bot reconnect': 'admin',
  'platform bot reconnect poll': 'admin',
  'admin audit': 'admin',
  'content definitions': 'admin',
  'content definition': 'admin',
  'content definition create': 'admin',
  'content version draft': 'admin',
  'content definition retire': 'admin',
  'trust safety review queue': 'admin',
  'trust safety signals': 'admin',
  'trust safety network blocks': 'admin',
  'trust safety block preview': 'admin',
  'trust safety confirm': 'admin',
  'trust safety overturn': 'admin',
  'trust safety block apply': 'admin',
  'trust safety block lift': 'admin',
  'admin feature flags': 'admin',
  'admin flag overrides': 'admin',
  'admin flag blast radius': 'admin',
  'admin providers': 'admin',
  'admin provider save': 'admin',
  'admin provider clear': 'admin',
  suspend: 'admin',
  reinstate: 'admin',
  'begin access': 'admin',
  impersonate: 'admin',
  'tenant members': 'admin',
  'tenant limits': 'admin',
  'tenant limit save': 'admin',
  'tenant limit clear': 'admin',
  'tenant remigrate': 'admin',
  'tenant erase preview': 'admin',
  'tenant erase': 'admin',
  'tenant export': 'admin',
  'analytics summary': 'analytics',
  'analytics daily': 'analytics',
  'analytics chat hours': 'analytics',
  'analytics streams': 'analytics',
  'chat emotes': 'chat',
  'chat search': 'chat',
  'user chat': 'chat',
  'chat send': 'chat',
  'viewer chat': 'chat',
  'viewer chat emotes': 'chat',
  'viewer chat send': 'chat',
  'chat delete': 'chat',
  'chat timeout': 'chat',
  'chat ban': 'chat',
  'chat unban': 'chat',
  'chat identity': 'chat',
  'chat color': 'chat',
  'scope regrant': 'chat',
  'chat message too long': 'chat',
  'code scripts': 'codeScripts',
  'code script': 'codeScripts',
  'code script switch': 'codeScripts',
  'create code script': 'codeScripts',
  'blast radius': 'codeScripts',
  'delete code script': 'codeScripts',
  'test run': 'codeScripts',
  'test triggers': 'codeScripts',
  'script versions': 'codeScripts',
  'publish version': 'codeScripts',
  'delete version': 'codeScripts',
  commands: 'commands',
  'built-in commands': 'commands',
  'command presets': 'commands',
  'save command': 'commands',
  'delete command': 'commands',
  'create command': 'commands',
  'reset command': 'commands',
  'built-in command': 'commands',
  'community emotes': 'community',
  community: 'community',
  'vip change': 'community',
  'community ban': 'community',
  'viewer search': 'community',
  'twitch lookup': 'community',
  'discord connections': 'discord',
  'discord channels': 'discord',
  'discord roles': 'discord',
  'discord rules': 'discord',
  'save discord rule': 'discord',
  'create discord rule': 'discord',
  'discord preview': 'discord',
  'discord dispatch log': 'discord',
  'set discord streamer enabled': 'discord',
  'discord notify roles': 'discord',
  'create discord notify role': 'discord',
  'save discord notify role': 'discord',
  'post discord role button': 'discord',
  'discord live role': 'discord',
  'save discord live role': 'discord',
  'delete discord live role': 'discord',
  'currency config': 'economy',
  'save currency': 'economy',
  'earning rules': 'economy',
  'save earning rule': 'economy',
  'delete earning rule': 'economy',
  'savings jars': 'economy',
  'create savings jar': 'economy',
  'save savings jar': 'economy',
  'delete savings jar': 'economy',
  'catalog items': 'economy',
  'create catalog item': 'economy',
  'save catalog item': 'economy',
  'delete catalog item': 'economy',
  'leaderboard configs': 'economy',
  'save leaderboard config': 'economy',
  'delete leaderboard config': 'economy',
  'invite jar channel': 'economy',
  wallets: 'economy',
  'adjust wallet': 'economy',
  'freeze wallet': 'economy',
  'automation tokens': 'settings',
  'create automation token': 'settings',
  'rotate automation token': 'settings',
  'revoke automation token': 'settings',
  'automation event catalog': 'settings',
  'mint pairing code': 'settings',
  'approve device': 'settings',
  'engagement config': 'engagement',
  'engagement defaults': 'engagement',
  'save engagement config': 'engagement',
  'live game catalog': 'games',
  'active live game': 'games',
  'live game history': 'games',
  'start live game': 'games',
  'cancel live game': 'games',
  'game configs': 'games',
  'save game config': 'games',
  'favorite emotes': 'favoriteEmotes',
  'favorite emote change': 'favoriteEmotes',
  stats: 'home',
  'review queue': 'home',
  connections: 'home',
  'bot status': 'home',
  activity: 'home',
  resolve: 'home',
  polls: 'liveOps',
  'poll history': 'liveOps',
  giveaways: 'liveOps',
  'giveaway winners': 'liveOps',
  'open giveaway': 'liveOps',
  'close giveaway': 'liveOps',
  'draw giveaway': 'liveOps',
  'redraw winner': 'liveOps',
  'giveaway entries': 'liveOps',
  'code pools': 'liveOps',
  'create code pool': 'liveOps',
  'add codes to code pool': 'liveOps',
  'save giveaway': 'liveOps',
  'delete giveaway': 'liveOps',
  'giveaway blast radius': 'liveOps',
  quotes: 'liveOps',
  'random quote': 'liveOps',
  'save quote': 'liveOps',
  'delete quote': 'liveOps',
  'custom event feeds': 'settings',
  'custom event feed presets': 'settings',
  'save custom event feed': 'settings',
  'delete custom event feed': 'settings',
  'custom event feed blast radius': 'settings',
  'test custom event feed': 'settings',
  'fetch custom event feed': 'settings',
  'data requests': 'settings',
  consents: 'settings',
  'erasure preview': 'settings',
  'export data': 'settings',
  'request erasure': 'settings',
  'opt out of processing': 'settings',
  'change consent': 'settings',
  'federation opt-ins': 'settings',
  'save federation opt-in': 'settings',
  'disable federation opt-in': 'settings',
  'network block opt-in': 'settings',
  'save network block opt-in': 'settings',
  timers: 'liveOps',
  'event responses': 'liveOps',
  'event response': 'liveOps',
  'save event response': 'liveOps',
  'reset event response': 'liveOps',
  'timer-toggle': 'liveOps',
  timer: 'liveOps',
  'create timer': 'liveOps',
  'save timer': 'liveOps',
  'media share': 'liveOps',
  'media share config': 'liveOps',
  'media share player': 'liveOps',
  'save media share config': 'liveOps',
  'media share action': 'liveOps',
  'delete timer': 'liveOps',
  'chat triggers': 'liveOps',
  'create chat trigger': 'liveOps',
  'save chat trigger': 'liveOps',
  'delete chat trigger': 'liveOps',
  'voice triggers': 'liveOps',
  'voice listener link': 'liveOps',
  stickers: 'liveOps',
  'create voice trigger': 'liveOps',
  'save voice trigger': 'liveOps',
  'delete voice trigger': 'liveOps',
  'timer default': 'liveOps',
  'restore timer default': 'liveOps',
  'poll-start': 'liveOps',
  predictions: 'liveOps',
  'prediction-start': 'liveOps',
  'prediction-end': 'liveOps',
  'poll-end': 'liveOps',
  schedule: 'liveOps',
  'schedule-settings': 'liveOps',
  'schedule-segment': 'liveOps',
  'raid-rules': 'liveOps',
  'raid-rules-save': 'liveOps',
  commercial: 'liveOps',
  'moderation history': 'moderation',
  'moderation standing': 'moderation',
  'escalation policy': 'moderation',
  'save escalation policy': 'moderation',
  'spam defense policy': 'moderation',
  'save spam defense policy': 'moderation',
  'chat settings': 'modTools',
  'raid assessments': 'modTools',
  'mass ban': 'modTools',
  'clear chat': 'modTools',
  'supporter events': 'supporters',
  'supporter connections': 'supporters',
  'supporter connection': 'supporters',
  'music queue': 'music',
  'blocked tracks': 'music',
  'music config': 'music',
  'music control': 'music',
  'song requests': 'music',
  promote: 'music',
  move: 'music',
  'remove song': 'music',
  'remove requester songs': 'music',
  'ban song': 'music',
  'block song': 'music',
  'unblock song': 'music',
  'save music config': 'music',
  'player buttons': 'music',
  'save player buttons': 'music',
  'run action': 'music',
  'music history': 'music',
  'music playlists': 'music',
  'music devices': 'music',
  'top requesters': 'music',
  'community list': 'people',
  'viewer profile': 'people',
  'user notes': 'people',
  'add note': 'people',
  'edit note': 'people',
  'delete note': 'people',
  shoutout: 'people',
  timeout: 'people',
  'timeout refused': 'people',
  warn: 'people',
  'warn refused': 'people',
  unban: 'people',
  'lift ban': 'people',
  'pipeline runs': 'pipelineRuns',
  'pipeline run': 'pipelineRuns',
  pipelines: 'pipelines',
  pipeline: 'pipelines',
  'create pipeline': 'pipelines',
  'pipeline delete': 'pipelines',
  'pipeline blast radius': 'pipelines',
  'pipeline catalog': 'pipelines',
  'pipeline options': 'pipelines',
  'pipeline test run': 'pipelines',
  rewards: 'rewards',
  redemptions: 'rewards',
  'update reward': 'rewards',
  'fulfill redemption': 'rewards',
  'refund redemption': 'rewards',
  shield: 'shield',
  'shoutout suggestions': 'shoutouts',
  'dismiss suggestion': 'shoutouts',
  'set shoutout line': 'shoutouts',
  'remove shoutout line': 'shoutouts',
  stream: 'stream',
  'ad schedule': 'stream',
  snooze: 'stream',
  'stream history': 'stream',
  categories: 'stream',
  'stream info': 'stream',
  connect: 'stream',
  'obs connection': 'obs',
  'obs connection status': 'obs',
  'obs bridge': 'obs',
  'obs state': 'obs',
  'obs scenes': 'obs',
  'obs switch scene': 'obs',
  'obs studio mode': 'obs',
  'obs preview scene': 'obs',
  'obs studio transition': 'obs',
  'obs transitions': 'obs',
  'obs set transition': 'obs',
  'obs save connection': 'obs',
  'obs hotkeys': 'obs',
  'obs hotkey': 'obs',
  'obs screenshot': 'obs',
  'obs inputs': 'obs',
  'obs input mute': 'obs',
  'obs input volume': 'obs',
  'obs stats': 'obs',
  'obs virtual cam': 'obs',
  'obs output': 'obs',
  'obs scene items': 'obs',
  'obs source filters': 'obs',
  'obs scene item enabled': 'obs',
  'obs scene item locked': 'obs',
  'obs filter enabled': 'obs',
  'vts connection': 'vts',
  'vts inventory': 'vts',
  'vts save connection': 'vts',
  'vts rotate bridge token': 'vts',
  'vts authorize': 'vts',
  'vts control': 'vts',
  'tts config': 'tts',
  'tts voices': 'tts',
  'tts history': 'tts',
  'person voice': 'tts',
  'tts sample': 'tts',
  'tts lexicon': 'tts',
  'tts key': 'tts',
  'name pronunciation': 'tts',
  'viewer channel': 'viewer',
  'viewer summary': 'viewer',
  'viewer queue': 'viewer',
  'watched channels': 'viewer',
  'pronoun catalog': 'viewer',
  'my pronouns': 'viewer',
  'set my pronouns': 'viewer',
  'my identities': 'viewer',
  'unlink identity': 'viewer',
  'my standing': 'viewer',
  'my voice': 'viewer',
  'set my voice': 'viewer',
  'clear my voice': 'viewer',
  'public profile': 'community',
  widgets: 'widgets',
  'required overlays': 'widgets',
  'audio mix': 'widgets',
  'widget settings': 'widgets',
  'widget test': 'widgets',
  'widget project': 'widgets',
  'save widget project': 'widgets',
  'widget samples': 'widgets',
  'widget preview action': 'widgets',
  'widget versions': 'widgets',
  'rollback widget': 'widgets',
  'switch widget': 'widgets',
  'save widget settings': 'widgets',
  'personality settings': 'settings',
  'save personality': 'settings',
  'basics settings': 'settings',
  'save basics': 'settings',
  'resource limits': 'settings',
  'plan usage': 'settings',
  'plan entitlement': 'settings',
  'start bot login': 'settings',
  'poll bot login': 'settings',
  'start bot browser login': 'settings',
  'disconnect bot': 'settings',
  'channel features': 'settings',
  'toggle feature': 'settings',
  integrations: 'settings',
  'disconnect impact': 'settings',
  'disconnect integration': 'settings',
  'connect integration': 'settings',
  'spotify credentials': 'settings',
  'save spotify credentials': 'settings',
  'clear spotify credentials': 'settings',
  'billing subscription': 'settings',
  'channel billing tiers': 'settings',
  'channel billing invoices': 'settings',
  'billing founders badge': 'settings',
  'start checkout': 'settings',
  'change plan': 'settings',
  'cancel plan': 'settings',
  'resume plan': 'settings',
  'billing portal': 'settings',
  'check invite code': 'settings',
  'redeem invite code': 'settings',
  'export event journal': 'settings',
  'read event journal': 'settings',
  'import event journal': 'settings',
  'replay imported events': 'settings',
  'import legacy history': 'settings',
  'rebuild projections': 'settings',
  'outbound webhooks': 'settings',
  'inbound webhooks': 'settings',
  'webhook event catalog': 'settings',
  'save outbound webhook': 'settings',
  'delete outbound webhook': 'settings',
  'reenable outbound webhook': 'settings',
  'save inbound webhook': 'settings',
  'delete inbound webhook': 'settings',
  'inbound webhook blast radius': 'settings',
  'outbound webhook blast radius': 'settings',
  'rotate inbound webhook link': 'settings',
  'rotate outbound webhook secret': 'settings',
  'test outbound webhook': 'settings',
  'outbound webhook deliveries': 'settings',
  'retry outbound delivery': 'settings',
  'my channels': 'settings',
  'moderated channels': 'settings',
  'channel info': 'settings',
  'deleted channels': 'settings',
  'join channel': 'settings',
  'leave channel': 'settings',
  'channel delete preview': 'settings',
  'delete channel': 'settings',
  'channel reset preview': 'settings',
  'reset channel': 'settings',
  'restore channel': 'settings',
  'management roles': 'settings',
  'set management role': 'settings',
  'remove management role': 'settings',
  'action permission matrix': 'settings',
  'set action level': 'settings',
  'reset action level': 'settings',
  'person grants': 'settings',
  'grant person capability': 'settings',
  'revoke person capability': 'settings',
  'trust policy': 'settings',
  'trust policy defaults': 'settings',
  'save trust policy': 'settings',
} as const satisfies Record<string, ApiOrigin>;

export type ApiOperation = keyof typeof apiOperations;

// Why a save was refused, as data: the i18n key of the message and the request field to mark. The bot's
// validators send it in the `data` member of the 400 body.
export type ApiViolation = {
  key: string;
  field: string;
};

// One broken rule from a refusal that lists them all: a stable reason key, the group it is about, the name the
// owner gave the rule, and the number and limit the reason may use.
export type ApiFieldProblem = {
  reason: string;
  field: string;
  subject: string;
  value?: number;
  limit?: number;
  // The accepted range of an out-of-range value, when the server states one.
  minimum?: number;
  maximum?: number;
};

// The members of a ProblemDetails body, plus the `errors` of a ValidationProblemDetails, the `traceId`
// extension ASP.NET adds and the bot's `data` violation. Every member is optional: a proxy error page has
// none of them.
export type ApiProblem = {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: Record<string, string[]>;
  traceId?: string;
  violation?: ApiViolation;
  problems?: ApiFieldProblem[];
  // The bot's own error code (for example DISCORD_BOT_TOKEN_NOT_CONFIGURED), when it sent one.
  serverCode?: string;
};

function isRecord(value: unknown): value is JsonObject {
  return typeof value === 'object' && value !== null;
}

function textOf(body: JsonObject, key: string): string | undefined {
  const value = body[key];

  return typeof value === 'string' && value !== ''
    ? value
    : undefined;
}

function statusOf(body: JsonObject): number | undefined {
  const value = Number(body.status);

  return Number.isInteger(value) && value > 0
    ? value
    : undefined;
}

function fieldErrorsOf(body: JsonObject): Record<string, string[]> | undefined {
  const value = body.errors;

  if (!isRecord(value))
    return undefined;

  const entries = Object.entries(value).filter((entry): entry is [string, string[]] =>
    Array.isArray(entry[1]),
  );

  return entries.length > 0
    ? Object.fromEntries(entries)
    : undefined;
}

function violationOf(body: JsonObject): ApiViolation | undefined {
  const data = body.data;

  if (!isRecord(data))
    return undefined;

  const key = textOf(data, 'key');
  const field = textOf(data, 'field');

  return key !== undefined && field !== undefined
    ? { key, field }
    : undefined;
}

function numberOf(body: JsonObject, key: string): number | undefined {
  const value = body[key];

  return typeof value === 'number'
    ? value
    : undefined;
}

// eslint-disable-next-line no-restricted-syntax -- one element of a parsed JSON array can be anything
function fieldProblemOf(entry: unknown): ApiFieldProblem | undefined {
  if (!isRecord(entry))
    return undefined;

  const reason = textOf(entry, 'reason');
  const field = textOf(entry, 'field');

  if (reason === undefined || field === undefined)
    return undefined;

  return {
    reason,
    field,
    subject: textOf(entry, 'subject') ?? '',
    value: numberOf(entry, 'value'),
    limit: numberOf(entry, 'limit'),
  };
}

// One entry of a `violations` list: the key is the reason, and the accepted range rides along when stated.
// eslint-disable-next-line no-restricted-syntax -- one element of a parsed JSON array can be anything
function violationProblemOf(entry: unknown): ApiFieldProblem | undefined {
  if (!isRecord(entry))
    return undefined;

  const reason = textOf(entry, 'key');
  const field = textOf(entry, 'field');

  if (reason === undefined || field === undefined)
    return undefined;

  return {
    reason,
    field,
    subject: '',
    minimum: numberOf(entry, 'minimum'),
    maximum: numberOf(entry, 'maximum'),
  };
}

function problemsOf(body: JsonObject): ApiFieldProblem[] | undefined {
  const data = body.data;

  if (!isRecord(data))
    return undefined;

  const listed = Array.isArray(data.problems)
    ? data.problems.map(fieldProblemOf)
    : [];

  const violations = Array.isArray(data.violations)
    ? data.violations.map(violationProblemOf)
    : [];

  const found = merge(listed, violations).filter(
    (problem): problem is ApiFieldProblem => problem !== undefined,
  );

  return found.length > 0
    ? found
    : undefined;
}

// Reads a response body into a problem. The bot's own refusals carry `message` instead of `detail`.
// eslint-disable-next-line no-restricted-syntax -- every endpoint's error body has a different generated type, so the input must accept any of them
export function parseProblem(body: unknown) {
  if (!isRecord(body))
    return {} as const satisfies ApiProblem;

  const problem: ApiProblem = {
    type: textOf(body, 'type'),
    title: textOf(body, 'title'),
    status: statusOf(body),
    detail: textOf(body, 'detail') ?? textOf(body, 'message'),
    instance: textOf(body, 'instance'),
    errors: fieldErrorsOf(body),
    traceId: textOf(body, 'traceId'),
    violation: violationOf(body),
    problems: problemsOf(body),
    serverCode: textOf(body, 'code'),
  };

  return Object.fromEntries(Object.entries(problem).filter(([, value]) => value !== undefined));
}

function messageKeyOf(status: number | undefined, detail: string | undefined): string {
  if (status === undefined)
    return detail === undefined
      ? 'errors.failed'
      : 'errors.refused';

  if (status === 404)
    return 'errors.notFound';

  if (status === 401 || status === 403)
    return 'errors.forbidden';

  if (status === 429)
    return 'errors.rateLimited';

  if (status === 400 || status === 409 || status === 422)
    return 'errors.invalid';

  return status >= 500
    ? 'errors.server'
    : 'errors.failed';
}

export class ApiError extends Error {
  override readonly name = 'ApiError';
  // `<origin>/<operation>`, for example `music/music-queue`. Stable: logs and tests can match on it.
  readonly code: string;
  readonly origin: ApiOrigin;
  readonly operation: ApiOperation;
  // The i18n key of the text a screen shows; the raw message below is for logs.
  readonly messageKey: string;
  readonly status?: number;
  readonly type?: string;
  readonly title?: string;
  readonly detail?: string;
  readonly instance?: string;
  readonly errors?: Record<string, string[]>;
  readonly traceId?: string;
  readonly violation?: ApiViolation;
  readonly problems?: ApiFieldProblem[];
  readonly serverCode?: string;

  constructor(operation: ApiOperation, status?: number, problem: ApiProblem = {}) {
    const origin = apiOperations[operation];
    const code = `${origin}/${operation.replaceAll(' ', '-')}`;

    super(problem.detail ?? (status === undefined
      ? operation
      : `${operation} ${status}`));

    this.code = code;
    this.origin = origin;
    this.operation = operation;
    this.status = status ?? problem.status;
    this.messageKey = messageKeyOf(status ?? problem.status, problem.detail);
    this.type = problem.type;
    this.title = problem.title;
    this.detail = problem.detail;
    this.instance = problem.instance;
    this.errors = problem.errors;
    this.traceId = problem.traceId;
    this.violation = problem.violation;
    this.problems = problem.problems;
    this.serverCode = problem.serverCode;
  }
}

// A response with an error status and the body the server sent with it, when it sent one.
// eslint-disable-next-line no-restricted-syntax -- every endpoint's error body has a different generated type, so the input must accept any of them
export function failed(operation: ApiOperation, status: number, body?: unknown): ApiError {
  return new ApiError(operation, status, parseProblem(body));
}

// The same, for a body the caller already holds.
// eslint-disable-next-line no-restricted-syntax -- every endpoint's error body has a different generated type, so the input must accept any of them
export function problemError(operation: ApiOperation, status: number, body: unknown): ApiError {
  return failed(operation, status, body);
}

// The bot answered 200 and still refused; its message says why.
export function refused(operation: ApiOperation, reason?: string | null): ApiError {
  return new ApiError(operation, undefined, { detail: reason ?? undefined });
}
