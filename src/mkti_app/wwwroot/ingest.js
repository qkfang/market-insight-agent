const btn = document.getElementById('ingest-btn');
const spinner = document.getElementById('ingest-spinner');
const fromInput = document.getElementById('ingest-from');
const toInput = document.getElementById('ingest-to');
const meta = document.getElementById('ingest-meta');
const summary = document.getElementById('ingest-summary');
const filesEl = document.getElementById('ingest-files');
const pre = document.getElementById('result');

function renderFiles(filenames) {
  if (!filenames || filenames.length === 0) {
    filesEl.innerHTML = '<li>No articles stored yet.</li>';
    return;
  }
  filesEl.innerHTML = filenames.map(f => `<li>${escapeHtml(f)}</li>`).join('');
}

async function loadExisting() {
  try {
    const params = new URLSearchParams();
    if (fromInput.value) params.set('from', fromInput.value);
    if (toInput.value) params.set('to', toInput.value);
    const response = await fetch(`/api/articles/list?${params}`);
    if (!response.ok) return;
    const json = await response.json();
    renderFiles(json.filenames);
  } catch (e) {
    console.warn('Failed to load existing articles:', e);
  }
}

loadExisting();

fromInput.addEventListener('change', loadExisting);
toInput.addEventListener('change', loadExisting);

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
    renderFiles(json.filenames);
    pre.textContent = json.message || '';
  } catch (e) {
    summary.innerHTML = `<span class="status-badge error">Error: ${escapeHtml(e.message)}</span>`;
    pre.textContent = `Error: ${e.message}`;
  } finally {
    btn.disabled = false;
    spinner.hidden = true;
  }
};
