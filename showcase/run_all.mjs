import { spawnSync } from 'node:child_process';
const steps = process.argv.slice(2).length ? process.argv.slice(2) : ['stage1', 'families', 'stage2', 'stage3', 'stage3b'];
for (const s of steps) {
  console.log('=== ' + s);
  const r = spawnSync('node', [`showcase/${s}.mjs`], { stdio: 'inherit', cwd: process.cwd() });
  if (r.status !== 0) { console.log('FAILED at ' + s); process.exit(1); }
}
