const isMobile = /Android|iPhone|iPad|iPod/.test(navigator.userAgent);
let lastTap = 0;

function copyToClipboard(text) {
    navigator.clipboard.writeText(text).then(() => {
        if (!isMobile) {
            const toast = document.createElement('div');
            toast.className = 'toast';
            toast.textContent = 'Feed URL copied to clipboard!';
            document.body.appendChild(toast);

            toast.addEventListener('animationend', (e) => {
                if (e.animationName === 'fadeOut') {
                    toast.remove();
                }
            });
        }
    });
}

document.querySelector('.feeds')?.addEventListener('click', (e) => {
    const link = e.target.closest('.feed-link');
    if (!link) return;

    e.preventDefault();
    const url = link.href;
    const now = Date.now();
    const timeDiff = now - lastTap;

    if (timeDiff < 300 && timeDiff > 0) {
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