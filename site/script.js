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

async function displayFeedContent(url) {
    // Create a container for episodes
    const episodesContainer = document.createElement('div');
    episodesContainer.className = 'episodes-container';
    
    // Add a back button
    const backButton = document.createElement('button');
    backButton.textContent = '← Back';
    backButton.className = 'back-button';
    backButton.onclick = () => {
        document.body.classList.remove('showing-episodes');
        episodesContainer.remove();
    };
    episodesContainer.appendChild(backButton);

    try {
        // Fetch feed content (you'll need a proxy/backend service for this)
        const response = await fetch(`/api/feed?url=${encodeURIComponent(url)}`);
        const episodes = await response.json();

        // Create episodes list
        const list = document.createElement('ul');
        list.className = 'episodes';
        episodes.forEach(episode => {
            const li = document.createElement('li');
            li.innerHTML = `
                <h3>${episode.title}</h3>
                <p>${episode.description}</p>
                <audio controls src="${episode.audioUrl}"></audio>
            `;
            list.appendChild(li);
        });
        episodesContainer.appendChild(list);
    } catch (error) {
        episodesContainer.innerHTML += '<p class="error">Failed to load feed content</p>';
    }

    document.body.appendChild(episodesContainer);
    document.body.classList.add('showing-episodes');
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