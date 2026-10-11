// Enhances the server-rendered feed cards: search, topic filter, sorting, relative dates and copy.
// Without JavaScript the page still lists every feed with working links.
(() => {
    const DAY = 86400000;
    const NEW_PODCAST_DAYS = 30;
    const NEW = 'nye-podcasts'; // the "Nye podcasts" chip's filter value
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
        const badge = (className, text) => {
            const b = document.createElement('span');
            b.className = className;
            b.textContent = text;
            el.querySelector('.tags')?.prepend(b);
        };
        if (age < 2 * DAY) badge('fresh', 'Nyt afsnit');
        // Set by the server only for podcasts in their own right, not rolling radio archives.
        const first = Date.parse(el.dataset.firstEpisode ?? '');
        const isNew = !Number.isNaN(first) && Date.now() - first < NEW_PODCAST_DAYS * DAY;
        if (isNew) badge('debut', 'Ny podcast');
        // Only what describes the podcast, not button labels or the feed address.
        const text = ['h2', '.sub', '.episode'].map(s => el.querySelector(s)?.textContent ?? '').join(' ');
        return {
            el,
            age,
            isNew,
            title: el.dataset.title ?? '',
            categories: (el.dataset.categories ?? '').split('|').filter(Boolean),
            hay: `${fold(text, false)} ${fold(text, true)}`,
        };
    });

    // ── Copy feed link ──────────────────────────
    // The copy buttons are in the expanded card, which is rebuilt on every open; one listener serves them all.
    document.addEventListener('click', async e => {
        const button = e.target.closest('.copy');
        if (!button) return;
        try {
            await navigator.clipboard.writeText(button.dataset.url);
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

    // ── Expanded card ───────────────────────────
    // Clicking a card grows it into a dialog with the same content plus the full description.
    // Where the browser supports view transitions, the card itself morphs into the dialog and back.
    // The descriptions aren't in the page: descriptions.json is fetched the first time a card opens.
    const dialog = document.getElementById('details');
    const dialogCard = document.getElementById('details-card');
    if (dialog && dialogCard && typeof dialog.showModal === 'function') {
        const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
        const morphName = 'expanding-card';
        let openCard = null;
        // The cards only link to their feed; the expanded card has the subscribe controls.
        shelf.classList.add('expandable');

        // The ways into each app's subscribe flow; all take the feed's public address unchanged.
        const appLinks = (feedUrl, title) => {
            const url = encodeURIComponent(feedUrl);
            const withoutScheme = feedUrl.replace(/^[a-z][a-z0-9+.-]*:\/\//i, '');
            return [
                ['AntennaPod', `https://antennapod.org/deeplink/subscribe?url=${url}&title=${encodeURIComponent(title)}`],
                ['Apple Podcasts', `podcast://${withoutScheme}`],
                ['Overcast', `overcast://x-callback-url/add?url=${url}`],
                ['Pocket Casts', `pktc://subscribe/${withoutScheme}`],
            ];
        };

        const element = (tag, className, text) => {
            const el = document.createElement(tag);
            if (className) el.className = className;
            if (text) el.textContent = text;
            return el;
        };

        // The copy button and the "Åbn i app" menu.
        const subscribeControls = (feedUrl, title) => {
            const copyButton = element('button', 'btn primary copy', 'Kopiér feed-link');
            copyButton.type = 'button';
            copyButton.dataset.url = feedUrl;
            const menu = element('div', 'menu');
            for (const [app, href] of appLinks(feedUrl, title)) {
                const link = element('a', null, app);
                link.href = href;
                link.rel = 'nofollow';
                menu.append(link);
            }
            const openIn = element('details', 'open-in');
            openIn.append(element('summary', 'btn', 'Åbn i app'), menu);
            const actions = element('div', 'actions');
            actions.append(copyButton, openIn);
            return actions;
        };

        // Every description by slug, fetched once. A failed fetch leaves the description out and is
        // retried on the next open.
        let descriptions = null;
        const loadDescriptions = () => descriptions ??= (shelf.dataset.descriptions
            ? fetch(shelf.dataset.descriptions).then(r => {
                if (!r.ok) throw new Error(r.statusText);
                return r.json();
            })
            : Promise.resolve({})
        ).catch(() => {
            descriptions = null;
            return {};
        });

        // Hands the view-transition name from one element to the other across the update, so the
        // browser animates the first into the second.
        const morph = (from, to, update) => {
            if (!document.startViewTransition || reducedMotion.matches) {
                update();
                return;
            }
            from.style.viewTransitionName = morphName;
            const transition = document.startViewTransition(() => {
                from.style.viewTransitionName = '';
                update();
                to.style.viewTransitionName = morphName;
            });
            transition.finished.finally(() => { to.style.viewTransitionName = ''; });
        };

        const expand = card => {
            const copy = card.cloneNode(true);
            const heading = copy.querySelector('h2');
            if (heading) {
                heading.textContent = card.dataset.title ?? heading.textContent;
                heading.id = 'details-title';
            }
            // The feed link's href is the feed's address exactly as the server wrote it.
            const feedLink = copy.querySelector('.url');
            if (feedLink) feedLink.before(subscribeControls(feedLink.getAttribute('href'), card.dataset.title ?? ''));
            // Below the tagline; filled in once descriptions.json is here. Each open has its own paragraph,
            // so a late answer for a dialog that has since closed only fills in one that's gone.
            const description = document.createElement('p');
            description.className = 'description';
            description.hidden = true;
            copy.querySelector('.latest')?.before(description);
            const slug = card.dataset.slug;
            loadDescriptions().then(all => {
                if (!slug || !Object.hasOwn(all, slug)) return;
                description.textContent = all[slug];
                description.hidden = false;
            });
            openCard = card;
            morph(card, dialog, () => {
                dialogCard.replaceChildren(...copy.children);
                dialog.showModal();
            });
        };

        const collapse = () => {
            if (!dialog.open) return;
            morph(dialog, openCard ?? dialog, () => dialog.close());
        };

        for (const card of shelf.querySelectorAll('.card')) {
            const heading = card.querySelector('h2');
            if (!heading) continue;
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'title-button';
            button.textContent = heading.textContent;
            button.setAttribute('aria-haspopup', 'dialog');
            heading.replaceChildren(button);

            // The whole card opens it; the title button is how keyboards and screen readers get there.
            // Its own controls keep their job, and selecting text on the card doesn't open it.
            card.addEventListener('click', e => {
                if (e.target.closest('a, button:not(.title-button), summary, .menu')) return;
                if (getSelection()?.toString()) return;
                expand(card);
            });
        }

        document.getElementById('details-close')?.addEventListener('click', collapse);
        // Escape: animate the close instead of letting the dialog vanish.
        dialog.addEventListener('cancel', e => { e.preventDefault(); collapse(); });
        // The padding sits on .details-body, so a click on the dialog element itself is the backdrop.
        dialog.addEventListener('click', e => { if (e.target === dialog) collapse(); });
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
    const newCount = cards.filter(c => c.isNew).length;

    // Built once; a topic change only moves aria-pressed.
    const chipButtons = [];
    const chip = (label, value, n) => {
        const b = document.createElement('button');
        b.type = 'button';
        b.className = 'chip';
        const small = document.createElement('small');
        small.textContent = n;
        b.append(label, small);
        b.addEventListener('click', () => { topic = value; render(); });
        chipButtons.push([b, value]);
        return b;
    };
    if (topics.length > 0 || newCount > 0)
        chips.replaceChildren(
            chip('Alle', null, cards.length),
            ...(newCount > 0 ? [chip('Nye podcasts', NEW, newCount)] : []),
            ...topics.map(([t, n]) => chip(t, t, n)));

    // Cards are only moved when the order changes, not on every keystroke: moving all 680 makes the
    // browser lay out the whole shelf again.
    const byTitle = (a, b) => collator.compare(a.title, b.title);
    // Undated cards (age Infinity) would give NaN; || falls back to the title.
    const byNewest = (a, b) => (a.age - b.age) || byTitle(a, b);
    const applySort = () => {
        const ordered = [...cards].sort(sort === 'new' ? byNewest : byTitle);
        if (ordered.some((c, i) => shelf.children[i] !== c.el)) shelf.append(...ordered.map(c => c.el));
    };

    function render() {
        for (const [b, value] of chipButtons) b.setAttribute('aria-pressed', String(value === topic));
        sortNew?.setAttribute('aria-pressed', String(sort === 'new'));
        sortAz?.setAttribute('aria-pressed', String(sort === 'az'));

        const terms = fold(search.value.trim(), false).split(/\s+/).filter(Boolean);
        let shown = 0;
        for (const c of cards) {
            const match = (!topic || (topic === NEW ? c.isNew : c.categories.includes(topic))) && terms.every(t => c.hay.includes(t));
            if (c.el.hidden === match) c.el.hidden = !match;
            if (match) shown++;
        }

        if (count) count.textContent = shown === cards.length ? '' : `${shown} af ${cards.length} podcasts`;
        if (empty) {
            empty.hidden = shown > 0;
            empty.textContent = `Ingen podcasts matcher “${search.value.trim()}”${topic ? ` i ${topic}` : ''}. Prøv et kortere ord, eller vælg “Alle”.`;
        }
    }

    // Typing waits for a pause rather than filtering on every key.
    let searchTimer;
    search.addEventListener('input', () => {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(render, 100);
    });
    const sortBy = order => { sort = order; applySort(); render(); };
    sortNew?.addEventListener('click', () => sortBy('new'));
    sortAz?.addEventListener('click', () => sortBy('az'));
    applySort();
    render();
})();
