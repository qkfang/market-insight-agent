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

const singleIngestBtn = document.getElementById('single-ingest-btn');
const singleSpinner = document.getElementById('single-spinner');
const singleUrlInput = document.getElementById('single-url');
const singleUseText = document.getElementById('single-use-text');
const singleTextArea = document.getElementById('single-text-area');
const singleText = document.getElementById('single-text');
const singleTitle = document.getElementById('single-title');
const singleResult = document.getElementById('single-result');

singleUseText.addEventListener('change', () => {
  singleTextArea.hidden = !singleUseText.checked;
  singleUrlInput.disabled = singleUseText.checked;
});

singleIngestBtn.onclick = async () => {
  const url = singleUrlInput.value.trim();
  const text = singleText.value.trim();
  const title = singleTitle.value.trim();
  const useText = singleUseText.checked;

  if (!useText && !url) {
    singleResult.innerHTML = '<span class="status-badge error">Please enter a URL or paste text.</span>';
    return;
  }
  if (useText && !text) {
    singleResult.innerHTML = '<span class="status-badge error">Please paste article text.</span>';
    return;
  }

  singleIngestBtn.disabled = true;
  singleSpinner.hidden = false;
  singleResult.innerHTML = '';

  try {
    const body = {};
    if (!useText && url) body.url = url;
    if (useText && text) body.text = text;
    if (title) body.title = title;

    const response = await fetch('/api/news/ingest/single', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });

    const json = await response.json();
    if (!response.ok || json.error) {
      singleResult.innerHTML = `<span class="status-badge error">Error: ${escapeHtml(json.error || 'Unknown error')}</span>`;
    } else if (json.success) {
      singleResult.innerHTML = `<span class="status-badge success">✓ Saved as ${escapeHtml(json.filename)}</span>`;
      singleUrlInput.value = '';
      singleText.value = '';
      singleTitle.value = '';
      await refreshCache();
    } else {
      singleResult.innerHTML = `<span class="status-badge error">Failed: ${escapeHtml(json.error || 'Unknown error')}</span>`;
    }
  } catch (e) {
    singleResult.innerHTML = `<span class="status-badge error">Error: ${escapeHtml(e.message)}</span>`;
  } finally {
    singleIngestBtn.disabled = false;
    singleSpinner.hidden = true;
  }
};

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
    const r = await fetch('/api/cache/ingest');
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
