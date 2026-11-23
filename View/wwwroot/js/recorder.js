const sessions = new Map();
export function supported() { return !!(navigator.mediaDevices?.getUserMedia && window.MediaRecorder); }
export async function start(id, reference, maxSeconds) {
    dispose(id);
    if (!supported()) throw new Error('ההקלטה דורשת דפדפן עדכני וחיבור HTTPS.');
    const types = ['audio/webm;codecs=opus', 'audio/mp4', 'audio/ogg;codecs=opus'];
    const type = types.find(t => MediaRecorder.isTypeSupported(t));
    if (!type) throw new Error('הדפדפן אינו תומך בפורמט הקלטה מתאים.');
    const stream = await navigator.mediaDevices.getUserMedia({audio: true});
    const state = {stream, chunks: [], disposed: false, seconds: 0, key: crypto.randomUUID()};
    sessions.set(id, state);
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
export async function upload(id, exerciseId, token) {
    const state = sessions.get(id);
    if (!state?.blob?.size) return {ok: false, message: 'לא נמצאה הקלטה. הקליטו שוב.'};
    const form = new FormData();
    const extension = state.blob.type.includes('mp4') ? 'mp4' : state.blob.type.includes('ogg') ? 'ogg' : 'webm';
    form.append('file', state.blob, `recording.${extension}`);
    form.append('exerciseId', exerciseId);
    form.append('submissionKey', state.key); // Retained on network failure and duplicate clicks.
    form.append('consent', 'true');
    try {
        const response = await fetch('/practice/upload', {method: 'POST', body: form, headers: {'X-CSRF-TOKEN': token}, credentials: 'same-origin'});
        const value = await response.json().catch(() => ({}));
        if (!response.ok) return {ok: false, message: value.message || (response.status === 401 ? 'החיבור פג. התחברו מחדש.' : 'השליחה נכשלה. אפשר לנסות שוב.')};
        return {ok: true, id: value.id};
    } catch { return {ok: false, message: 'החיבור נותק. נסו לשלוח שוב; לא ייווצר ניסיון כפול.'}; }
}
export function csrf(id) { return document.getElementById(id)?.querySelector('input[name="__RequestVerificationToken"]')?.value; }
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
