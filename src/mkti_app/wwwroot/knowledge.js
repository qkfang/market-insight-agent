document.getElementById('knowledge-run-btn').onclick = async () => {
  const pre = document.getElementById('result');
  const btn = document.getElementById('knowledge-run-btn');
  const fromInput = document.getElementById('knowledge-from');
  const toInput = document.getElementById('knowledge-to');
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
