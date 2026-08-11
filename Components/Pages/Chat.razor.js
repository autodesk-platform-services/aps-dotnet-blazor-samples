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

export function setupEnterToSend(element, dotNetHelper) {
    if (!element) return;

    element.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            // FluentTextArea's default action on Enter is to insert a newline, which lands in the
            // message even for short, single-line text since the async round trip to send the
            // message can't beat the browser inserting it synchronously. Take over submission
            // ourselves so a plain Enter never inserts anything - only Shift+Enter should.
            e.preventDefault();
            e.stopPropagation();
            dotNetHelper.invokeMethodAsync('SendMessageFromJs');
        }
    });
}
