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

import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';

import { parse } from '@vue/compiler-sfc';

const srcRoot = join(__dirname, '..');
const scanRoot = join(srcRoot, 'views', 'dashboard');
const controlTag = /^(Button|Switch|LabeledSwitch|Checkbox|Toggle|ToggleGroupItem|DropdownMenuItem|button)$|Button$/;
// A permission check reads can(...), canManage, canWrite, or a holds/allowed flag.
const gateExpression = /\bcan\b|\bcan[A-Z]\w*|\.can\(|\bholds\w*|\ballowed\b/;
const gateDirectives = ['if', 'else-if', 'show'];
const elementNode = 1;
const directiveProp = 7;

// The parts of the template AST this scan reads.
interface TemplateProp {
  type: number;
  name: string;
  exp?: { content: string };
  arg?: { content: string };
  value?: { content: string };
}

interface TemplateNode {
  type: number;
  tag?: string;
  props?: TemplateProp[];
  children?: TemplateNode[];
  loc: { start: { line: number }; source: string };
}

export interface Control {
  // Path under views/dashboard.
  file: string;
  line: number;
  tag: string;
  // What the control does: its handler or v-model, then its first text key.
  signature: string;
  // A permission check sits above it in its own template, or in every parent that mounts its component.
  gated: boolean;
}

interface Usage {
  tag: string;
  gated: boolean;
}

interface FileScan {
  controls: Omit<Control, 'gated'>[];
  localGate: boolean[];
  usages: Usage[];
  imports: Map<string, string>;
}

function vueFiles(folder: string): string[] {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
    const path = join(folder, entry.name);

    if (entry.isDirectory())
      return entry.name === '__tests__'
        ? []
        : vueFiles(path);

    return path.endsWith('.vue')
      ? [path]
      : [];
  });
}

function directives(node: TemplateNode, names: string[]): TemplateProp[] {
  const props = node.props ?? [];

  return props.filter((prop) => prop.type === directiveProp && names.includes(prop.name));
}

// A LabeledSwitch given a static `action` renders GatedSwitch, which checks that permission itself.
// A PageCreateButton stays visible but turns off when its `disabled-reason` is set from a permission check.
function carriesOwnGate(node: TemplateNode): boolean {
  const staticAction = (node.props ?? []).some((prop) => prop.name === 'action' && prop.value?.content);
  const reasonGated = directives(node, ['bind'])
    .some((prop) => /^disabled-?[rR]eason$/.test(prop.arg?.content ?? '') && gateExpression.test(prop.exp?.content ?? ''));

  return (node.tag === 'LabeledSwitch' && staticAction) || (node.tag === 'PageCreateButton' && reasonGated);
}

function isGate(node: TemplateNode): boolean {
  const gatedByDirective = directives(node, gateDirectives)
    .some((prop) => gateExpression.test(prop.exp?.content ?? ''));

  return gatedByDirective || carriesOwnGate(node);
}

function signatureOf(node: TemplateNode): string {
  const handlers = directives(node, ['on']).map((prop) => `@${prop.arg?.content ?? ''}=${prop.exp?.content ?? ''}`);
  const handler = handlers.find((text) => /^@(click|update|select)/.test(text));
  const model = directives(node, ['model']).map((prop) => `v-model=${prop.exp?.content ?? ''}`)[0];
  const type = (node.props ?? []).find((prop) => prop.name === 'type')?.value?.content;
  const text = node.loc.source.match(/\$t\(\s*'([^']+)'/)?.[1];
  const action = handler ?? model ?? (type === 'submit'
    ? 'submit'
    : '');

  return [action, text ?? ''].filter(Boolean).join(' ').slice(0, 120);
}

function pascal(tag: string): string {
  return tag.replace(/(^|-)(\w)/g, (_match, _dash, letter: string) =>
    letter.toUpperCase());
}

function walk(nodes: TemplateNode[], gated: boolean, scan: FileScan): void {
  let chainGated = false;

  for (const node of nodes) {
    if (node.type !== elementNode) continue;

    const isIf = directives(node, ['if']).length > 0;
    const isElse = directives(node, ['else', 'else-if']).length > 0;
    const ownGate = isGate(node);

    if (isIf) {
      chainGated = ownGate;
    }

    const here = gated || ownGate || (isElse && chainGated);

    if (isElse && ownGate) {
      chainGated = true;
    }

    if (node.tag !== undefined && controlTag.test(node.tag)) {
      scan.controls.push({
        file: '',
        line: node.loc.start.line,
        tag: node.tag,
        signature: signatureOf(node),
      });

      scan.localGate.push(here);
    }

    if (node.tag !== undefined) {
      scan.usages.push({ tag: pascal(node.tag), gated: here });
    }

    walk(node.children ?? [], here, scan);
  }
}

function importsOf(script: string, from: string): Map<string, string> {
  const found = new Map<string, string>();

  for (const match of script.matchAll(/import\s+(\w+)\s+from\s+'([^']+\.vue)'/g)) {
    const [, name = '', specifier = ''] = match;
    const target = specifier.startsWith('@/')
      ? join(srcRoot, specifier.slice(2))
      : resolve(dirname(from), specifier);

    found.set(name, target);
  }

  return found;
}

function scanFile(path: string): FileScan {
  const { descriptor } = parse(readFileSync(path, 'utf8'));
  const ast = descriptor.template?.ast as unknown as TemplateNode | undefined;
  const script = `${descriptor.scriptSetup?.content ?? ''}\n${descriptor.script?.content ?? ''}`;
  const scan: FileScan = {
    controls: [],
    localGate: [],
    usages: [],
    imports: importsOf(script, path),
  };

  walk(ast?.children ?? [], false, scan);

  return scan;
}

// A component is covered when it is mounted somewhere and every mount sits under a permission check
// (or inside another covered component). A page, or a component nobody mounts, is never covered.
function coveredFiles(scans: Map<string, FileScan>): Set<string> {
  const mounts = new Map<string, { by: string; gated: boolean }[]>();

  for (const [by, scan] of scans) {
    for (const usage of scan.usages) {
      const target = scan.imports.get(usage.tag);

      if (target !== undefined) {
        mounts.set(target, [...(mounts.get(target) ?? []), { by, gated: usage.gated }]);
      }
    }
  }

  const covered = new Set<string>();
  let grew = true;

  while (grew) {
    grew = false;

    for (const [target, list] of mounts) {
      const everyMountGated = list.every((mount) => mount.gated || covered.has(mount.by));

      if (everyMountGated && !covered.has(target)) {
        covered.add(target);
        grew = true;
      }
    }
  }

  return covered;
}

// Every action control in every dashboard view template, with whether a permission check sits above it.
export function scanControls(): Control[] {
  const scans = new Map(vueFiles(scanRoot).map((path) => [path, scanFile(path)] as const));
  const covered = coveredFiles(scans);

  return [...scans].flatMap(([path, scan]) =>
    scan.controls.map((control, index) => ({
      ...control,
      file: relative(scanRoot, path).replace(/\\/g, '/'),
      gated: scan.localGate[index] || covered.has(path),
    }) as const satisfies Control));
}
