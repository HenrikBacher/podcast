let lastTap = 0;
let isScrolling = false;

function showToast(message) {
    const toast = document.createElement('div');
    toast.className = 'toast';
    toast.textContent = message;
    document.body.appendChild(toast);
    
    toast.addEventListener('animationend', (e) => {
        if (e.animationName === 'fadeOut') {
            toast.remove();
        }
    });
}

function copyToClipboard(text) {
    const isIOS = /iPad|iPhone|iPod/.test(navigator.userAgent);
    const isAndroid = /Android/.test(navigator.userAgent);
    
    if (isIOS || isAndroid) {
        // Try to open in Pocket Casts first
        const feedUrl = text.replace(/^https?:\/\//, '');
        const pocketCastsUrl = `pktc://subscribe/${feedUrl}`;
        
        // Fallback to clipboard after a delay
        setTimeout(() => {
            navigator.clipboard.writeText(text).then(() => {
                showToast('Feed URL copied to clipboard!');
            });
        }, 300);

        window.location.href = pocketCastsUrl;
    } else {
        navigator.clipboard.writeText(text).then(() => {
            showToast('Feed URL copied to clipboard!');
        });
    }
}

document.addEventListener('DOMContentLoaded', () => {
    // Add scroll detection
    let scrollTimeout;
    document.addEventListener('scroll', () => {
        isScrolling = true;
        clearTimeout(scrollTimeout);
        scrollTimeout = setTimeout(() => {
            isScrolling = false;
        }, 150);
    });

    document.querySelectorAll('.feed-link').forEach(link => {
        const url = link.href;
        
        link.addEventListener('click', (e) => {
            e.preventDefault();
            const now = Date.now();
            const timeDiff = now - lastTap;
            
            if (timeDiff < 300 && timeDiff > 0) {
                displayFeedContent(url);
            } else {
                copyToClipboard(url);
            }
            lastTap = now;
        });
    });
});