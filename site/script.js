// Enhances the server-rendered feed cards: search, topic filter, sorting, relative dates and copy.
// Without JavaScript the page still lists every feed with working links.
(() => {
    const DAY = 86400000;
    const collator = new Intl.Collator('da');

    const ago = ms => {
        const days = Math.floor(ms / DAY);
        if (days <= 0) return 'i dag';
        if (days === 1) return 'i går';
        if (days < 14) return `for ${days} dage siden`;
        if (days < 60) return `for ${Math.round(days / 7)} uger siden`;
        if (days < 365) return `for ${Math.round(days / 30)} måneder siden`;
        return 'for over et år siden';
    };

    // Lets "hort", "hoert" and "hørt" all find "Hørt".
    const fold = (s, long) => s.toLowerCase()
        .replace(/æ/g, 'ae').replace(/ø/g, long ? 'oe' : 'o').replace(/å/g, long ? 'aa' : 'a')
        .normalize('NFD').replace(/[̀-ͯ]/g, '');

    // ── Last updated ────────────────────────────
    const deployTime = document.querySelector('.deploy-time');
    const deployed = deployTime && Date.parse(deployTime.getAttribute('datetime'));
    if (deployed) deployTime.textContent = ago(Date.now() - deployed);

    // ── Toast ───────────────────────────────────
    const toastEl = document.getElementById('toast');
    let toastTimer;
    const toast = msg => {
        toastEl.textContent = msg;
        toastEl.hidden = false;
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => { toastEl.hidden = true; }, 3200);
    };

    // ── Cards ───────────────────────────────────
    const shelf = document.getElementById('shelf');
    if (!shelf) return;

    const cards = Array.from(shelf.querySelectorAll('.card')).map(el => {
        const time = el.querySelector('time');
        const latest = time ? Date.parse(time.getAttribute('datetime')) : NaN;
        const age = Number.isNaN(latest) ? Infinity : Date.now() - latest;
        if (time && age !== Infinity) {
            time.title = time.textContent;
            time.textContent = ago(age);
        }
        if (age < 2 * DAY) {
            const badge = document.createElement('span');
            badge.className = 'fresh';
            badge.textContent = 'Nyt afsnit';
            el.querySelector('.tags')?.prepend(badge);
        }
        // Only what describes the podcast, not button labels or the feed address.
        const text = ['h2', '.sub', '.episode'].map(s => el.querySelector(s)?.textContent ?? '').join(' ');
        return {
            el,
            age,
            title: el.dataset.title ?? '',
            categories: (el.dataset.categories ?? '').split('|').filter(Boolean),
            hay: `${fold(text, false)} ${fold(text, true)}`,
        };
    });

    const stamp = document.getElementById('stamp');
    const newest = Math.min(...cards.map(c => c.age));
    if (stamp && newest !== Infinity) stamp.textContent = `${cards.length} podcasts · nyeste afsnit ${ago(newest)}`;

    // ── Copy feed link ──────────────────────────
    for (const button of shelf.querySelectorAll('.copy')) {
        button.hidden = false;
        button.addEventListener('click', async () => {
            const url = button.dataset.url;
            try {
                await navigator.clipboard.writeText(url);
                toast('Feed-linket er kopieret. Indsæt det i din podcast-app.');
            } catch {
                // No clipboard (e.g. plain http): select the link text so it can be copied by hand.
                const link = button.closest('.info')?.querySelector('.url');
                if (link) {
                    const range = document.createRange();
                    range.selectNodeContents(link);
                    const selection = getSelection();
                    selection.removeAllRanges();
                    selection.addRange(range);
                }
                toast('Linket er markeret. Kopiér det med Ctrl+C eller ⌘C.');
            }
        });
    }

    // Close an open "Åbn i app" menu on an outside click or once a link in it is chosen.
    document.addEventListener('click', e => {
        for (const menu of document.querySelectorAll('.open-in[open]')) {
            if (!menu.contains(e.target) || e.target.closest('.menu a')) menu.open = false;
        }
    });

    // ── Search, topics and sorting ──────────────
    const toolbar = document.getElementById('toolbar');
    const search = document.getElementById('search');
    const chips = document.getElementById('chips');
    const count = document.getElementById('count');
    const empty = document.getElementById('empty');
    const sortNew = document.getElementById('sort-new');
    const sortAz = document.getElementById('sort-az');
    if (!toolbar || !search || !chips) return;
    toolbar.hidden = false;

    let topic = null;
    let sort = 'new';

    const topicCounts = new Map();
    for (const c of cards) for (const t of c.categories) topicCounts.set(t, (topicCounts.get(t) ?? 0) + 1);
    const topics = [...topicCounts].sort((a, b) => b[1] - a[1] || collator.compare(a[0], b[0]));

    const chip = (label, value, n) => {
        const b = document.createElement('button');
        b.type = 'button';
        b.className = 'chip';
        b.setAttribute('aria-pressed', String(value === topic));
        const small = document.createElement('small');
        small.textContent = n;
        b.append(label, small);
        b.addEventListener('click', () => { topic = value; render(); });
        return b;
    };

    function render() {
        if (topics.length > 0)
            chips.replaceChildren(chip('Alle', null, cards.length), ...topics.map(([t, n]) => chip(t, t, n)));
        sortNew?.setAttribute('aria-pressed', String(sort === 'new'));
        sortAz?.setAttribute('aria-pressed', String(sort === 'az'));

        const terms = fold(search.value.trim(), false).split(/\s+/).filter(Boolean);
        const shown = cards
            .filter(c => !topic || c.categories.includes(topic))
            .filter(c => terms.every(t => c.hay.includes(t)))
            // Undated cards (age Infinity) would give NaN; || falls back to the title.
            .sort(sort === 'new' ? (a, b) => (a.age - b.age) || collator.compare(a.title, b.title) : (a, b) => collator.compare(a.title, b.title));

        const visible = new Set(shown);
        for (const c of cards) c.el.hidden = !visible.has(c);
        shelf.append(...shown.map(c => c.el));

        if (count) count.textContent = shown.length === cards.length ? '' : `${shown.length} af ${cards.length} podcasts`;
        if (empty) {
            empty.hidden = shown.length > 0;
            empty.textContent = `Ingen podcasts matcher “${search.value.trim()}”${topic ? ` i ${topic}` : ''}. Prøv et kortere ord, eller vælg “Alle”.`;
        }
    }

    search.addEventListener('input', render);
    sortNew?.addEventListener('click', () => { sort = 'new'; render(); });
    sortAz?.addEventListener('click', () => { sort = 'az'; render(); });
    render();
})();
