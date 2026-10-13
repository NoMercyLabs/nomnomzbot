// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useTimeoutFn } from '@vueuse/core';
import { ref, type Ref } from 'vue';

import { runScriptTest, type TestRunOutcome } from '@/api/codeScripts';
import type { TestRunResult, TestTrigger } from '@/composables/useEditorBridge';

const FALLBACK_WAIT_SECONDS = 10;

export interface ScriptTester {
  running: Ref<boolean>;
  // True while the bot asked us to wait; Run test stays disabled until the window ends.
  limited: Ref<boolean>;
  last: Ref<TestRunResult | null>;
  failure: Ref<string | undefined>;
  run: (
    trigger: TestTrigger | null,
  ) => Promise<{ result: TestRunResult; trigger: string | null } | null>;
}

// A dry run of one script: nothing it does reaches chat. A refusal shows its reason; a 429 starts a wait.
export function useScriptTester(scriptId: () => string): ScriptTester {
  const running = ref(false);
  const limited = ref(false);
  const last = ref<TestRunResult | null>(null);
  const failure = ref<string | undefined>(undefined);
  const waitMs = ref(FALLBACK_WAIT_SECONDS * 1000);
  const wait = useTimeoutFn(
    () => {
      limited.value = false;
    },
    waitMs,
    { immediate: false },
  );

  async function run(
    trigger: TestTrigger | null,
  ): Promise<{ result: TestRunResult; trigger: string | null } | null> {
    running.value = true;
    failure.value = undefined;

    try {
      const outcome: TestRunOutcome = await runScriptTest(scriptId(), {
        variables: trigger?.variables ?? {},
        args: [],
        trigger: trigger?.id ?? null,
      });

      if (outcome.kind === 'rateLimited') {
        waitMs.value = (outcome.retryAfterSeconds ?? FALLBACK_WAIT_SECONDS) * 1000;
        limited.value = true;
        wait.start();

        return null;
      }

      last.value = outcome.result;

      return { result: outcome.result, trigger: trigger?.label ?? null };
    } catch (error) {
      failure.value = (error as Error).message;

      return null;
    } finally {
      running.value = false;
    }
  }

  return {
    running,
    limited,
    last,
    failure,
    run,
  };
}
