using System.Linq;
using UnityEngine;

namespace JustReadTheInstructions
{
    public class ScattererDepthRedirect : MonoBehaviour
    {
        private Camera _camera;
        private Camera _depthCam;
        private int _frameCount;

        void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        void OnPreRender()
        {
            _frameCount++;

            if (_depthCam == null)
            {
                TryFindDepthCamera();
                return;
            }

            var origParent = _depthCam.transform.parent;
            var origPos = _depthCam.transform.position;
            var origRot = _depthCam.transform.rotation;
            var origFOV = _depthCam.fieldOfView;
            var origNear = _depthCam.nearClipPlane;
            var origFar = _depthCam.farClipPlane;

            _depthCam.transform.parent = null;
            _depthCam.transform.SetPositionAndRotation(_camera.transform.position, _camera.transform.rotation);
            _depthCam.fieldOfView = _camera.fieldOfView;
            _depthCam.nearClipPlane = _camera.nearClipPlane;
            _depthCam.farClipPlane = _camera.farClipPlane;

            _depthCam.Render();

            _depthCam.transform.parent = origParent;
            _depthCam.transform.SetPositionAndRotation(origPos, origRot);
            _depthCam.fieldOfView = origFOV;
            _depthCam.nearClipPlane = origNear;
            _depthCam.farClipPlane = origFar;
        }

        private void TryFindDepthCamera()
        {
            if (_frameCount % 10 != 1)
                return;

            var allCams = Resources.FindObjectsOfTypeAll<Camera>();

            foreach (var cam in allCams)
            {
                if (cam.name == "ScattererPartialDepthBuffer")
                {
                    _depthCam = cam;
                    Debug.Log($"[JRTI-DepthRedirect]: Found ScattererPartialDepthBuffer " +
                              $"enabled={cam.enabled} depth={cam.depth} FOV={cam.fieldOfView} " +
                              $"targetTexture={(cam.targetTexture != null ? $"{cam.targetTexture.width}x{cam.targetTexture.height}" : "null")}");
                    return;
                }
            }

            if (_frameCount == 61)
            {
                var names = string.Join(", ", allCams.Select(c => $"{c.name}(enabled={c.enabled})"));
                Debug.Log($"[JRTI-DepthRedirect]: ScattererPartialDepthBuffer still not found at frame 60. All cameras: {names}");
            }
        }
    }
}