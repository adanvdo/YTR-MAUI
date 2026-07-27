// Resize grip: captures pointer to ensure smooth dragging even when cursor leaves the grip element
window.resizeGrip = {
    capture: function (element, pointerId) {
        if (element && element.setPointerCapture) {
            element.setPointerCapture(pointerId);
        }
    },
    release: function (element, pointerId) {
        if (element && element.releasePointerCapture) {
            try { element.releasePointerCapture(pointerId); } catch { }
        }
    },
    getContentWidth: function (gripElement) {
        var container = gripElement ? gripElement.parentElement : null;
        return container ? container.clientWidth : 0;
    }
};
