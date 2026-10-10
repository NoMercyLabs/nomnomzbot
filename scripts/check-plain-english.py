# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot, free software licensed under the GNU Affero
#  General Public License v3.0 or later. You may redistribute and/or modify it
#  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
#
#  SPDX-License-Identifier: AGPL-3.0-or-later
# -----------------------------------------------------------------------------
#
# Gate for rule plain-english-shared-docs (Stoney 2026-10-10: "i want a reliable paper that follows my rules
# and uses the wording patterns from our dyslexia rules and the documentation skill" and "the document is to
# share with other streamers and should not contain file references or wording made from our chats").
# Simplified Technical English plus the documentation skill's checklist. Fails when a document:
#   - has a sentence (prose, bullet or table cell) over 20 words, after quotes, code and links are removed;
#   - uses a complex word that has a plain one ("utilize" -> "use");
#   - uses an acronym that the glossary table does not define;
#   - (full documents) lacks an Audience:, Owner: or Date: line, a "## Contents" section or a Glossary heading;
#   - (shared documents, --share) holds a file reference, a line cite, a slice ID, inline code, a code
#     fence or internal wording (the owner's name, PLAN, session, scratchpad).
#
# Usage: python scripts/check-plain-english.py [--file <md>] [--share] [--fragment]
#        With no --file it checks every shared document in SHARED_DOCS.

import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SHARED_DOCS = [ROOT / 'docs' / 'community-safety.md']
MAX_WORDS = 20

PLAIN_WORDS = {
    'utilize': 'use', 'utilise': 'use', 'leverage': 'use', 'facilitate': 'help', 'approximately': 'about',
    'numerous': 'many', 'additional': 'more', 'however': 'but', 'therefore': 'so', 'thus': 'so',
    'hence': 'so', 'subsequently': 'then', 'commence': 'start', 'terminate': 'stop', 'sufficient': 'enough',
    'regarding': 'about', 'mitigate': 'reduce', 'via': 'through', 'prior to': 'before', 'in order to': 'to',
    'e.g.': 'for example', 'i.e.': 'that is', 'etc.': 'name every item', 'endeavor': 'try', 'obtain': 'get',
    'demonstrate': 'show', 'assist': 'help', 'in the event that': 'if', 'whilst': 'while',
    'robust': 'strong', 'aforementioned': 'this', 'whereas': 'but', 'nevertheless': 'but',
}
ACRONYM_OK = {'OK', 'ID', 'IDS'}

QUOTED = re.compile(r'"[^"\n]*"|“[^”\n]*”')
CODE = re.compile(r'`[^`\n]*`')
LINK_TARGET = re.compile(r'\]\([^)]*\)')
URL = re.compile(r'https?://\S+')
SENTENCE_END = re.compile(r'(?<=[.!?])\s+')
WORD = re.compile(r"[A-Za-z0-9][A-Za-z0-9'’-]*")
ACRONYM = re.compile(r'\b[A-Z][A-Z0-9]{1,6}s?\b')
FILE_REF = re.compile(r'\b[\w./-]+\.(?:cs|kt|kts|md|json|py|ps1|ts|js|yml|yaml|csproj|sql)\b')
LINE_CITE = re.compile(r'\b\w+:\d+(?:-\d+)?\b')
SLICE_ID = re.compile(r'(?<![A-Za-z0-9-])S-[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)*\b')
INTERNAL = re.compile(r'\b(?:Stoney|Arc|PLAN|session|scratchpad|verbatim)\b')


def plain_text(segment):
    for pattern in (CODE, QUOTED, LINK_TARGET, URL, SLICE_ID):
        segment = pattern.sub(' ', segment)
    return segment


