using System;
using UnityEngine;

namespace JustReadTheInstructions
{
    public sealed class DockingCameraOverlay : IDisposable
    {
        private readonly DockingTelemetry _telemetry;

        private GameObject _rootGO;
        private Camera _camera;
        private TextMesh _textMesh;
        private TextMesh _shadowMesh;

        private const int Layer = 31;

        public DockingCameraOverlay(Vessel vessel, RenderTexture targetTexture, int instanceId)
        {
            _telemetry = new DockingTelemetry(vessel);
            Setup(targetTexture, instanceId);
        }

        public void Render(RenderTexture targetTexture)
        {
            _telemetry.Update();

            string text = _telemetry.OverlayText;
            if (string.IsNullOrEmpty(text))
                return;

            _textMesh.text = text;
            _shadowMesh.text = text;

            _camera.targetTexture = targetTexture;
            _textMesh.gameObject.SetActive(true);
            _camera.Render();
            _textMesh.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            if (_rootGO == null) return;
            UnityEngine.Object.Destroy(_rootGO);
            _rootGO = null;
            _camera = null;
            _textMesh = null;
            _shadowMesh = null;
        }

        private void Setup(RenderTexture targetTexture, int instanceId)
        {
            float w = JRTISettings.RenderWidth;
            float h = JRTISettings.RenderHeight;

            _rootGO = new GameObject("JRTI_DockingOverlay_" + instanceId);
            _rootGO.transform.position = new Vector3(0f, 0f, -100f);

            _camera = _rootGO.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = h * 0.5f;
            _camera.clearFlags = CameraClearFlags.Nothing;
            _camera.cullingMask = 1 << Layer;
            _camera.nearClipPlane = 0.3f;
            _camera.farClipPlane = 1000f;
            _camera.depth = 99f;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.targetTexture = targetTexture;
            _camera.enabled = false;

            SetupText(w, h, instanceId);
        }

        private void SetupText(float renderWidth, float renderHeight, int instanceId)
        {
            const float margin = 10f;
            float cropOffsetX = (JRTISettings.FixedPreviewAspectRatio && renderWidth > renderHeight)
                ? (renderWidth - renderHeight) * 0.5f
                : 0f;

            Font font = LoadFont();
            float charSize = (renderHeight / 720f) * 2.6f;
            float shadowOffset = charSize * 0.25f;

            var textGO = new GameObject("JRTI_DockingText_" + instanceId) { layer = Layer };
            textGO.transform.parent = _rootGO.transform;
            textGO.transform.localPosition = new Vector3(
                -renderWidth * 0.5f + cropOffsetX + margin,
                renderHeight * 0.5f - margin,
                100f);
            textGO.transform.localRotation = Quaternion.identity;
            textGO.transform.localScale = Vector3.one;

            var shadowGO = new GameObject("Shadow") { layer = Layer };
            shadowGO.transform.parent = textGO.transform;
            shadowGO.transform.localPosition = new Vector3(shadowOffset, -shadowOffset, 0.1f);
            shadowGO.transform.localRotation = Quaternion.identity;
            shadowGO.transform.localScale = Vector3.one;
            _shadowMesh = CreateTextMesh(shadowGO, font, charSize, Color.black);

            _textMesh = CreateTextMesh(textGO, font, charSize, Color.white);
        }

        private static Font LoadFont()
            => Font.CreateDynamicFontFromOSFont("Consolas", 100)
            ?? Font.CreateDynamicFontFromOSFont("Courier New", 100);

        private static TextMesh CreateTextMesh(GameObject go, Font font, float charSize, Color color)
        {
            var mesh = go.AddComponent<TextMesh>();
            mesh.anchor = TextAnchor.UpperLeft;
            mesh.alignment = TextAlignment.Left;
            mesh.fontSize = 100;
            mesh.characterSize = charSize;
            mesh.color = color;
            mesh.richText = false;
            mesh.text = string.Empty;

            if (font != null)
            {
                mesh.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }

            return mesh;
        }
    }

    public class DockingTelemetry
    {
        private readonly Vessel _vessel;

        public bool HasTargetData { get; private set; }
        public string OverlayText { get; private set; } = string.Empty;

        public DockingTelemetry(Vessel vessel)
        {
            _vessel = vessel ?? throw new ArgumentNullException(nameof(vessel));
        }

