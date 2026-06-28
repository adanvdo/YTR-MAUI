// Scroll indicator: shows/hides based on whether the active tab panel content
// overflows and whether the user has scrolled to the bottom.
window.scrollIndicator = {
    _observer: null,
    _mutationObserver: null,
    _scrollHandler: null,
    _container: null,

    init: function () {
        this.dispose();

        // Small delay to let MudBlazor finish rendering tabs
        setTimeout(() => this._setup(), 100);
    },

    _setup: function () {
        const indicator = document.querySelector('.settings-tabs > .scroll-fade-indicator');
        if (!indicator) return;

        // The .mud-tabs-panels div is the scrollable container
        const container = document.querySelector('.settings-tabs .mud-tabs-panels');
        if (!container) return;

        this._container = container;
        this._indicator = indicator;

        const update = () => {
            const hasOverflow = container.scrollHeight > container.clientHeight + 1;
            const atBottom = container.scrollHeight - container.scrollTop - container.clientHeight < 4;

            if (hasOverflow && !atBottom) {
                indicator.classList.add('visible');
            } else {
                indicator.classList.remove('visible');
            }
        };

        this._scrollHandler = update;
        container.addEventListener('scroll', update, { passive: true });

        // ResizeObserver detects container or content size changes
        this._observer = new ResizeObserver(() => {
            // Debounce slightly for resize events
            setTimeout(update, 50);
        });
        this._observer.observe(container);
        // Also observe children for size changes
        for (const child of container.children) {
            this._observer.observe(child);
        }

        // MutationObserver detects tab switches (child content changes)
        this._mutationObserver = new MutationObserver(() => {
            // After tab switch, content changes — recheck after render settles
            setTimeout(update, 50);
        });
        this._mutationObserver.observe(container, { childList: true, subtree: true, attributes: true });

        // Initial check
        update();
    },

    dispose: function () {
        if (this._container && this._scrollHandler) {
            this._container.removeEventListener('scroll', this._scrollHandler);
        }
        if (this._observer) {
            this._observer.disconnect();
            this._observer = null;
        }
        if (this._mutationObserver) {
            this._mutationObserver.disconnect();
            this._mutationObserver = null;
        }
        this._container = null;
        this._scrollHandler = null;
        this._indicator = null;
    }
};
