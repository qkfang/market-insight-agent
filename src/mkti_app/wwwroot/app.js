const tabs = [
  { key: 'ingest', title: 'News Ingestion' },
  { key: 'analyze', title: 'News Analysis' },
  { key: 'research', title: 'Market Research' },
  { key: 'generate', title: 'Insight Generation' },
  { key: 'subscription', title: 'Subscription' },
  { key: 'delivery', title: 'Insight Delivery' }
];

const tabsEl = document.getElementById('tabs');
const contentEl = document.getElementById('content');

function setTab(key) {
  [...tabsEl.children].forEach(btn => btn.classList.toggle('active', btn.dataset.key === key));
  renderTab(key);
}

function addActionTab(key, title, endpoint, resultFormatter = (r) => JSON.stringify(r, null, 2)) {
  contentEl.innerHTML = `<h2>${title}</h2><button class="action">Run</button><pre id="result">Ready</pre>`;
  contentEl.querySelector('button').onclick = async () => {
    const pre = contentEl.querySelector('#result');
    pre.textContent = 'Running...';
    try {
      const response = await fetch(endpoint);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const json = await response.json();
      pre.textContent = resultFormatter(json);
    } catch (e) {
      pre.textContent = `Error: ${e.message}`;
    }
  };
}

function escapeHtml(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function closeModal() {
  const existing = document.getElementById('modal-overlay');
  if (existing) existing.remove();
}

function showModal(title, body) {
  closeModal();
  const overlay = document.createElement('div');
  overlay.id = 'modal-overlay';
  overlay.className = 'modal-overlay';
  overlay.innerHTML = `
    <div class="modal">
      <div class="modal-header">
        <strong>${escapeHtml(title)}</strong>
        <button class="modal-close" type="button">&times;</button>
      </div>
      <pre class="modal-body">${escapeHtml(body)}</pre>
    </div>`;
  overlay.addEventListener('click', (e) => { if (e.target === overlay) closeModal(); });
  overlay.querySelector('.modal-close').onclick = closeModal;
  document.body.appendChild(overlay);
}

async function previewAnalysis(filename) {
  showModal(filename, 'Loading...');
  try {
    const response = await fetch(`/api/news/analysis/content?name=${encodeURIComponent(filename)}`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const data = await response.json();
    if (data.status !== 'ok') throw new Error(data.error || 'Failed to load content.');
    let body = data.content;
    try {
      const parsed = JSON.parse(data.content);
      body = parsed.markdownContent || data.content;
    } catch {}
    showModal(filename, body);
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

async function renderAnalyzeTab() {
  contentEl.innerHTML = `
    <h2>News Analysis</h2>
    <p>Parse unprocessed news articles with Document Intelligence into structured JSON.</p>
    <button class="action" id="analyze-btn">Analyze Articles</button>
    <pre id="result">Ready</pre>
    <h3>Analyzed Articles</h3>
    <div id="analysis-table"></div>`;

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

  await loadAnalysisTable();
}

async function renderTab(key) {
  if (key === 'ingest') return addActionTab(key, 'News Ingestion', '/api/news/ingest');
  if (key === 'analyze') return renderAnalyzeTab();
  if (key === 'research') return addActionTab(key, 'Market Research', '/api/market/research');
  if (key === 'generate') return addActionTab(key, 'Insight Generation', '/api/insight/generate');

  if (key === 'subscription') {
    const markets = ['Copper', 'Aluminum', 'Nickel'];
    const items = ['DailyInsight', 'WeeklyTrend', 'RiskAlert'];
    contentEl.innerHTML = `
      <h2>Subscription</h2>
      <div><strong>Markets</strong>${markets.map(m => `<label><input type="checkbox" name="market" value="${m}"> ${m}</label>`).join('')}</div>
      <div><strong>Items</strong>${items.map(i => `<label><input type="checkbox" name="item" value="${i}"> ${i}</label>`).join('')}</div>
      <button class="action">Save</button>
      <pre id="result">Ready</pre>`;

    try {
      const subscriptionResponse = await fetch('/api/subscription');
      if (!subscriptionResponse.ok) throw new Error(`HTTP ${subscriptionResponse.status}`);
      const current = await subscriptionResponse.json();
      const parsed = JSON.parse(current.subscription || '{}');
      document.querySelectorAll('input[name="market"]').forEach(c => c.checked = (parsed.markets || []).includes(c.value));
      document.querySelectorAll('input[name="item"]').forEach(c => c.checked = (parsed.items || []).includes(c.value));
    } catch {}

    contentEl.querySelector('button').onclick = async () => {
      const selectedMarkets = [...document.querySelectorAll('input[name="market"]:checked')].map(x => x.value);
      const selectedItems = [...document.querySelectorAll('input[name="item"]:checked')].map(x => x.value);
      const pre = document.getElementById('result');
      pre.textContent = 'Saving...';
      try {
        const response = await fetch('/api/subscription', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ markets: selectedMarkets, items: selectedItems })
        });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        pre.textContent = JSON.stringify(await response.json(), null, 2);
      } catch (e) {
        pre.textContent = `Error: ${e.message}`;
      }
    };
    return;
  }

  if (key === 'delivery') {
    contentEl.innerHTML = '<h2>Insight Delivery</h2><pre id="result">Loading...</pre>';
    try {
      const insightResponse = await fetch('/api/insight/latest');
      if (!insightResponse.ok) throw new Error(`HTTP ${insightResponse.status}`);
      const data = await insightResponse.json();
      document.getElementById('result').textContent = data.insight || 'No insight available.';
    } catch (e) {
      document.getElementById('result').textContent = `Error: ${e.message}`;
    }
  }
}

for (const tab of tabs) {
  const btn = document.createElement('button');
  btn.textContent = tab.title;
  btn.dataset.key = tab.key;
  btn.onclick = () => setTab(tab.key);
  tabsEl.appendChild(btn);
}

setTab('ingest');
