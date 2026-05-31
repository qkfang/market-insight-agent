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
    } catch {}
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
  if (key === 'ingest') return renderIngestTab();
  if (key === 'analyze') return addActionTab(key, 'News Analysis', '/api/news/analyze');
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
