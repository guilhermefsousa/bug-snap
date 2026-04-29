window.__bugSnap = {
    errors: [],
    maxErrors: 10,
    _autoCaptureHelper: null,
    _consoleErrorRing: [],
    _consoleErrorMax: 5,
    _blazorUiFired: false,
    _blazorUiLastFireMs: 0,
    _blazorUiCooldownMs: 30000,
    _initialized: false,

    init(maxErrors) {
        if (typeof maxErrors === 'number' && maxErrors > 0) {
            this.maxErrors = maxErrors;
        }
        if (this._initialized) return;
        this._initialized = true;

        const self = this;

        window.addEventListener('error', (e) => {
            const entry = self._buildEntry(
                e.message || 'Unknown error',
                e.filename || null,
                e.lineno || null,
                e.colno || null,
                e.error && e.error.stack ? e.error.stack : null
            );
            self._pushAndDispatch(entry);
        });

        window.addEventListener('unhandledrejection', (e) => {
            const reason = e.reason;
            const message = reason && reason.message ? reason.message : String(reason);
            const stack = reason && reason.stack ? reason.stack : null;
            const entry = self._buildEntry(
                message || 'Unhandled rejection',
                'unhandledrejection',
                null, null, stack
            );
            self._pushAndDispatch(entry);
        });

        self._patchConsoleError();
        self._observeBlazorErrorUi();
    },

    initAutoCapture(dotNetHelper) {
        this._autoCaptureHelper = dotNetHelper;
    },

    _buildEntry(message, source, line, column, stackTrace) {
        const trimmed = (s) => (typeof s === 'string' && s.length > 4000) ? s.slice(0, 4000) : s;
        return {
            message: trimmed(message) || 'Unknown error',
            source: source,
            line: line,
            column: column,
            stackTrace: trimmed(stackTrace),
            timestamp: new Date().toISOString()
        };
    },

    _pushAndDispatch(entry) {
        this.errors.push(entry);
        if (this.errors.length > this.maxErrors) this.errors.shift();
        if (this._autoCaptureHelper) {
            try {
                this._autoCaptureHelper.invokeMethodAsync('OnJsError', entry).catch(() => { });
            } catch (_) {
                // WASM runtime may be broken after a renderer crash — swallow.
            }
        }
    },

    _patchConsoleError() {
        if (this._consolePatched) return;
        this._consolePatched = true;
        const originalError = console.error.bind(console);
        const self = this;
        console.error = function () {
            try {
                let stack = null;
                const parts = [];
                for (let i = 0; i < arguments.length; i++) {
                    const a = arguments[i];
                    if (a instanceof Error) {
                        if (!stack && a.stack) stack = a.stack;
                        parts.push(a.stack || a.message);
                    } else if (a && typeof a === 'object') {
                        try { parts.push(JSON.stringify(a)); }
                        catch (_) { parts.push(String(a)); }
                    } else {
                        parts.push(String(a));
                    }
                }
                self._consoleErrorRing.push({
                    message: parts.join(' '),
                    stack: stack,
                    timestamp: new Date().toISOString()
                });
                if (self._consoleErrorRing.length > self._consoleErrorMax) {
                    self._consoleErrorRing.shift();
                }
            } catch (_) {
                // never break console.error
            }
            return originalError.apply(console, arguments);
        };
    },

    _observeBlazorErrorUi() {
        const self = this;
        const tryAttach = () => {
            const el = document.getElementById('blazor-error-ui');
            if (!el) return false;

            const check = () => {
                const style = window.getComputedStyle(el);
                const isVisible = style.display !== 'none' && style.visibility !== 'hidden';

                // Transition visible -> hidden: arm for next crash (user dismissed overlay).
                if (!isVisible) {
                    self._blazorUiFired = false;
                    return;
                }

                // visible: dispatch once per cooldown window to avoid runaway loops.
                const now = Date.now();
                if (self._blazorUiFired) return;
                if (now - self._blazorUiLastFireMs < self._blazorUiCooldownMs) return;
                self._blazorUiFired = true;
                self._blazorUiLastFireMs = now;
                self._reportBlazorRendererError();
            };

            // Observer kept alive for the whole tab lifetime — intentional, no disconnect needed
            // since we want every renderer crash captured for the lifetime of the SPA.
            const observer = new MutationObserver(check);
            observer.observe(el, { attributes: true, attributeFilter: ['style', 'class'] });
            check();
            return true;
        };

        if (tryAttach()) return;
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', tryAttach, { once: true });
        } else {
            setTimeout(tryAttach, 100);
        }
    },

    _reportBlazorRendererError() {
        const last = this._consoleErrorRing.length > 0
            ? this._consoleErrorRing[this._consoleErrorRing.length - 1]
            : null;
        const entry = this._buildEntry(
            (last && last.message) || 'Blazor renderer crashed (#blazor-error-ui shown)',
            'blazor-renderer',
            null, null,
            last && last.stack ? last.stack : null
        );
        this._pushAndDispatch(entry);
    },

    getErrors() { return JSON.parse(JSON.stringify(this.errors)); },
    clearErrors() { this.errors = []; },
    getBrowserInfo() { return navigator.userAgent; },
    getScreenSize() { return window.innerWidth + 'x' + window.innerHeight; },

    log(message) { console.log('%c[BugSnap]%c ' + message, 'color: #0F8B95; font-weight: bold', 'color: inherit'); },

    initPasteHandler(textareaElement, dotNetHelper) {
        textareaElement.addEventListener('paste', async (e) => {
            const items = e.clipboardData && e.clipboardData.items;
            if (!items) return;
            for (const item of items) {
                if (item.type && item.type.startsWith('image/')) {
                    e.preventDefault();
                    const blob = item.getAsFile();
                    const buffer = await blob.arrayBuffer();
                    const bytes = Array.from(new Uint8Array(buffer));
                    await dotNetHelper.invokeMethodAsync('OnImagePasted', bytes, blob.name || 'screenshot.png');
                    return;
                }
            }
        });
    }
};

// Auto-init on script load — registers listeners + observer immediately,
// without depending on .NET-side InitAsync being called.
window.__bugSnap.init();