        public void Update()
        {
            if (_vessel != FlightGlobals.ActiveVessel)
            {
                HasTargetData = false;
                OverlayText = string.Empty;
                return;
            }

            HasTargetData = _vessel.targetObject is Vessel
                         || _vessel.targetObject is ModuleDockingNode;

            OverlayText = HasTargetData ? GetOverlayText() : string.Empty;
        }

        private string GetOverlayText()
        {
            string targetName = FlightGlobals.fetch.VesselTarget.GetName();
            Vessel targetVessel = _vessel.targetObject is Vessel v
                ? v
                : ((ModuleDockingNode)_vessel.targetObject).vessel;

            double ut = Planetarium.GetUniversalTime();
            Vector3d ownPos = _vessel.orbit.getRelativePositionAtUT(ut) + _vessel.orbit.referenceBody.position;
            Vector3d tgtPos = targetVessel.orbit.getRelativePositionAtUT(ut) + targetVessel.orbit.referenceBody.position;
            Vector3d losVector = tgtPos - ownPos;
            double distance = losVector.magnitude;
            Vector3d losDir = losVector.normalized;

            Vector3d relVel = FlightGlobals.ship_tgtVelocity;
            double rangeRate = Vector3d.Dot(relVel, losDir);
            double offAxisAngle = distance > 0.1 ? Vector3d.Angle(_vessel.ReferenceTransform.up, losDir) : 0.0;

            Transform refT = _vessel.ReferenceTransform;
            Vector3d approachAxis = refT.up;
            Vector3d lateralOffset = losVector - Vector3d.Dot(losVector, approachAxis) * approachAxis;
            double offsetLat = Vector3d.Dot(lateralOffset, refT.right);
            double offsetNrm = Vector3d.Dot(lateralOffset, refT.forward);
            double velClo = Vector3d.Dot(relVel, refT.up);
            double velLat = Vector3d.Dot(relVel, refT.right);
            double velNrm = Vector3d.Dot(relVel, refT.forward);

            Quaternion relRotation = Quaternion.Inverse(refT.rotation) * targetVessel.ReferenceTransform.rotation;
            Vector3 euler = relRotation.eulerAngles;
            float pitch = NormalizeAngle(euler.x);
            float yaw   = NormalizeAngle(euler.y);
            float roll  = NormalizeAngle(euler.z);

            return
                $"TGT: {targetName}\n\n" +
                $"RANGE\n" +
                $"   DST: {FormatDistance(distance)}\n" +
                $"  RATE: {FormatRate(rangeRate)}\n" +
                $"   TTC: {FormatTTC(distance, rangeRate)}\n" +
                $"   ANG: {offAxisAngle:F1} DEG\n\n" +
                $"OFFSET (M)\n" +
                $"   LAT: {Sign(offsetLat)}{Math.Abs(offsetLat):F2}\n" +
                $"   NRM: {Sign(offsetNrm)}{Math.Abs(offsetNrm):F2}\n\n" +
                $"REL VEL (M/S)\n" +
                $"   CLO: {Sign(velClo)}{Math.Abs(velClo):F3}\n" +
                $"   LAT: {Sign(velLat)}{Math.Abs(velLat):F3}\n" +
                $"   NRM: {Sign(velNrm)}{Math.Abs(velNrm):F3}\n\n" +
                $"REL ATT (DEG)\n" +
                $"   PCH: {Sign(pitch)}{Math.Abs(pitch):F1}\n" +
                $"   YAW: {Sign(yaw)}{Math.Abs(yaw):F1}\n" +
                $"   ROL: {Sign(roll)}{Math.Abs(roll):F1}\n";
        }

        private static string FormatDistance(double meters)
            => meters >= 1000.0 ? $"{meters / 1000.0:F3} KM" : $"{meters:F2} M";

        private static string FormatRate(double rate)
            => $"{Sign(rate)}{Math.Abs(rate):F3} M/S";

        private static string FormatTTC(double distance, double rangeRate)
        {
            if (rangeRate <= 0.01) return "N/A";
            double ttcSec = distance / rangeRate;
            if (ttcSec >= 3600.0) return ">1H";
            return $"{(int)(ttcSec / 60):D2}:{(int)(ttcSec % 60):D2}";
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)  angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }

        private static string Sign(double v) => v >= 0 ? "+" : "-";
        private static string Sign(float v)  => v >= 0 ? "+" : "-";
    }
}
