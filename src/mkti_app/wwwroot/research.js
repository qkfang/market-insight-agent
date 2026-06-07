const btn = document.getElementById('research-btn');
const spinner = document.getElementById('research-spinner');
const fromInput = document.getElementById('research-from');
const toInput = document.getElementById('research-to');
const resultEl = document.getElementById('research-result');
const pre = document.getElementById('result');

const badges = {
  bullish: { cls: 'bullish', icon: '▲', label: 'Bullish' },
  bearish: { cls: 'bearish', icon: '▼', label: 'Bearish' },
  neutral: { cls: 'neutral', icon: '◆', label: 'Neutral' }
};

const marketIcons = { copper: '🟤', gold: '🟡', silver: '⚪', oil: '🛢️' };

function getCheckedMarkets() {
  return Array.from(document.querySelectorAll('input[name="market"]:checked')).map(cb => cb.value);
}

function renderMarketCard(m) {
  const key = String(m.sentiment || 'neutral').toLowerCase();
  const badge = badges[key] || badges.neutral;
  const confidencePct = Math.round((Number(m.confidence) || 0) * 100);
  const drivers = Array.isArray(m.keyDrivers) ? m.keyDrivers : [];
  const driversHtml = drivers.length
    ? `<ul>${drivers.map(d => `<li>${escapeHtml(d)}</li>`).join('')}</ul>`
    : '<p>No key drivers identified.</p>';
  const icon = marketIcons[m.market] || '📊';
  return `
    <div class="research-card">
      <div class="research-card-header">
        <span class="research-market-name">${icon} ${escapeHtml((m.market || '').toUpperCase())}</span>
        <div class="sentiment-badge ${badge.cls}">
          <span class="sentiment-icon">${badge.icon}</span>
          <span class="sentiment-label">${badge.label}</span>
          <span class="sentiment-confidence">${confidencePct}% confidence</span>
        </div>
      </div>
      <div class="research-key-drivers">
        <h4>Key Drivers</h4>
        ${driversHtml}
      </div>
      <div class="research-summary">${escapeHtml(m.summary || '')}</div>
    </div>`;
}

function renderResult(data) {
  const markets = Array.isArray(data.markets) ? data.markets : [];
  if (markets.length === 0) {
    resultEl.innerHTML = '<p class="research-error">No results returned.</p>';
    return;
  }
  resultEl.innerHTML = `<div class="research-cards-grid">${markets.map(renderMarketCard).join('')}</div>`;
}

btn.onclick = async () => {
  const selected = getCheckedMarkets();
  if (selected.length === 0) {
    resultEl.innerHTML = '<p class="research-error">Please select at least one market.</p>';
    return;
  }
  btn.disabled = true;
  spinner.hidden = false;
  resultEl.innerHTML = '';
  pre.hidden = true;
  pre.textContent = '';
  try {
    const params = new URLSearchParams();
    if (fromInput.value) params.set('from', fromInput.value);
    if (toInput.value) params.set('to', toInput.value);
    params.set('markets', selected.join(','));
    const response = await fetch(`/api/market/research?${params}`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const json = await response.json();
    if (json.status === 'error') throw new Error(json.error || 'Research failed.');
    renderResult(json);
    await loadResearchTable();
  } catch (e) {
    resultEl.innerHTML = `<p class="research-error">Error: ${escapeHtml(e.message)}</p>`;
  } finally {
    btn.disabled = false;
    spinner.hidden = true;
  }
};

async function previewResearch(filename) {
  showModal(filename, 'Loading…');
  try {
    const response = await fetch(`/api/market/research/content?name=${encodeURIComponent(filename)}`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const data = await response.json();
    if (data.status !== 'ok') throw new Error(data.error || 'Failed to load content.');
    showModalWithTabs(filename, data.content, data.content);
  } catch (e) {
    showModal(filename, `Error: ${e.message}`);
  }
}

function renderResearchTable(reports) {
  const tableEl = document.getElementById('research-table');
  if (!tableEl) return;
  if (!reports || reports.length === 0) {
    tableEl.innerHTML = '<p>No research history yet.</p>';
    return;
  }
  const rows = reports.map(r => `
    <tr class="analysis-row" data-name="${escapeHtml(r.filename)}">
      <td>${escapeHtml(r.filename)}</td>
      <td>${escapeHtml(r.market || '')}</td>
      <td>${escapeHtml(r.weekStart || '')}</td>
    </tr>`).join('');
  tableEl.innerHTML = `
    <table class="analysis">
      <thead><tr><th>File</th><th>Market</th><th>Week Start</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>`;
  tableEl.querySelectorAll('.analysis-row').forEach(row => {
    row.onclick = () => previewResearch(row.dataset.name);
  });
}

async function loadResearchTable() {
  const tableEl = document.getElementById('research-table');
  if (tableEl) tableEl.innerHTML = '<p>Loading research history...</p>';
  try {
    const response = await fetch('/api/market/research/list');
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const data = await response.json();
    renderResearchTable(data.reports || []);
  } catch (e) {
    if (tableEl) tableEl.innerHTML = `Error: ${escapeHtml(e.message)}`;
  }
}

loadResearchTable();
