let lastTap = 0;
let isScrolling = false;

function isPocketCastsInstalled() {
    return new Promise((resolve) => {
        const iframe = document.createElement('iframe');
        iframe.style.display = 'none';
        document.body.appendChild(iframe);
        
        // Try to open Pocket Casts
        const timeout = setTimeout(() => {
            document.body.removeChild(iframe);
            resolve(false);
        }, 500);

        // If we successfully return to the page, the app is installed
        window.onblur = () => {
            clearTimeout(timeout);
            document.body.removeChild(iframe);
            window.onblur = null;
            resolve(true);
        };

        // Test the deep link
        iframe.src = 'pktc://';
    });
}

function copyToClipboard(text) {
    const isIOS = /iPad|iPhone|iPod/.test(navigator.userAgent);
    const isAndroid = /Android/.test(navigator.userAgent);
    
    if (isIOS || isAndroid) {
        isPocketCastsInstalled().then(isInstalled => {
            if (isInstalled) {
                const feedUrl = text.replace(/^https?:\/\//, '');
                window.location.href = `pktc://subscribe/${feedUrl}`;
            } else {
                // Fall back to clipboard copy with toast on mobile
                navigator.clipboard.writeText(text).then(() => {
                    const toast = document.createElement('div');
                    toast.className = 'toast';
                    toast.textContent = 'Feed URL copied to clipboard!';
                    document.body.appendChild(toast);
                    
                    toast.addEventListener('animationend', (e) => {
                        if (e.animationName === 'fadeOut') {
                            toast.remove();
                        }
                    });
                });
            }
        });
    } else {
        navigator.clipboard.writeText(text).then(() => {
            const toast = document.createElement('div');
            toast.className = 'toast';
            toast.textContent = 'Feed URL copied to clipboard!';
            document.body.appendChild(toast);
            
            toast.addEventListener('animationend', (e) => {
                if (e.animationName === 'fadeOut') {
                    toast.remove();
                }
            });
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