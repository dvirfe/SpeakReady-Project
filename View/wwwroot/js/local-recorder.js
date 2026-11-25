const sessions = new Map();
export function supported() { return !!(navigator.mediaDevices?.getUserMedia && window.MediaRecorder); }
export async function start(id, reference, maxSeconds) {
    dispose(id);
    if (!supported()) throw new Error('ההקלטה דורשת דפדפן עדכני וחיבור HTTPS.');
    const types = ['audio/webm;codecs=opus', 'audio/mp4', 'audio/ogg;codecs=opus'];
    const type = types.find(t => MediaRecorder.isTypeSupported(t));
    if (!type) throw new Error('הדפדפן אינו תומך בפורמט הקלטה מתאים.');
    const state = {chunks: [], disposed: false, seconds: 0};
    sessions.set(id, state);
    let stream;
    try { stream = await navigator.mediaDevices.getUserMedia({audio: true}); }
    catch (error) { if (sessions.get(id) === state) sessions.delete(id); throw error; }
    if (state.disposed) { stream.getTracks().forEach(track => track.stop()); return; }
    state.stream = stream;
    try {
        state.recorder = new MediaRecorder(stream, {mimeType: type, audioBitsPerSecond: 64000});
        state.recorder.ondataavailable = e => { if (e.data.size) state.chunks.push(e.data); };
        state.recorder.onerror = () => {
            stop(id);
            reference.invokeMethodAsync('RecordingError', 'ההקלטה נכשלה. נסו שוב.').catch(() => {});
        };
        state.recorder.onstop = () => {
            clearInterval(state.timer);
            stream.getTracks().forEach(track => track.stop());
            if (state.disposed) return;
            state.blob = new Blob(state.chunks, {type});
            state.url = URL.createObjectURL(state.blob);
            reference.invokeMethodAsync('RecordingReady', state.url, state.seconds).catch(() => dispose(id));
        };
        state.recorder.start(250);
        state.timer = setInterval(() => {
            state.seconds++;
            reference.invokeMethodAsync('RecordingTick', state.seconds).catch(() => dispose(id));
            if (state.seconds >= maxSeconds) stop(id);
        }, 1000);
    } catch (error) { dispose(id); throw error; }
}
export function stop(id) {
    const state = sessions.get(id);
    if (state?.recorder?.state === 'recording') state.recorder.stop();
}
export function dispose(id) {
    const state = sessions.get(id);
    if (!state) return;
    state.disposed = true;
    clearInterval(state.timer);
    if (state.recorder?.state === 'recording') state.recorder.stop();
    state.stream?.getTracks().forEach(track => track.stop());
    if (state.url) URL.revokeObjectURL(state.url);
    sessions.delete(id);
}
window.addEventListener('pagehide', () => { for (const id of sessions.keys()) dispose(id); });
