document.getElementById('knowledge-run-btn').onclick = async () => {
  const pre = document.getElementById('result');
  const btn = document.getElementById('knowledge-run-btn');
  btn.disabled = true;
  pre.textContent = 'Running centralized pipeline... this may take a moment.';
  try {
    const response = await fetch('/api/knowledge/run');
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const json = await response.json();
    pre.textContent = JSON.stringify(json, null, 2);
  } catch (e) {
    pre.textContent = `Error: ${e.message}`;
  } finally {
    btn.disabled = false;
  }
};
