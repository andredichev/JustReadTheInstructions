using System.Collections.Generic;
using HullcamVDS;
using KSP.UI.Screens;
using UnityEngine;

namespace JustReadTheInstructions
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class JRTIMainGUI : MonoBehaviour
    {
        private const string WindowTitle = "Just Read The Instructions";
        private const int WindowId = 1900;
        private const float WindowWidth = 300;
        private const float DraggableHeight = 30;
        private const float LeftIndent = 12;
        private const float ContentTop = 35;
        private const float EntryHeight = 22;
        private const float Margin = 5;
        private const float ContentWidth = WindowWidth - 2 * LeftIndent;
        private const float CameraListRefreshInterval = 1f;

        private static Texture2D _appIcon;
        private static ApplicationLauncherButton _toolbarButton;
        private static bool _hasAddedButton;

        private bool _isVisible;
        private bool _uiHidden;
        private Rect _windowRect;
        private float _windowHeight = 100;

        private GUIStyle _buttonStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _titleStyle;

        private List<MuMechModuleHullCamera> _cachedAllCameras = new List<MuMechModuleHullCamera>();
        private List<MuMechModuleHullCamera> _cachedAvailableCameras = new List<MuMechModuleHullCamera>();
        private float _lastCameraRefresh;

        void Start()
        {
            InitializeStyles();
            _windowRect = new Rect(Screen.width - WindowWidth - 50, 100, WindowWidth, _windowHeight);

            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);

            AddToolbarButton();
            Debug.Log("[JRTI]: Main GUI initialized");
        }

        void OnDestroy()
        {
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);
            RemoveToolbarButton();
        }

        void Update()
        {
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) &&
                Input.GetKeyDown(KeyCode.F7))
            {
                _isVisible = !_isVisible;
            }

            if (_isVisible && Time.unscaledTime - _lastCameraRefresh > CameraListRefreshInterval)
            {
                RefreshCameraList();
                _lastCameraRefresh = Time.unscaledTime;
            }
        }

        private void RefreshCameraList()
        {
            _cachedAllCameras = HullCameraManager.GetAllAvailableCameras();
            _cachedAvailableCameras.Clear();

            foreach (var camera in _cachedAllCameras)
            {
                if (camera == null || camera.vessel == null) continue;

                bool isOpen = HullCameraManager.Instance?.IsCameraOpen(camera) ?? false;
                bool isStreamOnly = HullCameraManager.Instance?.IsStreamOnly(camera) ?? false;

                if (!isOpen || isStreamOnly)
                    _cachedAvailableCameras.Add(camera);
            }
        }

        void OnGUI()
        {
            if (_isVisible && !_uiHidden)
            {
                _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, "");
                ClampToScreen();
            }
        }

        private void InitializeStyles()
        {
            _titleStyle = new GUIStyle(HighLogic.Skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold
            };

            _buttonStyle = new GUIStyle(HighLogic.Skin.button)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 5, 5)
            };

            _labelStyle = new GUIStyle(HighLogic.Skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.gray }
            };
        }

        private void DrawWindow(int windowId)
        {
            GUI.DragWindow(new Rect(0, 0, WindowWidth, DraggableHeight));

            int line = 0;

            GUI.Label(new Rect(0, 0, WindowWidth, 20), WindowTitle, _titleStyle);

            int openCount = HullCameraManager.Instance?.GetOpenCameraCount() ?? 0;
            GUI.Label(
                new Rect(LeftIndent, ContentTop - 15, ContentWidth, 15),
                $"Cameras: {openCount} open / {_cachedAllCameras.Count} total",
                _labelStyle
            );

            if (_cachedAvailableCameras.Count == 0)
            {
                line++;
                GUI.Label(
                    new Rect(LeftIndent, ContentTop + line * EntryHeight, ContentWidth, EntryHeight),
                    "No cameras available",
                    _labelStyle
                );
                line++;
            }
            else
            {
                foreach (var camera in _cachedAvailableCameras)
                {
                    line++;
                    DrawCameraRow(camera, line);
                }
            }

            line++;
            if (GUI.Button(
                new Rect(LeftIndent, ContentTop + line * EntryHeight, ContentWidth, EntryHeight),
                "Copy Index URL", _buttonStyle))
            {
                GUIUtility.systemCopyBuffer = $"http://localhost:{JRTISettings.StreamPort}/";
            }

            line++;
            GUI.Label(
                new Rect(LeftIndent, ContentTop + line * EntryHeight, ContentWidth, EntryHeight),
                $"localhost:{JRTISettings.StreamPort}",
                _labelStyle
            );

            if (openCount > 0)
            {
                line++;
                if (GUI.Button(
                    new Rect(LeftIndent, ContentTop + line * EntryHeight, ContentWidth, EntryHeight),
                    "Close All Cameras", _buttonStyle))
                {
                    HullCameraManager.Instance?.CloseAllCameras();
                }
            }

            line++;
            _windowHeight = ContentTop + (line + 1) * EntryHeight + Margin;
            _windowRect.height = _windowHeight;
        }

        private void DrawCameraRow(MuMechModuleHullCamera camera, int line)
        {
            if (camera == null || camera.vessel == null) return;

            int stableId = HullCameraRenderer.GetStableId(camera);
            string displayName = $"{camera.vessel.GetDisplayName()}.{camera.cameraName}";
            bool streamOnly = HullCameraManager.Instance?.IsStreamOnly(camera) ?? false;
            bool streaming = JRTIStreamServer.Instance?.IsStreaming(stableId) ?? false;

            float halfWidth = (ContentWidth - Margin) / 2f;
            Rect openRect = new Rect(LeftIndent, ContentTop + line * EntryHeight, halfWidth, EntryHeight);
            Rect streamRect = new Rect(LeftIndent + halfWidth + Margin, ContentTop + line * EntryHeight, halfWidth, EntryHeight);

            if (GUI.Button(openRect, displayName, _buttonStyle))
                HullCameraManager.Instance?.OpenCamera(camera);

            GUI.color = streaming ? Color.green : Color.white;

            if (streamOnly)
            {
                if (GUI.Button(streamRect, "■ Stop", _buttonStyle))
                    HullCameraManager.Instance?.StopStream(stableId);
            }
            else
            {
                if (GUI.Button(streamRect, streaming ? "● Stream" : "○ Stream", _buttonStyle))
                {
                    HullCameraManager.Instance?.StreamCamera(camera);
                    GUIUtility.systemCopyBuffer = $"http://localhost:{JRTISettings.StreamPort}/camera/{stableId}/stream";
                }
            }

            GUI.color = Color.white;
        }

        private void ClampToScreen()
        {
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);
        }

        private void AddToolbarButton()
        {
            if (_hasAddedButton) return;

            _appIcon = GameDatabase.Instance.GetTexture("JustReadTheInstructions/Textures/icon", false);

            if (_appIcon == null)
                _appIcon = Texture2D.whiteTexture;

            _toolbarButton = ApplicationLauncher.Instance.AddModApplication(
                OnToolbarButtonToggle,
                OnToolbarButtonToggle,
                null, null, null, null,
                ApplicationLauncher.AppScenes.FLIGHT,
                _appIcon
            );

            _hasAddedButton = true;
        }

        private void RemoveToolbarButton()
        {
            if (_toolbarButton != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(_toolbarButton);
                _toolbarButton = null;
                _hasAddedButton = false;
            }
        }

        private void OnToolbarButtonToggle() => _isVisible = !_isVisible;
        private void OnHideUI() => _uiHidden = true;
        private void OnShowUI() => _uiHidden = false;
    }
}