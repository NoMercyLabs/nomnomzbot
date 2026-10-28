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

import { useQueryClient } from '@tanstack/vue-query';
import { ref, type Ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  fetchWidgetProject,
  fetchWidgetSamples,
  fetchWidgetSdkTypes,
  fetchWidgetVersions,
  rollbackWidget,
  runWidgetPreviewAction,
  saveWidgetProject,
  type WidgetProject,
} from '@/api/widgetCode';
import { widgetsKey, type Widget } from '@/api/widgets';
import type {
  CompileFeedback,
  EditorFiles,
  EditorHistoryRunner,
  EditorOutcome,
  PreviewActionRequest,
  PreviewActionResult,
  VersionsPage,
  VersionSummary,
} from '@/composables/useEditorBridge';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { merge } from '@/lib/array';
import { errorText } from '@/lib/errors';
import type { JsonObject } from '@/lib/json';

export type OverlayEditorSession = {
  project: WidgetProject;
  sdkTypes: string | null;
  samples: JsonObject;
  samplesError: string | undefined;
  versions: VersionsPage;
};

export type OverlayEditor = {
  session: Ref<OverlayEditorSession | null>;
  opening: Ref<boolean>;
  failure: Ref<string | null>;
  open: () => Promise<void>;
  close: () => Promise<void>;
  save: (files: EditorFiles) => Promise<CompileFeedback>;
  previewAction: (request: PreviewActionRequest) => Promise<PreviewActionResult>;
  history: EditorHistoryRunner;
};

// Everything the code editor asks of one overlay: its files, save and compile, the typed `nnz` declarations,
// the sample events and preview actions of its fire bar, and its saved versions (read, load more, roll back).
export function useOverlayEditor(widget: () => Widget) {
  const { t } = useI18n();
  const { channelId } = useCurrentChannel();
  const queries = useQueryClient();

  const session = ref<OverlayEditorSession | null>(null);
  const opening = ref(false);
  const failure = ref<string | null>(null);

  // The versions the editor has been shown so far; its list is replaced by each page it is sent.
  let loaded: VersionSummary[] = [];
  let page = 1;
  let hasMore = false;

  function channel(): string {
    return channelId.value as string;
  }

  function shown() {
    return { versions: loaded, hasMore } as const satisfies VersionsPage;
  }

  async function open(): Promise<void> {
    const current = widget();

    opening.value = true;
    failure.value = null;

    try {
      const activeId = current.activeVersionId ?? null;
      const first = await fetchWidgetVersions(channel(), current.id, activeId, 1);
      const project = await fetchWidgetProject(channel(), current.id);
      const sdkTypes = await fetchWidgetSdkTypes(current.id);
      const samples = await samplesOf();

      loaded = first.versions;
      page = 1;
      hasMore = first.hasMore;

      session.value = {
        project,
        sdkTypes,
        samples: samples.table,
        samplesError: samples.error,
        versions: shown(),
      };
    } catch (error) {
      failure.value = errorText(error, t);
    } finally {
      opening.value = false;
    }
  }

  // A failed samples read still opens the editor; its fire bar then says so and fires nothing.
  async function samplesOf(): Promise<{ table: JsonObject; error: string | undefined }> {
    try {
      return { table: await fetchWidgetSamples(channel()), error: undefined };
    } catch {
      return { table: {}, error: t('dashboard.overlays.editor.samplesFailed') };
    }
  }

  async function close(): Promise<void> {
    session.value = null;
    await queries.invalidateQueries({ queryKey: widgetsKey(channelId.value) });
  }

  async function save(files: EditorFiles): Promise<CompileFeedback> {
    if (!session.value)
      return { ok: false, message: '' };

    const feedback = await saveWidgetProject(channel(), widget().id, {
      ...session.value.project,
      files,
    });

    return feedback.ok
      ? { ok: true, message: t('dashboard.overlays.editor.compiled') }
      : feedback;
  }

  function previewAction(request: PreviewActionRequest): Promise<PreviewActionResult> {
    return runWidgetPreviewAction(channel(), request);
  }

  async function guarded(run: () => Promise<void>): Promise<EditorOutcome<VersionsPage>> {
    try {
      await run();

      return { ok: true, value: shown() };
    } catch (error) {
      return { ok: false, message: errorText(error, t) };
    }
  }

  const history = {
    loadMore: () =>
      guarded(async () => {
        const id = widget().id;
        const next = await fetchWidgetVersions(channel(), id, currentVersionId(), page + 1);

        loaded = Array.from(merge(loaded, next.versions));
        page += 1;
        hasMore = next.hasMore;
      }),
    rollback: (versionId: string) =>
      guarded(async () => {
        await rollbackWidget(channel(), widget().id, versionId);

        loaded = loaded.map((entry) => ({
          ...entry,
          isCurrent: entry.id === versionId,
        }));
      }),
    // The bot has no way to delete a saved widget version; the editor shows the reason.
    remove: () =>
      Promise.resolve({ ok: false, message: t('dashboard.overlays.editor.deleteUnavailable') }),
  } as const satisfies EditorHistoryRunner;

  function currentVersionId(): string | null {
    return loaded.find((entry) => entry.isCurrent)?.id ?? null;
  }

  return {
    session,
    opening,
    failure,
    open,
    close,
    save,
    previewAction,
    history,
  } as const satisfies OverlayEditor;
}
