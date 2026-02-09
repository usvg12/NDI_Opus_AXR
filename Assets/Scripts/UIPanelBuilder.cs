// UI Panel Builder - Programmatically creates the floating control panel
// Builds Canvas, layout, buttons, dropdowns, toggles, and status displays.
// Designed for VR readability: large fonts, high contrast, clear labels.

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

namespace NDIViewer
{
    /// <summary>
    /// Creates the entire floating UI panel programmatically at runtime.
    /// This avoids dependency on serialized scene/prefab assets.
    /// The panel is grabbable and repositionable in 3D space.
    /// </summary>
    public class UIPanelBuilder : MonoBehaviour
    {
        [Header("Panel Settings")]
        [SerializeField] private float panelWidth = 0.6f;
        [SerializeField] private float panelHeight = 0.45f;
        [SerializeField] private float panelDistance = 1.5f;
        [SerializeField] private float panelOffsetRight = 0.8f;
        [SerializeField] private float panelOffsetDown = 0.2f;

        [Header("Theme")]
        [SerializeField] private Color panelBackground = new Color(0.08f, 0.08f, 0.12f, 0.92f);
        [SerializeField] private Color buttonColor = new Color(0.2f, 0.45f, 0.8f, 1f);
        [SerializeField] private Color buttonTextColor = Color.white;
        [SerializeField] private Color labelColor = new Color(0.85f, 0.85f, 0.9f, 1f);
        [SerializeField] private Color accentColor = new Color(0.3f, 0.75f, 0.95f, 1f);

        // Built references (exposed for UIController to use)
        [HideInInspector] public TMP_Dropdown SourceDropdown;
        [HideInInspector] public Button ConnectButton;
        [HideInInspector] public TMP_Text ConnectButtonText;
        [HideInInspector] public Toggle SBSToggle;
        [HideInInspector] public TMP_Text SBSToggleLabel;
        [HideInInspector] public TMP_Text StatusText;
        [HideInInspector] public TMP_Text ResolutionText;
        [HideInInspector] public TMP_Text FpsText;
        [HideInInspector] public Button ResetPositionButton;
        [HideInInspector] public Image ConnectionIndicator;
        [HideInInspector] public Canvas PanelCanvas;

        private const int FONT_SIZE_TITLE = 24;
        private const int FONT_SIZE_LABEL = 18;
        private const int FONT_SIZE_BUTTON = 20;
        private const int FONT_SIZE_STATUS = 16;

        /// <summary>Build the complete UI panel. Call from AppController.Start().</summary>
        public GameObject Build()
        {
            // Root panel object
            var panelRoot = new GameObject("UI_ControlPanel");
            panelRoot.transform.SetParent(transform, false);

            // Position panel to the right and slightly below center
            var cam = Camera.main?.transform;
            if (cam != null)
            {
                Vector3 forward = cam.forward;
                forward.y = 0;
                forward.Normalize();
                Vector3 right = cam.right;

                panelRoot.transform.position = cam.position
                    + forward * panelDistance
                    + right * panelOffsetRight
                    + Vector3.down * panelOffsetDown;
                panelRoot.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }

            // Make panel grabbable
            var rb = panelRoot.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var grabInteractable = panelRoot.AddComponent<XRGrabInteractable>();
            grabInteractable.movementType = XRGrabInteractable.MovementType.VelocityTracking;
            grabInteractable.throwOnDetach = false;
            grabInteractable.useDynamicAttach = true;

            // Add box collider for grab detection
            var collider = panelRoot.AddComponent<BoxCollider>();
            collider.size = new Vector3(panelWidth, panelHeight, 0.02f);

            // World-space canvas
            var canvasGO = new GameObject("Canvas");
            canvasGO.transform.SetParent(panelRoot.transform, false);

            PanelCanvas = canvasGO.AddComponent<Canvas>();
            PanelCanvas.renderMode = RenderMode.WorldSpace;

            var rectTransform = canvasGO.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(panelWidth * 1000, panelHeight * 1000);
            rectTransform.localScale = Vector3.one * 0.001f; // 1mm per pixel
            rectTransform.localPosition = Vector3.zero;

            // Canvas scaler and raycaster for XR UI interaction
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10;

            // TrackedDeviceGraphicRaycaster handles XR controller/hand raycasting.
            // Standard GraphicRaycaster is intentionally omitted to avoid duplicate
            // event processing that causes double-tap and missed input issues in XR.
            canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();

            // Background panel
            var bgImage = canvasGO.AddComponent<Image>();
            bgImage.color = panelBackground;

            // Build layout
            BuildLayout(canvasGO);

            return panelRoot;
        }

