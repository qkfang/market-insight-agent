// ── Insight Reports Viewer ────────────────────────────────────────────────────
async function showInsightForMarket(market) {
  await loadMarked();
  const panel   = document.getElementById('insight-reports-panel');
  const content = document.getElementById('delivery-content');
  const dateEl  = document.getElementById('delivery-date');
  const descEl  = document.getElementById('insight-panel-desc');

  panel.style.display = 'block';
  content.innerHTML = '<p style="color:var(--color-text-muted)">Loading insight…</p>';
  if (descEl) descEl.textContent = `Matched market insight — ${market.toUpperCase()}`;

  try {
    const listR = await fetch('/api/insight/list');
    if (!listR.ok) throw new Error(`HTTP ${listR.status}`);
    const listJson = await listR.json();

    const match = (listJson.reports || [])
      .filter(r => r.market === market)
      .sort((a, b) => (b.date || '').localeCompare(a.date || ''))
      [0];

    if (!match || !match.filename) {
      content.innerHTML = '<p style="color:var(--color-text-muted)">No insight report found for this market. Generate insights first.</p>';
      dateEl.textContent = '';
      return;
    }

    const r = await fetch(`/api/insight/content?name=${encodeURIComponent(match.filename)}`);
    if (!r.ok) throw new Error(`HTTP ${r.status}`);
    const json = await r.json();
    dateEl.textContent = match.date ? `Report date: ${match.date}` : '';
    renderMarkdown(content, json.content || '');
  } catch (e) {
    content.innerHTML = `<p class="research-error">Error loading insight: ${escapeHtml(e.message)}</p>`;
  }
}

// ── Subscription Report Generator ────────────────────────────────────────────
const generateBtn  = document.getElementById('subscription-generate');
const createPdfBtn = document.getElementById('subscription-create-pdf');
const subRefreshBtn  = document.getElementById('sub-refresh-btn');
const subCacheTimeEl = document.getElementById('sub-cache-time');
const subSpinner   = document.getElementById('sub-spinner');
const subStatus    = document.getElementById('sub-status');
const fromInput    = document.getElementById('sub-from');
const toInput      = document.getElementById('sub-to');
const audienceSelect = document.getElementById('sub-audience');

async function loadSubscriptionCache() {
  try {
    const r = await fetch('/temp/cache-subscription.json');
    if (!r.ok) throw new Error('no cache');
    const json = await r.json();
    if (json.cachedAt && subCacheTimeEl) subCacheTimeEl.textContent = `cached ${new Date(json.cachedAt).toLocaleString()}`;
  } catch {
    if (subCacheTimeEl) subCacheTimeEl.textContent = '';
  }
}

async function refreshSubscriptionCache() {
  if (subRefreshBtn) subRefreshBtn.disabled = true;
  try {
    const r = await fetch('/api/insight/list');
    if (!r.ok) throw new Error(`HTTP ${r.status}`);
    const now = new Date().toLocaleString();
    if (subCacheTimeEl) subCacheTimeEl.textContent = `cached ${now}`;
  } catch (e) {
    if (subCacheTimeEl) subCacheTimeEl.textContent = `refresh failed`;
  } finally {
    if (subRefreshBtn) subRefreshBtn.disabled = false;
  }
}

if (subRefreshBtn) subRefreshBtn.onclick = refreshSubscriptionCache;
loadSubscriptionCache();

const marketIcons = { copper: '🟤', gold: '🟡', silver: '⚪', oil: '🛢️' };

generateBtn.onclick = async () => {
  const selectedMarkets = Array.from(document.querySelectorAll('input[name="sub-market"]:checked')).map(cb => cb.value);
  if (selectedMarkets.length === 0) {
    subStatus.innerHTML = '<p class="research-error">Please select at least one market.</p>';
    return;
  }

  generateBtn.disabled = true;
  subSpinner.hidden = false;
  subStatus.innerHTML = '';

  try {
    await showInsightForMarket(selectedMarkets[0]);
  } catch (e) {
    subStatus.innerHTML = `<p class="research-error">Error: ${escapeHtml(e.message)}</p>`;
  } finally {
    generateBtn.disabled = false;
    subSpinner.hidden = true;
  }
};

if (createPdfBtn) createPdfBtn.onclick = async () => {
  const selectedMarkets = Array.from(document.querySelectorAll('input[name="sub-market"]:checked')).map(cb => cb.value);
  if (selectedMarkets.length === 0) {
    subStatus.innerHTML = '<p class="research-error">Please select at least one market.</p>';
    return;
  }

  const audience = audienceSelect.value;
  const from = fromInput.value;
  const to   = toInput.value;

  createPdfBtn.disabled = true;
  subSpinner.hidden = false;
  subStatus.innerHTML = `<p style="color:var(--color-text-muted);font-size:13px;">Generating report for ${escapeHtml(audience)}… this may take 30–60 seconds.</p>`;

  try {
    const response = await fetch('/api/subscription/generate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ markets: selectedMarkets, audience, from, to })
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const json = await response.json();
    if (json.status === 'error') throw new Error(json.error || 'Generation failed.');

    subStatus.innerHTML = `<p style="color:var(--color-success);font-size:13px;">✓ ${(json.reports || []).length} report(s) generated for <strong>${escapeHtml(audience)}</strong>.</p>`;

    // Open each report in a new tab with auto-print so user can save as PDF
    for (const report of (json.reports || [])) {
      let htmlContent = '';
      if (report.htmlBase64) {
        htmlContent = atob(report.htmlBase64);
      } else if (report.reportUrl) {
        const r = await fetch(report.reportUrl);
        if (r.ok) htmlContent = await r.text();
      }
      if (htmlContent) {
        // Inject auto-print script before </body>
        const printScript = '<script>window.onload = function() { window.print(); };<\/script>';
        const printHtml = htmlContent.includes('</body>')
          ? htmlContent.replace('</body>', printScript + '</body>')
          : htmlContent + printScript;
        const blob = new Blob([printHtml], { type: 'text/html' });
        const url = URL.createObjectURL(blob);
        const tab = window.open(url, '_blank');
        // Revoke after a short delay to allow the tab to load
        setTimeout(() => URL.revokeObjectURL(url), 30000);
      }
    }

    // Refresh the markdown preview
    if (selectedMarkets.length > 0) {
      await showInsightForMarket(selectedMarkets[0]);
    }
  } catch (e) {
    subStatus.innerHTML = `<p class="research-error">Error: ${escapeHtml(e.message)}</p>`;
  } finally {
    createPdfBtn.disabled = false;
    subSpinner.hidden = true;
  }
};
