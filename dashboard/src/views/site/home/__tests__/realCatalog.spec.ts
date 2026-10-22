// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { merge } from '@/lib/array';

import builtins from '../preview/fixtures/builtins.json';
import commandPresets from '../preview/fixtures/command-presets.json';
import commandsCapture from '../preview/fixtures/commands.json';
import helpersCommand from '../preview/fixtures/helpers-command.json';
import integrationsCapture from '../preview/fixtures/integrations.json';
import integrationStatus from '../preview/fixtures/integrations-status.json';
import musicConfigCapture from '../preview/fixtures/music-config.json';
import voiceOptions from '../preview/fixtures/options-voice.json';
import pipelineActions from '../preview/fixtures/pipeline-actions.json';
import spamPolicy from '../preview/fixtures/spam-policy.json';
import galleryCapture from '../preview/fixtures/widget-gallery.json';
import widgetsCapture from '../preview/fixtures/widgets.json';
import economyRules from '../preview/fixtures/economy-earning-rules.json';
import type { Json } from '../preview/fakeApi';
import { sampleCustomCommands } from '../preview/replies/commands';
import { sampleReplies } from '../preview/sampleReplies';

// The oracle is the checked-in capture of a fresh NomNomzBot install (preview/fixtures/*.json): every list
// below is read from what the server itself answered on a throwaway install, plus the comparison operators
// read from the server source file. A name the sample invents is a name the capture does not hold.
const channel = '/api/v1/channels/sample-channel';

type Row = Record<string, Json>;

function serve(path: string): Json {
  const url = new URL(path, 'http://sample.test');
  const reply = sampleReplies.find((entry) => {
    const pattern = new RegExp(`^${entry.path.replaceAll('*', '[^/]+')}$`);

    return entry.method === 'GET' && pattern.test(url.pathname);
  });

  if (!reply)
    throw new Error(`The sample does not answer ${path}`);

  return reply.body(url);
}

function rows(path: string): Row[] {
  const body = serve(path) as { data: Json };
  const data = body.data;

  return (Array.isArray(data)
    ? data
    : []) as Row[];
}

const helperKeys = (helpersCommand.data as { key: string }[]).map((helper) => helper.key);

const capturedNames = commandsCapture.data.map((command) => command.name);

// A helper key the server lists may carry a placeholder (`random<n>`); a used key matches it by its stem.
function isListedHelper(key: string): boolean {
  const stem = key.replace(/\d+$/, '');

  return helperKeys.some((helper) => {
    const open = helper.indexOf('<');

    return helper === key ||
      helper.replace(/<[^>]+>/, '') === stem ||
      (open >= 0 && key.startsWith(helper.slice(0, open)));
  });
}

function usedKeys(template: string): string[] {
  const matches = template.matchAll(/\{([a-zA-Z][\w.]*)\}/g);

  return Array.from(matches, (used) =>
    used[1] as string);
}

describe('the commands preview', () => {
  const builtinNames = builtins.data.map((builtin) => builtin.builtinKey);
  const presetNames = commandPresets.data.map((preset) => preset.key);
  const realNames = new Set<string>(merge(capturedNames, builtinNames, presetNames));

  it('lists the commands a fresh install holds, with their real tiers and permission levels', () => {
    const served = rows(`${channel}/commands`);
    const captured = new Map<string, Row>(
      commandsCapture.data.map((command) => [command.name, command as unknown as Row]),
    );

    const drifted = served
      .filter((command) => captured.has(command.name as string))
      .filter((command) => {
        const real = captured.get(command.name as string) as Row;

        return command.tier !== real.tier ||
          command.minPermissionLevel !== real.minPermissionLevel ||
          (command.presetKey ?? null) !== (real.presetKey ?? null);
      })
      .map((command) => command.name);

    expect(drifted).toEqual([]);
    expect(served.map((command) => command.name)).toEqual(expect.arrayContaining(capturedNames));
  });

  it('adds only custom commands whose names are free and whose replies use helpers the server lists', () => {
    expect(sampleCustomCommands.length).toBeGreaterThan(0);

    const shadowing = sampleCustomCommands
      .filter((command) => realNames.has(command.name))
      .map((command) => command.name);
    const templates = sampleCustomCommands.map((command) => command.response);
    const keys = templates.flatMap(usedKeys);
    const unknown = keys.filter((key) => !isListedHelper(key));

    expect(shadowing).toEqual([]);
    expect(templates.filter((template) => template.includes('{{'))).toEqual([]);
    expect(keys.length).toBeGreaterThan(0);
    expect(unknown).toEqual([]);
  });

  it('serves the built-in commands the server seeds, and no other', () => {
    const served = rows(`${channel}/builtins`).map((builtin) => builtin.builtinKey);

    expect(served).toEqual(builtins.data.map((builtin) => builtin.builtinKey));
  });
});

