// The decisions behind scripts/demo-check.mjs (B7), kept pure so they can be tested without a network. Each returns
// null when the check passes, or the reason it fails in words a person at the demo laptop can act on.

/** The value of `key` in a dotenv-style file (`KEY=value`, `#` comments, optional quotes); undefined when absent. */
export function envValue(text, key) {
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (line === '' || line.startsWith('#')) continue;
    const eq = line.indexOf('=');
    if (eq < 0 || line.slice(0, eq).trim() !== key) continue;
    const value = line.slice(eq + 1).trim();
    const quoted = value.length >= 2 && (value[0] === '"' || value[0] === "'") && value.at(-1) === value[0];
    return quoted ? value.slice(1, -1) : value;
  }
  return undefined;
}

/** True for names and addresses that only this machine can reach: localhost, 127.0.0.0/8, 0.0.0.0, ::1, ::. */
export function isLoopbackHost(hostname) {
  const host = hostname.toLowerCase().replace(/^\[|\]$/g, '');
  if (host === 'localhost' || host.endsWith('.localhost')) return true;
  if (host === '::1' || host === '::' || host === '0.0.0.0') return true;
  return /^127(\.\d{1,3}){3}$/.test(host);
}

/** The URL without a trailing slash, so `http://x:3000/` and `http://x:3000` compare equal. */
export const normalizeUrl = (url) => url.replace(/\/+$/, '');

/** Why `value` cannot be the origin phones open, or null: it must be an http(s) origin with no path. */
export function baseUrlProblem(value) {
  if (value === undefined || value.trim() === '') {
    return 'PUBLIC_BASE_URL is empty: set it in .env.demo to http://<laptop-ip>:3000 (`ipconfig` or `ip -4 addr`).';
  }
  let url;
  try {
    url = new URL(value);
  } catch {
    return `${value} is not a URL; write it as http://<laptop-ip>:3000.`;
  }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') return `${value} must start with http:// or https://.`;
  if (url.pathname !== '/' || url.search !== '' || url.hash !== '') {
    return `${value} must be an origin only (scheme, host and port), without a path.`;
  }
  return null;
}

/** Why a phone could not reach `value`, or null. */
export function loopbackProblem(value) {
  const { hostname } = new URL(value);
  return isLoopbackHost(hostname)
    ? `${hostname} is this laptop's own loopback address; a phone needs the LAN or hotspot IP (e.g. 192.168.137.1).`
    : null;
}

/** Why `GET /api/health` does not show a working API, or null. 503 carries the same body while the database is down. */
export function healthProblem(status, body) {
  if (typeof body !== 'object' || body === null) return `/api/health answered HTTP ${status} without the API's JSON body.`;
  if (status !== 200 || body.ok !== true) return `/api/health answered HTTP ${status} (db: ${body.db ?? 'unknown'}).`;
  return null;
}

/**
 * Why the API would put another origin than `base` in its QR links, or null. It must use PUBLIC_BASE_URL as configured
 * (`source: "configured"`); anything else means the containers did not get the value, e.g. .env.demo was edited after
 * the stack started.
 */
export function linkProblem(body, base) {
  const info = body?.baseUrl;
  if (typeof info?.url !== 'string') return '/api/health did not report the base URL the API builds links from.';
  if (normalizeUrl(info.url) !== normalizeUrl(base)) {
    return `The API builds links from ${info.url} (${info.source}), not from ${normalizeUrl(base)}. Restart the stack after editing .env.demo.`;
  }
  if (info.source !== 'configured') {
    return `The API takes this address from the request (${info.source}), so links follow the address the dashboard is opened at; opened at localhost, it prints localhost links. Set PUBLIC_BASE_URL in .env.demo and restart the stack.`;
  }
  if (info.loopback === true) return `The API reports ${info.url} as a loopback address.`;
  return null;
}

/** Why `GET /api/events` is not a live event stream, or null: it must be text/event-stream and deliver its first event. */
export function streamProblem(status, contentType, firstChunk) {
  if (status !== 200) return `/api/events answered HTTP ${status}.`;
  if (!(contentType ?? '').startsWith('text/event-stream')) {
    return `/api/events answered ${contentType ?? 'no content type'}, not text/event-stream.`;
  }
  if (!/^event: /m.test(firstChunk)) {
    return '/api/events opened but sent no event within the time limit; a proxy may be buffering it.';
  }
  return null;
}
