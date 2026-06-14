import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const publicDir = join(__dirname, '..', 'public');
const apiBaseUrl = process.env.API_BASE_URL ?? 'https://localhost:7128/api';

mkdirSync(publicDir, { recursive: true });
writeFileSync(
  join(publicDir, 'runtime-config.js'),
  `window.padelConfig = ${JSON.stringify({ apiBaseUrl }, null, 2)};\n`,
  'utf8'
);
