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

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref, type PropType } from 'vue';

import { fieldLabels, unlinkedLabels } from '@/__tests__/fieldLinks';
import { queryClient } from '@/api/queryClient';
import type { CompileFeedback, EditorFiles } from '@/composables/useEditorBridge';
import { i18n } from '@/i18n';
import { adminHandlers } from '@/realtime/admin';
import { dashboardRoutes } from '@/router/dashboard';

import AdminContentView from '../AdminContentView.vue';
import en from '../i18n/en.json';
import { adminOnly, dashboardAdminContentRoute } from '../route';

type Options = {
  params?: { query?: Record<string, unknown>; path?: Record<string, unknown> };
  body?: unknown;
};
type Call = {
  method: string;
  path: string;
  options?: Options;
};

const calls: Call[] = [];

const audioPath = '/api/v{version}/platform/content/audio-assets';
const audioItemPath = `${audioPath}/{id}`;

type AudioAsset = {
  id: string;
  displayName: string;
  fileName: string;
  sizeBytes: number;
  usedByTemplates: string[];
};

const stinger: AudioAsset = {
  id: 'asset1',
  displayName: 'Raid stinger',
  fileName: 'raid.mp3',
  sizeBytes: 2_097_152,
  usedByTemplates: [],
};

let audioAssets: AudioAsset[] = [];

const definition = {
  id: 'def1',
  kind: 'command',
  key: 'hug',
  displayName: 'Hug command',
  description: 'A warm hug',
  currentVersion: 2,
  createdAt: '2026-10-01T00:00:00Z',
};

const defaultDraftPayload = '{"reply":"draft hug"}';
let draftPayload = defaultDraftPayload;
const previewedCount = 7;

const catalog = {
  actions: [
    {
      type: 'send_message',
      category: { key: 'pipeline.category.chat' },
      description: { key: 'pipeline.send_message.description' },
      fields: [
        {
          name: 'message',
          kind: 'text',
          required: true,
          repeatable: false,
          description: null,
          templated: false,
          declaresVariable: false,
        },
      ],
    },
    {
      type: 'play_sound',
      category: { key: 'pipeline.category.media' },
      description: { key: 'pipeline.play_sound.description' },
      fields: [
        {
          name: 'volume',
          kind: 'number',
          required: false,
          repeatable: false,
          description: null,
          templated: false,
          declaresVariable: false,
        },
      ],
    },
  ],
  conditions: [],
};

const pipelineStep = {
  id: 'step-a',
  parent_step_id: null,
  branch: null,
  block_kind: null,
  block_config: null,
  order: 0,
  action: { type: 'send_message', message: 'welcome in' },
  condition: null,
  continue_on_error: false,
  stop_on_match: false,
};

const pipelinePayload = JSON.stringify({ steps: [pipelineStep] });

// One line: the stand-in editor holds the file in a one-line input.
const scriptSource = 'const roll = nnz.random.int(1, Number(bot.args[0]) || 6);';
const scriptPayload = JSON.stringify({ sourceCode: scriptSource });

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('chan-1') }),
}));

