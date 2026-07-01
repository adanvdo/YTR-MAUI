// Crop tool JS interop - provides accurate element dimensions for coordinate mapping
window.cropToolInterop = {
    getElementBounds: function (element) {
        if (!element) return null;
        const rect = element.getBoundingClientRect();
        return { width: rect.width, height: rect.height, left: rect.left, top: rect.top };
    }
};
