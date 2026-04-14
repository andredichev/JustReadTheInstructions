using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace JustReadTheInstructions
{
    public class CameraRenderDiagnostics : MonoBehaviour
    {
        private Camera _camera;
        private int _renderCount;
        private static readonly CameraEvent[] AllEvents =
            (CameraEvent[])Enum.GetValues(typeof(CameraEvent));

        void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        void OnPreRender()
        {
            _renderCount++;

            if (_renderCount != 1 && _renderCount != 10 && _renderCount != 60)
                return;

            Debug.Log($"[JRTI-RenderDiag]: === OnPreRender #{_renderCount} for {_camera.name} ===");
            Debug.Log($"[JRTI-RenderDiag]: name={_camera.name} depth={_camera.depth} depthTextureMode={_camera.depthTextureMode}");
            Debug.Log($"[JRTI-RenderDiag]: renderingPath={_camera.renderingPath} allowMSAA={_camera.allowMSAA}");
            Debug.Log($"[JRTI-RenderDiag]: targetTexture={(camera.targetTexture != null ? $"{_camera.targetTexture.width}x{_camera.targetTexture.height}" : "screen")}");

            int total = 0;
            foreach (var evt in AllEvents)
            {
                var bufs = _camera.GetCommandBuffers(evt);
                foreach (var buf in bufs)
                {
                    Debug.Log($"[JRTI-RenderDiag]: buffer '{buf.name}' at {evt}");
                    total++;
                }
            }

            if (total == 0)
                Debug.Log($"[JRTI-RenderDiag]: NO command buffers at render time");

            var components = _camera.gameObject.GetComponents<MonoBehaviour>();
            foreach (var c in components)
            {
                if (c == null) continue;
                var typeName = c.GetType().FullName;
                if (typeName.StartsWith("Scatterer") || typeName.StartsWith("TUFX") || typeName.StartsWith("Deferred"))
                    Debug.Log($"[JRTI-RenderDiag]: component {typeName} enabled={c.enabled}");
            }
        }

        private Camera camera => _camera;
    }
}