        private void BuildLayout(GameObject canvas)
        {
            var canvasRect = canvas.GetComponent<RectTransform>();

            // Vertical layout group
            var layoutGO = CreateChild(canvas, "Layout");
            var layoutRect = layoutGO.GetComponent<RectTransform>();
            StretchToParent(layoutRect);
            layoutRect.offsetMin = new Vector2(20, 15);
            layoutRect.offsetMax = new Vector2(-20, -15);

            var vlg = layoutGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12;
            vlg.padding = new RectOffset(0, 0, 0, 0);
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // ─── Title ────────────────────────────────────────────────
            var titleText = CreateText(layoutGO, "Title", "NDI XR Viewer", FONT_SIZE_TITLE, accentColor);
            SetPreferredHeight(titleText.gameObject, 32);

            // ─── Source Dropdown ───────────────────────────────────────
            var sourceRow = CreateRow(layoutGO, "SourceRow", 42);

            var sourceLabel = CreateText(sourceRow, "Label", "Source:", FONT_SIZE_LABEL, labelColor);
            var sourceLabelLE = sourceLabel.gameObject.AddComponent<LayoutElement>();
            sourceLabelLE.preferredWidth = 100;
            sourceLabelLE.flexibleWidth = 0;

            SourceDropdown = CreateDropdown(sourceRow, "SourceDropdown");
            var ddLE = SourceDropdown.gameObject.AddComponent<LayoutElement>();
            ddLE.flexibleWidth = 1;
            ddLE.preferredHeight = 40;

            // ─── Connect Button ───────────────────────────────────────
            ConnectButton = CreateButton(layoutGO, "ConnectButton", "Connect", 44);
            ConnectButtonText = ConnectButton.GetComponentInChildren<TMP_Text>();

            // ─── SBS Toggle Row ───────────────────────────────────────
            var sbsRow = CreateRow(layoutGO, "SBSRow", 40);

            SBSToggle = CreateToggle(sbsRow, "SBSToggle");
            var toggleLE = SBSToggle.gameObject.AddComponent<LayoutElement>();
            toggleLE.preferredWidth = 60;
            toggleLE.flexibleWidth = 0;

            SBSToggleLabel = CreateText(sbsRow, "SBSLabel", "SBS 3D: OFF", FONT_SIZE_LABEL, labelColor);
            SBSToggleLabel.alignment = TextAlignmentOptions.Left;

            // ─── Status Row ───────────────────────────────────────────
            var statusRow = CreateRow(layoutGO, "StatusRow", 30);

            // Connection indicator dot
            var indicatorGO = CreateChild(statusRow, "Indicator");
            ConnectionIndicator = indicatorGO.AddComponent<Image>();
            ConnectionIndicator.color = new Color(0.5f, 0.5f, 0.5f);
            var indicatorLE = indicatorGO.AddComponent<LayoutElement>();
            indicatorLE.preferredWidth = 16;
            indicatorLE.preferredHeight = 16;
            indicatorLE.flexibleWidth = 0;

            StatusText = CreateText(statusRow, "Status", "Disconnected", FONT_SIZE_STATUS, labelColor);

            // ─── Info Row ─────────────────────────────────────────────
            var infoRow = CreateRow(layoutGO, "InfoRow", 26);

            ResolutionText = CreateText(infoRow, "Resolution", "-- x --", FONT_SIZE_STATUS, labelColor);
            FpsText = CreateText(infoRow, "FPS", "FPS: --", FONT_SIZE_STATUS, labelColor);

            // ─── Reset Position Button ────────────────────────────────
            ResetPositionButton = CreateButton(layoutGO, "ResetButton", "Reset Window", 36);
            var resetColors = ResetPositionButton.colors;
            resetColors.normalColor = new Color(0.3f, 0.3f, 0.35f, 1f);
            ResetPositionButton.colors = resetColors;
        }

        // ─── Factory Helpers ──────────────────────────────────────────

