using KSP.UI.Screens;
using System.Globalization;
using UnityEngine;

namespace JustReadTheInstructions
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class JRTISettingsGUI : MonoBehaviour
    {
        public static JRTISettingsGUI Instance { get; private set; }

        private bool _isVisible;
        private bool _lastHotkeyState;
        private Rect _windowRect = new Rect(200, 100, 340, 300);
        private const int WindowId = 1902;

        private ApplicationLauncherButton _toolbarButton;
        private Texture2D _icon;

        private string _renderWidth;
        private string _renderHeight;
        private string _antiAliasing;
        private string _streamPort;
        private string _jpegQuality;
        private string _maxFps;
        private string _defaultFov;
        private string _maxOpenCameras;

        private GUIStyle _labelStyle;
        private GUIStyle _fieldStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _noteStyle;
        private bool _stylesInitialized;

        void Awake()
        {
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
            DontDestroyOnLoad(this);
        }

        void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnLauncherDestroyed);
        }

        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnLauncherDestroyed);
            RemoveToolbarButton();
        }

        private void OnLauncherReady()
        {
            if (_toolbarButton != null) return;

            if (_icon == null)
                _icon = GameDatabase.Instance.GetTexture("JustReadTheInstructions/Textures/icon", false);

            const ApplicationLauncher.AppScenes nonFlightScenes =
                ApplicationLauncher.AppScenes.SPACECENTER |
                ApplicationLauncher.AppScenes.VAB |
                ApplicationLauncher.AppScenes.SPH |
                ApplicationLauncher.AppScenes.TRACKSTATION |
                ApplicationLauncher.AppScenes.MAINMENU;

            _toolbarButton = ApplicationLauncher.Instance.AddModApplication(
                Toggle, Toggle,
                null, null, null, null,
                nonFlightScenes,
                _icon ?? Texture2D.whiteTexture
            );
        }

        private void OnLauncherDestroyed()
        {
            _toolbarButton = null;
        }

        private void RemoveToolbarButton()
        {
            if (_toolbarButton == null) return;
            if (ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(_toolbarButton);
            _toolbarButton = null;
        }

        public void Toggle()
        {
            if (!_isVisible)
                SyncFromSettings();
            _isVisible = !_isVisible;

            if (_toolbarButton != null)
            {
                if (_isVisible) _toolbarButton.SetTrue(false);
                else _toolbarButton.SetFalse(false);
            }
        }

        void Update()
        {
            bool hotkeyPressed =
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) &&
                Input.GetKey(KeyCode.F9);

            if (hotkeyPressed && !_lastHotkeyState)
                Toggle();

            _lastHotkeyState = hotkeyPressed;
        }

        void OnGUI()
        {
            if (!_isVisible) return;

            if (!_stylesInitialized)
                InitStyles();

            _windowRect = GUILayout.Window(WindowId, _windowRect, DrawWindow, "JRTI Settings  (Ctrl+Alt+F9)");
            ClampToScreen();
        }

        private void InitStyles()
        {
            var skin = HighLogic.Skin ?? GUI.skin;
            _labelStyle = new GUIStyle(skin.label) { fontSize = 11, normal = { textColor = Color.white } };
            _fieldStyle = new GUIStyle(skin.textField) { fontSize = 11 };
            _buttonStyle = new GUIStyle(skin.button) { fontSize = 11 };
            _headerStyle = new GUIStyle(skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.8f, 0.9f, 1f) }
            };
            _noteStyle = new GUIStyle(skin.label)
            {
                fontSize = 10,
                wordWrap = true,
                normal = { textColor = new Color(1f, 1f, 0.65f) }
            };
            _stylesInitialized = true;
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical(GUILayout.Width(320));

            GUILayout.Label("Rendering", _headerStyle);
            DrawField("Width", ref _renderWidth);
            DrawField("Height", ref _renderHeight);
            DrawField("Anti-Aliasing  (1/2/4/8)", ref _antiAliasing);
            DrawField("Default FOV", ref _defaultFov);
            DrawField("Max Open Cameras", ref _maxOpenCameras);

            GUILayout.Space(8);
            GUILayout.Label("Streaming", _headerStyle);
            DrawField("Port", ref _streamPort);
            DrawField("JPEG Quality  (1-100)", ref _jpegQuality);
            DrawField("Max FPS", ref _maxFps);

            GUILayout.Space(8);
            GUILayout.Label("Rendering resolution and AA apply on next launch.", _noteStyle);
            GUILayout.Label("Stream port change requires game restart.", _noteStyle);

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save", _buttonStyle)) ApplyAndSave();
            if (GUILayout.Button("Close", _buttonStyle)) Toggle();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawField(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _labelStyle, GUILayout.Width(200));
            value = GUILayout.TextField(value, _fieldStyle, GUILayout.Width(100));
            GUILayout.EndHorizontal();
        }

        private void ApplyAndSave()
        {
            if (int.TryParse(_renderWidth, out int w)) JRTISettings.RenderWidth = w;
            if (int.TryParse(_renderHeight, out int h)) JRTISettings.RenderHeight = h;
            if (int.TryParse(_antiAliasing, out int aa)) JRTISettings.AntiAliasing = aa;
            if (int.TryParse(_streamPort, out int port)) JRTISettings.StreamPort = port;
            if (int.TryParse(_jpegQuality, out int q)) JRTISettings.StreamJpegQuality = q;
            if (int.TryParse(_maxFps, out int fps)) JRTISettings.StreamMaxFps = fps;
            if (float.TryParse(_defaultFov, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                JRTISettings.DefaultFOV = f;
            if (uint.TryParse(_maxOpenCameras, out uint maxOpenCameras))
            {
                if (maxOpenCameras < 1u) maxOpenCameras = 1u;
                else if (maxOpenCameras > 64u) maxOpenCameras = 64u;
                JRTISettings.MaxOpenCameras = maxOpenCameras;
            }

            JRTISettings.Save();
            SyncFromSettings();
        }

        private void SyncFromSettings()
        {
            _renderWidth = JRTISettings.RenderWidth.ToString();
            _renderHeight = JRTISettings.RenderHeight.ToString();
            _antiAliasing = JRTISettings.AntiAliasing.ToString();
            _streamPort = JRTISettings.StreamPort.ToString();
            _jpegQuality = JRTISettings.StreamJpegQuality.ToString();
            _maxFps = JRTISettings.StreamMaxFps.ToString();
            _defaultFov = JRTISettings.DefaultFOV.ToString("F0");
            _maxOpenCameras = JRTISettings.MaxOpenCameras.ToString();
        }

        private void ClampToScreen()
        {
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);
        }
    }
}