let lastTap = 0;
let isScrolling = false;

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

function convertToPocketCastsUrl(url) {
    return 'pktc://subscribe/' + url.replace(/^https?:\/\//, '');
}

function isPocketCastsInstalled() {
    return new Promise((resolve) => {
        const isIOS = /iPad|iPhone|iPod/.test(navigator.userAgent);
        const isAndroid = /Android/.test(navigator.userAgent);

        if (!isIOS && !isAndroid) {
            resolve(false);
            return;
        }

        if (isIOS) {
            const iframe = document.createElement('iframe');
            iframe.style.display = 'none';
            document.body.appendChild(iframe);

            const timeoutID = setTimeout(() => {
                document.body.removeChild(iframe);
                resolve(false);
            }, 2000);

            iframe.onload = () => {
                clearTimeout(timeoutID);
                document.body.removeChild(iframe);
                resolve(true);
            };

            iframe.src = 'pktc://';
        } else {
            const intent = 'intent://dummy#Intent;scheme=pktc;package=au.com.shiftyjelly.pocketcasts;end';
            const iframe = document.createElement('iframe');
            iframe.style.display = 'none';
            document.body.appendChild(iframe);

            const timeoutID = setTimeout(() => {
                document.body.removeChild(iframe);
                resolve(false);
            }, 2000);

            iframe.onload = () => {
                clearTimeout(timeoutID);
                document.body.removeChild(iframe);
                resolve(true);
            };

            iframe.src = intent;
        }
    });
}

async function copyToClipboard(text) {
    const isIOS = /iPad|iPhone|iPod/.test(navigator.userAgent);
    const isAndroid = /Android/.test(navigator.userAgent);
    
    if (isIOS || isAndroid) {
        const hasPocketCasts = await isPocketCastsInstalled();
        if (hasPocketCasts) {
            text = convertToPocketCastsUrl(text);
        }
    }
    
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