        private GameObject CreateChild(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private GameObject CreateRow(GameObject parent, string name, float height)
        {
            var row = CreateChild(parent, name);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            SetPreferredHeight(row, height);
            return row;
        }

        private TMP_Text CreateText(GameObject parent, string name, string text,
            int fontSize, Color color)
        {
            var go = CreateChild(parent, name);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.enableAutoSizing = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }

        private Button CreateButton(GameObject parent, string name, string label, float height)
        {
            var go = CreateChild(parent, name);
            SetPreferredHeight(go, height);

            var image = go.AddComponent<Image>();
            image.color = buttonColor;

            // Rounded corners via sprite if available, otherwise solid color
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1;

            var button = go.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = buttonColor;
            colors.highlightedColor = buttonColor * 1.2f;
            colors.pressedColor = buttonColor * 0.8f;
            button.colors = colors;

            var textGO = CreateChild(go, "Text");
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = FONT_SIZE_BUTTON;
            tmp.color = buttonTextColor;
            tmp.alignment = TextAlignmentOptions.Center;
            StretchToParent(textGO.GetComponent<RectTransform>());

            return button;
        }

        private TMP_Dropdown CreateDropdown(GameObject parent, string name)
        {
            var go = CreateChild(parent, name);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.15f, 0.15f, 0.2f, 1f);

            var dropdown = go.AddComponent<TMP_Dropdown>();

            // Label
            var labelGO = CreateChild(go, "Label");
            var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
            labelTMP.fontSize = FONT_SIZE_LABEL;
            labelTMP.color = labelColor;
            labelTMP.alignment = TextAlignmentOptions.Left;
            var labelRect = labelGO.GetComponent<RectTransform>();
            StretchToParent(labelRect);
            labelRect.offsetMin = new Vector2(10, 0);
            labelRect.offsetMax = new Vector2(-30, 0);

            dropdown.captionText = labelTMP;

            // Template (dropdown list)
            var templateGO = CreateChild(go, "Template");
            templateGO.SetActive(false);
            var templateRect = templateGO.GetComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0, 0);
            templateRect.anchorMax = new Vector2(1, 0);
            templateRect.pivot = new Vector2(0.5f, 1);
            templateRect.sizeDelta = new Vector2(0, 200);

            var templateImage = templateGO.AddComponent<Image>();
            templateImage.color = new Color(0.12f, 0.12f, 0.16f, 0.98f);

            var scrollRect = templateGO.AddComponent<ScrollRect>();

            // Viewport
            var viewportGO = CreateChild(templateGO, "Viewport");
            var viewportRect = viewportGO.GetComponent<RectTransform>();
            StretchToParent(viewportRect);
            viewportGO.AddComponent<Mask>();
            viewportGO.AddComponent<Image>().color = Color.clear;

            // Content
            var contentGO = CreateChild(viewportGO, "Content");
            var contentRect = contentGO.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 40);

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;

            // Item template
            var itemGO = CreateChild(contentGO, "Item");
            var itemRect = itemGO.GetComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0, 0.5f);
            itemRect.anchorMax = new Vector2(1, 0.5f);
            itemRect.sizeDelta = new Vector2(0, 40);

            var itemToggle = itemGO.AddComponent<Toggle>();

            var itemLabelGO = CreateChild(itemGO, "Item Label");
            var itemLabelTMP = itemLabelGO.AddComponent<TextMeshProUGUI>();
            itemLabelTMP.fontSize = FONT_SIZE_LABEL;
            itemLabelTMP.color = labelColor;
            var itemLabelRect = itemLabelGO.GetComponent<RectTransform>();
            StretchToParent(itemLabelRect);
            itemLabelRect.offsetMin = new Vector2(10, 0);

            dropdown.template = templateRect;
            dropdown.itemText = itemLabelTMP;

            // Arrow indicator
            var arrowGO = CreateChild(go, "Arrow");
            var arrowTMP = arrowGO.AddComponent<TextMeshProUGUI>();
            arrowTMP.text = "\u25BC"; // Down arrow
            arrowTMP.fontSize = 14;
            arrowTMP.color = labelColor;
            arrowTMP.alignment = TextAlignmentOptions.Center;
            var arrowRect = arrowGO.GetComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(1, 0);
            arrowRect.anchorMax = new Vector2(1, 1);
            arrowRect.pivot = new Vector2(1, 0.5f);
            arrowRect.sizeDelta = new Vector2(30, 0);
            arrowRect.anchoredPosition = new Vector2(-5, 0);

            return dropdown;
        }

        private Toggle CreateToggle(GameObject parent, string name)
        {
            var go = CreateChild(parent, name);

            var toggle = go.AddComponent<Toggle>();
            toggle.isOn = false;

            // Background
            var bgGO = CreateChild(go, "Background");
            var bgImage = bgGO.AddComponent<Image>();
            bgImage.color = new Color(0.25f, 0.25f, 0.3f, 1f);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.sizeDelta = new Vector2(50, 28);
            bgRect.anchorMin = new Vector2(0, 0.5f);
            bgRect.anchorMax = new Vector2(0, 0.5f);
            bgRect.pivot = new Vector2(0, 0.5f);

            // Checkmark
            var checkGO = CreateChild(bgGO, "Checkmark");
            var checkImage = checkGO.AddComponent<Image>();
            checkImage.color = accentColor;
            var checkRect = checkGO.GetComponent<RectTransform>();
            checkRect.sizeDelta = new Vector2(22, 22);
            checkRect.anchorMin = new Vector2(0.5f, 0.5f);
            checkRect.anchorMax = new Vector2(0.5f, 0.5f);

            toggle.graphic = checkImage;
            toggle.targetGraphic = bgImage;

            return toggle;
        }

        private void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void SetPreferredHeight(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
        }
    }
}
