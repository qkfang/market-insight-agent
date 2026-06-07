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
const subSpinner   = document.getElementById('sub-spinner');
const subStatus    = document.getElementById('sub-status');
const pdfSection   = document.getElementById('sub-pdf-section');
const pdfReports   = document.getElementById('sub-pdf-reports');
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

  const audience = audienceSelect.value;
  const from = fromInput.value;
  const to   = toInput.value;

  generateBtn.disabled = true;
  subSpinner.hidden = false;
  subStatus.innerHTML = `<p style="color:var(--color-text-muted);font-size:13px;">Generating branded reports for ${escapeHtml(audience)}… this may take 30–60 seconds.</p>`;
  pdfSection.style.display = 'none';
  pdfReports.innerHTML = '';

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

    // Show the insight for the first selected market
    if (selectedMarkets.length > 0) {
      await showInsightForMarket(selectedMarkets[0]);
    }

    renderPdfReports(json.reports || [], audience);
  } catch (e) {
    subStatus.innerHTML = `<p class="research-error">Error: ${escapeHtml(e.message)}</p>`;
  } finally {
    generateBtn.disabled = false;
    subSpinner.hidden = true;
  }
};

function renderPdfReports(reports, audience) {
  if (reports.length === 0) return;

  pdfReports.innerHTML = '';
  reports.forEach(r => {
    const icon = marketIcons[r.market] || '📊';
    const wrapper = document.createElement('div');
    wrapper.className = 'panel';
    wrapper.style.marginBottom = '20px';

    const header = document.createElement('div');
    header.className = 'panel-header';
    header.innerHTML = `
      <div>
        <h2 class="panel-title">${icon} ${escapeHtml((r.market || '').toUpperCase())} — ${escapeHtml(audience)}</h2>
        <p class="panel-desc">Branded intelligence report · ${escapeHtml(r.filename || '')}</p>
      </div>
      <div style="display:flex;gap:8px;align-items:center;">
        ${r.reportUrl ? `<a class="action" style="padding:4px 12px;font-size:12px;" href="${escapeHtml(r.reportUrl)}" target="_blank">🔗 Open in Tab</a>` : ''}
        ${r.pdfUrl ? `<a class="action" style="padding:4px 12px;font-size:12px;" href="${escapeHtml(r.pdfUrl)}" target="_blank" download>📄 Download PDF</a>` : ''}
        <button class="action" style="padding:4px 12px;font-size:12px;" onclick="printFrame('frame-${escapeHtml(r.market)}')">🖨️ Print / Save PDF</button>
      </div>`;

    wrapper.appendChild(header);

    if (r.htmlBase64) {
      const html = decodeBase64Utf8(r.htmlBase64);
      const blob = new Blob([html], { type: 'text/html; charset=utf-8' });
      const blobUrl = URL.createObjectURL(blob);

      const frame = document.createElement('iframe');
      frame.id = `frame-${r.market}`;
      frame.src = blobUrl;
      frame.style.cssText = 'width:100%;height:820px;border:1px solid var(--color-border);border-radius:6px;margin-top:12px;background:#fff;';
      frame.setAttribute('title', `${r.market} insight report for ${audience}`);
      wrapper.appendChild(frame);
    } else if (r.reportUrl) {
      const frame = document.createElement('iframe');
      frame.id = `frame-${r.market}`;
      frame.src = r.reportUrl;
      frame.style.cssText = 'width:100%;height:820px;border:1px solid var(--color-border);border-radius:6px;margin-top:12px;background:#fff;';
      frame.setAttribute('title', `${r.market} insight report for ${audience}`);
      wrapper.appendChild(frame);
    } else {
      const msg = document.createElement('p');
      msg.className = 'research-error';
      msg.textContent = 'Report content unavailable.';
      wrapper.appendChild(msg);
    }

    pdfReports.appendChild(wrapper);
  });

  pdfSection.style.display = 'block';
  pdfSection.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

function printFrame(frameId) {
  const frame = document.getElementById(frameId);
  if (!frame || !frame.contentWindow) return;
  frame.contentWindow.focus();
  frame.contentWindow.print();
}

function decodeBase64Utf8(base64) {
  try {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    return new TextDecoder('utf-8').decode(bytes);
  } catch {
    return atob(base64);
  }
}
