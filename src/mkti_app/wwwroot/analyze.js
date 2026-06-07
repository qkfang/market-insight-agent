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

async function loadAnalysisTable() {
  const tableEl = document.getElementById('analysis-table');
  if (tableEl) tableEl.innerHTML = '<p>Loading analyzed articles...</p>';
  try {
    const response = await fetch('/api/news/analysis');
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const data = await response.json();
    renderAnalysisTable(data.articles || []);
  } catch (e) {
    if (tableEl) tableEl.innerHTML = `Error: ${escapeHtml(e.message)}`;
  }
}

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
    await loadAnalysisTable();
  } catch (e) {
    pre.textContent = `Error: ${e.message}`;
  } finally {
    btn.disabled = false;
  }
};

loadAnalysisTable();
