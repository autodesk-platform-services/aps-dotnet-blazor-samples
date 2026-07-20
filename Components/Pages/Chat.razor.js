export function scrollIntoView(element) {
    if (element && typeof element.scrollIntoView === 'function') {
        element.scrollIntoView({ behavior: 'auto', block: 'end' });
    }
}

export function scrollToElementById(id) {
    const element = document.getElementById(id);
    if (element && typeof element.scrollIntoView === 'function') {
        element.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
}
