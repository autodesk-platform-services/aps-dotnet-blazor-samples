export function scrollIntoView(element) {
    if (element && typeof element.scrollIntoView === 'function') {
        element.scrollIntoView({ behavior: 'auto', block: 'end' });
    }
}
