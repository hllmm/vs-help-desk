#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const docsDir = path.join(root, 'docs');

const mdFiles = fs.readdirSync(docsDir).filter(f => f.endsWith('.md'));

const linkRegex = /\[([^\]]+)\]\(([^)]+)\)/g;

let failures = [];

for (const file of mdFiles) {
  const fullPath = path.join(docsDir, file);
  const content = fs.readFileSync(fullPath, 'utf8');
  let match;
  while ((match = linkRegex.exec(content)) !== null) {
    const link = match[2].trim();
    // Skip external links, anchors, mailto, etc.
    if (/^(https?:|mailto:|#)/i.test(link)) continue;
    // Remove anchor fragment
    const [filePart] = link.split('#');
    if (!filePart) continue;
    // Handle relative paths: ../src/... or ../deploy/... or ./...
    // Resolve relative to docsDir
    const resolved = path.resolve(docsDir, filePart);
    // Also try resolving relative to root if it starts with /
    const candidate = filePart.startsWith('/') ? path.join(root, filePart) : resolved;
    try {
      fs.accessSync(candidate, fs.constants.F_OK);
    } catch {
      // Try also as directory?
      failures.push(`${file}: broken link "${link}" -> ${candidate} not found`);
    }
  }
}

if (failures.length > 0) {
  console.error('Markdown link check FAILED:');
  for (const f of failures) console.error(' - ' + f);
  process.exit(1);
} else {
  console.log(`Markdown link check PASS: checked ${mdFiles.length} files, no broken local links`);
}
