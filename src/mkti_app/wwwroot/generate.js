const btn = document.getElementById('generate-btn');
const spinner = document.getElementById('generate-spinner');
const fromInput = document.getElementById('generate-from');
const toInput = document.getElementById('generate-to');
const summary = document.getElementById('generate-summary');
const preview = document.getElementById('generate-preview');
const link = document.getElementById('generate-link');

btn.onclick = async () => {
  btn.disabled = true;
  spinner.hidden = false;
  summary.innerHTML = '';
  link.innerHTML = '';
  preview.textContent = 'Generating insight report... this may take 30–60 seconds.';
  preview.className = '';
  try {
    const params = new URLSearchParams();
    if (fromInput.value) params.set('from', fromInput.value);
    if (toInput.value) params.set('to', toInput.value);
    const response = await fetch(`/api/insight/generate?${params}`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const json = await response.json();
    if (!json.success) throw new Error(json.error || 'Generation failed.');
    summary.innerHTML = `<span class="status-badge success">✓ Stored as ${escapeHtml(json.filename || '')} &mdash; ${escapeHtml(json.date || '')}</span>`;
    preview.textContent = json.preview || '(empty report)';
    const fullLink = document.createElement('a');
    fullLink.className = 'action';
    fullLink.textContent = '→ View Full Report';
    fullLink.href = '/delivery.html';
    link.appendChild(fullLink);
  } catch (e) {
    summary.innerHTML = `<span class="status-badge error">Error: ${escapeHtml(e.message)}</span>`;
    preview.textContent = `Error: ${e.message}`;
  } finally {
    btn.disabled = false;
    spinner.hidden = true;
  }
};
