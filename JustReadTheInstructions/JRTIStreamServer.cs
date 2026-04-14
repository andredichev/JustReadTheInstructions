using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace JustReadTheInstructions
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class JRTIStreamServer : MonoBehaviour
    {
        public static JRTIStreamServer Instance { get; private set; }

        private HttpListener _listener;
        private Thread _listenerThread;
        private volatile bool _running;

        private readonly ConcurrentDictionary<int, CameraStreamState> _states
            = new ConcurrentDictionary<int, CameraStreamState>();

        private readonly ConcurrentDictionary<int, float> _lastCaptureTimes
            = new ConcurrentDictionary<int, float>();

        private readonly ConcurrentDictionary<int, bool> _captureInFlight
            = new ConcurrentDictionary<int, bool>();

        private float MinCapturePeriod => 1f / Mathf.Max(1, JRTISettings.StreamMaxFps);

        private static readonly string WebRoot =
            KSPUtil.ApplicationRootPath + "GameData/JustReadTheInstructions/Web/";

        void Awake()
        {
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
        }

        void Start() => StartServer();

        void OnDestroy()
        {
            StopServer();
            if (Instance == this) Instance = null;
        }

        public void RegisterCamera(int cameraId)
            => _states.GetOrAdd(cameraId, _ => new CameraStreamState());

        public void UnregisterCamera(int cameraId)
        {
            if (_states.TryRemove(cameraId, out var state))
                state.Dispose();
            _lastCaptureTimes.TryRemove(cameraId, out _);
            _captureInFlight.TryRemove(cameraId, out _);
        }

        public bool IsStreaming(int cameraId)
            => _states.TryGetValue(cameraId, out var s) && s.MjpegClientCount > 0;

        public void TryCaptureFrame(int cameraId, RenderTexture renderTexture)
        {
            if (!_states.TryGetValue(cameraId, out var state) || !state.HasActiveClients)
                return;

            float now = Time.unscaledTime;
            _lastCaptureTimes.TryGetValue(cameraId, out float last);
            if (now - last < MinCapturePeriod)
                return;

            _captureInFlight.TryGetValue(cameraId, out bool inFlight);
            if (inFlight)
                return;

            _lastCaptureTimes[cameraId] = now;
            _captureInFlight[cameraId] = true;

            int rtWidth = renderTexture.width;
            int rtHeight = renderTexture.height;
            int quality = JRTISettings.StreamJpegQuality;

            AsyncGPUReadback.Request(renderTexture, 0, TextureFormat.RGB24, (request) =>
            {
                _captureInFlight[cameraId] = false;

                if (request.hasError)
                    return;

                if (!_states.TryGetValue(cameraId, out var s))
                    return;

                var raw = request.GetData<byte>().ToArray();

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    var jpeg = ImageConversion.EncodeArrayToJPG(
                        raw,
                        GraphicsFormat.R8G8B8_UNorm,
                        (uint)rtWidth,
                        (uint)rtHeight,
                        0,
                        quality);

                    if (jpeg != null && _states.TryGetValue(cameraId, out var s2))
                        s2.PushFrame(jpeg);
                });
            });
        }

        private void StartServer()
        {
            if (!HttpListener.IsSupported)
            {
                Debug.LogWarning("[JRTI-Stream]: HttpListener not supported on this platform");
                return;
            }

            _listener = new HttpListener();

            bool started = TryBind($"http://*:{JRTISettings.StreamPort}/")
                        || TryBind($"http://localhost:{JRTISettings.StreamPort}/");

            if (!started)
            {
                Debug.LogError("[JRTI-Stream]: Could not bind to any address. Streaming disabled.");
                return;
            }

            _running = true;
            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "JRTI-StreamServer"
            };
            _listenerThread.Start();

            Debug.Log($"[JRTI-Stream]: Web UI at http://localhost:{JRTISettings.StreamPort}/");
        }

        private bool TryBind(string prefix)
        {
            try
            {
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add(prefix);
                _listener.Start();
                Debug.Log($"[JRTI-Stream]: Listening on {prefix}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-Stream]: Could not bind {prefix}: {ex.Message}");
                return false;
            }
        }

        private void StopServer()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            _listenerThread?.Join(2000);

            foreach (var state in _states.Values)
                state.Dispose();
            _states.Clear();
        }

        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
                }
                catch (HttpListenerException) when (!_running) { break; }
                catch (Exception ex)
                {
                    if (_running)
                        Debug.LogError($"[JRTI-Stream]: Accept error: {ex.Message}");
                }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url.AbsolutePath.TrimEnd('/');

                if (path == "" || path == "/index.html")
                    ServeIndex(ctx);
                else if (path == "/cameras")
                    ServeCameraList(ctx);
                else if (path.StartsWith("/camera/"))
                    ServeCameraEndpoint(ctx, path);
                else if (path.StartsWith("/images/"))
                    ServeStaticImage(ctx, path);
                else
                    ServeError(ctx, 404, "Not found");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JRTI-Stream]: Request handler error: {ex.Message}");
                try { ctx.Response.Close(); } catch { }
            }
        }

        private void ServeIndex(HttpListenerContext ctx)
        {
            try
            {
                ServeText(ctx, File.ReadAllText(WebRoot + "index.html"), "text/html");
            }
            catch (Exception ex)
            {
                ServeError(ctx, 500, $"Could not read index.html: {ex.Message}");
            }
        }

        private static void ServeStaticImage(HttpListenerContext ctx, string path)
        {
            var relative = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(WebRoot, relative));

            if (!full.StartsWith(Path.GetFullPath(WebRoot)) || !File.Exists(full))
            {
                ServeError(ctx, 404, "Not found");
                return;
            }

            var ext = Path.GetExtension(full).ToLowerInvariant();
            string contentType;
            if (ext == ".png") contentType = "image/png";
            else if (ext == ".jpg" || ext == ".jpeg") contentType = "image/jpeg";
            else contentType = "application/octet-stream";

            var bytes = File.ReadAllBytes(full);
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.Headers.Add("Cache-Control", "no-cache");
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private void ServeCameraList(HttpListenerContext ctx)
        {
            var sb = new StringBuilder("[");
            bool first = true;

            foreach (var kv in _states)
            {
                if (HullCameraManager.Instance != null && !HullCameraManager.Instance.HasCamera(kv.Key))
                    continue;

                if (!first) sb.Append(',');
                int id = kv.Key;
                string name = HullCameraManager.Instance?.GetCameraDisplayName(id) ?? id.ToString();
                sb.Append($"{{\"id\":{id},");
                sb.Append($"\"name\":\"{EscapeJson(name)}\",");
                sb.Append($"\"streaming\":true,");
                sb.Append($"\"snapshotUrl\":\"/camera/{id}/snapshot\",");
                sb.Append($"\"streamUrl\":\"/camera/{id}\"}}");
                first = false;
            }

            sb.Append(']');
            ServeText(ctx, sb.ToString(), "application/json");
        }

        private void ServeCameraEndpoint(HttpListenerContext ctx, string path)
        {
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !int.TryParse(parts[1], out int cameraId))
            {
                ServeError(ctx, 400, "Invalid camera ID");
                return;
            }

            if (parts.Length == 2)
            {
                ServeCameraViewer(ctx, cameraId);
                return;
            }

            if (!_states.TryGetValue(cameraId, out var state))
            {
                ServeError(ctx, 404, "Camera not found");
                return;
            }

            switch (parts[2])
            {
                case "snapshot": ServeSnapshot(ctx, state); break;
                case "stream": ServeMjpeg(ctx, state); break;
                case "status": ServeText(ctx, "ok", "text/plain"); break;
                default: ServeError(ctx, 404, "Unknown action"); break;
            }
        }

        private static void ServeCameraViewer(HttpListenerContext ctx, int cameraId)
        {
            string name = HullCameraManager.Instance?.GetCameraDisplayName(cameraId) ?? cameraId.ToString();
            string safeName = EscapeHtml(name);
            string html =
                "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\">"
                + $"<title>{safeName} - JRTI Stream</title>"
                + "<style>html,body{margin:0;background:#000;height:100%;overflow:hidden;}img{display:block;width:100%;height:100%;object-fit:contain;}</style>"
                + "</head><body>"
                + $"<img id=\"s\" src=\"/camera/{cameraId}/stream\" alt=\"{safeName}\">"
                + "<script>"
                + "var img=document.getElementById('s'),base='/camera/" + cameraId + "/stream',offAt=0;"
                + "img.onerror=function(){"
                + "if(!offAt)offAt=Date.now();"
                + "if(Date.now()-offAt>=5000){img.src='/images/los.png';}"
                + "else{setTimeout(function(){img.src=base+'?r='+Date.now();},2000);}"
                + "};"
                + "img.onload=function(){"
                + "if(img.src.indexOf(base)>=0){offAt=0;}"
                + "else if(offAt){setTimeout(function(){img.src=base+'?r='+Date.now();},2000);}"
                + "};"
                + "setInterval(function(){"
                + "if(img.src.indexOf('/images/los.png')>=0){"
                + "fetch('/camera/" + cameraId + "/status').then(function(r){if(r.ok)location.reload();}).catch(function(){}); return;"
                + "}"
                + "fetch('/camera/" + cameraId + "/status').then(function(r){"
                + "if(r.status===404){"
                + "if(!offAt)offAt=Date.now();"
                + "if(Date.now()-offAt>=5000)img.src='/images/los.png';"
                + "}else if(r.ok){offAt=0;}"
                + "}).catch(function(){});"
                + "},1000);"
                + "</script>"
                + "</body></html>";

            ServeText(ctx, html, "text/html");
        }

        private static void ServeSnapshot(HttpListenerContext ctx, CameraStreamState state)
        {
            state.MarkSnapshotInterest();

            byte[] jpeg;
            lock (state.JpegLock)
                jpeg = state.LatestJpeg;

            if (jpeg == null)
            {
                ServeError(ctx, 503, "No frame available yet");
                return;
            }

            ctx.Response.ContentType = "image/jpeg";
            ctx.Response.ContentLength64 = jpeg.Length;
            ctx.Response.Headers.Add("Cache-Control", "no-cache");
            ctx.Response.OutputStream.Write(jpeg, 0, jpeg.Length);
            ctx.Response.Close();
        }

        private static void ServeMjpeg(HttpListenerContext ctx, CameraStreamState state)
        {
            const string boundary = "jrtiboundary";
            ctx.Response.ContentType = $"multipart/x-mixed-replace; boundary={boundary}";
            ctx.Response.SendChunked = true;

            var clientId = Guid.NewGuid();
            var slot = new LatestFrameSlot();
            state.MjpegClients[clientId] = slot;

            try
            {
                var outStream = ctx.Response.OutputStream;
                var boundaryBytes = Encoding.ASCII.GetBytes($"--{boundary}\r\n");
                var crlf = Encoding.ASCII.GetBytes("\r\n");
                var headerPrefix = Encoding.ASCII.GetBytes("Content-Type: image/jpeg\r\nContent-Length: ");
                var headerSuffix = Encoding.ASCII.GetBytes("\r\n\r\n");

                while (true)
                {
                    var jpeg = slot.Take(30_000);
                    if (jpeg == null)
                        break;

                    var lengthBytes = Encoding.ASCII.GetBytes(jpeg.Length.ToString());

                    outStream.Write(boundaryBytes, 0, boundaryBytes.Length);
                    outStream.Write(headerPrefix, 0, headerPrefix.Length);
                    outStream.Write(lengthBytes, 0, lengthBytes.Length);
                    outStream.Write(headerSuffix, 0, headerSuffix.Length);
                    outStream.Write(jpeg, 0, jpeg.Length);
                    outStream.Write(crlf, 0, crlf.Length);
                    outStream.Flush();
                }
            }
            catch { }
            finally
            {
                state.MjpegClients.TryRemove(clientId, out _);
                slot.Dispose();
                try { ctx.Response.Close(); } catch { }
            }
        }

        private static void ServeText(HttpListenerContext ctx, string text, string contentType)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            ctx.Response.ContentType = contentType + "; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static void ServeError(HttpListenerContext ctx, int code, string message)
        {
            ctx.Response.StatusCode = code;
            ServeText(ctx, message, "text/plain");
        }

        private static string EscapeJson(string s)
            => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")
                .Replace("\r", "\\r").Replace("\t", "\\t");

        private static string EscapeHtml(string s)
            => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

        internal sealed class LatestFrameSlot : IDisposable
        {
            private byte[] _frame;
            private readonly ManualResetEventSlim _signal = new ManualResetEventSlim(false);
            private volatile bool _disposed;

            public void Push(byte[] jpeg)
            {
                if (_disposed) return;
                Interlocked.Exchange(ref _frame, jpeg);
                _signal.Set();
            }

            public byte[] Take(int timeoutMs)
            {
                if (_disposed) return null;
                if (!_signal.Wait(timeoutMs)) return null;
                _signal.Reset();
                return Interlocked.Exchange(ref _frame, null);
            }

            public void Dispose()
            {
                _disposed = true;
                _signal.Set();
            }
        }

        internal sealed class CameraStreamState : IDisposable
        {
            private const float SnapshotInterestDuration = 5f;

            public byte[] LatestJpeg;
            public readonly object JpegLock = new object();

            private volatile float _lastSnapshotInterest;

            public readonly ConcurrentDictionary<Guid, LatestFrameSlot> MjpegClients
                = new ConcurrentDictionary<Guid, LatestFrameSlot>();

            public int MjpegClientCount => MjpegClients.Count;

            public bool HasActiveClients =>
                MjpegClients.Count > 0
                || (Time.unscaledTime - _lastSnapshotInterest < SnapshotInterestDuration);

            public void MarkSnapshotInterest()
            {
                _lastSnapshotInterest = Time.unscaledTime;
            }

            public void PushFrame(byte[] jpeg)
            {
                lock (JpegLock)
                    LatestJpeg = jpeg;

                foreach (var kv in MjpegClients)
                    kv.Value.Push(jpeg);
            }

            public void Dispose()
            {
                foreach (var kv in MjpegClients)
                    kv.Value.Dispose();
            }
        }
    }
}