// The signed-in admin holds every platform key, so every action button shows.
vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => {
      calls.push({
        method: 'GET',
        path,
        options,
      });

      if (path === audioPath)
        return {
          data: {
            data: audioAssets,
            hasMore: false,
            total: audioAssets.length,
          },
          response: { status: 200, ok: true },
        };

      if (path.endsWith('/pipelines/actions'))
        return { data: { data: catalog }, response: { status: 200, ok: true } };

      if (path.endsWith('/versions/{versionId}'))
        return {
          data: {
            data: {
              id: options?.params?.path?.versionId,
              version: 1,
              payloadJson: draftPayload,
              publishedAt: null,
            },
          },
          response: { status: 200, ok: true },
        };

      if (path.endsWith('/publish-jobs/{id}'))
        return {
          data: {
            data: {
              id: 'job1',
              definitionId: 'def1',
              toVersion: 1,
              mode: 'update_in_place_where_untouched',
              status: 'completed',
              previewAffectedCount: previewedCount,
              previewSkippedCount: 2,
              confirmedAffectedCount: previewedCount,
              rebuildFailedWidgetIds: [],
              validationFailedPipelineIds: [],
              validationFailedCodeScriptIds: [],
              updateFailedTemplateRowIds: [],
            },
          },
          response: { status: 200, ok: true },
        };

      if (path.endsWith('/definitions/{id}'))
        return {
          data: {
            data: {
              definition,
              versions: [
                {
                  id: 'v2',
                  version: 2,
                  payloadJson: '{"reply":"hug"}',
                  publishedAt: '2026-10-02T00:00:00Z',
                },
                {
                  id: 'v1',
                  version: 1,
                  payloadJson: '{}',
                  publishedAt: null,
                },
              ],
              installs: {
                installedCount: 12,
                behindCount: 3,
                editedCount: 2,
              },
            },
          },
          response: { status: 200, ok: true },
        };

      return {
        data: {
          data: [definition],
          hasMore: false,
          total: 1,
        },
        response: { status: 200, ok: true },
      };
    },
    POST: async (path: string, options?: Options) => {
      calls.push({
        method: 'POST',
        path,
        options,
      });

      if (path === audioPath) {
        const form = (options as { bodySerializer?: () => FormData }).bodySerializer?.();
        const file = form?.get('file') as File;
        const created: AudioAsset = {
          id: 'asset2',
          displayName: String(form?.get('displayName') ?? ''),
          fileName: file.name,
          sizeBytes: file.size,
          usedByTemplates: [],
        };

        audioAssets = [...audioAssets, created];

        return { data: { data: created }, response: { status: 200, ok: true } };
      }

      if (path.endsWith('/publish-preview'))
        return {
          data: {
            data: {
              affectedCount: previewedCount,
              skippedCount: 2,
              sampleTenantNames: ['alpha_channel', 'beta_channel'],
            },
          },
          response: { status: 200, ok: true },
        };

      if (path.endsWith('/publish'))
        return {
          data: {
            data: {
              id: 'job1',
              definitionId: 'def1',
              toVersion: 1,
              mode: 'update_in_place_where_untouched',
              status: 'running',
              previewAffectedCount: previewedCount,
              previewSkippedCount: 2,
              rebuildFailedWidgetIds: [],
              validationFailedPipelineIds: [],
              validationFailedCodeScriptIds: [],
              updateFailedTemplateRowIds: [],
            },
          },
          response: { status: 200, ok: true },
        };

      return { data: { data: {} }, response: { status: 200, ok: true } };
    },
    DELETE: async (path: string, options?: Options) => {
      calls.push({
        method: 'DELETE',
        path,
        options,
      });

      if (path === audioItemPath) {
        audioAssets = audioAssets.filter((row) => row.id !== options?.params?.path?.id);
      }

      return { data: undefined, response: { status: 200, ok: true } };
    },
  },
}));

