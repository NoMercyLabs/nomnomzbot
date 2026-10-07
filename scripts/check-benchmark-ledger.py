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
# Gate for rule research-is-a-benchmark (Stoney 2026-10-07: "teach it what it means to research a topic
# and don't make me ask for enhancement of user experience more than once"). Research for a screen is a
# feature-by-feature comparison with the real products in its category, not a reading list. Fails when:
#   - an OWNER REQUEST section of SHORTCOMINGS-EXECUTION-PLAN.md asks for research (or names a product to
#     be "like") and links no .claude/docs/design/benchmarks/*.md file;
#   - a benchmark names fewer than 3 products, or a product without a source URL;
#   - a benchmark has fewer than 25 feature rows (a handful of rows is a reading list, not a benchmark);
#   - a feature row has a status other than have / partial / missing / not-doing;
#   - a partial or missing row names no slice, or a slice that is not in the plan;
#   - a not-doing row gives no reason;
#   - a row has no source for the feature, or a have/partial row does not say where ours lives.
# Closing a slice therefore means: build it, set its benchmark rows to have, then delete the slice.
#
# Usage: python scripts/check-benchmark-ledger.py [--plan <md>] [--dir <benchmarks dir>]

import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
DESIGN = ROOT / '.claude' / 'docs' / 'design'
STATUSES = ('have', 'partial', 'missing', 'not-doing')
MIN_PRODUCTS = 3
MIN_FEATURES = 25
CELL_SPLIT = re.compile(r'(?<!\\)\|')
SLICE_ID = re.compile(r'\bS-[A-Z0-9][A-Z0-9-]*[A-Z0-9]\b')
URL = re.compile(r'https?://\S+')
ASKS_RESEARCH = re.compile(r'research|\blike (?:vs ?code|codepen|stackblitz|codesandbox|figma|notion)|goes vs ?code', re.I)
BENCHMARK_LINK = re.compile(r'benchmarks/[\w.-]+\.md')
HEADER_COLUMNS = ('feature', 'seen in', 'source', 'ours', 'status', 'slice')


def owner_request_sections(plan_text):
    parts = re.split(r'^## ', plan_text, flags=re.M)
    return [p for p in parts if p.startswith('OWNER REQUEST')]


def plan_slices(plan_text):
    return set(SLICE_ID.findall(plan_text))


def table_rows(text):
    rows = []
    for line in text.splitlines():
        if not line.strip().startswith('|'):
            continue
        cells = [c.strip() for c in CELL_SPLIT.split(line.strip().strip('|'))]
        if all(re.fullmatch(r':?-{2,}:?', c) for c in cells if c):
            continue
        rows.append(cells)
    return rows


def products(text):
    match = re.search(r'^## Products\s*$(.*?)(?=^## |\Z)', text, flags=re.M | re.S)
    if not match:
        return None
    return [l for l in match.group(1).splitlines() if l.strip().startswith('- ')]


def check_benchmark(path, slices):
    problems = []
    text = path.read_text(encoding='utf-8')
    name = path.name

    listed = products(text)
    if listed is None:
        problems.append(f'{name}: no "## Products" section')
    else:
        if len(listed) < MIN_PRODUCTS:
            problems.append(f'{name}: {len(listed)} products, need at least {MIN_PRODUCTS}')
        for line in listed:
            if not URL.search(line):
                problems.append(f'{name}: product without a source URL: {line.strip()}')

    rows = table_rows(text)
    header = next((r for r in rows if [c.lower() for c in r[:6]] == list(HEADER_COLUMNS)), None)
    if header is None:
        problems.append(f'{name}: no feature table with columns {" | ".join(HEADER_COLUMNS)}')
        return problems

    features = [r for r in rows if r is not header and len(r) >= 6 and r[0].lower() != 'feature']
    if len(features) < MIN_FEATURES:
        problems.append(f'{name}: {len(features)} feature rows, need at least {MIN_FEATURES}')

    for row in features:
        feature, seen_in, source, ours, status, slice_cell = row[:6]
        label = f'{name}: "{feature}"'
        status = status.lower()
        if status not in STATUSES:
            problems.append(f'{label}: status "{status}" is not one of {", ".join(STATUSES)}')
            continue
        if not seen_in:
            problems.append(f'{label}: no product listed under "seen in"')
        if not source:
            problems.append(f'{label}: no source for the feature')
        if status in ('partial', 'missing'):
            named = SLICE_ID.findall(slice_cell)
            if not named:
                problems.append(f'{label}: {status} but names no slice')
            for slice_id in named:
                if slice_id not in slices:
                    problems.append(f'{label}: slice {slice_id} is not in the plan')
        if status == 'not-doing' and len(slice_cell) < 10:
            problems.append(f'{label}: not-doing needs a reason in the slice column')
        if status in ('have', 'partial') and not ours:
            problems.append(f'{label}: {status} but "ours" does not say where (file or screen)')
    return problems


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--plan', default=str(DESIGN / 'SHORTCOMINGS-EXECUTION-PLAN.md'))
    parser.add_argument('--dir', default=str(DESIGN / 'benchmarks'))
    args = parser.parse_args()

    plan_text = pathlib.Path(args.plan).read_text(encoding='utf-8')
    slices = plan_slices(plan_text)
    bench_dir = pathlib.Path(args.dir)
    problems = []

    for section in owner_request_sections(plan_text):
        title = section.splitlines()[0]
        if not ASKS_RESEARCH.search(section):
            continue
        links = BENCHMARK_LINK.findall(section)
        if not links:
            problems.append(f'plan "{title}": asks for research but links no benchmarks/*.md')
        for link in links:
            if not (bench_dir / pathlib.Path(link).name).is_file():
                problems.append(f'plan "{title}": links {link}, which does not exist')

    for path in sorted(bench_dir.glob('*.md')) if bench_dir.is_dir() else []:
        problems.extend(check_benchmark(path, slices))

    for problem in problems:
        print(problem)
    print(f'check-benchmark-ledger: {len(problems)} problem(s)')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
