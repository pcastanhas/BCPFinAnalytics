// BCPFinAnalytics — Client-side JavaScript helpers

/**
 * Triggers a file download in the browser from a base64-encoded byte array.
 * Called from Blazor via IJSRuntime for Excel and PDF exports.
 *
 * @param {string} fileName - The download filename (e.g. "PL001_20240101_120000.xlsx")
 * @param {string} mimeType - The MIME type (e.g. "application/pdf")
 * @param {string} base64   - Base64-encoded file content
 */
window.downloadFile = function (fileName, mimeType, base64) {
    const byteChars = atob(base64);
    const byteArrays = [];

    for (let offset = 0; offset < byteChars.length; offset += 1024) {
        const slice = byteChars.slice(offset, offset + 1024);
        const byteNumbers = new Array(slice.length);
        for (let i = 0; i < slice.length; i++) {
            byteNumbers[i] = slice.charCodeAt(i);
        }
        byteArrays.push(new Uint8Array(byteNumbers));
    }

    const blob = new Blob(byteArrays, { type: mimeType });
    const url = URL.createObjectURL(blob);

    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);

    // Release the object URL after a short delay
    setTimeout(() => URL.revokeObjectURL(url), 1000);
};