// The real editor is a full-window page in a frame. This stands in for that frame only: it shows the one file,
// lets the test change its text, and answers the page's Save through the same callback the real bridge calls.
const editorFrame = defineComponent({
  name: 'CodeEditor',
  props: {
    entry: { type: String, required: true },
    files: { type: Object as PropType<EditorFiles>, required: true },
    save: {
      type: Function as PropType<(files: EditorFiles) => Promise<CompileFeedback>>,
      required: true,
    },
  },
  setup(props) {
    const source = ref(props.files[props.entry] ?? '');
    const message = ref('');

    async function saveFile(): Promise<void> {
      const feedback = await props.save({ [props.entry]: source.value });

      message.value = feedback.message;
    }

    return () => h('div', { 'data-code-editor': props.entry }, [
      h('input', {
        'data-editor-source': '',
        value: source.value,
        onInput: (event: Event) => {
          source.value = (event.target as HTMLInputElement).value;
        },
      }),
      h('button', { 'data-editor-save': '', onClick: saveFile }, 'Save'),
      h('p', { 'data-editor-message': '' }, message.value),
    ]);
  },
});

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  definition.kind = 'command';
  draftPayload = defaultDraftPayload;
  audioAssets = [{ ...stinger }];

  Element.prototype.hasPointerCapture = (): boolean => false;
  Element.prototype.releasePointerCapture = (): void => undefined;
  Element.prototype.scrollIntoView = (): void => undefined;
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  queryClient.clear();
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(): Promise<void> {
  mounted = mount(AdminContentView, {
    attachTo: document.body,
    global: {
      stubs: { CodeEditor: editorFrame },
      plugins: [
        i18n,
        pinia,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await settle();
}

const text = (): string => document.body.textContent ?? '';
const byText = (selector: string, label: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>(selector)].find(
    (node) => node.textContent?.trim() === label,
  );

const first = (selector: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(selector)!;

async function type(selector: string, value: string): Promise<void> {
  const field = document.body.querySelector<HTMLInputElement>(selector)!;

  field.value = value;
  field.dispatchEvent(new Event('input'));
  await settle();
}

async function click(target: HTMLElement): Promise<void> {
  target.click();
  await settle();
}

async function chooseKind(label: string): Promise<void> {
  first('[data-field-kind]').dispatchEvent(
    new PointerEvent('pointerdown', {
      bubbles: true,
      button: 0,
      pointerType: 'mouse',
    }),
  );

  await settle();

  const option = [...document.body.querySelectorAll<HTMLElement>('[role="option"]')].find(
    (node) => node.textContent?.trim() === label,
  )!;

  option.dispatchEvent(
    new PointerEvent('pointerup', {
      bubbles: true,
      button: 0,
      pointerType: 'mouse',
    }),
  );

  option.click();
  await settle();
}

async function pickDraftVersion(): Promise<void> {
  await open();
  await click(first('[data-definition-row]'));

  const versions = [...document.body.querySelectorAll<HTMLElement>('[data-version-row]')];

  await click(versions[1]!);
}

describe('AdminContentView', () => {
  it('lists the definitions and loads the history and install counts of the picked one', async () => {
    await open();

    expect(document.body.querySelectorAll('[data-definition-row]')).toHaveLength(1);
    expect(text()).toContain('Hug command');

    (document.body.querySelector('[data-definition-row]') as HTMLElement).click();
    await settle();

    const detail = calls.find((call) => call.path.endsWith('/definitions/{id}'))!;

    expect(detail.options?.params?.path?.id).toBe('def1');
    expect(document.body.querySelectorAll('[data-version-row]')).toHaveLength(2);
    expect(text()).toContain('12');
  });

  it('lists definitions and versions as SummaryRows inside RowGroups, each with its title', async () => {
    await open();

    const definition = document.body.querySelector<HTMLElement>('[data-definition-row]')!;

    expect(definition.tagName).toBe('BUTTON');
    expect(definition.dataset.slot).toBe('summary-row-body');
    expect(definition.querySelector('[data-slot="summary-row-title"]')!.textContent).toContain('Hug command');
    expect(definition.closest('.rounded-xl')).not.toBeNull();

    definition.click();
    await settle();

    expect(definition.className).toContain('bg-accent');

    const versions = [...document.body.querySelectorAll<HTMLElement>('[data-version-row]')];

    for (const version of versions) {
      expect(version.dataset.slot).toBe('summary-row-body');
      expect(version.closest('.rounded-xl')).not.toBeNull();
    }

    expect(versions[0]!.querySelector('[data-slot="summary-row-title"]')!.textContent).toContain('Version');
    expect(document.body.querySelector('ul.divide-y')).toBeNull();
  });

  it('links every label of the page filter and the create form to its control', async () => {
    await open();

    expect(fieldLabels(document.body)).toHaveLength(1);

    byText('button', 'New definition')!.click();
    await settle();

    const dialog = document.body.querySelector<HTMLElement>('[role="dialog"]')!;

    expect(fieldLabels(dialog)).toHaveLength(5);
    expect(unlinkedLabels(document.body)).toEqual([]);
  });

  it('creates a definition with the typed fields and the payload written in the editor', async () => {
    await open();
    byText('button', 'New definition')!.click();
    await settle();
    await chooseKind('Command');
    await type('[data-field-key]', 'wave');
    await type('[data-field-name]', 'Wave command');
    await click(first('[data-field-payload]'));

    expect(first('[data-code-editor]').dataset.codeEditor).toBe('payload.json');
    expect(document.body.querySelector('textarea')).toBeNull();

    await type('[data-editor-source]', '{"reply":"wave"}');
    await click(first('[data-editor-save]'));
    byText('[role="dialog"] button', 'Create')!.click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.path).toBe('/api/v{version}/platform/content/definitions');

    expect(post.options?.body).toMatchObject({
      kind: 'command',
      key: 'wave',
      displayName: 'Wave command',
      payloadJson: '{"reply":"wave"}',
    });
  });

  it('refuses a payload that is not JSON in the editor and keeps it from the create request', async () => {
    await open();
    byText('button', 'New definition')!.click();
    await settle();
    await chooseKind('Command');
    await type('[data-field-key]', 'wave');
    await type('[data-field-name]', 'Wave command');
    await click(first('[data-field-payload]'));
    await type('[data-editor-source]', 'not json');
    await click(first('[data-editor-save]'));

    expect(first('[data-editor-message]').textContent).toBe('The payload must be valid JSON.');

    byText('[role="dialog"] button', 'Create')!.click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.options?.body).toMatchObject({ payloadJson: '{}' });
  });

  it('drafts a new version in the editor, seeded from the picked version and saved as the draft body', async () => {
    await pickDraftVersion();

    const read = calls.find((call) => call.path.endsWith('/versions/{versionId}'))!;

    expect(read.options?.params?.path).toMatchObject({ id: 'def1', versionId: 'v1' });

    await click(byText('button', 'Draft new version')!);

    expect((first('[data-editor-source]') as HTMLInputElement).value).toBe(draftPayload);

    await type('[data-editor-source]', '{"reply":"big hug"}');
    await click(first('[data-editor-save]'));

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.path).toBe('/api/v{version}/platform/content/definitions/{id}/versions');
    expect(post.options?.params?.path?.id).toBe('def1');
    expect(post.options?.body).toEqual({ payloadJson: '{"reply":"big hug"}' });
    expect(first('[data-editor-message]').textContent).toBe('The draft is saved as a new version.');
  });

  it('offers the server kinds as a Select and creates with the chosen kind', async () => {
    await open();
    byText('button', 'New definition')!.click();
    await settle();

    expect(document.body.querySelector('input[data-field-kind]')).toBeNull();

    first('[data-field-kind]').dispatchEvent(
      new PointerEvent('pointerdown', {
        bubbles: true,
        button: 0,
        pointerType: 'mouse',
      }),
    );

    await settle();

    const labels = [...document.body.querySelectorAll('[role="option"]')].map((node) =>
      node.textContent?.trim());

    expect(labels).toEqual([
      'Command',
      'Widget',
      'Pipeline',
      'Code script',
      'Event response',
      'Timer',
      'Reward',
      'Pick list',
      'Sound clip',
    ]);

    document.body.querySelector<HTMLElement>('[role="option"]')!.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }),
    );

    await settle();
    await chooseKind('Widget');
    await type('[data-field-key]', 'clock');
    await type('[data-field-name]', 'Clock widget');
    byText('[role="dialog"] button', 'Create')!.click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.options?.body).toMatchObject({
      kind: 'widget',
      key: 'clock',
      displayName: 'Clock widget',
    });
  });

  it('opens a command draft in the JSON editor, not the step editor', async () => {
    await pickDraftVersion();
    await click(byText('button', 'Draft new version')!);

    expect(document.body.querySelector('[data-code-editor]')).not.toBeNull();
    expect(document.body.querySelector('[data-pipeline-payload-editor]')).toBeNull();
  });

  it('opens a pipeline draft in the step editor with the steps of the payload', async () => {
    definition.kind = 'pipeline';
    draftPayload = pipelinePayload;
    await pickDraftVersion();
    await click(byText('button', 'Draft new version')!);

    expect(document.body.querySelector('[data-code-editor]')).toBeNull();
    expect(first('[data-pipeline-payload-editor]')).not.toBeNull();

    await vi.waitFor(() => {
      expect(document.body.querySelectorAll('[data-step]')).toHaveLength(1);
    }, { timeout: 5000 });

    const cards = [...document.body.querySelectorAll('[data-step]')];

    expect(cards).toHaveLength(1);
    expect(cards[0]!.getAttribute('data-step-id')).toBe('step-a');
  });

  it('saves a pipeline draft with an added step in the shape the server reads', async () => {
    definition.kind = 'pipeline';
    draftPayload = pipelinePayload;
    await pickDraftVersion();
    await click(byText('button', 'Draft new version')!);

    await vi.waitFor(() => {
      expect(document.body.querySelector('[data-steps-add]')).not.toBeNull();
    }, { timeout: 5000 });

    await click(first('[data-steps-add]'));

    await vi.waitFor(() => {
      expect(document.body.querySelector('[data-palette-action="play_sound"]')).not.toBeNull();
    }, { timeout: 5000 });

    await click(first('[data-palette-action="play_sound"]'));
    await click(first('[data-pipeline-save]'));

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.path).toBe('/api/v{version}/platform/content/definitions/{id}/versions');
    expect(post.options?.params?.path?.id).toBe('def1');

    const body = post.options?.body as { payloadJson: string };
    const saved = JSON.parse(body.payloadJson) as {
      steps: Record<string, unknown>[];
    };

    expect(Object.keys(saved)).toEqual(['steps']);
    expect(saved.steps).toHaveLength(2);
    expect(saved.steps[0]).toEqual(pipelineStep);

    expect(saved.steps[1]).toMatchObject({
      parent_step_id: null,
      branch: null,
      block_kind: null,
      order: 1,
      action: { type: 'play_sound' },
      condition: null,
      continue_on_error: false,
      stop_on_match: false,
    });

    expect(typeof saved.steps[1]!.id).toBe('string');
    expect(first('[data-pipeline-message]').textContent).toBe('The draft is saved as a new version.');
  });

  it('keeps the raw JSON editor one press away on a pipeline draft', async () => {
    definition.kind = 'pipeline';
    draftPayload = pipelinePayload;
    await pickDraftVersion();
    await click(byText('button', 'Draft new version')!);
    await click(first('[data-pipeline-edit-json]'));

    expect(document.body.querySelector('[data-pipeline-payload-editor]')).toBeNull();

    expect(JSON.parse((first('[data-editor-source]') as HTMLInputElement).value)).toEqual({
      steps: [pipelineStep],
    });
  });

  it('opens a pipeline payload the step model cannot hold as JSON, so nothing is lost', async () => {
    definition.kind = 'pipeline';
    draftPayload = '{"steps":[],"note":"keep me"}';
    await pickDraftVersion();
    await click(byText('button', 'Draft new version')!);

    expect(document.body.querySelector('[data-pipeline-payload-editor]')).toBeNull();
    expect((first('[data-editor-source]') as HTMLInputElement).value).toBe(draftPayload);
  });

  it('opens a code script draft in the script code editor with the source of the payload', async () => {
    definition.kind = 'code_script';
    draftPayload = scriptPayload;
    await pickDraftVersion();
    await click(byText('button', 'Draft new version')!);

    expect(document.body.querySelector('[data-code-editor="payload.json"]')).toBeNull();
    expect(first('[data-code-editor]').getAttribute('data-code-editor')).toBe('index.ts');
    expect((first('[data-editor-source]') as HTMLInputElement).value).toBe(scriptSource);
  });

  it('saves a code script draft as the payload shape the server reads, with the edited source', async () => {
    definition.kind = 'code_script';
    draftPayload = scriptPayload;
    await pickDraftVersion();
    await click(byText('button', 'Draft new version')!);
    await type('[data-editor-source]', 'nnz.api.chat.send("hello");');
    await click(first('[data-editor-save]'));

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.path).toBe('/api/v{version}/platform/content/definitions/{id}/versions');
    expect(post.options?.params?.path?.id).toBe('def1');

    const body = post.options?.body as { payloadJson: string };

    expect(JSON.parse(body.payloadJson)).toEqual({ sourceCode: 'nnz.api.chat.send("hello");' });
    expect(first('[data-editor-message]').textContent).toBe('The draft is saved as a new version.');
  });

  it('opens a code script payload with an unknown key as JSON, so nothing is lost', async () => {
    definition.kind = 'code_script';
    draftPayload = JSON.stringify({ sourceCode: scriptSource, note: 'keep me' });
    await pickDraftVersion();
    await click(byText('button', 'Draft new version')!);

    expect(first('[data-code-editor]').getAttribute('data-code-editor')).toBe('payload.json');
    expect((first('[data-editor-source]') as HTMLInputElement).value).toBe(draftPayload);
  });

  it('shows what retiring touches before it deletes, then retires', async () => {
    await open();
    (document.body.querySelector('[data-definition-row]') as HTMLElement).click();
    await settle();
    byText('button', 'Retire')!.click();
    await settle();

    expect(text()).toContain('12 installed copies keep running');
    expect(calls.some((call) => call.method === 'DELETE')).toBe(false);

    byText('[role="dialog"] button', 'Retire definition')!.click();
    await settle();

    const removal = calls.find((call) => call.method === 'DELETE')!;

    expect(removal.path).toBe('/api/v{version}/platform/content/definitions/{id}');
    expect(removal.options?.params?.path?.id).toBe('def1');
  });

  it('reloads the page when the hub says another operator changed the catalog', () => {
    const invalidate = vi.fn<(filter: { queryKey: string[] }) => void>();
    const handler = adminHandlers.find((entry) => entry.event === 'ReceiveContentChanged')!;

    handler.handle(
      { queries: { invalidateQueries: invalidate } as unknown as QueryClient, channelId: null },
      { action: 'retired', definitionId: 'def1' } as never,
    );

    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['admin', 'content'] });
  });

  it('registers an admin-only route', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-content');

    expect(registered?.path).toBe('admin/content');
    expect(registered?.beforeEnter).toBe(adminOnly);
    expect(registered?.component).toBe(dashboardAdminContentRoute.component);
    expect(registered?.meta?.comingSoon).toBeUndefined();
  });
});

