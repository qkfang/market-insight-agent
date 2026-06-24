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
const subSpinner   = document.getElementById('sub-spinner');
const subStatus    = document.getElementById('sub-status');
const fromInput    = document.getElementById('sub-from');
const toInput      = document.getElementById('sub-to');
const audienceSelect = document.getElementById('sub-audience');

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

    // Download each report as PDF directly
    let downloaded = 0;
    const pdfErrors = [];
    for (const report of (json.reports || [])) {
      if (report.pdfUrl) {
        const a = document.createElement('a');
        a.href = report.pdfUrl;
        a.download = report.pdfFilename || `${report.market}-report.pdf`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        downloaded += 1;
      } else if (report.pdfError) {
        pdfErrors.push(`${report.market}: ${report.pdfError}`);
      }
    }

    if (downloaded === 0) {
      const details = pdfErrors.length ? `<br>${escapeHtml(pdfErrors.join(' | '))}` : '';
      subStatus.innerHTML = `<p class="research-error">No PDF was generated.${details}</p>`;
      return;
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
