// PreToolUse hook for Edit and Write: blocks banned terms (docs/PRIORITY-CHANGES.md §4)
// before they reach a file. Exit code 2 sends stderr back to Claude as the tool's error,
// so it rewrites the text instead of writing it. No dependencies.

import { findBannedTerms, isExempt, toRepoPath } from '../../scripts/lib/banned-terms.mjs';

let raw = '';
for await (const chunk of process.stdin) raw += chunk;

let payload;
try {
  payload = JSON.parse(raw);
} catch {
  process.exit(0); // not a hook payload; nothing to guard
}

const input = payload.tool_input ?? {};
const projectDir = process.env.CLAUDE_PROJECT_DIR || payload.cwd || process.cwd();
const repoPath = input.file_path ? toRepoPath(input.file_path, projectDir) : null;
if (repoPath === null || isExempt(repoPath)) process.exit(0);

const texts = [input.content, input.new_string, ...(input.edits ?? []).map((e) => e.new_string)];
const found = findBannedTerms(texts.filter((t) => typeof t === 'string').join('\n'));
if (found.length === 0) process.exit(0);

const lines = found.map(({ term, instead }) => `- "${term}": ${instead}`);
process.stderr.write(
  `copy-guard: ${repoPath} would contain banned terms (docs/PRIORITY-CHANGES.md §4). Rewrite the text:\n${lines.join('\n')}\n`,
);
process.exit(2);
