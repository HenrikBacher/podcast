let lastTap = 0;
let isScrolling = false;

function isPocketCastsInstalled() {
    return new Promise((resolve) => {
        const isIOS = /iPad|iPhone|iPod/.test(navigator.userAgent);
        
        if (isIOS) {
            // iOS: Try to open app and assume success if we don't return within timeout
            const start = Date.now();
            window.location.href = 'pktc://';
            
            setTimeout(() => {
                // If we're still here after 500ms, app is not installed
                if (document.hidden || Date.now() - start > 1500) {
                    resolve(false);
                } else {
                    resolve(true);
                }
            }, 500);
        } else {
            // Android: Use iframe method
            const iframe = document.createElement('iframe');
            iframe.style.display = 'none';
            document.body.appendChild(iframe);
            
            const timeout = setTimeout(() => {
                document.body.removeChild(iframe);
                resolve(false);
            }, 1500);

            window.onblur = () => {
                clearTimeout(timeout);
                document.body.removeChild(iframe);
                window.onblur = null;
                resolve(true);
            };

            iframe.src = 'pktc://';
        }
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