let viewer = null;

export async function initViewer(containerId, tokenEndpoint) {
    return new Promise((resolve, reject) => {
        Autodesk.Viewing.Initializer({
            getAccessToken: async (callback) => {
                const response = await fetch(tokenEndpoint);
                const data = await response.json();
                callback(data.accessToken, data.expiresIn);
            }
        }, () => {
            const container = document.getElementById(containerId);
            viewer = new Autodesk.Viewing.GuiViewer3D(container);
            viewer.start();
            resolve();
        });
    });
}

export function loadModel(urn) {
    Autodesk.Viewing.Document.load(
        'urn:' + urn,
        (doc) => {
            const viewable = doc.getRoot().getDefaultGeometry();
            viewer.loadDocumentNode(doc, viewable);
        },
        (errorCode, errorMsg) => {
            console.error('Failed to load model:', errorCode, errorMsg);
        }
    );
}

export function destroyViewer() {
    if (viewer) {
        viewer.finish();
        viewer = null;
    }
}
