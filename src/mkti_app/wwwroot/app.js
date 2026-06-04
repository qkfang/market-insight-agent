const tabs = [
  { key: 'knowledge', title: 'Knowledge' },
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

function renderResearchTab() {
  contentEl.innerHTML = `
    <h2>Market Research</h2>
    <p>Research current copper market sentiment from the latest news analysis, Bing search, and the Fabric data agent. This may take 30-60 seconds.</p>
    <button class="action" id="research-btn">Research Market</button>
    <span id="research-spinner" class="spinner" hidden></span>
    <div id="research-result"></div>
    <pre id="result" hidden></pre>`;

  const btn = document.getElementById('research-btn');
  const spinner = document.getElementById('research-spinner');
  const resultEl = document.getElementById('research-result');
  const pre = document.getElementById('result');

  const badges = {
    bullish: { cls: 'bullish', icon: '🟢', label: 'Bullish' },
    bearish: { cls: 'bearish', icon: '🔴', label: 'Bearish' },
    neutral: { cls: 'neutral', icon: '🟡', label: 'Neutral' }
  };

  function renderResult(data) {
    const key = String(data.sentiment || 'neutral').toLowerCase();
    const badge = badges[key] || badges.neutral;
    const confidencePct = Math.round((Number(data.confidence) || 0) * 100);
    const drivers = Array.isArray(data.keyDrivers) ? data.keyDrivers : [];
    const driversHtml = drivers.length
      ? `<ul>${drivers.map(d => `<li>${escapeHtml(d)}</li>`).join('')}</ul>`
      : '<p>No key drivers identified.</p>';

    resultEl.innerHTML = `
      <div class="sentiment-badge ${badge.cls}">
        <span class="sentiment-icon">${badge.icon}</span>
        <span class="sentiment-label">${badge.label}</span>
        <span class="sentiment-confidence">${confidencePct}% confidence</span>
      </div>
      <h3>Key Drivers</h3>
      ${driversHtml}
      <h3>Research Summary</h3>
      <p class="research-summary">${escapeHtml(data.summary || '')}</p>
      ${data.timestamp ? `<p class="research-timestamp"><em>Generated: ${new Date(data.timestamp).toLocaleString()}</em></p>` : ''}`;
  }

  btn.onclick = async () => {
    btn.disabled = true;
    spinner.hidden = false;
    resultEl.innerHTML = '';
    pre.hidden = true;
    pre.textContent = '';
    try {
      const response = await fetch('/api/market/research');
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const json = await response.json();
      if (json.status === 'error') throw new Error(json.error || 'Research failed.');
      renderResult(json);
    } catch (e) {
      resultEl.innerHTML = `<p class="research-error">Error: ${escapeHtml(e.message)}</p>`;
    } finally {
      btn.disabled = false;
      spinner.hidden = true;
    }
  };
}

function renderIngestTab() {
  contentEl.innerHTML = `
    <h2>News Ingestion</h2>
    <p>Download the latest copper market news from RSS feeds and store articles in Fabric Datalake and Azure Blob Storage.</p>
    <button class="action" id="ingest-btn">Ingest Now</button>
    <span id="ingest-spinner" class="spinner" hidden></span>
    <div id="ingest-meta"></div>
    <div id="ingest-summary"></div>
    <h3>Stored Articles</h3>
    <ul id="ingest-files"><li>No articles loaded yet.</li></ul>
    <pre id="result">Ready</pre>`;

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
    filesEl.innerHTML = filenames.map(f => `<li>${f}</li>`).join('');
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
      summary.innerHTML = `<strong>Error:</strong> ${e.message}`;
      pre.textContent = `Error: ${e.message}`;
    } finally {
      btn.disabled = false;
      spinner.hidden = true;
    }
  };
}

async function renderTab(key) {
  if (key === 'knowledge') return renderKnowledgeTab();
  if (key === 'ingest') return renderIngestTab();
  if (key === 'analyze') return renderAnalyzeTab();
  if (key === 'research') return renderResearchTab();
  if (key === 'generate') return renderGenerateTab();
  if (key === 'subscription') return renderSubscriptionTab();
  if (key === 'delivery') return renderDeliveryTab();
}

function renderKnowledgeTab() {
  contentEl.innerHTML = `
    <h2>Knowledge</h2>
    <p>Run the centralized pipeline on top articles: ingestion, analysis, market research, and insight generation.</p>
    <button class="action" id="knowledge-run-btn">Run Knowledge Pipeline</button>
    <pre id="result">Ready</pre>`;

  document.getElementById('knowledge-run-btn').onclick = async () => {
    const pre = document.getElementById('result');
    const btn = document.getElementById('knowledge-run-btn');
    btn.disabled = true;
    pre.textContent = 'Running centralized pipeline... this may take a moment.';
    try {
      const response = await fetch('/api/knowledge/run');
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const json = await response.json();
      pre.textContent = JSON.stringify(json, null, 2);
    } catch (e) {
      pre.textContent = `Error: ${e.message}`;
    } finally {
      btn.disabled = false;
    }
  };
}

