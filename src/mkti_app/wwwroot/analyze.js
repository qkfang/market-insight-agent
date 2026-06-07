async function previewAnalysis(filename) {
  showModal(filename, 'Loading…');
  try {
    const response = await fetch(`/api/news/analysis/content?name=${encodeURIComponent(filename)}`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const data = await response.json();
    if (data.status !== 'ok') throw new Error(data.error || 'Failed to load content.');
    showModalWithTabs(filename, data.content, JSON.stringify(data, null, 2));
  } catch (e) {
    showModal(filename, `Error: ${e.message}`);
  }
}

function renderAnalysisTable(articles) {
  const tableEl = document.getElementById('analysis-table');
  if (!tableEl) return;
  if (!articles || articles.length === 0) {
    tableEl.innerHTML = '<p>No analyzed articles yet.</p>';
    return;
  }
  const rows = articles.map(a => `
    <tr class="analysis-row" data-name="${escapeHtml(a.filename)}">
      <td>${escapeHtml(a.title || a.filename)}</td>
      <td>${escapeHtml(a.date || '')}</td>
      <td>${a.wordCount ?? ''}</td>
    </tr>`).join('');
  tableEl.innerHTML = `
    <table class="analysis">
      <thead><tr><th>Article</th><th>Date</th><th>Word Count</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>`;
  tableEl.querySelectorAll('.analysis-row').forEach(row => {
    row.onclick = () => previewAnalysis(row.dataset.name);
  });
}

const refreshBtn = document.getElementById('analyze-refresh-btn');
const cacheTimeEl = document.getElementById('analyze-cache-time');

async function loadAnalysisTable() {
  const tableEl = document.getElementById('analysis-table');
  try {
    const r = await fetch('/temp/cache-analyze.json');
    if (!r.ok) throw new Error('no cache');
    const data = await r.json();
    if (data.cachedAt && cacheTimeEl) cacheTimeEl.textContent = `cached ${new Date(data.cachedAt).toLocaleString()}`;
    renderAnalysisTable(data.articles || []);
  } catch {
    if (tableEl) tableEl.innerHTML = '<p style="color:var(--color-text-muted)">No cache yet — click ↻ Refresh List to load.</p>';
  }
}

async function refreshAnalysisCache() {
  if (refreshBtn) refreshBtn.disabled = true;
  const tableEl = document.getElementById('analysis-table');
  if (tableEl) tableEl.innerHTML = '<p>Refreshing from blob storage… this may take a moment.</p>';
  try {
    const r = await fetch('/api/cache/refresh/analyze', { method: 'POST' });
    if (!r.ok) throw new Error(`HTTP ${r.status}`);
    const data = await r.json();
    if (cacheTimeEl) cacheTimeEl.textContent = `refreshed ${new Date().toLocaleString()}`;
    renderAnalysisTable(data.articles || []);
  } catch (e) {
    if (tableEl) tableEl.innerHTML = `<p style="color:var(--color-text-muted)">Refresh failed: ${escapeHtml(e.message)}</p>`;
  } finally {
    if (refreshBtn) refreshBtn.disabled = false;
  }
}

if (refreshBtn) refreshBtn.onclick = refreshAnalysisCache;

document.getElementById('analyze-btn').onclick = async () => {
  const pre = document.getElementById('result');
  const btn = document.getElementById('analyze-btn');
  btn.disabled = true;
  pre.textContent = 'Analyzing... this may take a moment.';
  try {
    const response = await fetch('/api/news/analyze');
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const json = await response.json();
    pre.textContent = JSON.stringify(json, null, 2);
    await refreshAnalysisCache();
  } catch (e) {
    pre.textContent = `Error: ${e.message}`;
  } finally {
    btn.disabled = false;
  }
};

loadAnalysisTable();
