#!/usr/bin/env node
/**
 * Smoke check for CI supply-chain and secret scanning gates (Task 9).
 * Verifies:
 *  - .github/workflows/ci.yml is valid YAML and contains npm audit, gitleaks, trivy, dotnet vulnerable gates
 *  - .github/dependabot.yml exists and covers npm + nuget (dotnet) weekly
 *  - kustomize builds are valid (optional, if kubectl available)
 * Usage: node scripts/verify-ci-gates.mjs
 */
import fs from 'node:fs';
import path from 'node:path';
import { execSync } from 'node:child_process';

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const ciPath = path.join(root, '.github/workflows/ci.yml');
const dependabotPath = path.join(root, '.github/dependabot.yml');

let failures = [];

function check(condition, msg) {
  if (!condition) failures.push(msg);
  console.log(`${condition ? 'PASS' : 'FAIL'}: ${msg}`);
}

function readFile(p) {
  try { return fs.readFileSync(p, 'utf8'); } catch { return null; }
}

// 1. CI workflow exists and is parseable
const ciRaw = readFile(ciPath);
check(ciRaw !== null, `.github/workflows/ci.yml exists`);

let ciParsed = null;
if (ciRaw) {
  // Basic YAML parse via node fallback: check brace validity by requiring no parse error via simple check.
  // Use python yaml if available else regex checks.
  try {
    // Try to use js-yaml if available is overkill; just check required strings.
    check(ciRaw.includes('npm audit --audit-level=moderate'), 'frontend job has npm audit --audit-level=moderate');
    check(ciRaw.includes('gitleaks/gitleaks-action@v2'), 'secrets-scan job uses gitleaks/gitleaks-action@v2');
    check(ciRaw.includes('secrets-scan'), 'ci.yml contains secrets-scan job');
    check(ciRaw.includes('aquasecurity/trivy-action'), 'ci.yml contains Trivy scan (aquasecurity/trivy-action)');
    check(ciRaw.includes('image-scan'), 'ci.yml contains image-scan job');
    check(ciRaw.includes('dotnet list') && ciRaw.includes('--vulnerable'), 'backend job runs dotnet list package --vulnerable');
    check(ciRaw.includes('--include-transitive'), 'dotnet vulnerable check includes --include-transitive');
    // Validate YAML via python if available
    try {
      execSync(`python3 -c "import yaml, sys; yaml.safe_load(open('${ciPath}'))"`, { stdio: 'pipe' });
      check(true, 'ci.yml is valid YAML (python3 yaml.safe_load)');
    } catch (e) {
      check(false, `ci.yml YAML parse failed: ${e.message}`);
    }
  } catch (e) {
    check(false, `CI file checks error: ${e.message}`);
  }
}

// 2. Dependabot
const depRaw = readFile(dependabotPath);
check(depRaw !== null, '.github/dependabot.yml exists');
if (depRaw) {
  check(depRaw.includes('package-ecosystem: npm'), 'dependabot covers npm');
  check(depRaw.includes('package-ecosystem: nuget'), 'dependabot covers nuget (dotnet)');
  check(depRaw.includes('interval: weekly'), 'dependabot uses weekly interval');
  try {
    execSync(`python3 -c "import yaml; yaml.safe_load(open('${dependabotPath}'))"`, { stdio: 'pipe' });
    check(true, 'dependabot.yml is valid YAML');
  } catch (e) {
    check(false, `dependabot.yml YAML parse failed: ${e.message}`);
  }
}

// 3. Kustomize validation (best-effort)
try {
  execSync('kubectl kustomize deploy/k8s/base >/dev/null', { cwd: root, stdio: 'pipe' });
  check(true, 'kubectl kustomize deploy/k8s/base succeeds');
} catch (e) {
  check(false, `kubectl kustomize base failed: ${e.message}`);
}
try {
  execSync('kubectl kustomize deploy/k8s/overlays/prod >/dev/null', { cwd: root, stdio: 'pipe' });
  check(true, 'kubectl kustomize deploy/k8s/overlays/prod succeeds');
} catch (e) {
  check(false, `kubectl kustomize prod failed: ${e.message}`);
}

// Summary
console.log('\n' + (failures.length === 0 ? 'All CI gates verified.' : `${failures.length} check(s) failed.`));
if (failures.length) {
  for (const f of failures) console.error(' - ' + f);
  process.exit(1);
}
