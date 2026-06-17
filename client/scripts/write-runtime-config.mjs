import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const publicDir = join(__dirname, '..', 'public');

function normalizeApiBaseUrl(url) {
  const trimmed = url.trim().replace(/\/$/, '');
  return trimmed.endsWith('/api') ? trimmed : `${trimmed}/api`;
}

function resolveApiBaseUrl() {
  const configuredUrl = process.env.API_BASE_URL ?? process.env.API_SERVICE_URL;
  if (configuredUrl) {
    return normalizeApiBaseUrl(configuredUrl);
  }

  return 'https://localhost:7128/api';
}

const apiBaseUrl = resolveApiBaseUrl();

mkdirSync(publicDir, { recursive: true });
writeFileSync(
  join(publicDir, 'runtime-config.js'),
  `window.padelConfig = ${JSON.stringify({ apiBaseUrl }, null, 2)};\n`,
  'utf8'
);
