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
# Gate for the streamer SDK docs (docs/sdk). A page may ship only when:
#   - it declares the plan slices it describes on a line "<!-- slices: S-A, S-B -->" (or "none"),
#     and none of them is still open in SHORTCOMINGS-EXECUTION-PLAN.md;
#   - the claim audit has a section "## <path under docs/sdk>" carrying
#     "<!-- audited sha256:<hash> -->" for the page's current text (an edit after the audit fails);
#   - every claim row in that section has a verdict starting with "true".
# Stoney 2026-10-03: "i need good docs so why did you write bad ones?" (62 stale and 8 wrong of 399 claims).
#
# Usage: python scripts/check-sdk-docs.py [--docs docs/sdk] [--audit docs-work/audit.md] [--plan <plan>]

import argparse
import hashlib
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SLICES_LINE = re.compile(r'<!--\s*slices:\s*(.*?)\s*-->', re.IGNORECASE)
AUDITED_LINE = re.compile(r'<!--\s*audited sha256:([0-9a-f]{64})\s*-->')
OPEN_SLICE = re.compile(r'^- \*\*(S-[A-Za-z0-9-]+)\*\*', re.MULTILINE)
CLAIM_ROW = re.compile(r'^\|(?P<claim>[^|]+)\|(?P<verdict>[^|]+)\|')


def page_hash(text: str) -> str:
    return hashlib.sha256(text.replace('\r\n', '\n').encode('utf-8')).hexdigest()


def audit_sections(audit_text: str) -> dict[str, str]:
    sections: dict[str, str] = {}
    current = None
    for line in audit_text.replace('\r\n', '\n').split('\n'):
        heading = re.match(r'^## (\S+\.md)\b', line)
        if heading:
            current = heading.group(1)
            sections[current] = ''
        elif line.startswith('#'):
            current = None
        elif current is not None:
            sections[current] += line + '\n'
    return sections


def claim_problems(rel: str, section: str) -> list[str]:
    problems = []
    rows = 0
    for line in section.split('\n'):
        row = CLAIM_ROW.match(line)
        if not row:
            continue
        claim = row.group('claim').strip()
        verdict = row.group('verdict').strip()
        if claim == 'Claim' or set(claim) <= set('-: '):
            continue
        rows += 1
        if not verdict.lower().startswith('true'):
            problems.append(f'{rel}: claim not verified ({verdict}): {claim}')
    if rows == 0:
        problems.append(f'{rel}: its audit section has no claim rows')
    return problems


def check(docs: pathlib.Path, audit: pathlib.Path, plan: pathlib.Path) -> list[str]:
    open_slices = set(OPEN_SLICE.findall(plan.read_text(encoding='utf-8')))
    sections = audit_sections(audit.read_text(encoding='utf-8')) if audit.exists() else {}
    problems: list[str] = []
    for page in sorted(docs.rglob('*.md')):
        rel = page.relative_to(docs).as_posix()
        text = page.read_text(encoding='utf-8')

        declared = SLICES_LINE.search(text)
        if not declared:
            problems.append(f'{rel}: no "<!-- slices: ... -->" line naming the plan slices it describes (or "none")')
        else:
            for slice_id in re.split(r'[\s,]+', declared.group(1)):
                if slice_id in open_slices:
                    problems.append(f'{rel}: describes {slice_id}, which is still open in the plan')

        section = sections.get(rel)
        if section is None:
            problems.append(f'{rel}: no audit section "## {rel}" in {audit.name}')
            continue
        audited = AUDITED_LINE.search(section)
        if not audited or audited.group(1) != page_hash(text):
            problems.append(f'{rel}: page changed since its audit (or the section has no "audited sha256" line); re-audit it')
            continue
        problems.extend(claim_problems(rel, section))
    return problems


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--docs', default=str(ROOT / 'docs' / 'sdk'))
    parser.add_argument('--audit', default=str(ROOT / 'docs-work' / 'audit.md'))
    parser.add_argument('--plan', default=str(ROOT / '.claude' / 'docs' / 'design' / 'SHORTCOMINGS-EXECUTION-PLAN.md'))
    args = parser.parse_args()
    docs = pathlib.Path(args.docs)
    if not docs.exists():
        print(f'check-sdk-docs: {docs} does not exist; nothing to check')
        return 0
    problems = check(docs, pathlib.Path(args.audit), pathlib.Path(args.plan))
    pages = len(list(docs.rglob('*.md')))
    for problem in problems:
        print(problem)
    failed = len({p.split(':', 1)[0] for p in problems})
    print(f'check-sdk-docs: {pages - failed} of {pages} pages may ship; {len(problems)} problems')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
