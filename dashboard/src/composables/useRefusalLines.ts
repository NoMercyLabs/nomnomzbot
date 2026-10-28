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

import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import type { ApiFieldProblem } from '@/api/errors';
import { refusalProblems } from '@/lib/errors';

// The lines a refused save shows next to the boxes it names. The server's reason is a full translation key, so
// the line is that key translated under `scope` (the page's own translation root, since a page file holds only its
// own keys); `refuse` is the mutation's onError and `clear` runs before the next save.
export function useRefusalLines(scope: string) {
  const { t } = useI18n();
  const problems = ref<ApiFieldProblem[]>([]);

  function refuse(error: Error): void {
    problems.value = refusalProblems(error);
  }

  function clear(): void {
    problems.value = [];
  }

  function linesFor(field: string): string[] {
    return problems.value
      .filter((problem) => problem.field === field)
      .map((problem) =>
        t(`${scope}.${problem.reason}`, { min: problem.minimum, max: problem.maximum }),
      );
  }

  return {
    refuse,
    clear,
    linesFor,
  };
}
