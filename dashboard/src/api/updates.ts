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

import { useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, type Ref } from 'vue';

// Every release is a GitHub release whose tag names its channel (stable-2026.10.26-12), made by release.yml.
// The browser asks GitHub's public API directly, so the bot needs no token and no new endpoint.
const REPO_API: string = 'https://api.github.com/repos/NoMercyLabs/nomnomzbot';
const CHANNEL: string = 'stable';

// GitHub allows 60 anonymous calls an hour; one check per six hours stays far below that.
const CHECK_EVERY_MS = 6 * 60 * 60 * 1000;

type GitHubRelease = {
  tag_name: string;
  target_commitish: string;
  html_url: string;
  published_at: string;
};

type GitHubCompare = { status: 'ahead' | 'behind' | 'identical' | 'diverged' };

export type UpdateCheck =
  | { state: 'current' } |
  {
    state: 'available';
    version: string;
    url: string;
    publishedAt: string;
  } |
  { state: 'unknown' };

// The bot reports 0.1.0+<commit>; a build without a commit (a local dotnet run) cannot be compared.
export function commitOf(botVersion: string): string | null {
  const commit = botVersion.split('+')[1] ?? '';

  return /^[0-9a-f]{7,40}$/i.test(commit)
    ? commit
    : null;
}

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${REPO_API}${path}`, {
    headers: { Accept: 'application/vnd.github+json' },
  });

  if (!response.ok)
    throw new Error(`GitHub ${path} answered ${response.status}`);

  return (await response.json()) as T;
}

async function checkFor(commit: string): Promise<UpdateCheck> {
  const releases = await getJson<GitHubRelease[]>('/releases?per_page=30');
  const latest = releases.find((release) => release.tag_name.startsWith(`${CHANNEL}-`));

  if (latest === undefined)
    return { state: 'unknown' };

  const compare = await getJson<GitHubCompare>(`/compare/${commit}...${latest.target_commitish}`);

  if (compare.status !== 'ahead')
    return { state: 'current' };

  return {
    state: 'available',
    version: latest.tag_name,
    url: latest.html_url,
    publishedAt: latest.published_at,
  };
}

// Whether a newer stable release exists than the build this bot runs.
export function useUpdateCheck(
  botVersion: Ref<string | undefined>,
): UseQueryReturnType<UpdateCheck, Error> {
  const commit = computed<string | null>(() => commitOf(botVersion.value ?? ''));

  return useQuery({
    queryKey: ['update-check', CHANNEL, commit],
    queryFn: async (): Promise<UpdateCheck> => {
      if (commit.value === null)
        return { state: 'unknown' };

      return await checkFor(commit.value);
    },
    enabled: computed<boolean>(() => botVersion.value !== undefined),
    staleTime: CHECK_EVERY_MS,
    refetchInterval: CHECK_EVERY_MS,
    retry: false,
  });
}