function renderGenerateTab() {
  contentEl.innerHTML = `
    <h2>Insight Generation</h2>
    <p>Generate today's copper market insight report. This may take 30-60 seconds.</p>
    <button class="action" id="generate-btn">Generate Today's Insight</button>
    <span id="generate-spinner" class="spinner" hidden></span>
    <div id="generate-summary"></div>
    <h3>Preview</h3>
    <pre id="generate-preview">No report generated yet.</pre>
    <div id="generate-link"></div>`;

  const btn = document.getElementById('generate-btn');
  const spinner = document.getElementById('generate-spinner');
  const summary = document.getElementById('generate-summary');
  const preview = document.getElementById('generate-preview');
  const link = document.getElementById('generate-link');

  btn.onclick = async () => {
    btn.disabled = true;
    spinner.hidden = false;
    summary.textContent = '';
    link.innerHTML = '';
    preview.textContent = 'Generating insight report... this may take 30-60 seconds.';
    try {
      const response = await fetch('/api/insight/generate');
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const json = await response.json();
      if (!json.success) throw new Error(json.error || 'Generation failed.');
      summary.innerHTML = `<strong>Success:</strong> stored as ${escapeHtml(json.filename || '')} (${escapeHtml(json.date || '')}).`;
      preview.textContent = json.preview || '(empty report)';
      const fullLink = document.createElement('button');
      fullLink.className = 'action';
      fullLink.textContent = 'View Full Report';
      fullLink.onclick = () => setTab('delivery');
      link.appendChild(fullLink);
    } catch (e) {
      summary.innerHTML = `<strong>Error:</strong> ${escapeHtml(e.message)}`;
      preview.textContent = `Error: ${e.message}`;
    } finally {
      btn.disabled = false;
      spinner.hidden = true;
    }
  };
}

async function renderSubscriptionTab() {
  const markets = ['Copper', 'Gold', 'Silver', 'Aluminum'];
  const items = ['Daily Report', 'Sentiment Alert', 'Price Alert'];
  contentEl.innerHTML = `
    <h2>Insight Subscription</h2>
    <div><strong>Markets</strong>${markets.map(m => `<label><input type="checkbox" name="market" value="${escapeHtml(m)}"> ${escapeHtml(m)}</label>`).join('')}</div>
    <div><strong>Items</strong>${items.map(i => `<label><input type="checkbox" name="item" value="${escapeHtml(i)}"> ${escapeHtml(i)}</label>`).join('')}</div>
    <button class="action" id="subscription-save">Save Preferences</button>
    <pre id="result">Ready</pre>`;

  try {
    const subscriptionResponse = await fetch('/api/subscription');
    if (!subscriptionResponse.ok) throw new Error(`HTTP ${subscriptionResponse.status}`);
    const current = await subscriptionResponse.json();
    const selMarkets = current.markets || [];
    const selItems = current.items || [];
    document.querySelectorAll('input[name="market"]').forEach(c => c.checked = selMarkets.includes(c.value));
    document.querySelectorAll('input[name="item"]').forEach(c => c.checked = selItems.includes(c.value));
  } catch {}

  document.getElementById('subscription-save').onclick = async () => {
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
      const json = await response.json();
      pre.textContent = json.success ? 'Preferences saved.' : JSON.stringify(json, null, 2);
    } catch (e) {
      pre.textContent = `Error: ${e.message}`;
    }
  };
}

function loadMarked() {
  return new Promise((resolve) => {
    if (window.marked) return resolve(true);
    const script = document.createElement('script');
    script.src = 'https://cdn.jsdelivr.net/npm/marked/marked.min.js';
    script.onload = () => resolve(true);
    script.onerror = () => resolve(false);
    document.head.appendChild(script);
  });
}

function renderMarkdown(targetEl, markdown) {
  if (window.marked && typeof window.marked.parse === 'function') {
    targetEl.innerHTML = window.marked.parse(markdown || '');
  } else {
    targetEl.innerHTML = `<pre>${escapeHtml(markdown || '')}</pre>`;
  }
}

async function renderDeliveryTab() {
  contentEl.innerHTML = `
    <h2>Insight Delivery</h2>
    <div id="delivery-controls">
      <label>Previous Reports:
        <select id="delivery-select"><option value="">Latest</option></select>
      </label>
      <span id="delivery-date"></span>
    </div>
    <article id="delivery-content" class="markdown">Loading...</article>`;

  await loadMarked();

  const select = document.getElementById('delivery-select');
  const dateEl = document.getElementById('delivery-date');
  const contentDiv = document.getElementById('delivery-content');

  async function showInsight(payload) {
    if (!payload || !payload.content) {
      contentDiv.innerHTML = '<p>No insight available.</p>';
      dateEl.textContent = '';
      return;
    }
    dateEl.textContent = payload.date ? `Report date: ${payload.date}` : '';
    renderMarkdown(contentDiv, payload.content);
  }

  async function loadLatest() {
    try {
      const response = await fetch('/api/insight/latest');
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      await showInsight(await response.json());
    } catch (e) {
      contentDiv.innerHTML = `Error: ${escapeHtml(e.message)}`;
    }
  }

  async function loadByDate(date) {
    if (!date) return loadLatest();
    try {
      const response = await fetch(`/api/insight/byDate?date=${encodeURIComponent(date)}`);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      await showInsight(await response.json());
    } catch (e) {
      contentDiv.innerHTML = `Error: ${escapeHtml(e.message)}`;
    }
  }

  try {
    const listResponse = await fetch('/api/insight/list');
    if (listResponse.ok) {
      const listJson = await listResponse.json();
      (listJson.reports || []).forEach(r => {
        if (!r.date) return;
        const option = document.createElement('option');
        option.value = r.date;
        option.textContent = r.date;
        select.appendChild(option);
      });
    }
  } catch {}

  select.onchange = () => loadByDate(select.value);

  await loadLatest();
}

for (const tab of tabs) {
  const btn = document.createElement('button');
  btn.textContent = tab.title;
  btn.dataset.key = tab.key;
  btn.onclick = () => setTab(tab.key);
  tabsEl.appendChild(btn);
}

setTab('knowledge');
