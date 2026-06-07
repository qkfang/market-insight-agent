const btn = document.getElementById('ingest-btn');
const spinner = document.getElementById('ingest-spinner');
const fromInput = document.getElementById('ingest-from');
const toInput = document.getElementById('ingest-to');
const meta = document.getElementById('ingest-meta');
const summary = document.getElementById('ingest-summary');
const filesEl = document.getElementById('ingest-files');
const pre = document.getElementById('result');
const refreshBtn = document.getElementById('ingest-refresh-btn');
const cacheTimeEl = document.getElementById('ingest-cache-time');

function filterByDate(filenames) {
  const from = fromInput.value;
  const to = toInput.value;
  return filenames.filter(f => {
    const prefix = f.slice(0, 10);
    if (from && prefix < from) return false;
    if (to && prefix > to) return false;
    return true;
  });
}

function renderFiles(filenames) {
  if (!filenames || filenames.length === 0) {
    filesEl.innerHTML = '<li>No articles stored yet.</li>';
    return;
  }
  filesEl.innerHTML = filenames.map(f => `<li>${escapeHtml(f)}</li>`).join('');
}

let cachedFilenames = [];

async function loadFromCache() {
  try {
    const r = await fetch('/temp/cache-ingest.json');
    if (!r.ok) throw new Error('no cache');
    const json = await r.json();
    cachedFilenames = json.filenames || [];
    if (json.cachedAt) cacheTimeEl.textContent = `cached ${new Date(json.cachedAt).toLocaleString()}`;
    renderFiles(filterByDate(cachedFilenames));
  } catch {
    filesEl.innerHTML = '<li style="color:var(--color-text-muted)">No cache yet — click ↻ Refresh List to load.</li>';
    cachedFilenames = [];
  }
}

async function refreshCache() {
  refreshBtn.disabled = true;
  filesEl.innerHTML = '<li>Refreshing…</li>';
  try {
    const r = await fetch('/api/cache/refresh/ingest', { method: 'POST' });
    if (!r.ok) throw new Error(`HTTP ${r.status}`);
    const json = await r.json();
    cachedFilenames = json.filenames || [];
    cacheTimeEl.textContent = `refreshed ${new Date().toLocaleString()}`;
    renderFiles(filterByDate(cachedFilenames));
  } catch (e) {
    filesEl.innerHTML = `<li style="color:var(--color-text-muted)">Refresh failed: ${escapeHtml(e.message)}</li>`;
  } finally {
    refreshBtn.disabled = false;
  }
}

loadFromCache();

fromInput.addEventListener('change', () => renderFiles(filterByDate(cachedFilenames)));
toInput.addEventListener('change', () => renderFiles(filterByDate(cachedFilenames)));
refreshBtn.onclick = refreshCache;

btn.onclick = async () => {
  btn.disabled = true;
  spinner.hidden = false;
  summary.innerHTML = '';
  pre.textContent = 'Ingesting news...';
  pre.className = '';
  try {
    const params = new URLSearchParams();
    if (fromInput.value) params.set('from', fromInput.value);
    if (toInput.value) params.set('to', toInput.value);
    const response = await fetch(`/api/news/ingest?${params}`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const json = await response.json();
    const count = json.articlesStored ?? 0;
    summary.innerHTML = json.success
      ? `<span class="status-badge success">✓ ${count} article(s) stored</span>`
      : `<span class="status-badge error">Failed</span>`;
    meta.innerHTML = `🕐 Last ingestion: ${new Date().toLocaleString()}`;
    pre.textContent = json.message || '';
    await refreshCache();
  } catch (e) {
    summary.innerHTML = `<span class="status-badge error">Error: ${escapeHtml(e.message)}</span>`;
    pre.textContent = `Error: ${e.message}`;
  } finally {
    btn.disabled = false;
    spinner.hidden = true;
  }
};
