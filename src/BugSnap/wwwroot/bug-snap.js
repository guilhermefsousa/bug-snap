window.__bugSnap = {
    errors: [],
    maxErrors: 10,
    _autoCaptureHelper: null,

    init(maxErrors) {
        this.maxErrors = maxErrors || 10;
        window.addEventListener('error', (e) => {
            const entry = {
                message: e.message || 'Unknown error',
                source: e.filename || null,
                line: e.lineno || null,
                column: e.colno || null,
                stackTrace: e.error?.stack || null,
                timestamp: new Date().toISOString()
            };
            this.errors.push(entry);
            if (this.errors.length > this.maxErrors) this.errors.shift();
            if (this._autoCaptureHelper) {
                this._autoCaptureHelper.invokeMethodAsync('OnJsError', entry).catch(() => {});
            }
        });
        window.addEventListener('unhandledrejection', (e) => {
            const entry = {
                message: e.reason?.message || String(e.reason) || 'Unhandled rejection',
                source: 'unhandledrejection',
                line: null,
                column: null,
                stackTrace: e.reason?.stack || null,
                timestamp: new Date().toISOString()
            };
            this.errors.push(entry);
            if (this.errors.length > this.maxErrors) this.errors.shift();
            if (this._autoCaptureHelper) {
                this._autoCaptureHelper.invokeMethodAsync('OnJsError', entry).catch(() => {});
            }
        });
    },

    initAutoCapture(dotNetHelper) {
        this._autoCaptureHelper = dotNetHelper;
    },

    getErrors() { return JSON.parse(JSON.stringify(this.errors)); },
    clearErrors() { this.errors = []; },
    getBrowserInfo() { return navigator.userAgent; },
    getScreenSize() { return window.innerWidth + 'x' + window.innerHeight; },

    log(message) { console.log('%c[BugSnap]%c ' + message, 'color: #0F8B95; font-weight: bold', 'color: inherit'); },

    initPasteHandler(textareaElement, dotNetHelper) {
        textareaElement.addEventListener('paste', async (e) => {
            const items = e.clipboardData?.items;
            if (!items) return;
            for (const item of items) {
                if (item.type.startsWith('image/')) {
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