function fieldNames(action: { fields: { name: string }[] }): string[] {
  return action.fields.map((field) => field.name);
}

describe('the pipelines preview', () => {
  const { actions, conditions } = pipelineActions.data;
  const types = new Set<string>(actions.map((action) => action.type));
  const conditionTypes = new Set<string>(conditions.map((condition) => condition.type));
  const fieldsOf = new Map<string, Set<string>>(
    actions.map((action) => [action.type, new Set<string>(fieldNames(action))]),
  );

  const serverFile = join(
    process.cwd(),
    '..',
    'server',
    'src',
    'NomNomzBot.Infrastructure',
    'Platform',
    'Pipeline',
    'CoreActions',
    'ComparisonCondition.cs',
  );
  const operatorCases = existsSync(serverFile)
    ? readFileSync(serverFile, 'utf8').matchAll(/case "([a-z_]+)":/g)
    : [];
  const operators = Array.from(operatorCases, (match) =>
    match[1] as string);

  function stepsOf(): Row[] {
    const listed = rows(`${channel}/pipelines`);

    return listed.flatMap((pipeline) => {
      const detail = serve(`${channel}/pipelines/${pipeline.id as string}`) as {
        data: { graph: { steps: Row[] } };
      };

      return detail.data.graph.steps;
    });
  }

  function unknownFieldsOf(action: Record<string, Json>): string[] {
    const type = action.type as string;

    return Object.keys(action)
      .filter((name) => name !== 'type')
      .filter((name) => !fieldsOf.get(type)?.has(name))
      .map((name) => `${type}.${name}`);
  }

  it('builds every step from an action type and fields the server lists', () => {
    const steps = stepsOf();

    expect(steps.length).toBeGreaterThan(8);

    const actions = steps
      .filter((step) => typeof step.block_kind !== 'string')
      .map((step) => step.action as Record<string, Json>);
    const unknownTypes = actions
      .map((action) => action.type as string)
      .filter((type) => !types.has(type));
    const unknownFields = actions.flatMap((action) => unknownFieldsOf(action));

    expect(unknownTypes).toEqual([]);
    expect(unknownFields).toEqual([]);
  });

  it('uses condition types and comparison operators the server knows', () => {
    const used = stepsOf()
      .map((step) => step.condition as Record<string, Json> | null)
      .filter((condition) => condition !== null);

    expect(used.length).toBeGreaterThan(0);
    expect(operators.length).toBeGreaterThan(5);

    const unknownTypes = used
      .map((condition) => condition.type as string)
      .filter((type) => !conditionTypes.has(type));
    const unknownOperators = used
      .filter((condition) => condition.type === 'comparison')
      .map((condition) => String(condition.operator))
      .filter((operator) => !operators.includes(operator));

    expect(unknownTypes).toEqual([]);
    expect(unknownOperators).toEqual([]);
  });

  it('shows run history for the pipeline, from steps the pipeline really has', () => {
    const runs = rows(`${channel}/pipeline-executions`);

    expect(runs.length).toBeGreaterThan(2);

    const fieldSet = [
      'id',
      'pipelineId',
      'triggerKind',
      'status',
      'hostCallCount',
      'durationMs',
      'errorMessage',
      'startedAt',
      'completedAt',
    ];
    const pipelineIds = new Set(rows(`${channel}/pipelines`).map((pipeline) => pipeline.id));

    // cspell:disable-next-line -- the bot's wire name for a run status
    const statuses = ['completed', 'failed', 'running', 'cancelled', 'timed_out'];

    for (const run of runs) {
      expect(statuses).toContain(run.status);
      expect(Object.keys(run).sort()).toEqual([...fieldSet].sort());
      expect(pipelineIds.has(run.pipelineId as string)).toBe(true);
    }
  });
});

// cspell:disable-next-line -- the bot's wire name for the list of settings
const listName = 'catalogue';

type Policy = { settings: Row } & Record<typeof listName, Row[]>;

describe('the moderation preview', () => {
  it('shows the spam-defense settings and groups the server sends, and no others', () => {
    const body = serve(`${channel}/spam-defense/policy`) as { data: Policy };
    const real = spamPolicy.data as unknown as Policy;

    const servedKeys = body.data[listName].map((knob) => knob.key);
    const realKeys = real[listName].map((knob) => knob.key);
    const servedGroups = new Set(body.data[listName].map((knob) => knob.group));
    const realGroups = new Set(real[listName].map((knob) => knob.group));

    expect(Object.keys(body.data.settings).sort()).toEqual(Object.keys(real.settings).sort());
    expect(servedKeys).toEqual(realKeys);
    expect(servedGroups).toEqual(realGroups);
  });
});

