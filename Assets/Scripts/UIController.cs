// UI Controller - Floating spatial UI panel for NDI stream controls
// Provides source selection, connect button, SBS toggle, and status display.
// Designed for VR readability with large fonts and clear contrast.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace NDIViewer
{
    /// <summary>
    /// Manages the floating control panel UI in 3D space.
    /// Handles NDI source list, connection controls, SBS toggle, and status display.
    /// Shows clear status messages when NDI library is unavailable.
    /// </summary>
    public class UIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NDISourceDiscovery sourceDiscovery;
        [SerializeField] private NDIReceiver receiver;
        [SerializeField] private NDIVideoDisplay videoDisplay;
        [SerializeField] private SpatialWindowController windowController;

        [Header("UI Elements")]
        [SerializeField] private TMP_Dropdown sourceDropdown;
        [SerializeField] private Button connectButton;
        [SerializeField] private TMP_Text connectButtonText;
        [SerializeField] private Toggle sbsToggle;
        [SerializeField] private TMP_Text sbsToggleLabel;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text resolutionText;
        [SerializeField] private TMP_Text fpsText;
        [SerializeField] private Button resetPositionButton;
        [SerializeField] private Image connectionIndicator;

        [Header("Status Colors")]
        [SerializeField] private Color connectedColor = new Color(0.2f, 0.8f, 0.2f);
        [SerializeField] private Color connectingColor = new Color(0.9f, 0.7f, 0.1f);
        [SerializeField] private Color disconnectedColor = new Color(0.5f, 0.5f, 0.5f);
        [SerializeField] private Color errorColor = new Color(0.9f, 0.2f, 0.2f);
        [SerializeField] private Color testPatternColor = new Color(0.9f, 0.6f, 0.1f);

        /// <summary>
        /// Wire component references at runtime. Replaces reflection-based wiring
        /// with an explicit API that is safe against field renames.
        /// </summary>
        public void SetReferences(
            NDISourceDiscovery discovery, NDIReceiver recv,
            NDIVideoDisplay display, SpatialWindowController window,
            UIPanelBuilder builder)
        {
            sourceDiscovery = discovery;
            receiver = recv;
            videoDisplay = display;
            windowController = window;

            if (builder != null)
            {
                sourceDropdown = builder.SourceDropdown;
                connectButton = builder.ConnectButton;
                connectButtonText = builder.ConnectButtonText;
                sbsToggle = builder.SBSToggle;
                sbsToggleLabel = builder.SBSToggleLabel;
                statusText = builder.StatusText;
                resolutionText = builder.ResolutionText;
                fpsText = builder.FpsText;
                resetPositionButton = builder.ResetPositionButton;
                connectionIndicator = builder.ConnectionIndicator;
            }

            ValidateReferences();
        }

        private void ValidateReferences()
        {
            if (sourceDiscovery == null) Debug.LogError("[UIController] sourceDiscovery is null after wiring.");
            if (receiver == null) Debug.LogError("[UIController] receiver is null after wiring.");
            if (videoDisplay == null) Debug.LogError("[UIController] videoDisplay is null after wiring.");
            if (sourceDropdown == null) Debug.LogError("[UIController] sourceDropdown is null after wiring.");
            if (connectButton == null) Debug.LogError("[UIController] connectButton is null after wiring.");
            if (statusText == null) Debug.LogError("[UIController] statusText is null after wiring.");
        }

        private List<NDISourceDiscovery.DiscoveredSource> _currentSources =
            new List<NDISourceDiscovery.DiscoveredSource>();
        private bool _isConnected;
        private bool _isConnecting;
        private bool _ndiUnavailable;
        private string _previousSelectedUrl; // Track by URL for unique source matching

        private void Awake()
        {
            // Wire up button/toggle events
            if (connectButton != null)
                connectButton.onClick.AddListener(OnConnectButtonClicked);

            if (sbsToggle != null)
                sbsToggle.onValueChanged.AddListener(OnSBSToggleChanged);

            if (resetPositionButton != null)
                resetPositionButton.onClick.AddListener(OnResetPositionClicked);
        }

        private void OnEnable()
        {
            // Subscribe to events
            if (sourceDiscovery != null)
            {
                sourceDiscovery.OnSourcesUpdated += OnSourcesUpdated;
                sourceDiscovery.OnNDILibraryUnavailable += OnNDILibraryUnavailable;
            }

            if (receiver != null)
            {
                receiver.OnConnectionStateChanged += OnConnectionStateChanged;
                receiver.OnVideoFrameReceived += OnVideoFrameReceived;
            }

            UpdateUI();
        }

        private void OnDisable()
        {
            if (sourceDiscovery != null)
            {
                sourceDiscovery.OnSourcesUpdated -= OnSourcesUpdated;
                sourceDiscovery.OnNDILibraryUnavailable -= OnNDILibraryUnavailable;
            }

            if (receiver != null)
            {
                receiver.OnConnectionStateChanged -= OnConnectionStateChanged;
                receiver.OnVideoFrameReceived -= OnVideoFrameReceived;
            }
        }

        private void Update()
        {
            // Update FPS display
            if (fpsText != null && receiver != null && _isConnected)
            {
                fpsText.text = $"FPS: {receiver.CurrentFps:F1}";
            }

            // Check if NDI became unavailable after OnEnable (late initialization)
            if (!_ndiUnavailable && sourceDiscovery != null && sourceDiscovery.IsNDIUnavailable)
            {
                OnNDILibraryUnavailable(sourceDiscovery.NDIUnavailableReason);
            }
        }

        // ─── Event Handlers ───────────────────────────────────────────

        private void OnSourcesUpdated(List<NDISourceDiscovery.DiscoveredSource> sources)
        {
            _currentSources = sources;
            UpdateSourceDropdown();
        }

        private void OnConnectionStateChanged(NDIReceiver.ConnectionState state)
        {
            _isConnected = state == NDIReceiver.ConnectionState.Connected;
            _isConnecting = state == NDIReceiver.ConnectionState.Connecting;
            UpdateStatusDisplay(state);
            UpdateConnectButton(state);
            UpdateDropdownInteractable();
        }

        private void OnVideoFrameReceived(Texture2D texture, NDIReceiver.FrameInfo info)
        {
            if (resolutionText != null)
            {
                resolutionText.text = $"{info.Width} x {info.Height}";
            }

            // Forward to video display
            if (videoDisplay != null)
            {
                videoDisplay.UpdateVideoFrame(texture, info);
            }
        }

        /// <summary>
        /// Called when the NDI native library is not available.
        /// Disables NDI controls and shows test pattern with status message.
        /// </summary>
        private void OnNDILibraryUnavailable(string reason)
        {
            _ndiUnavailable = true;
            Debug.LogWarning($"[UI] NDI library unavailable: {reason}");

            // Update UI to reflect NDI unavailability
            if (statusText != null)
                statusText.text = "NDI Library Missing";
            SetIndicatorColor(testPatternColor);

            if (connectButton != null)
                connectButton.interactable = false;
            if (connectButtonText != null)
                connectButtonText.text = "NDI Unavailable";

            if (sourceDropdown != null)
            {
                sourceDropdown.ClearOptions();
                sourceDropdown.AddOptions(new List<string> { "NDI lib not found" });
                sourceDropdown.interactable = false;
            }

            if (resolutionText != null)
                resolutionText.text = $"{TestPatternGenerator.PATTERN_WIDTH}x{TestPatternGenerator.PATTERN_HEIGHT} (test)";
            if (fpsText != null)
                fpsText.text = "Test Pattern";

            // Switch video display to test pattern mode
            if (videoDisplay != null)
                videoDisplay.ShowTestPattern();
        }

        private void OnConnectButtonClicked()
        {
            if (_ndiUnavailable || _isConnecting) return;

            if (_isConnected)
            {
                // Disconnect
                receiver?.Disconnect();
                videoDisplay?.ShowPlaceholder();
            }
            else
            {
                // Connect to selected source
                if (sourceDropdown == null || sourceDropdown.value < 0 ||
                    sourceDropdown.value >= _currentSources.Count)
                {
                    SetStatusError("No NDI source selected");
                    return;
                }

                var selectedSource = _currentSources[sourceDropdown.value];
                receiver?.Connect(selectedSource);
            }
        }

        private void OnSBSToggleChanged(bool isOn)
        {
            if (videoDisplay != null)
            {
                videoDisplay.SetSBSMode(isOn);
            }

            if (sbsToggleLabel != null)
            {
                sbsToggleLabel.text = isOn ? "SBS 3D: ON" : "SBS 3D: OFF";
            }
        }

        private void OnResetPositionClicked()
        {
            windowController?.ResetToDefaultPosition();
        }

        // ─── UI Update Methods ────────────────────────────────────────

        private void UpdateSourceDropdown()
        {
            if (sourceDropdown == null) return;

            // Remember current selection by URL (unique) for stable re-selection
            int previousSelection = sourceDropdown.value;
            if (previousSelection >= 0 && previousSelection < _currentSources.Count)
                _previousSelectedUrl = _currentSources[previousSelection].Url;

            sourceDropdown.ClearOptions();

            if (_currentSources.Count == 0)
            {
                sourceDropdown.AddOptions(new List<string> { "Scanning for NDI sources..." });
                sourceDropdown.interactable = false;
                return;
            }

            sourceDropdown.interactable = !_isConnected && !_isConnecting;
            var options = new List<string>();
            int newSelection = 0;

            for (int i = 0; i < _currentSources.Count; i++)
            {
                options.Add(_currentSources[i].Name);
                // Match on URL for uniqueness (two sources can share a name)
                if (_currentSources[i].Url == _previousSelectedUrl)
                    newSelection = i;
            }

            sourceDropdown.AddOptions(options);
            sourceDropdown.value = newSelection;
        }

        private void UpdateStatusDisplay(NDIReceiver.ConnectionState state)
        {
            if (statusText == null) return;

            switch (state)
            {
                case NDIReceiver.ConnectionState.Connected:
                    statusText.text = "Connected";
                    SetIndicatorColor(connectedColor);
                    break;

                case NDIReceiver.ConnectionState.Connecting:
                    statusText.text = "Connecting...";
                    SetIndicatorColor(connectingColor);
                    break;

                case NDIReceiver.ConnectionState.Disconnected:
                    statusText.text = "Disconnected";
                    SetIndicatorColor(disconnectedColor);
                    ClearFrameInfo();
                    break;

                case NDIReceiver.ConnectionState.Error:
                    statusText.text = "Connection Error";
                    SetIndicatorColor(errorColor);
                    ClearFrameInfo();
                    break;
            }
        }

        private void UpdateConnectButton(NDIReceiver.ConnectionState state)
        {
            if (connectButtonText == null) return;

            bool isConnecting = state == NDIReceiver.ConnectionState.Connecting;
            bool isActive = state == NDIReceiver.ConnectionState.Connected || isConnecting;

            connectButtonText.text = isConnecting ? "Connecting..." :
                                     isActive ? "Disconnect" : "Connect";

            if (connectButton != null)
                connectButton.interactable = !isConnecting && !_ndiUnavailable &&
                    (isActive || _currentSources.Count > 0);
        }

        private void UpdateDropdownInteractable()
        {
            if (sourceDropdown == null || _ndiUnavailable) return;

            sourceDropdown.interactable = !_isConnected && !_isConnecting &&
                                          _currentSources.Count > 0;
        }

        private void SetStatusError(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
                SetIndicatorColor(errorColor);
            }
        }

        private void SetIndicatorColor(Color color)
        {
            if (connectionIndicator != null)
                connectionIndicator.color = color;
        }

        private void ClearFrameInfo()
        {
            if (resolutionText != null) resolutionText.text = "-- x --";
            if (fpsText != null) fpsText.text = "FPS: --";
        }

        private void UpdateUI()
        {
            if (sbsToggleLabel != null)
            {
                bool isOn = sbsToggle != null && sbsToggle.isOn;
                sbsToggleLabel.text = isOn ? "SBS 3D: ON" : "SBS 3D: OFF";
            }

            UpdateStatusDisplay(receiver?.State ?? NDIReceiver.ConnectionState.Disconnected);
            ClearFrameInfo();
        }
    }
}
