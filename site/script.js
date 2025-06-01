let lastTap = 0;
let isScrolling = false;

function isPocketCastsSupported() {
    return /Android|iPhone|iPad|iPod/.test(navigator.userAgent);
}

function convertToPocketCastsUrl(feedUrl) {
    return 'pktc://subscribe/' + feedUrl.replace(/^https?:\/\//, '');
}

function copyToClipboard(text) {
    const isIOS = /iPad|iPhone|iPod/.test(navigator.userAgent);
    const isAndroid = /Android/.test(navigator.userAgent);
    
    navigator.clipboard.writeText(text).then(() => {
        if (!isIOS && !isAndroid) {
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
                // Double-tap to open feed in new tab
                window.open(url, '_blank');
            } else {
                if (isPocketCastsSupported()) {
                    const pocketCastsUrl = convertToPocketCastsUrl(url);
                    window.location.href = pocketCastsUrl;
                    // Fallback to copying if app switch fails
                    setTimeout(() => {
                        copyToClipboard(url);
                    }, 500);
                } else {
                    copyToClipboard(url);
                }
            }
            lastTap = now;
        });
    });
});