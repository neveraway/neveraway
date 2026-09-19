import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync, mkdirSync, existsSync, cpSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { generateKeyPairSync, createPublicKey, verify, constants } from 'node:crypto';

const root = new URL('../', import.meta.url);
const run = (script, env) => spawnSync('bash', [new URL(script, root).pathname], { env: { ...process.env, ...env }, encoding: 'utf8' });

test('tag signing rejects each missing credential and the wrong team', () => {
  const credentials = { CERT_P12_B64: 'fixture', CERT_PWD: 'fixture', APPLE_ID: 'fixture', APPLE_APP_SPECIFIC_PASSWORD: 'fixture', TEAM_ID: '44Y2L8A2CV' };
  assert.equal(run('scripts/check-release-credentials.sh', credentials).status, 0);
  for (const name of Object.keys(credentials)) assert.notEqual(run('scripts/check-release-credentials.sh', { ...credentials, [name]: '' }).status, 0, name);
  assert.notEqual(run('scripts/check-release-credentials.sh', { ...credentials, TEAM_ID: 'another-team' }).status, 0);
});

test('Sparkle download failure and corrupt bytes never reach extraction', () => {
  const dir = mkdtempSync(join(tmpdir(), 'neveraway-download-'));
  writeFileSync(join(dir, 'curl'), '#!/bin/bash\n[ "$FAIL_DOWNLOAD" = yes ] && exit 22\nwhile [ "$1" != -o ]; do shift; done\nprintf corrupt > "$2"\n', { mode: 0o755 });
  writeFileSync(join(dir, 'tar'), '#!/bin/bash\ntouch "$RUNNER_TEMP/extracted"\n', { mode: 0o755 });
  for (const FAIL_DOWNLOAD of ['yes', 'no']) {
    const result = run('scripts/download-sparkle.sh', { PATH: dir + ':' + process.env.PATH, RUNNER_TEMP: dir, FAIL_DOWNLOAD });
    assert.notEqual(result.status, 0);
    assert.equal(existsSync(join(dir, 'extracted')), false);
  }
});

test('Windows signer authenticates the exact bytes and refuses a different private key', () => {
  const dir = mkdtempSync(join(tmpdir(), 'neveraway-sign-'));
  cpSync(new URL('scripts/sign-windows-release.mjs', root), join(dir, 'sign.mjs'));
  const { privateKey, publicKey } = generateKeyPairSync('rsa', { modulusLength: 3072 });
  writeFileSync(join(dir, 'windows-release-public.pem'), publicKey.export({ type: 'spki', format: 'pem' }));
  const archive = join(dir, 'archive.zip');
  writeFileSync(archive, 'archive fixture');
  const result = spawnSync(process.execPath, [join(dir, 'sign.mjs'), archive], { env: { ...process.env, WINDOWS_RELEASE_KEY: privateKey.export({ type: 'pkcs8', format: 'der' }).toString('base64') } });
  assert.equal(result.status, 0, result.stderr.toString());
  const signature = readFileSync(archive + '.sig');
  assert.equal(signature.length, 384);
  assert(verify('SHA256', readFileSync(archive), { key: publicKey, padding: constants.RSA_PKCS1_PADDING }, signature));
  assert.equal(verify('SHA256', Buffer.from('altered archive'), publicKey, signature), false);
  const other = generateKeyPairSync('rsa', { modulusLength: 3072 }).privateKey;
  const rejected = spawnSync(process.execPath, [join(dir, 'sign.mjs'), archive], { env: { ...process.env, WINDOWS_RELEASE_KEY: other.export({ type: 'pkcs8', format: 'der' }).toString('base64') } });
  assert.notEqual(rejected.status, 0);
  const installer = readFileSync(new URL('scripts/install.ps1', root), 'utf8');
  const embedded = installer.match(/Modulus = \[Convert\]::FromBase64String\('([^']+)'\)/)[1];
  const actual = createPublicKey(readFileSync(new URL('scripts/windows-release-public.pem', root))).export({ format: 'jwk' });
  assert.equal(embedded, Buffer.from(actual.n, 'base64url').toString('base64'));
});

test('mac installer authenticates before stopping or replacing, and rolls back failed replacement', () => {
  for (const failure of ['', 'codesign', 'spctl', 'move']) {
    const dir = mkdtempSync(join(tmpdir(), 'neveraway-install-'));
    const bin = join(dir, 'bin');
    const applications = join(dir, 'Applications');
    mkdirSync(bin);
    mkdirSync(join(applications, 'NeverAway.app'), { recursive: true });
    writeFileSync(join(applications, 'NeverAway.app', 'old'), 'keep me');
    const script = readFileSync(new URL('scripts/install.sh', root), 'utf8').replaceAll('/Applications', applications);
    const installer = join(dir, 'install.sh');
    writeFileSync(installer, script);
    const stub = `#!/usr/bin/env node
const fs = require('node:fs');
const path = require('node:path');
const cmd = path.basename(process.argv[1]);
const args = process.argv.slice(2);
fs.appendFileSync(process.env.LOG, cmd + '\\n');
if (process.env.FAIL === cmd) process.exit(1);
if (cmd === 'codesign' && !args[args.indexOf('-R') + 1]?.startsWith('=anchor apple')) process.exit(2);
if (cmd === 'uname') console.log(args[0] === '-s' ? 'Darwin' : 'arm64');
if (cmd === 'curl') {
  if (args.includes('-o')) fs.writeFileSync(args[args.indexOf('-o') + 1], 'zip fixture');
  else console.log('{"browser_download_url":"https://example.test/NeverAway-3.2.1.zip"}');
}
if (cmd === 'ditto') {
  if (args[0] === '-x') {
    fs.mkdirSync(path.join(args[3], 'NeverAway.app'), { recursive: true });
    fs.writeFileSync(path.join(args[3], 'NeverAway.app', 'new'), 'verified fixture');
  } else fs.cpSync(args[0], args[1], { recursive: true });
}
if (cmd === 'mv') {
  if (process.env.FAIL === 'move' && args[0].includes('.neveraway.') && args[0].endsWith('/NeverAway.app')) process.exit(1);
  fs.renameSync(args[0], args[1]);
}
`;
    for (const command of ['uname', 'curl', 'ditto', 'codesign', 'spctl', 'xcrun', 'pkill', 'sleep', 'open', 'mv']) writeFileSync(join(bin, command), stub, { mode: 0o755 });
    const log = join(dir, 'calls');
    const result = spawnSync('bash', [installer], { env: { ...process.env, PATH: bin + ':' + process.env.PATH, FAIL: failure, LOG: log }, encoding: 'utf8' });
    const calls = readFileSync(log, 'utf8').trim().split('\n');
    if (failure) {
      assert.notEqual(result.status, 0, failure);
      assert.equal(readFileSync(join(applications, 'NeverAway.app', 'old'), 'utf8'), 'keep me');
      assert.equal(calls.includes('open'), false);
      if (failure !== 'move') assert.equal(calls.includes('pkill'), false);
    } else {
      assert.equal(result.status, 0, result.stderr);
      assert.equal(readFileSync(join(applications, 'NeverAway.app', 'new'), 'utf8'), 'verified fixture');
      assert(calls.indexOf('spctl') < calls.indexOf('pkill'));
      assert.equal(calls.includes('xcrun'), false);
      assert(calls.includes('open'));
    }
  }
});
