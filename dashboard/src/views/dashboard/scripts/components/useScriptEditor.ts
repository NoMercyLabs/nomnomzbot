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
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  codeScriptKey,
  codeScriptsKey,
  deleteScriptVersion,
  fetchScriptProject,
  fetchScriptSdkTypes,
  fetchScriptVersions,
  publishScriptVersion,
  saveScriptProject,
  type ScriptProject,
} from '@/api/codeScripts';
import type {
  CompileFeedback,
  EditorFiles,
  EditorHistoryRunner,
  EditorOutcome,
  VersionsPage,
  VersionSummary,
} from '@/composables/useEditorBridge';
import { merge } from '@/lib/array';
import { errorText } from '@/lib/errors';

type EditorSession = {
  project: ScriptProject;
  sdkTypes: string | null;
  versions: VersionsPage;
};

// The code editor for one script: Edit code loads the project, the typed declarations and the saved versions,
// Save & Compile publishes a new version, and the detail and the list read the new version afterward.
// The history runner answers the editor's version list: load more, make an older version live, delete one.
export function useScriptEditor(scriptId: () => string, liveVersionId: () => string | null) {
  const { t } = useI18n();
  const queries = useQueryClient();
  const session = ref<EditorSession | null>(null);
  const opening = ref(false);
  const missing = ref(false);

  // The versions the editor has been shown so far; its list is replaced by each page it is sent.
  let loaded: VersionSummary[] = [];
  let page = 1;
  let hasMore = false;

  function shown() {
    return { versions: loaded, hasMore } as const satisfies VersionsPage;
  }

  async function readFirstPage(): Promise<void> {
    const first = await fetchScriptVersions(scriptId(), liveVersionId(), 1);

    loaded = first.versions;
    page = 1;
    hasMore = first.hasMore;
  }

  async function open(): Promise<void> {
    opening.value = true;
    missing.value = false;

    try {
      const project = await fetchScriptProject(scriptId());

      if (!project) {
        missing.value = true;

        return;
      }

      const sdkTypes = await fetchScriptSdkTypes(scriptId());

      await readFirstPage();

      session.value = {
        project,
        sdkTypes,
        versions: shown(),
      };
    } finally {
      opening.value = false;
    }
  }

  async function refreshDetail(): Promise<void> {
    await queries.invalidateQueries({ queryKey: codeScriptKey(scriptId()) });
    await queries.invalidateQueries({ queryKey: codeScriptsKey });
  }

  async function save(files: EditorFiles): Promise<CompileFeedback> {
    if (!session.value)
      return { ok: false, message: '' };

    const outcome = await saveScriptProject(scriptId(), { ...session.value.project, files });

    if (outcome.ok) {
      await refreshDetail();
      await readFirstPage();
    }

    return outcome;
  }

  async function guarded(run: () => Promise<void>): Promise<EditorOutcome<VersionsPage>> {
    try {
      await run();

      return { ok: true, value: shown() };
    } catch (error) {
      return { ok: false, message: errorText(error, t) };
    }
  }

  function isLive(versionId: string): boolean {
    return loaded.find((entry) => entry.id === versionId)?.isCurrent ?? false;
  }

  const history = {
    loadMore: () =>
      guarded(async () => {
        const next = await fetchScriptVersions(scriptId(), liveVersionId(), page + 1);

        loaded = Array.from(merge(loaded, next.versions));
        page += 1;
        hasMore = next.hasMore;
      }),
    rollback: (versionId: string) =>
      guarded(async () => {
        await publishScriptVersion(scriptId(), versionId);

        loaded = loaded.map((entry) => ({
          ...entry,
          isCurrent: entry.id === versionId,
        }));

        await refreshDetail();
      }),
    // The live version is not deleted: the script runs it.
    remove: (versionId: string) =>
      guarded(async () => {
        if (isLive(versionId))
          throw new Error(t('dashboard.scripts.versions.deleteLive'));

        await deleteScriptVersion(scriptId(), versionId);

        loaded = loaded.filter((entry) => entry.id !== versionId);
      }),
  } as const satisfies EditorHistoryRunner;

  return {
    session,
    opening,
    missing,
    open,
    save,
    history,
    close: (): void => {
      session.value = null;
    },
  } as const;
}
