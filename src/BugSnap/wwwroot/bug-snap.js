window.__bugSnap = {
    errors: [],
    maxErrors: 10,

    init(maxErrors) {
        this.maxErrors = maxErrors || 10;
        window.addEventListener('error', (e) => {
            this.errors.push({
                message: e.message || 'Unknown error',
                source: e.filename || null,
                line: e.lineno || null,
                column: e.colno || null,
                timestamp: new Date().toISOString()
            });
            if (this.errors.length > this.maxErrors) this.errors.shift();
        });
        window.addEventListener('unhandledrejection', (e) => {
            this.errors.push({
                message: e.reason?.message || String(e.reason) || 'Unhandled rejection',
                source: 'unhandledrejection',
                line: null,
                column: null,
                timestamp: new Date().toISOString()
            });
            if (this.errors.length > this.maxErrors) this.errors.shift();
        });
    },

    getErrors() { return JSON.parse(JSON.stringify(this.errors)); },
    clearErrors() { this.errors = []; },
    getBrowserInfo() { return navigator.userAgent; },
    getScreenSize() { return window.innerWidth + 'x' + window.innerHeight; },

    log(message) { console.log('%c[BugSnap]%c ' + message, 'color: #0F8B95; font-weight: bold', 'color: inherit'); }
};
