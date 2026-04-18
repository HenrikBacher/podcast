const isMobile = /Android|iPhone|iPad|iPod/.test(navigator.userAgent);
let lastTap = 0;

// ── Relative deploy time ────────────────────────
const deployTimeEl = document.querySelector('.deploy-time');
if (deployTimeEl) {
    const dt = new Date(deployTimeEl.getAttribute('datetime'));
    if (!isNaN(dt)) {
        const diffMs = Date.now() - dt;
        const diffMin = Math.round(diffMs / 60000);
        const diffH = Math.round(diffMs / 3600000);
        const diffD = Math.round(diffMs / 86400000);

        let rel;
        if (diffMin < 2)       rel = 'lige nu';
        else if (diffMin < 60) rel = `${diffMin} minutter siden`;
        else if (diffH < 24)   rel = `${diffH} time${diffH !== 1 ? 'r' : ''} siden`;
        else                   rel = `${diffD} dag${diffD !== 1 ? 'e' : ''} siden`;

        deployTimeEl.textContent = rel;
    }
}

// ── Search ──────────────────────────────────────
const searchInput = document.getElementById('search');
const feedsList = document.querySelector('.feeds');
const noResults = document.querySelector('.no-results');

if (searchInput && feedsList) {
    const items = Array.from(feedsList.querySelectorAll('li'));

    searchInput.addEventListener('input', () => {
        const q = searchInput.value.trim().toLowerCase();
        let visible = 0;

        items.forEach(li => {
            const title = li.querySelector('.feed-title')?.textContent.toLowerCase() ?? '';
            const match = !q || title.includes(q);
            li.hidden = !match;
            if (match) visible++;
        });

        if (noResults) noResults.hidden = visible > 0;
    });
}

// ── Clipboard toast ─────────────────────────────
function showToast(msg) {
    const existing = document.querySelector('.toast');
    if (existing) existing.remove();

    const toast = document.createElement('div');
    toast.className = 'toast';
    toast.textContent = msg;
    document.body.appendChild(toast);

    toast.addEventListener('animationend', e => {
        if (e.animationName === 'toastOut') toast.remove();
    });
}

function copyToClipboard(text) {
    navigator.clipboard.writeText(text).then(() => {
        if (!isMobile) showToast('Feed-URL kopieret!');
    }).catch(() => {});
}

// ── Feed click handler ───────────────────────────
feedsList?.addEventListener('click', e => {
    const link = e.target.closest('.feed-link');
    if (!link) return;

    e.preventDefault();
    const url = link.href;
    const now = Date.now();
    const diff = now - lastTap;

    if (diff < 300 && diff > 0) {
        window.open(url, '_blank');
    } else if (isMobile) {
        const pocketCastsUrl = 'pktc://subscribe/' + url.replace(/^https?:\/\//, '');
        window.location.href = pocketCastsUrl;
        setTimeout(() => copyToClipboard(url), 500);
    } else {
        copyToClipboard(url);
    }
    lastTap = now;
});
