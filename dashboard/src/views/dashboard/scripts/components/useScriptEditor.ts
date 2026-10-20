// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useQueryClient } from '@tanstack/vue-query';
import { ref } from 'vue';

import {
  codeScriptKey,
  codeScriptsKey,
  fetchScriptProject,
  fetchScriptSdkTypes,
  saveScriptProject,
  type ScriptProject,
} from '@/api/codeScripts';
import type { CompileFeedback, EditorFiles } from '@/composables/useEditorBridge';

type EditorSession = { project: ScriptProject; sdkTypes: string | null };

// The code editor for one script: Edit code loads the project and the typed declarations, Save & Compile
// publishes a new version, and the detail and the list read the new version afterward.
export function useScriptEditor(scriptId: () => string) {
  const queries = useQueryClient();
  const session = ref<EditorSession | null>(null);
  const opening = ref(false);
  const missing = ref(false);

  async function open(): Promise<void> {
    opening.value = true;
    missing.value = false;

    try {
      const project = await fetchScriptProject(scriptId());

      if (!project) {
        missing.value = true;

        return;
      }

      session.value = { project, sdkTypes: await fetchScriptSdkTypes(scriptId()) };
    } finally {
      opening.value = false;
    }
  }

  async function save(files: EditorFiles): Promise<CompileFeedback> {
    if (!session.value)
      return { ok: false, message: '' };

    const outcome = await saveScriptProject(scriptId(), { ...session.value.project, files });

    if (outcome.ok) {
      await queries.invalidateQueries({ queryKey: codeScriptKey(scriptId()) });
      await queries.invalidateQueries({ queryKey: codeScriptsKey });
    }

    return outcome;
  }

  return {
    session,
    opening,
    missing,
    open,
    save,
    close: (): void => {
      session.value = null;
    },
  } as const;
}
