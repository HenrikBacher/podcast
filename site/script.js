let pressTimer;
let lastTap = 0;
let isScrolling = false;

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
                window.open(url, '_blank');
            } else {
                copyToClipboard(url);
            }
            lastTap = now;
        });
        
        link.addEventListener('mousedown', (e) => {
            pressTimer = setTimeout(() => {
                window.open(url, '_blank');
            }, 500);
        });
        
        link.addEventListener('mouseup', () => {
            clearTimeout(pressTimer);
        });
        
        link.addEventListener('mouseleave', () => {
            clearTimeout(pressTimer);
        });
        
        link.addEventListener('touchstart', (e) => {
            if (!isScrolling) {
                pressTimer = setTimeout(() => {
                    window.open(url, '_blank');
                }, 500);
            }
        });
        
        link.addEventListener('touchend', () => {
            if (!isScrolling) {
                clearTimeout(pressTimer);
            }
        });
        
        link.addEventListener('touchcancel', () => {
            clearTimeout(pressTimer);
        });
    });
});