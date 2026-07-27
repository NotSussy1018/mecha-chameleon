using Unity.Netcode;
using UnityEngine;

namespace MechaChameleon
{
    public sealed class MvpHud : MonoBehaviour
    {
        const int ColorWheelTextureSize = 192;
        const float ColorWheelDisplaySize = 180f;

        [SerializeField] private RoomConnector connector;
        [SerializeField] private string joinCode = "";
        GUIStyle timerStyle;
        GUIStyle roleStyle;
        GUIStyle resultStyle;
        Texture2D colorWheelTexture;
        Texture2D colorPickerMarker;
        ChameleonPaint activePickerPaint;
        Color32 pickerAppliedColor;
        float pickerHue;
        float pickerSaturation;
        float pickerValue = 1f;

        void Awake()
        {
            timerStyle = new GUIStyle
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = 44,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            roleStyle = new GUIStyle
            {
                alignment = TextAnchor.UpperRight,
                fontSize = 36,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            resultStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 62,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            colorWheelTexture = CreateColorWheelTexture();
            colorPickerMarker = CreatePickerMarker();
        }

        void OnDestroy()
        {
            if (colorWheelTexture != null) Destroy(colorWheelTexture);
            if (colorPickerMarker != null) Destroy(colorPickerMarker);
        }

        void OnGUI()
        {
            DrawTimer();
            DrawRoleBadge();
            DrawResultOverlay();

            const int w = 320;
            GUILayout.BeginArea(new Rect(16, 16, w, Screen.height - 32), GUI.skin.box);
            GUILayout.Label("Paint Hideout MVP");

            GUILayout.Label(connector != null ? connector.Status : "No RoomConnector.");
            if (connector != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                GUILayout.Label($"Connected players: {connector.ConnectedPlayerCount}");

            if (connector != null && !string.IsNullOrEmpty(connector.JoinCode))
                GUILayout.TextField(connector.JoinCode);

            joinCode = GUILayout.TextField(joinCode);

            if (GUILayout.Button("Host Local")) connector?.HostLocal();
            if (GUILayout.Button("Join Local")) connector?.JoinLocal();
            GUILayout.Space(6);
            if (GUILayout.Button("Host Relay Session")) connector?.Host();
            if (GUILayout.Button("Join Code")) connector?.Join(joinCode);
            if (GUILayout.Button("Leave")) connector?.Leave();

            GUILayout.Space(12);

            var round = ChameleonRoundManager.Instance;
            GUILayout.Label(round != null ? $"Phase: {round.Phase.Value}" : "Round manager not spawned.");

            var localPlayer = ChameleonPlayer.Local;
            if (localPlayer == null)
            {
                GUILayout.Label("Player: not spawned yet. Click Host Local.");
            }
            else
            {
                var role = localPlayer.Role.Value == PlayerRole.Seeker ? "Hunter" : "Hider";
                GUILayout.Label($"You are: {role} | Alive: {localPlayer.Alive.Value}");
                if (round != null && round.Phase.Value == GamePhase.Lobby)
                {
                    GUILayout.Label(round.IsOnHunterPlatform(localPlayer.transform.position)
                        ? "Hunter choice: opted in"
                        : "Hunter choice: stand on yellow platform to opt in");
                }

                DrawPaintControls(localPlayer, round);

                if (localPlayer.Role.Value == PlayerRole.Seeker)
                {
                    if (!string.IsNullOrEmpty(localPlayer.LastShotStatus))
                        GUILayout.Label($"Last shot: {localPlayer.LastShotStatus}");
                }
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                if (GUILayout.Button("Start")) round?.StartRound();
                if (GUILayout.Button("Reset Lobby")) round?.ResetToLobby();
            }

            GUILayout.Space(12);
            GUILayout.Label("Move: WASD");
            GUILayout.Label("Wall climb: hold Space, release to cling, Space to drop");
            GUILayout.Label("Look: mouse, Esc releases cursor");
            GUILayout.Label("Pose: 1/2/3");
            GUILayout.Label("Paint mode: P, draw: left mouse");
            GUILayout.Label("Paint color: Z/X, brush: B, clear: C");
            GUILayout.Label("Orbit while painting: drag empty space");
            GUILayout.Label("Hunter shoot: left mouse or F");
            GUILayout.EndArea();
        }

        void DrawPaintControls(ChameleonPlayer player, ChameleonRoundManager round)
        {
            var paint = player.Paint;
            var canUseBrush = paint != null &&
                              round != null &&
                              (round.Phase.Value == GamePhase.Paint ||
                               round.Phase.Value == GamePhase.Hunt) &&
                              player.Role.Value == PlayerRole.Hider;

            if (canUseBrush)
            {
                GUILayout.Space(8);
                if (GUILayout.Button(paint.IsPaintMode ? "Finish Painting (P)" : "Enter Paint Mode (P)"))
                    paint.TogglePaintMode();

                if (paint.IsPaintMode)
                {
                    DrawColorPicker(paint);

                    var previousColor = GUI.color;
                    GUI.color = paint.SelectedColor;
                    GUILayout.Box(GUIContent.none, GUILayout.Height(22), GUILayout.ExpandWidth(true));
                    GUI.color = previousColor;

                    GUILayout.Label($"Brush size: {paint.BrushRadius} px | Strokes: {paint.StrokeCount}/450");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Previous")) paint.CycleColor(-1);
                    if (GUILayout.Button("Next")) paint.CycleColor(1);
                    if (GUILayout.Button("Brush")) paint.CycleBrushSize();
                    GUILayout.EndHorizontal();

                    if (GUILayout.Button("Clear Painting")) paint.RequestClear();
                }

                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cycle Head")) player.CycleHeadColor();
            if (GUILayout.Button("Cycle Body")) player.CycleBodyColor();
            if (GUILayout.Button("Reset Colors")) player.ResetPaint();
            GUILayout.EndHorizontal();
        }

        void DrawColorPicker(ChameleonPaint paint)
        {
            if (activePickerPaint != paint || !pickerAppliedColor.Equals(paint.SelectedColor))
                SyncPickerFromPaint(paint);

            GUILayout.Label("Brush color");
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var wheelRect = GUILayoutUtility.GetRect(
                ColorWheelDisplaySize,
                ColorWheelDisplaySize,
                GUILayout.Width(ColorWheelDisplaySize),
                GUILayout.Height(ColorWheelDisplaySize));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUI.DrawTexture(wheelRect, colorWheelTexture, ScaleMode.StretchToFill, alphaBlend: true);
            HandleColorWheelInput(wheelRect, paint);
            DrawPickerMarker(wheelRect);

            GUILayout.Label("Brightness");
            var nextValue = GUILayout.HorizontalSlider(pickerValue, 0f, 1f);
            if (!Mathf.Approximately(nextValue, pickerValue))
            {
                pickerValue = nextValue;
                ApplyPickerColor(paint);
            }
        }

        void HandleColorWheelInput(Rect wheelRect, ChameleonPaint paint)
        {
            var currentEvent = Event.current;
            if (currentEvent.button != 0 ||
                (currentEvent.type != EventType.MouseDown && currentEvent.type != EventType.MouseDrag) ||
                !wheelRect.Contains(currentEvent.mousePosition))
                return;

            var center = wheelRect.center;
            var normalized = new Vector2(
                (currentEvent.mousePosition.x - center.x) / (wheelRect.width * 0.5f),
                -(currentEvent.mousePosition.y - center.y) / (wheelRect.height * 0.5f));
            if (normalized.sqrMagnitude > 1f) return;

            pickerSaturation = Mathf.Clamp01(normalized.magnitude);
            var angle = Mathf.Atan2(normalized.y, normalized.x);
            pickerHue = Mathf.Repeat(0.5f - angle / (Mathf.PI * 2f), 1f);
            ApplyPickerColor(paint);
            currentEvent.Use();
        }

        void DrawPickerMarker(Rect wheelRect)
        {
            var angle = (0.5f - pickerHue) * Mathf.PI * 2f;
            var radius = pickerSaturation * wheelRect.width * 0.5f;
            var point = wheelRect.center + new Vector2(
                Mathf.Cos(angle) * radius,
                -Mathf.Sin(angle) * radius);
            var markerRect = new Rect(point.x - 9f, point.y - 9f, 18f, 18f);
            GUI.DrawTexture(markerRect, colorPickerMarker, ScaleMode.StretchToFill, alphaBlend: true);
        }

        void SyncPickerFromPaint(ChameleonPaint paint)
        {
            activePickerPaint = paint;
            pickerAppliedColor = paint.SelectedColor;
            Color.RGBToHSV((Color)pickerAppliedColor, out pickerHue, out pickerSaturation, out pickerValue);
        }

        void ApplyPickerColor(ChameleonPaint paint)
        {
            var color = (Color32)Color.HSVToRGB(pickerHue, pickerSaturation, pickerValue);
            color.a = 255;
            pickerAppliedColor = color;
            paint.SetBrushColor(color);
        }

        static Texture2D CreateColorWheelTexture()
        {
            var texture = new Texture2D(
                ColorWheelTextureSize,
                ColorWheelTextureSize,
                TextureFormat.RGBA32,
                mipChain: false)
            {
                name = "Brush Color Wheel",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[ColorWheelTextureSize * ColorWheelTextureSize];
            for (var y = 0; y < ColorWheelTextureSize; y++)
            {
                for (var x = 0; x < ColorWheelTextureSize; x++)
                {
                    var normalized = new Vector2(
                        (x + 0.5f) / ColorWheelTextureSize * 2f - 1f,
                        (y + 0.5f) / ColorWheelTextureSize * 2f - 1f);
                    var saturation = normalized.magnitude;
                    if (saturation > 1f)
                    {
                        pixels[y * ColorWheelTextureSize + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    var angle = Mathf.Atan2(normalized.y, normalized.x);
                    var hue = Mathf.Repeat(0.5f - angle / (Mathf.PI * 2f), 1f);
                    pixels[y * ColorWheelTextureSize + x] =
                        (Color32)Color.HSVToRGB(hue, saturation, 1f);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture;
        }

        static Texture2D CreatePickerMarker()
        {
            const int size = 18;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "Color Picker Marker",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x, y), center);
                    pixels[y * size + x] = distance switch
                    {
                        >= 7.2f and <= 8.5f => new Color32(20, 20, 20, 255),
                        >= 5.2f and < 7.2f => new Color32(255, 255, 255, 255),
                        _ => new Color32(0, 0, 0, 0)
                    };
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture;
        }

        void DrawTimer()
        {
            var round = ChameleonRoundManager.Instance;
            if (round == null) return;
            if (round.Phase.Value != GamePhase.Paint && round.Phase.Value != GamePhase.Hunt) return;

            var label = round.Phase.Value == GamePhase.Paint ? "HIDE" : "HUNT";
            GUI.Label(new Rect(0, 18, Screen.width, 72), $"{label} {round.RemainingSeconds}", timerStyle);
        }

        void DrawRoleBadge()
        {
            var player = ChameleonPlayer.Local;
            if (player == null) return;

            var round = ChameleonRoundManager.Instance;
            var isHunter = player.Role.Value == PlayerRole.Seeker;
            if (round != null && round.Phase.Value == GamePhase.Lobby)
                isHunter = round.IsOnHunterPlatform(player.transform.position);

            var label = isHunter ? "HUNTER" : "HIDER";
            GUI.Label(new Rect(Screen.width - 280, 18, 260, 56), label, roleStyle);
        }

        void DrawResultOverlay()
        {
            var round = ChameleonRoundManager.Instance;
            var player = ChameleonPlayer.Local;
            if (round == null || player == null || round.Phase.Value != GamePhase.Result) return;

            var hidersWon = round.HidersWonLastRound.Value;
            var playerWon = player.Role.Value == PlayerRole.Hider ? hidersWon : !hidersWon;
            var message = playerWon ? "YOU WON" : "YOU LOST";

            var backdrop = new Rect(0, Screen.height * 0.35f, Screen.width, 150);
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(backdrop, Texture2D.whiteTexture);
            GUI.color = playerWon ? new Color(0.45f, 1f, 0.45f, 1f) : new Color(1f, 0.45f, 0.45f, 1f);
            GUI.Label(backdrop, message, resultStyle);
            GUI.color = Color.white;
        }
    }
}
