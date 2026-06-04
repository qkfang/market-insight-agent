async function loadSubscription() {
  try {
    const subscriptionResponse = await fetch('/api/subscription');
    if (!subscriptionResponse.ok) throw new Error(`HTTP ${subscriptionResponse.status}`);
    const current = await subscriptionResponse.json();
    const selMarkets = current.markets || [];
    const selItems = current.items || [];
    document.querySelectorAll('input[name="market"]').forEach(c => c.checked = selMarkets.includes(c.value));
    document.querySelectorAll('input[name="item"]').forEach(c => c.checked = selItems.includes(c.value));
  } catch {}
}

loadSubscription();

document.getElementById('subscription-save').onclick = async () => {
  const selectedMarkets = [...document.querySelectorAll('input[name="market"]:checked')].map(x => x.value);
  const selectedItems = [...document.querySelectorAll('input[name="item"]:checked')].map(x => x.value);
  const pre = document.getElementById('result');
  pre.textContent = 'Saving...';
  try {
    const response = await fetch('/api/subscription', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ markets: selectedMarkets, items: selectedItems })
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const json = await response.json();
    pre.textContent = json.success ? 'Preferences saved.' : JSON.stringify(json, null, 2);
  } catch (e) {
    pre.textContent = `Error: ${e.message}`;
  }
};
