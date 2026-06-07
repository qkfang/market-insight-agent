const btn = document.getElementById('research-btn');
const spinner = document.getElementById('research-spinner');
const resultEl = document.getElementById('research-result');
const pre = document.getElementById('result');

const badges = {
  bullish: { cls: 'bullish', icon: '▲', label: 'Bullish' },
  bearish: { cls: 'bearish', icon: '▼', label: 'Bearish' },
  neutral: { cls: 'neutral', icon: '◆', label: 'Neutral' }
};

function renderResult(data) {
  const key = String(data.sentiment || 'neutral').toLowerCase();
  const badge = badges[key] || badges.neutral;
  const confidencePct = Math.round((Number(data.confidence) || 0) * 100);
  const drivers = Array.isArray(data.keyDrivers) ? data.keyDrivers : [];
  const driversHtml = drivers.length
    ? `<ul>${drivers.map(d => `<li>${escapeHtml(d)}</li>`).join('')}</ul>`
    : '<p>No key drivers identified.</p>';
  const timestamp = data.timestamp
    ? `<span class="research-timestamp">Generated: ${new Date(data.timestamp).toLocaleString()}</span>`
    : '';

  resultEl.innerHTML = `
    <div class="research-card">
      <div class="research-card-header">
        <div class="sentiment-badge ${badge.cls}">
          <span class="sentiment-icon">${badge.icon}</span>
          <span class="sentiment-label">${badge.label}</span>
          <span class="sentiment-confidence">${confidencePct}% confidence</span>
        </div>
        ${timestamp}
      </div>
      <div class="research-key-drivers">
        <h4>Key Drivers</h4>
        ${driversHtml}
      </div>
      <div class="research-summary">${escapeHtml(data.summary || '')}</div>
    </div>`;
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
