const btn = document.getElementById('generate-btn');
const spinner = document.getElementById('generate-spinner');
const fromInput = document.getElementById('generate-from');
const toInput = document.getElementById('generate-to');
const resultEl = document.getElementById('generate-result');

const marketIcons = { copper: '🟤', gold: '🟡', silver: '⚪', oil: '🛢️' };

function getCheckedMarkets() {
  return Array.from(document.querySelectorAll('input[name="gen-market"]:checked')).map(cb => cb.value);
}

function renderInsightCard(m) {
  const icon = marketIcons[m.market] || '📊';
  const previewText = (m.preview || '').replace(/^#+\s.*/gm, '').replace(/\*\*/g, '').trim();
  const shortPreview = previewText.length > 400 ? previewText.slice(0, 400) + '…' : previewText;
  const filenameLabel = m.filename ? `<span class="status-badge success" style="font-size:11px;">${escapeHtml(m.filename)}</span>` : '';
  return `
    <div class="research-card">
      <div class="research-card-header">
        <span class="research-market-name">${icon} ${escapeHtml((m.market || '').toUpperCase())}</span>
        <div style="display:flex;align-items:center;gap:8px;">
          ${filenameLabel}
          <a class="action" style="padding:4px 12px;font-size:12px;" href="/delivery.html">→ Full Report</a>
        </div>
      </div>
      <div class="research-summary" style="white-space:pre-wrap;font-size:13px;margin-top:10px;">${escapeHtml(shortPreview)}</div>
    </div>`;
}

function renderResult(data) {
  const markets = Array.isArray(data.markets) ? data.markets : [];
  if (markets.length === 0) {
    resultEl.innerHTML = '<p class="research-error">No results returned.</p>';
    return;
  }
  resultEl.innerHTML = `<div class="research-cards-grid">${markets.map(renderInsightCard).join('')}</div>`;
}

btn.onclick = async () => {
  const selected = getCheckedMarkets();
  if (selected.length === 0) {
    resultEl.innerHTML = '<p class="research-error">Please select at least one market.</p>';
    return;
  }
  btn.disabled = true;
  spinner.hidden = false;
  resultEl.innerHTML = `<p style="color:var(--color-text-muted);font-size:13px;">Generating insight reports… this may take 30–90 seconds per market.</p>`;
  try {
    const params = new URLSearchParams();
    if (fromInput.value) params.set('from', fromInput.value);
    if (toInput.value) params.set('to', toInput.value);
    params.set('markets', selected.join(','));
    const response = await fetch(`/api/insight/generate?${params}`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const json = await response.json();
    if (json.status === 'error') throw new Error(json.error || 'Generation failed.');
    renderResult(json);
    await loadGenerateTable();
  } catch (e) {
    resultEl.innerHTML = `<p class="research-error">Error: ${escapeHtml(e.message)}</p>`;
  } finally {
    btn.disabled = false;
    spinner.hidden = true;
  }
};

async function previewInsight(filename) {
  showModal(filename, 'Loading…');
  try {
    const response = await fetch(`/api/insight/content?name=${encodeURIComponent(filename)}`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const data = await response.json();
    if (data.status !== 'ok') throw new Error(data.error || 'Failed to load content.');
    showModalWithTabs(filename, data.content, data.content);
  } catch (e) {
    showModal(filename, `Error: ${e.message}`);
  }
}

function renderGenerateTable(reports) {
  const tableEl = document.getElementById('generate-table');
  if (!tableEl) return;
  if (!reports || reports.length === 0) {
    tableEl.innerHTML = '<p>No generated insights yet.</p>';
    return;
  }
  const rows = reports.map(r => `
    <tr class="analysis-row" data-name="${escapeHtml(r.filename)}">
      <td>${escapeHtml(r.filename)}</td>
      <td>${escapeHtml(r.market || '')}</td>
      <td>${escapeHtml(r.date || '')}</td>
    </tr>`).join('');
  tableEl.innerHTML = `
    <table class="analysis">
      <thead><tr><th>File</th><th>Market</th><th>Date</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>`;
  tableEl.querySelectorAll('.analysis-row').forEach(row => {
    row.onclick = () => previewInsight(row.dataset.name);
  });
}

async function loadGenerateTable() {
  const tableEl = document.getElementById('generate-table');
  if (tableEl) tableEl.innerHTML = '<p>Loading generated insights...</p>';
  try {
    const response = await fetch('/api/insight/list');
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const data = await response.json();
    renderGenerateTable(data.reports || []);
  } catch (e) {
    if (tableEl) tableEl.innerHTML = `Error: ${escapeHtml(e.message)}`;
  }
}

loadGenerateTable();