describe('the overlays preview', () => {
  it('lists the first-party widgets a fresh install gets, and only gallery widgets besides, by their real names', () => {
    const names = rows(`${channel}/widgets`).map((widget) => widget.name as string);
    const seeded = widgetsCapture.data.map((widget) => widget.name);
    const galleryNames = galleryCapture.data.map((item) => item.name);

    expect(names.slice(0, seeded.length)).toEqual(seeded);

    const known = merge(seeded, galleryNames);

    expect(names.filter((name) => !known.includes(name))).toEqual([]);
  });
});

describe('the loyalty preview', () => {
  it('earns from the sources the server defines, and sells through sink types it knows', () => {
    const sources = economyRules.data.map((rule) => rule.source);
    const served = (serve(`${channel}/economy/earning-rules`) as { data: Row[] }).data.map((rule) => rule.source);

    expect(served.sort()).toEqual([...sources].sort());

    const sinks = rows(`${channel}/economy/catalog`).map((item) => item.sinkType);

    expect(sinks.length).toBeGreaterThan(0);

    // The server runs a store item's sink as a pipeline (EventResponseExecutor and CatalogService); no other sink is shown.
    expect(sinks.filter((sink) => sink !== 'pipeline')).toEqual([]);
  });
});

describe('the music preview', () => {
  it('shows the request settings the server sends, with no setting the server lacks', () => {
    const served = (serve(`${channel}/music/config`) as { data: Row }).data;

    const real = Object.keys((musicConfigCapture as { data: Row }).data);

    expect(Object.keys(served).filter((key) => !real.includes(key))).toEqual([]);
  });
});

describe('the text-to-speech preview', () => {
  const captured = voiceOptions.data.items.map((item) => item.value);

  it('lists only voices the server lists, and speaks history in those voices', () => {
    const voices = rows(`${channel}/tts/voices`).map((voice) => voice.id as string);
    const spoken = rows(`${channel}/tts/history`).map((entry) => entry.voiceId as string);

    expect(voices.length).toBeGreaterThan(10);
    expect(voices.filter((id) => !captured.includes(id))).toEqual([]);
    expect(spoken.filter((id) => !voices.includes(id))).toEqual([]);
  });

  it('starts from the defaults the server gives a new channel', () => {
    const config = (serve(`${channel}/tts/config`) as { data: Row }).data;

    expect(config.mode).toBe('self_host');
    expect(config.defaultProvider).toBe('edge');
    expect(config.maxCharacters).toBe(500);
    expect(config.cooldownSeconds).toBe(0);
    expect(config.minPermission).toBe('everyone');
  });
});

describe('the connections preview', () => {
  it('names only providers the server lists', () => {
    const statusNames = integrationStatus.data.map((entry) => entry.provider);
    const listNames = integrationsCapture.data.integrations.map((entry) => entry.id);
    const providers = new Set<string>(merge(statusNames, listNames));
    const statusBody = serve(`${channel}/integrations/status`) as { data: Row[] };
    const listBody = serve(`${channel}/integrations`) as { data: { integrations: Row[] } };
    const statuses = statusBody.data.map((entry) => entry.provider as string);
    const listed = listBody.data.integrations.map((entry) => entry.id as string);
    const named = merge(statuses, listed);

    expect(named.filter((provider) => !providers.has(provider))).toEqual([]);
  });
});

describe('the fixtures themselves', () => {
  const directory = join(process.cwd(), 'src', 'views', 'site', 'home', 'preview', 'fixtures');
  const files = readdirSync(directory).filter((file) => file.endsWith('.json'));

  it('hold a capture of every area a fresh install fills', () => {
    expect(files.length).toBeGreaterThanOrEqual(20);
  });

  it.each(files)('%s carries no id, address, token, time or person from the capture machine', (file) => {
    const text = readFileSync(join(directory, file), 'utf8');

    expect(text).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i);
    expect(text).not.toMatch(/localhost|127\.0\.0\.1|192\.168\./);
    expect(text).not.toMatch(/token=(?!sample)/);
    expect(text).not.toMatch(/[\w.+-]+@[\w-]+\.[\w.]+/);

    // The helper lists carry the server's own example value for a time helper; every other time is relative.
    const absoluteTimes = file.startsWith('helpers-')
      ? []
      : text.match(/\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/) ?? [];

    expect(absoluteTimes).toEqual([]);
  });
});
