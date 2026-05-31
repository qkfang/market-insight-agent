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
      const json = await response.json();
      pre.textContent = resultFormatter(json);
    } catch (e) {
      pre.textContent = `Error: ${e.message}`;
    }
  };
}

async function renderTab(key) {
  if (key === 'ingest') return addActionTab(key, 'News Ingestion', '/api/news/ingest');
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
      const current = await fetch('/api/subscription').then(r => r.json());
      const parsed = JSON.parse(current.subscription || '{}');
      document.querySelectorAll('input[name="market"]').forEach(c => c.checked = (parsed.markets || []).includes(c.value));
      document.querySelectorAll('input[name="item"]').forEach(c => c.checked = (parsed.items || []).includes(c.value));
    } catch {}

    contentEl.querySelector('button').onclick = async () => {
      const selectedMarkets = [...document.querySelectorAll('input[name="market"]:checked')].map(x => x.value);
      const selectedItems = [...document.querySelectorAll('input[name="item"]:checked')].map(x => x.value);
      const pre = document.getElementById('result');
      pre.textContent = 'Saving...';
      const response = await fetch('/api/subscription', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ markets: selectedMarkets, items: selectedItems })
      });
      pre.textContent = JSON.stringify(await response.json(), null, 2);
    };
    return;
  }

  if (key === 'delivery') {
    contentEl.innerHTML = '<h2>Insight Delivery</h2><pre id="result">Loading...</pre>';
    try {
      const data = await fetch('/api/insight/latest').then(r => r.json());
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
