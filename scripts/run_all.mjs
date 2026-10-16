#!/usr/bin/env node
// One-command regression: node scripts/run_all.mjs [scenario names...]  (default: the self-contained scenarios below)
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const dir = path.dirname(fileURLToPath(import.meta.url));
const defaults = ['struct3', 'mep4', 'arch5', 'arch6', 'arch7', 'cad1', 'struct4', 'mep5', 'fam5', 'mat6', 'doc7', 'stairs2', 'warnpol1', 'fam4', 'dyn2'];
const names = process.argv.length > 2 ? process.argv.slice(2) : defaults;
let failed = 0;
for (const n of names) {
  const r = spawnSync('node', [path.join(dir, 'smoke.mjs'), path.join(dir, 'scenarios', n + '.json')], { encoding: 'utf8' });
  const tail = (r.stdout || '').trim().split('\n').pop();
  const bad = (r.stdout || '').split('\n').filter(l => l.startsWith('✗'));
  console.log(`${bad.length || r.status ? 'FAIL' : 'PASS'}  ${n.padEnd(10)} ${tail}`);
  bad.forEach(l => console.log('   ' + l.slice(0, 200)));
  if (bad.length || r.status) failed++;
}
console.log(failed ? `${failed} scenario(s) failed` : 'all scenarios passed');
process.exit(failed ? 1 : 0);
