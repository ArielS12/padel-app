import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const publicDir = join(__dirname, '..', 'public');
const deployedApiBaseUrl = 'https://padel-api-xopm.onrender.com/api';
const localApiBaseUrl = 'https://localhost:7128/api';

function normalizeApiBaseUrl(url) {
  const trimmed = url.trim().replace(/\/$/, '');
  return trimmed.endsWith('/api') ? trimmed : `${trimmed}/api`;
}

function resolveApiBaseUrl() {
  if (process.env.RENDER) {
    return deployedApiBaseUrl;
  }

  if (process.env.API_BASE_URL) {
    return normalizeApiBaseUrl(process.env.API_BASE_URL);
  }

  return localApiBaseUrl;
}

const apiBaseUrl = resolveApiBaseUrl();

mkdirSync(publicDir, { recursive: true });
writeFileSync(
  join(publicDir, 'runtime-config.js'),
  `window.padelConfig = ${JSON.stringify({ apiBaseUrl }, null, 2)};\n`,
  'utf8'
);
