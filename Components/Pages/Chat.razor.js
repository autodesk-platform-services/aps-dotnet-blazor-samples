export function scrollIntoView(element) {
    if (element && typeof element.scrollIntoView === 'function') {
        element.scrollIntoView({ behavior: 'auto', block: 'end' });
    }
}

export function setupEnterToSend(element, dotNetHelper) {
    if (!element) return;

    element.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            // FluentTextArea's default action on Enter is to insert a newline; take over
            // submission ourselves and stop Blazor's own delegated keydown handling for it.
            e.preventDefault();
            e.stopPropagation();
            dotNetHelper.invokeMethodAsync('SendMessageFromJs');
        }
    });
}
