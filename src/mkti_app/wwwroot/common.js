// Shared helpers used across the physical pages.

// Default all From/To date inputs to the current week (Monday–Sunday).
document.addEventListener('DOMContentLoaded', () => {
  const pad = n => String(n).padStart(2, '0');
  const fmt = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  const today = new Date();
  const day = today.getDay(); // 0 = Sunday, 1 = Monday, ...
  const diffToMonday = (day + 6) % 7; // days since most recent Monday
  const monday = new Date(today);
  monday.setDate(today.getDate() - diffToMonday);
  const sunday = new Date(monday);
  sunday.setDate(monday.getDate() + 6);
  const weekStart = fmt(monday);
  const weekEnd = fmt(sunday);
  document.querySelectorAll('input[type="date"]').forEach(input => {
    if (input.id.endsWith('-from')) input.value = weekStart;
    else if (input.id.endsWith('-to')) input.value = weekEnd;
  });
});

// Agent Instructions — click to open as a formatted modal
document.addEventListener('DOMContentLoaded', () => {
  document.querySelectorAll('.agent-hint a').forEach(link => {
    link.addEventListener('click', e => {
      e.preventDefault();
      const hint = link.closest('.agent-hint');
      const popup = hint.querySelector('.agent-popup');
      if (!popup) return;
      const h2 = hint.closest('h2');
      const titleText = h2
        ? Array.from(h2.childNodes).filter(n => n.nodeType === 3).map(n => n.textContent.trim()).join('').trim()
        : 'Agent';
      showAgentInstructionsModal(titleText, popup.innerHTML);
    });
  });
});

function showAgentInstructionsModal(agentTitle, htmlContent) {
  const existing = document.getElementById('agent-modal-overlay');
  if (existing) existing.remove();
  const text = htmlContent.replace(/<br\s*\/?>/gi, '\n').replace(/<[^>]+>/g, '').trim();
  const overlay = document.createElement('div');
  overlay.id = 'agent-modal-overlay';
  overlay.className = 'modal-overlay';
  overlay.innerHTML = `
    <div class="modal">
      <div class="modal-header">
        <strong>🤖 Agent Instructions — ${escapeHtml(agentTitle)}</strong>
        <button class="modal-close" type="button">&times;</button>
      </div>
      <div class="modal-body">
        <div class="agent-instructions-body">${formatAgentInstructions(text)}</div>
      </div>
    </div>`;
  overlay.addEventListener('click', e => { if (e.target === overlay) overlay.remove(); });
  overlay.querySelector('.modal-close').onclick = () => overlay.remove();
  document.body.appendChild(overlay);
}

function formatAgentInstructions(text) {
  const paras = text.split(/\n\n+/);
  let html = '';
  let isFirst = true;
  for (const para of paras) {
    const trimmed = para.trim();
    if (!trimmed) continue;
    const lines = trimmed.split('\n').map(l => l.trim()).filter(Boolean);
    // JSON / code block
    if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
      html += `<pre class="ai-code">${escapeHtml(trimmed)}</pre>`;
      isFirst = false;
      continue;
    }
    // Separate numbered step lines from intro lines
    const introLines = [];
    const stepLines = [];
    for (const line of lines) {
      if (/^\d+\.\s/.test(line)) stepLines.push(line);
      else if (stepLines.length === 0) introLines.push(line);
    }
    if (stepLines.length > 0) {
      if (introLines.length) html += `<p class="ai-intro">${escapeHtml(introLines.join(' '))}</p>`;
      html += '<ol class="ai-steps">';
      for (const line of stepLines) {
        html += `<li>${escapeHtml(line.replace(/^\d+\.\s/, '').trim())}</li>`;
      }
      html += '</ol>';
      isFirst = false;
      continue;
    }
    // First paragraph = role description (highlighted)
    const cls = isFirst ? 'ai-role' : 'ai-para';
    html += `<p class="${cls}">${lines.map(l => escapeHtml(l)).join('<br>')}</p>`;
    isFirst = false;
  }
  return html;
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

function showModalWithTabs(title, markdownContent, jsonContent) {
  closeModal();
  const overlay = document.createElement('div');
  overlay.id = 'modal-overlay';
  overlay.className = 'modal-overlay';
  let jsonFormatted = jsonContent || '';
  try { jsonFormatted = JSON.stringify(JSON.parse(jsonContent), null, 2); } catch {}
  overlay.innerHTML = `
    <div class="modal">
      <div class="modal-header">
        <strong>${escapeHtml(title)}</strong>
        <button class="modal-close" type="button">&times;</button>
      </div>
      <div class="modal-tabs">
        <button class="modal-tab-btn active" data-tab="markdown">📄 Markdown</button>
        <button class="modal-tab-btn" data-tab="json">{ } JSON</button>
      </div>
      <div class="modal-body">
        <div class="modal-tab-content" id="modal-tab-markdown">
          <div class="modal-markdown-body">Loading…</div>
        </div>
        <div class="modal-tab-content hidden" id="modal-tab-json">
          <pre class="modal-json-body">${escapeHtml(jsonFormatted)}</pre>
        </div>
      </div>
    </div>`;
  overlay.addEventListener('click', (e) => { if (e.target === overlay) closeModal(); });
  overlay.querySelector('.modal-close').onclick = closeModal;
  overlay.querySelectorAll('.modal-tab-btn').forEach(btn => {
    btn.onclick = () => {
      overlay.querySelectorAll('.modal-tab-btn').forEach(b => b.classList.remove('active'));
      overlay.querySelectorAll('.modal-tab-content').forEach(c => c.classList.add('hidden'));
      btn.classList.add('active');
      overlay.querySelector(`#modal-tab-${btn.dataset.tab}`).classList.remove('hidden');
    };
  });
  document.body.appendChild(overlay);
  const mdEl = overlay.querySelector('.modal-markdown-body');
  loadMarked().then(() => renderMarkdown(mdEl, markdownContent || ''));
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
