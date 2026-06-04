(async function initDelivery() {
  await loadMarked();

  const select = document.getElementById('delivery-select');
  const dateEl = document.getElementById('delivery-date');
  const contentDiv = document.getElementById('delivery-content');

  function showInsight(payload) {
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
      showInsight(await response.json());
    } catch (e) {
      contentDiv.innerHTML = `Error: ${escapeHtml(e.message)}`;
    }
  }

  async function loadByDate(date) {
    if (!date) return loadLatest();
    try {
      const response = await fetch(`/api/insight/byDate?date=${encodeURIComponent(date)}`);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      showInsight(await response.json());
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
})();
