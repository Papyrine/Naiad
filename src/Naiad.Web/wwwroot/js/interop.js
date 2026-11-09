window.statePreference = {
    get: function (key) {
        return localStorage.getItem(key);
    },
    set: function (key, value) {
        localStorage.setItem(key, value);
    },
    remove: function (key) {
        localStorage.removeItem(key);
    }
};

window.fileDownload = {
    downloadBlob: function (filename, contentType, base64Content) {
        const byteCharacters = atob(base64Content);
        const byteNumbers = new Array(byteCharacters.length);
        for (let i = 0; i < byteCharacters.length; i++) {
            byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        const byteArray = new Uint8Array(byteNumbers);
        const blob = new Blob([byteArray], { type: contentType });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
    }
};

window.appInfo = {
    userAgent: function () {
        return navigator.userAgent;
    },
    // Totals the app's boot download from the browser's Resource Timing data: encodedBodySize is the
    // compressed bytes received over the wire, decodedBodySize the uncompressed bytes. Waits for the load
    // event (and web fonts) so every framework/asset request is in the timeline before summing. All assets
    // are same-origin, so the sizes are reported rather than zeroed for cross-origin opacity.
    downloadSize: async function () {
        if (document.readyState !== 'complete') {
            await new Promise(resolve => window.addEventListener('load', resolve, { once: true }));
        }
        try {
            await document.fonts.ready;
        } catch {
        }

        let zipped = 0;
        let unzipped = 0;
        const add = entry => {
            zipped += entry.encodedBodySize || 0;
            unzipped += entry.decodedBodySize || 0;
        };
        performance.getEntriesByType('navigation').forEach(add);
        performance.getEntriesByType('resource').forEach(add);
        return { zipped, unzipped };
    },
    // Approximate RAM the app occupies. Blazor's managed heap lives in WebAssembly linear memory, so the
    // WASM buffer size is the real footprint; fall back to Chromium's JS heap when the runtime handle isn't
    // exposed, and 0 when neither is available (so the caller can hide the figure).
    ramBytes: function () {
        try {
            const buffer = globalThis.getDotnetRuntime?.(0)?.Module?.HEAP8?.buffer;
            if (buffer) {
                return buffer.byteLength;
            }
        } catch {
        }
        return performance.memory?.usedJSHeapSize ?? 0;
    }
};

window.shareLink = {
    // Sets the URL fragment without adding a history entry or a navigation. Empty clears it.
    replaceHash: function (fragment) {
        const url = location.pathname + location.search + (fragment ? '#' + fragment : '');
        history.replaceState(history.state, '', url);
    }
};

window.themeManager = {
    applyTheme: function (themeName) {
        document.documentElement.setAttribute('data-theme', themeName.toLowerCase());
    },
    initializeTheme: function () {
        const savedTheme = localStorage.getItem('selectedTheme');
        if (savedTheme) {
            document.documentElement.setAttribute('data-theme', savedTheme.toLowerCase());
        }
    }
};

window.diagramExport = {
    // Rasterizes a self-contained SVG (native <text>, no foreignObject/@import) to a PNG, returned as a
    // bare base64 string for fileDownload.downloadBlob. The SVG's pixel size is taken from its viewBox —
    // Naiad emits width="100%" with no height, which gives an <img> no intrinsic size, so explicit
    // width/height are stamped on before serialising. scale multiplies that size for crisper output.
    svgToPng: async function (svgMarkup, scale) {
        const parsed = new DOMParser().parseFromString(svgMarkup, 'image/svg+xml');
        const svg = parsed.documentElement;

        let width = parseFloat(svg.getAttribute('width'));
        let height = parseFloat(svg.getAttribute('height'));
        const viewBox = svg.getAttribute('viewBox');
        if ((!width || !height) && viewBox) {
            const parts = viewBox.split(/[\s,]+/).map(Number);
            width = parts[2];
            height = parts[3];
        }
        width = Math.max(1, Math.round(width || 0));
        height = Math.max(1, Math.round(height || 0));

        svg.setAttribute('width', width);
        svg.setAttribute('height', height);
        const serialized = new XMLSerializer().serializeToString(svg);

        const url = URL.createObjectURL(new Blob([serialized], { type: 'image/svg+xml;charset=utf-8' }));
        try {
            const image = new Image();
            image.width = width;
            image.height = height;
            await new Promise((resolve, reject) => {
                image.onload = () => resolve();
                image.onerror = () => reject(new Error('Could not rasterize the diagram'));
                image.src = url;
            });

            const targetWidth = Math.max(1, Math.round(width * scale));
            const targetHeight = Math.max(1, Math.round(height * scale));
            const canvas = document.createElement('canvas');
            canvas.width = targetWidth;
            canvas.height = targetHeight;

            const context = canvas.getContext('2d');
            context.fillStyle = '#ffffff';
            context.fillRect(0, 0, targetWidth, targetHeight);
            context.drawImage(image, 0, 0, targetWidth, targetHeight);

            const dataUrl = canvas.toDataURL('image/png');
            return dataUrl.substring(dataUrl.indexOf(',') + 1);
        } finally {
            URL.revokeObjectURL(url);
        }
    }
};
