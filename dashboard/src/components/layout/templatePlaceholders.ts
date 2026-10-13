// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { TemplateHelper } from '@/api/templates'

export function braced(key: string): string {
  return `{${key}}`
}

export interface OpenPlaceholder {
  /** Index of the opening brace. */
  from: number
  /** What was typed after the brace. */
  query: string
}

// The brace the caret is inside, if the person is still writing its name.
export function openPlaceholder(text: string, caret: number): OpenPlaceholder | null {
  const match: RegExpExecArray | null = /\{([\w.<>:|-]*)$/.exec(text.slice(0, caret))
  return match ? { from: match.index, query: match[1] ?? '' } : null
}

// Helpers that start with the query come first, then those that only contain it.
export function filterHelpers(helpers: TemplateHelper[], query: string): TemplateHelper[] {
  const needle: string = query.toLowerCase()
  const starts: TemplateHelper[] = helpers.filter((helper: TemplateHelper) => helper.key.toLowerCase().startsWith(needle))
  const contains: TemplateHelper[] = helpers.filter(
    (helper: TemplateHelper) => !helper.key.toLowerCase().startsWith(needle) && helper.key.toLowerCase().includes(needle),
  )
  return [...starts, ...contains]
}

// A key such as "args.<n>" stands for a family: every name that starts with "args." and goes on.
function matches(helper: TemplateHelper, name: string): boolean {
  const key: string = helper.key.toLowerCase()
  const open: number = key.indexOf('<')
  if (open === -1) return key === name
  const prefix: string = key.slice(0, open)
  return name.length > prefix.length && name.startsWith(prefix)
}

// Every {name} in the text that no helper of this place accepts, each once, in written order.
export function unknownPlaceholders(text: string, helpers: TemplateHelper[]): string[] {
  const unknown: string[] = []
  for (const match of text.matchAll(/\{([^{}\s][^{}]*)\}/g)) {
    const name: string = (match[1] ?? '').trim().toLowerCase()
    if (helpers.some((helper: TemplateHelper) => matches(helper, name))) continue
    if (!unknown.includes(match[0])) unknown.push(match[0])
  }
  return unknown
}

// The panel groups helpers by the first part of the key: user, user.name and user.id are all "user".
export function helperGroup(key: string): string {
  return key.split(/[.:<]/)[0] ?? key
}

export function groupHelpers(helpers: TemplateHelper[]): Array<{ group: string; helpers: TemplateHelper[] }> {
  const groups: Map<string, TemplateHelper[]> = new Map()
  for (const helper of helpers) {
    const group: string = helperGroup(helper.key)
    groups.set(group, [...(groups.get(group) ?? []), helper])
  }
  return [...groups].map(([group, grouped]: [string, TemplateHelper[]]) => ({ group, helpers: grouped }))
}
