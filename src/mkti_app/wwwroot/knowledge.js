const fromInput = document.getElementById('knowledge-from');
const toInput = document.getElementById('knowledge-to');
const filesEl = document.getElementById('knowledge-files');

function renderArticleFiles(filenames) {
  if (!filenames || filenames.length === 0) {
    filesEl.innerHTML = '<li>No articles found for the selected date range.</li>';
    return;
  }
  filesEl.innerHTML = filenames.map(f => `<li>${escapeHtml(f)}</li>`).join('');
}

async function loadArticles() {
  try {
    const params = new URLSearchParams();
    if (fromInput.value) params.set('from', fromInput.value);
    if (toInput.value) params.set('to', toInput.value);
    const response = await fetch(`/api/articles/list?${params}`);
    if (!response.ok) return;
    const json = await response.json();
    renderArticleFiles(json.filenames);
  } catch (e) {
    console.warn('Failed to load articles:', e);
  }
}

loadArticles();
fromInput.addEventListener('change', loadArticles);
toInput.addEventListener('change', loadArticles);

document.getElementById('knowledge-run-btn').onclick = async () => {
  const pre = document.getElementById('result');
  const btn = document.getElementById('knowledge-run-btn');
  btn.disabled = true;
  pre.textContent = 'Running centralized pipeline... this may take a moment.';
  try {
    const params = new URLSearchParams();
    if (fromInput.value) params.set('from', fromInput.value);
    if (toInput.value) params.set('to', toInput.value);
    const response = await fetch(`/api/knowledge/run?${params}`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const json = await response.json();
    pre.textContent = JSON.stringify(json, null, 2);
  } catch (e) {
    pre.textContent = `Error: ${e.message}`;
  } finally {
    btn.disabled = false;
  }
};
