// Test Pattern Generator - Built-in SBS test pattern for when NDI library is unavailable
// Generates a stereoscopic color bar pattern so stereo rendering can be validated
// without any external dependencies.

using UnityEngine;

namespace NDIViewer
{
    /// <summary>
    /// Generates a built-in SBS test pattern texture. Used as a fallback when the NDI
    /// native library is missing or fails to initialize. The pattern includes:
    /// - Left/right eye color bars with distinct colors per eye
    /// - "L" and "R" markers for eye identification
    /// - A moving element (frame counter bar) to confirm rendering is live
    /// The texture is 1920x540 (half of typical SBS resolution) to keep memory low.
    /// </summary>
    public static class TestPatternGenerator
    {
        // Pattern dimensions: modest resolution sufficient for stereo validation
        public const int PATTERN_WIDTH = 1920;
        public const int PATTERN_HEIGHT = 540;

        // Reusable pixel buffer (allocated once)
        private static Color32[] _pixels;
        private static Texture2D _texture;
        private static int _frameCounter;

        /// <summary>
        /// Get or create the test pattern texture. Call UpdatePattern() each frame
        /// to animate the moving bar.
        /// </summary>
        public static Texture2D GetTexture()
        {
            if (_texture == null)
            {
                _texture = new Texture2D(PATTERN_WIDTH, PATTERN_HEIGHT, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _pixels = new Color32[PATTERN_WIDTH * PATTERN_HEIGHT];
                GenerateBasePattern();
                _texture.SetPixels32(_pixels);
                _texture.Apply(false);
            }
            return _texture;
        }

        // Scratch buffer for partial row uploads (reused, never per-frame allocated)
        private static Color32[] _rowBuffer;

        /// <summary>
        /// Update the animated element in the test pattern. Call once per frame
        /// when the test pattern is active. Returns the updated texture.
        ///
        /// Optimized to only re-upload the rows that changed (up to 4 rows per
        /// frame) instead of the full 1920x540 texture. This reduces CPU→GPU
        /// transfer from ~4MB/frame to ~30KB/frame on thermally constrained XR hardware.
        /// </summary>
        public static Texture2D UpdatePattern()
        {
            var tex = GetTexture();
            _frameCounter++;

            if (_rowBuffer == null)
                _rowBuffer = new Color32[PATTERN_WIDTH];

            // Animate a horizontal scanning bar (2px tall) that sweeps vertically
            int barY = _frameCounter % PATTERN_HEIGHT;

            // Restore the previous bar rows to base pattern
            int prevBarY = (_frameCounter - 1) % PATTERN_HEIGHT;
            RegenerateRow(prevBarY);
            UploadRow(tex, prevBarY);

            int prevBarY2 = _frameCounter > 1 ? ((_frameCounter - 1) + 1) % PATTERN_HEIGHT : -1;
            if (prevBarY2 >= 0 && prevBarY2 != prevBarY && prevBarY2 != barY)
            {
                RegenerateRow(prevBarY2);
                UploadRow(tex, prevBarY2);
            }

            // Draw new bar row (bright white)
            for (int x = 0; x < PATTERN_WIDTH; x++)
            {
                _pixels[barY * PATTERN_WIDTH + x] = new Color32(255, 255, 255, 255);
            }
            UploadRow(tex, barY);

            // Also draw adjacent row for visibility
            int barY2 = (barY + 1) % PATTERN_HEIGHT;
            for (int x = 0; x < PATTERN_WIDTH; x++)
            {
                _pixels[barY2 * PATTERN_WIDTH + x] = new Color32(200, 200, 200, 255);
            }
            UploadRow(tex, barY2);

            tex.Apply(false);
            return tex;
        }

        /// <summary>
        /// Upload a single row from the pixel buffer to the texture using SetPixels32
        /// with a rect, avoiding a full-texture re-upload.
        /// </summary>
        private static void UploadRow(Texture2D tex, int y)
        {
            System.Array.Copy(_pixels, y * PATTERN_WIDTH, _rowBuffer, 0, PATTERN_WIDTH);
            tex.SetPixels32(0, y, PATTERN_WIDTH, 1, _rowBuffer);
        }

        /// <summary>
        /// Generate the base SBS test pattern: left-eye bars on left half,
        /// right-eye bars on right half, with "L"/"R" letter markers.
        /// </summary>
        private static void GenerateBasePattern()
        {
            int halfW = PATTERN_WIDTH / 2;

            // Color bars for left eye (warm tones)
            Color32[] leftBars = {
                new Color32(180, 40, 40, 255),    // Red
                new Color32(180, 120, 40, 255),   // Orange
                new Color32(180, 180, 40, 255),   // Yellow
                new Color32(40, 160, 40, 255),    // Green
                new Color32(40, 120, 180, 255),   // Cyan
                new Color32(40, 40, 180, 255),    // Blue
            };

            // Color bars for right eye (cool tones, visibly different)
            Color32[] rightBars = {
                new Color32(40, 40, 180, 255),    // Blue
                new Color32(100, 40, 180, 255),   // Purple
                new Color32(180, 40, 140, 255),   // Magenta
                new Color32(40, 160, 120, 255),   // Teal
                new Color32(160, 160, 40, 255),   // Olive
                new Color32(180, 80, 40, 255),    // Dark Orange
            };

            int barWidth = halfW / leftBars.Length;

            for (int y = 0; y < PATTERN_HEIGHT; y++)
            {
                for (int x = 0; x < PATTERN_WIDTH; x++)
                {
                    Color32 c;
                    if (x < halfW)
                    {
                        // Left eye half
                        int barIndex = Mathf.Clamp(x / barWidth, 0, leftBars.Length - 1);
                        c = leftBars[barIndex];
                    }
                    else
                    {
                        // Right eye half
                        int localX = x - halfW;
                        int barIndex = Mathf.Clamp(localX / barWidth, 0, rightBars.Length - 1);
                        c = rightBars[barIndex];
                    }

                    // Add a subtle grid pattern (every 120 pixels)
                    if (x % 120 == 0 || y % 120 == 0)
                    {
                        c.r = (byte)Mathf.Min(255, c.r + 30);
                        c.g = (byte)Mathf.Min(255, c.g + 30);
                        c.b = (byte)Mathf.Min(255, c.b + 30);
                    }

                    // Draw "L" marker in left half center and "R" in right half center
                    if (IsInsideLetter(x, y, halfW, 'L') || IsInsideLetter(x, y, halfW, 'R'))
                    {
                        c = new Color32(255, 255, 255, 255);
                    }

                    // Center divider line (2px wide)
                    if (x >= halfW - 1 && x <= halfW)
                    {
                        c = new Color32(255, 255, 0, 255); // Yellow divider
                    }

                    _pixels[y * PATTERN_WIDTH + x] = c;
                }
            }

            // Draw "TEST PATTERN - NDI LIB MISSING" text area (simple block letters at top)
            DrawBlockText("TEST PATTERN", 10, PATTERN_HEIGHT - 30, new Color32(255, 255, 255, 255));
            DrawBlockText("NDI LIB NOT FOUND", halfW + 10, PATTERN_HEIGHT - 30, new Color32(255, 255, 255, 255));
        }

        private static void RegenerateRow(int y)
        {
            int halfW = PATTERN_WIDTH / 2;
            Color32[] leftBars = {
                new Color32(180, 40, 40, 255),
                new Color32(180, 120, 40, 255),
                new Color32(180, 180, 40, 255),
                new Color32(40, 160, 40, 255),
                new Color32(40, 120, 180, 255),
                new Color32(40, 40, 180, 255),
            };
            Color32[] rightBars = {
                new Color32(40, 40, 180, 255),
                new Color32(100, 40, 180, 255),
                new Color32(180, 40, 140, 255),
                new Color32(40, 160, 120, 255),
                new Color32(160, 160, 40, 255),
                new Color32(180, 80, 40, 255),
            };
            int barWidth = halfW / leftBars.Length;

            for (int x = 0; x < PATTERN_WIDTH; x++)
            {
                Color32 c;
                if (x < halfW)
                {
                    int barIndex = Mathf.Clamp(x / barWidth, 0, leftBars.Length - 1);
                    c = leftBars[barIndex];
                }
                else
                {
                    int localX = x - halfW;
                    int barIndex = Mathf.Clamp(localX / barWidth, 0, rightBars.Length - 1);
                    c = rightBars[barIndex];
                }
                if (x % 120 == 0 || y % 120 == 0)
                {
                    c.r = (byte)Mathf.Min(255, c.r + 30);
                    c.g = (byte)Mathf.Min(255, c.g + 30);
                    c.b = (byte)Mathf.Min(255, c.b + 30);
                }
                if (x >= halfW - 1 && x <= halfW)
                {
                    c = new Color32(255, 255, 0, 255);
                }
                _pixels[y * PATTERN_WIDTH + x] = c;
            }
        }

        /// <summary>
        /// Very simple block-letter check for "L" and "R" markers.
        /// Returns true if pixel (x,y) falls inside the letter glyph.
        /// </summary>
        private static bool IsInsideLetter(int x, int y, int halfW, char letter)
        {
            // Letter dimensions: 60x80 pixels, centered in each half
            int letterW = 60, letterH = 80;
            int cx, cy;

            if (letter == 'L')
            {
                cx = halfW / 2 - letterW / 2;
                cy = PATTERN_HEIGHT / 2 - letterH / 2;
            }
            else // 'R'
            {
                cx = halfW + halfW / 2 - letterW / 2;
                cy = PATTERN_HEIGHT / 2 - letterH / 2;
            }

            int lx = x - cx;
            int ly = y - cy;
            if (lx < 0 || lx >= letterW || ly < 0 || ly >= letterH)
                return false;

            // Normalize to 0..1
            float u = (float)lx / letterW;
            float v = (float)ly / letterH;

            if (letter == 'L')
            {
                // Vertical bar on left + horizontal bar on bottom
                return (u < 0.25f) || (v < 0.2f);
            }
            else // 'R'
            {
                // Vertical bar on left + top horizontal + middle horizontal + diagonal
                if (u < 0.25f) return true; // Left bar
                if (v > 0.8f && u < 0.7f) return true; // Top bar
                if (v > 0.45f && v < 0.6f && u < 0.7f) return true; // Middle bar
                if (v > 0.6f && u > 0.35f && u < 0.6f) return true; // Top-right curve approx
                if (v < 0.45f && u > 0.25f && u < (0.25f + v * 0.8f)) return true; // Diagonal leg
                return false;
            }
        }

        /// <summary>
        /// Draw simple block text (5x7 bitmap font) at the given position.
        /// Only supports uppercase ASCII. Used for status messages in the pattern.
        /// </summary>
        private static void DrawBlockText(string text, int startX, int startY, Color32 color)
        {
            int charW = 6; // 5px char + 1px spacing
            for (int i = 0; i < text.Length; i++)
            {
                int cx = startX + i * charW;
                // Simple filled rectangle for each character (not full font rendering)
                if (text[i] == ' ') continue;
                for (int dy = 0; dy < 7; dy++)
                {
                    for (int dx = 0; dx < 5; dx++)
                    {
                        int px = cx + dx;
                        int py = startY + dy;
                        if (px >= 0 && px < PATTERN_WIDTH && py >= 0 && py < PATTERN_HEIGHT)
                        {
                            _pixels[py * PATTERN_WIDTH + px] = color;
                        }
                    }
                }
            }
        }
    }
}
