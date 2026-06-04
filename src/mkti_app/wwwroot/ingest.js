const btn = document.getElementById('ingest-btn');
const spinner = document.getElementById('ingest-spinner');
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
    const response = await fetch('/api/news/list');
    if (!response.ok) return;
    const json = await response.json();
    renderFiles(json.filenames);
  } catch (e) {
    console.warn('Failed to load existing articles:', e);
  }
}

loadExisting();

btn.onclick = async () => {
  btn.disabled = true;
  spinner.hidden = false;
  summary.textContent = '';
  pre.textContent = 'Ingesting news...';
  try {
    const response = await fetch('/api/news/ingest');
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const json = await response.json();
    const count = json.articlesStored ?? 0;
    summary.innerHTML = `<strong>${json.success ? 'Success' : 'Failed'}:</strong> ${count} article(s) stored.`;
    meta.innerHTML = `<em>Last ingestion: ${new Date().toLocaleString()}</em>`;
    renderFiles(json.filenames);
    pre.textContent = json.message || '';
  } catch (e) {
    summary.innerHTML = `<strong>Error:</strong> ${escapeHtml(e.message)}`;
    pre.textContent = `Error: ${e.message}`;
  } finally {
    btn.disabled = false;
    spinner.hidden = true;
  }
};