describe('AdminContentView publishing', () => {
  it('keeps confirm disabled until the preview count shows, then publishes with that count and the chosen mode', async () => {
    await pickDraftVersion();

    expect(first('[data-publish-confirm]').hasAttribute('disabled')).toBe(true);
    expect(document.body.querySelector('[data-publish-count]')).toBeNull();

    await click(first('[data-publish-mode="update_in_place_where_untouched"]'));
    await click(first('[data-publish-preview]'));

    const preview = calls.find((call) => call.path.endsWith('/publish-preview'))!;

    expect(preview.options?.params?.path).toMatchObject({ id: 'def1', versionId: 'v1' });
    expect(preview.options?.body).toEqual({ mode: 'update_in_place_where_untouched' });
    expect(first('[data-publish-count]').textContent).toContain('7 channels');
    expect(first('[data-publish-count]').textContent).toContain('2 skipped');
    expect(text()).toContain('alpha_channel');
    expect(first('[data-publish-confirm]').hasAttribute('disabled')).toBe(false);
    expect(calls.some((call) => call.path.endsWith('/publish'))).toBe(false);

    await click(first('[data-publish-confirm]'));

    const publish = calls.find((call) => call.path.endsWith('/publish'))!;

    expect(publish.options?.params?.path).toMatchObject({ id: 'def1', versionId: 'v1' });

    expect(publish.options?.body).toEqual({
      mode: 'update_in_place_where_untouched',
      confirmedPreviewAffectedCount: previewedCount,
    });
  });

  it('renders the job the publish started, then its result read from the job route', async () => {
    await pickDraftVersion();
    await click(first('[data-publish-preview]'));
    await click(first('[data-publish-confirm]'));

    const read = calls.find((call) => call.path.endsWith('/publish-jobs/{id}'))!;

    expect(read.options?.params?.path).toMatchObject({ id: 'job1' });
    expect(first('[data-publish-job]').dataset.status).toBe('completed');
    expect(first('[data-publish-job]').textContent).toContain('Completed');
    expect(first('[data-publish-job]').textContent).toContain('7 channels updated');
  });

  it('drops the preview when the mode changes, so confirm needs a fresh count', async () => {
    await pickDraftVersion();
    await click(first('[data-publish-preview]'));

    expect(first('[data-publish-confirm]').hasAttribute('disabled')).toBe(false);

    await click(first('[data-publish-mode="force"]'));

    expect(document.body.querySelector('[data-publish-count]')).toBeNull();
    expect(first('[data-publish-confirm]').hasAttribute('disabled')).toBe(true);
  });

  it('keeps one primary action and gives force publishing a destructive confirm', async () => {
    await pickDraftVersion();

    expect(first('[data-publish-confirm]').dataset.variant).toBe('default');
    expect(first('[data-publish-preview]').dataset.variant).toBe('outline');

    await click(first('[data-publish-mode="force"]'));

    expect(first('[data-publish-confirm]').dataset.variant).toBe('destructive');
    expect(first('[data-publish-force-warning]').textContent).toContain('overwrites edits');
  });
});

