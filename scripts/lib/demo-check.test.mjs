// Run with `node --test scripts/lib/*.test.mjs`. No dependencies.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  baseUrlProblem,
  envValue,
  healthProblem,
  isLoopbackHost,
  linkNote,
  linkProblem,
  loopbackProblem,
  normalizeUrl,
  streamProblem,
} from './demo-check.mjs';

const envDemo = [
  '# Demo-only settings',
  'LINK_SECRET=demo-secret',
  'DEMO_TOOLS=1',
  '# The origin phones can reach',
  'PUBLIC_BASE_URL=',
].join('\n');

test('envValue reads a key, ignores comments, strips quotes and treats a missing key as undefined', () => {
  assert.equal(envValue(envDemo, 'DEMO_TOOLS'), '1');
  assert.equal(envValue(envDemo, 'PUBLIC_BASE_URL'), '');
  assert.equal(envValue('PUBLIC_BASE_URL="http://192.168.137.1:3000"\r\n', 'PUBLIC_BASE_URL'), 'http://192.168.137.1:3000');
  assert.equal(envValue("PUBLIC_BASE_URL = 'http://10.42.0.1:3000'", 'PUBLIC_BASE_URL'), 'http://10.42.0.1:3000');
  assert.equal(envValue('# PUBLIC_BASE_URL=http://x:3000', 'PUBLIC_BASE_URL'), undefined);
  assert.equal(envValue(envDemo, 'ETS_PRICE_EUR_PER_T'), undefined);
});

test('isLoopbackHost knows the names and addresses only this machine reaches', () => {
  for (const host of ['localhost', 'app.localhost', '127.0.0.1', '127.1.2.3', '0.0.0.0', '[::1]', '::1', '[::]']) {
    assert.equal(isLoopbackHost(host), true, host);
  }
  for (const host of ['192.168.137.1', '10.42.0.1', 'laptop.lan', '128.0.0.1', '[fe80::1]']) {
    assert.equal(isLoopbackHost(host), false, host);
  }
});

test('baseUrlProblem wants an http(s) origin without a path', () => {
  assert.equal(baseUrlProblem('http://192.168.137.1:3000'), null);
  assert.equal(baseUrlProblem('https://demo.example:443/'), null);
  assert.match(baseUrlProblem(''), /empty/);
  assert.match(baseUrlProblem(undefined), /empty/);
  assert.match(baseUrlProblem('192.168.137.1:3000'), /http/);
  assert.match(baseUrlProblem('not a url'), /not a URL/);
  assert.match(baseUrlProblem('ftp://192.168.137.1'), /http/);
  assert.match(baseUrlProblem('http://192.168.137.1:3000/verify'), /origin only/);
});

test('loopbackProblem refuses an origin a phone cannot reach', () => {
  assert.equal(loopbackProblem('http://192.168.137.1:3000'), null);
  assert.match(loopbackProblem('http://localhost:3000'), /loopback/);
  assert.match(loopbackProblem('http://127.0.0.1:3000'), /loopback/);
});

test('healthProblem passes only a 200 with ok: true', () => {
  assert.equal(healthProblem(200, { ok: true, db: 'ok' }), null);
  assert.match(healthProblem(503, { ok: false, db: 'down' }), /503 \(db: down\)/);
  assert.match(healthProblem(502, '<html>Bad Gateway</html>'), /502 without/);
  assert.match(healthProblem(200, null), /without/);
});

test('linkProblem, with PUBLIC_BASE_URL set: the API must use it as configured', () => {
  const base = 'http://192.168.137.1:3000';
  const health = (url, source, loopback = false) => ({ baseUrl: { url, source, loopback } });
  assert.equal(linkProblem(health(base, 'configured'), base, true), null);
  assert.equal(linkProblem(health(`${base}/`, 'configured'), base, true), null);
  assert.match(linkProblem(health(base, 'forwarded'), base, true), /from the request \(forwarded\), not from PUBLIC_BASE_URL.*Restart/);
  assert.match(linkProblem(health('http://10.42.0.1:3000', 'configured'), base, true), /not from http:\/\/192\.168\.137\.1:3000.*Restart/);
  assert.match(linkProblem({}, base, true), /did not report/);
});

test('linkProblem, with PUBLIC_BASE_URL empty: a request to the URL must yield links on it', () => {
  const base = 'http://192.168.137.1:3000';
  const health = (url, source, loopback = false) => ({ baseUrl: { url, source, loopback } });
  assert.equal(linkProblem(health(base, 'forwarded'), base, false), null);
  assert.equal(linkProblem(health(base, 'request'), base, false), null);
  const wrong = linkProblem(health('http://localhost:8080', 'request', true), base, false);
  assert.match(wrong, /localhost:8080 \(request\), not from/);
  assert.doesNotMatch(wrong, /Restart/);
});

test('linkNote reminds where to open the dashboard only when links follow the request', () => {
  const base = 'http://192.168.137.1:3000';
  assert.equal(linkNote({ baseUrl: { url: base, source: 'configured' } }, base), null);
  assert.match(linkNote({ baseUrl: { url: base, source: 'forwarded' } }, base), /open it at http:\/\/192\.168\.137\.1:3000, never at localhost/);
});

test('streamProblem wants an event stream that delivers its first event', () => {
  const hello = 'event: hello\ndata: {}\n\n';
  assert.equal(streamProblem(200, 'text/event-stream; charset=utf-8', hello), null);
  assert.match(streamProblem(404, 'application/json', ''), /HTTP 404/);
  assert.match(streamProblem(200, 'text/html', '<html>'), /not text\/event-stream/);
  assert.match(streamProblem(200, 'text/event-stream', ''), /buffering/);
});

test('normalizeUrl drops trailing slashes only', () => {
  assert.equal(normalizeUrl('http://x:3000/'), 'http://x:3000');
  assert.equal(normalizeUrl('http://x:3000'), 'http://x:3000');
});