def prose_segments(text):
    """Yields (line number, segment) for every prose line, bullet and table cell outside code fences."""
    in_fence = False
    for number, line in enumerate(text.splitlines(), start=1):
        stripped = line.strip()
        if stripped.startswith('```'):
            in_fence = not in_fence
            continue
        if in_fence or not stripped or stripped.startswith('#') or stripped == '---':
            continue
        if stripped.startswith('|'):
            cells = [c.strip() for c in stripped.strip('|').split('|')]
            if all(re.fullmatch(r':?-{2,}:?', c) for c in cells if c):
                continue
            for cell in cells:
                yield number, cell
            continue
        yield number, re.sub(r'^(?:[-*]|\d+\.)\s+', '', stripped)


def glossary_terms(text):
    match = re.search(r'^#{2,4} [^\n]*Glossary[^\n]*$', text, re.M | re.I)
    if not match:
        return set()
    rest = text[match.end():]
    end = re.search(r'^#{1,4} ', rest, re.M)
    body = rest[:end.start()] if end else rest
    terms = set()
    for line in body.splitlines():
        if line.strip().startswith('|'):
            first = line.strip().strip('|').split('|')[0]
            terms.update(t.upper() for t in WORD.findall(first.replace('*', '')))
    return terms


def check_front_matter(text, name):
    problems = []
    for label in ('Audience:', 'Owner:', 'Date:'):
        if not re.search(r'^\**' + re.escape(label), text, re.M):
            problems.append(f'{name}: no "{label}" line')
    if not re.search(r'^## Contents\b', text, re.M):
        problems.append(f'{name}: no "## Contents" section')
    if not re.search(r'^#{2,4} [^\n]*Glossary', text, re.M | re.I):
        problems.append(f'{name}: no Glossary heading')
    return problems


def check_shared(text, name):
    problems = []
    in_fence = False
    for number, line in enumerate(text.splitlines(), start=1):
        if line.strip().startswith('```'):
            problems.append(f'{name}:{number}: code fence in a shared document')
            in_fence = not in_fence
            continue
        bare = URL.sub(' ', LINK_TARGET.sub(' ', line))
        checks = (
            (CODE, 'inline code'),
            (FILE_REF, 'file reference'),
            (LINE_CITE, 'file reference (line cite)'),
            (SLICE_ID, 'slice ID'),
            (INTERNAL, 'internal wording'),
        )
        for pattern, label in checks:
            hit = pattern.search(bare)
            if hit:
                problems.append(f'{name}:{number}: {label} "{hit.group(0)}" in a shared document')
    return problems


def check_file(path, fragment=False, share=False):
    text = path.read_text(encoding='utf-8')
    name = path.name
    problems = [] if fragment else check_front_matter(text, name)
    glossary = glossary_terms(text)

    for number, segment in prose_segments(text):
        plain = plain_text(segment)
        for sentence in SENTENCE_END.split(plain):
            count = len(WORD.findall(sentence))
            if count > MAX_WORDS:
                problems.append(f'{name}:{number}: sentence has {count} words (max {MAX_WORDS}): '
                                f'{sentence.strip()[:80]}')
        lowered = plain.lower()
        for word, simple in PLAIN_WORDS.items():
            if re.search(r'(?<![\w.])' + re.escape(word) + r'(?![\w])', lowered):
                problems.append(f'{name}:{number}: "{word}" -> use "{simple}"')
        for acronym in ACRONYM.findall(plain):
            base = acronym[:-1] if acronym.endswith('s') else acronym
            if base not in ACRONYM_OK and base not in glossary:
                problems.append(f'{name}:{number}: acronym "{base}" is not in the glossary')

    if share:
        problems.extend(check_shared(text, name))
    return problems


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--file')
    parser.add_argument('--share', action='store_true')
    parser.add_argument('--fragment', action='store_true')
    args = parser.parse_args()

    if args.file:
        targets = [(pathlib.Path(args.file), args.share)]
    else:
        targets = [(path, True) for path in SHARED_DOCS if path.exists()]

    problems = []
    for path, share in targets:
        problems.extend(check_file(path, fragment=args.fragment, share=share))
    for problem in problems:
        print(problem)
    print(f'check-plain-english: {len(problems)} problem(s)')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
