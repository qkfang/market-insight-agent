// Shared helpers used across the physical pages.

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