describe('AdminContentView audio assets', () => {
  it('lists each platform audio asset with its name, file and size', async () => {
    audioAssets = [
      { ...stinger },
      {
        id: 'asset3',
        displayName: 'Subscriber chime',
        fileName: 'chime.ogg',
        sizeBytes: 512_000,
        usedByTemplates: ['welcome'],
      },
    ];

    await open();

    const rows = [...document.body.querySelectorAll<HTMLElement>('[data-audio-row]')];

    expect(rows).toHaveLength(2);
    expect(rows[0]!.textContent).toContain('Raid stinger');
    expect(rows[0]!.textContent).toContain('2 MB');
    expect(rows[1]!.textContent).toContain('Subscriber chime');
    expect(rows[1]!.textContent).toContain('chime.ogg');
    expect(calls.some((call) => call.method === 'GET' && call.path === audioPath)).toBe(true);
  });

  it('says so when there are no audio assets', async () => {
    audioAssets = [];
    await open();

    expect(document.body.querySelectorAll('[data-audio-row]')).toHaveLength(0);
    expect(text()).toContain('No audio files yet.');
  });

  it('uploads the chosen file as multipart and shows the new asset', async () => {
    await open();

    const input = first('[data-audio-file]') as HTMLInputElement;
    const file = new File(['sound'], 'win.wav', { type: 'audio/wav' });

    Object.defineProperty(input, 'files', { value: [file] });
    input.dispatchEvent(new Event('change'));
    await settle();

    const post = calls.find((call) => call.method === 'POST' && call.path === audioPath)!;
    const form = (post.options as { bodySerializer: () => FormData }).bodySerializer();

    expect((form.get('file') as File).name).toBe('win.wav');
    expect(document.body.querySelectorAll('[data-audio-row]')).toHaveLength(2);
    expect(text()).toContain('win.wav');
  });

  it('asks before deleting, then sends DELETE with the id and removes the row', async () => {
    await open();
    first('[data-row-delete]').click();
    await settle();

    expect(calls.some((call) => call.method === 'DELETE')).toBe(false);
    expect(first('[role="alertdialog"]').textContent).toContain('Raid stinger');

    first('[data-confirm-action]').click();
    await settle();

    const removal = calls.find((call) => call.method === 'DELETE')!;

    expect(removal.path).toBe(audioItemPath);
    expect(removal.options?.params?.path?.id).toBe('asset1');
    expect(document.body.querySelectorAll('[data-audio-row]')).toHaveLength(0);
  });
});
