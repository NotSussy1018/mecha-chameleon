using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace MechaChameleon
{
    public sealed class ChameleonPaint : NetworkBehaviour
    {
        const int TextureSize = 128;
        const int MaxStrokesPerRound = 450;
        const float StrokeInterval = 1f / 15f;
        const float ServerMinStrokeInterval = 1f / 20f;
        const int BrushOutlineSegments = 48;
        const byte GiantBrushRadius = 28;
        const float PaintEmissionStrength = 0.28f;

        [SerializeField] private ChameleonPlayer player;
        [SerializeField] private Renderer headRenderer;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private MeshCollider headPaintCollider;
        [SerializeField] private MeshCollider bodyPaintCollider;

        public NetworkList<PaintStroke> Strokes { get; } = new(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        readonly RaycastHit[] paintHits = new RaycastHit[16];
        readonly HashSet<ushort> predictedSequences = new();

        Texture2D headTexture;
        Texture2D bodyTexture;
        Material headMaterial;
        Material bodyMaterial;
        Material brushOutlineMaterial;
        LineRenderer brushOutline;
        bool headDirty;
        bool bodyDirty;
        bool hasPreviousUv;
        PaintPart previousPart;
        Vector2 previousUv;
        float nextStrokeAt;
        double lastServerStrokeAt = double.NegativeInfinity;
        ushort nextSequence;
        byte selectedColorIndex = 2;
        Color32 selectedColor;
        byte brushRadius = 4;
        float cameraOrbitYaw;
        bool isPaintDrag;
        bool isCameraDrag;
        PaintMeshGeometry headGeometry;
        PaintMeshGeometry bodyGeometry;

        sealed class PaintMeshGeometry
        {
            public readonly Vector3[] Vertices;
            public readonly Vector2[] Uvs;
            public readonly int[] Triangles;

            public PaintMeshGeometry(Mesh mesh)
            {
                Vertices = mesh.vertices;
                Uvs = mesh.uv;
                Triangles = mesh.triangles;
            }
        }

        public bool IsPaintMode { get; private set; }
        public bool IsReady => headTexture != null && bodyTexture != null;
        public int StrokeCount => Strokes.Count;
        public int BrushRadius => brushRadius;
        public Color32 SelectedColor => selectedColor;

        public override void OnNetworkSpawn()
        {
            if (player == null) player = GetComponent<ChameleonPlayer>();
            selectedColor = ChameleonPalette.Colors[selectedColorIndex];
            InitializeTextures();
            CachePaintGeometry();
            SetPaintColliders(false);
            Strokes.OnListChanged += OnStrokeListChanged;
            RebuildTextures();
        }

        public override void OnNetworkDespawn()
        {
            Strokes.OnListChanged -= OnStrokeListChanged;
            if (IsPaintMode) ExitPaintMode();
            SetPaintColliders(false);
            SetBrushOutlineVisible(false);
        }

        public override void OnDestroy()
        {
            if (headTexture != null) Destroy(headTexture);
            if (bodyTexture != null) Destroy(bodyTexture);
            if (brushOutlineMaterial != null) Destroy(brushOutlineMaterial);
            base.OnDestroy();
        }

        void Update()
        {
            if (!NetworkObject.IsLocalPlayer) return;

            if (Input.GetKeyDown(KeyCode.P))
                TogglePaintMode();

            if (!IsPaintMode) return;
            if (!CanPaintNow() || Input.GetKeyDown(KeyCode.Escape))
            {
                ExitPaintMode();
                return;
            }

            PaintPart part = default;
            Vector2 uv = default;
            RaycastHit hit = default;
            var canInteractWithWorld = Input.mousePosition.x > 340f;
            var hasPaintHit = canInteractWithWorld &&
                              TryGetPaintHit(out part, out uv, out hit);
            HandlePaintControls(canInteractWithWorld, hasPaintHit);
            UpdateBrushOutline(hasPaintHit && !isCameraDrag, part, uv, hit);
            HandlePainting(hasPaintHit, part, uv);
        }

        void LateUpdate()
        {
            if (headDirty && headTexture != null)
            {
                headTexture.Apply(updateMipmaps: false);
                headDirty = false;
            }

            if (bodyDirty && bodyTexture != null)
            {
                bodyTexture.Apply(updateMipmaps: false);
                bodyDirty = false;
            }
        }

        public void TogglePaintMode()
        {
            if (IsPaintMode)
            {
                ExitPaintMode();
                return;
            }

            if (!CanPaintNow()) return;

            IsPaintMode = true;
            cameraOrbitYaw = 0f;
            isPaintDrag = false;
            isCameraDrag = false;
            player?.SetPaintCameraOrbit(cameraOrbitYaw);
            SetPaintColliders(true);
            EnsureBrushOutline();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            hasPreviousUv = false;
        }

        public void CycleColor(int direction)
        {
            var count = ChameleonPalette.Colors.Length;
            selectedColorIndex = (byte)((selectedColorIndex + direction + count) % count);
            selectedColor = ChameleonPalette.Colors[selectedColorIndex];
        }

        public void SetBrushColor(Color32 color)
        {
            color.a = 255;
            selectedColor = color;
            selectedColorIndex = FindClosestPaletteIndex(color);
        }

        public void CycleBrushSize()
        {
            brushRadius = brushRadius switch
            {
                2 => 4,
                4 => 7,
                7 => GiantBrushRadius,
                _ => 2
            };
        }

        public void RequestClear()
        {
            if (!NetworkObject.IsLocalPlayer || !CanPaintNow()) return;
            RequestClearServerRpc();
        }

        public void ClearFromServer()
        {
            if (!IsServer) return;
            Strokes.Clear();
            lastServerStrokeAt = double.NegativeInfinity;
        }

        public bool ApplyBaseColors(Color32 head, Color32 body)
        {
            if (!IsReady) return false;

            if (headMaterial != null) headMaterial.color = Color.white;
            if (bodyMaterial != null) bodyMaterial.color = Color.white;
            RebuildTextures(head, body);
            return true;
        }

        void HandlePaintControls(bool canInteractWithWorld, bool hasPaintHit)
        {
            if (Input.GetKeyDown(KeyCode.Z)) CycleColor(-1);
            if (Input.GetKeyDown(KeyCode.X)) CycleColor(1);
            if (Input.GetKeyDown(KeyCode.B)) CycleBrushSize();
            if (Input.GetKeyDown(KeyCode.C)) RequestClear();

            if (Input.GetMouseButtonDown(0))
            {
                isPaintDrag = hasPaintHit;
                isCameraDrag = canInteractWithWorld && !hasPaintHit;
            }

            if (Input.GetMouseButtonUp(0))
            {
                isPaintDrag = false;
                isCameraDrag = false;
            }

            if (!isCameraDrag) return;

            cameraOrbitYaw += Input.GetAxisRaw("Mouse X") * 4f;
            player?.SetPaintCameraOrbit(cameraOrbitYaw);
        }

        void HandlePainting(bool hasPaintHit, PaintPart part, Vector2 uv)
        {
            if (!Input.GetMouseButton(0) || !isPaintDrag)
            {
                hasPreviousUv = false;
                return;
            }

            if (!hasPaintHit)
            {
                hasPreviousUv = false;
                return;
            }

            if (Time.unscaledTime < nextStrokeAt ||
                Strokes.Count >= MaxStrokesPerRound)
                return;

            var startUv = hasPreviousUv && previousPart == part ? previousUv : uv;
            var stroke = new PaintStroke(
                part,
                startUv,
                uv,
                selectedColor,
                brushRadius,
                nextSequence++);

            if (!IsServer)
            {
                DrawStroke(stroke);
                predictedSequences.Add(stroke.Sequence);
            }

            SubmitStrokeServerRpc(stroke);
            previousPart = part;
            previousUv = uv;
            hasPreviousUv = true;
            nextStrokeAt = Time.unscaledTime + StrokeInterval;
        }

        bool TryGetPaintHit(out PaintPart part, out Vector2 uv, out RaycastHit closestHit)
        {
            part = default;
            uv = default;
            closestHit = default;
            if (playerCamera == null) return false;

            var ray = playerCamera.ScreenPointToRay(Input.mousePosition);
            var count = Physics.RaycastNonAlloc(ray, paintHits, 10f, ~0, QueryTriggerInteraction.Ignore);
            var closestDistance = float.PositiveInfinity;
            var found = false;

            for (var i = 0; i < count; i++)
            {
                var hit = paintHits[i];
                if (hit.distance >= closestDistance) continue;

                if (hit.collider == headPaintCollider)
                    part = PaintPart.Head;
                else if (hit.collider == bodyPaintCollider)
                    part = PaintPart.Body;
                else
                    continue;

                uv = hit.textureCoord;
                closestHit = hit;
                closestDistance = hit.distance;
                found = true;
            }

            return found;
        }

        [ServerRpc]
        void SubmitStrokeServerRpc(PaintStroke stroke, ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                !CanServerAcceptPaint(stroke))
                return;

            Strokes.Add(stroke);
            lastServerStrokeAt = NetworkManager.ServerTime.Time;
        }

        [ServerRpc]
        void RequestClearServerRpc(ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                player == null ||
                player.Role.Value != PlayerRole.Hider ||
                ChameleonRoundManager.Instance == null ||
                !IsPaintingPhase(ChameleonRoundManager.Instance.Phase.Value))
                return;

            Strokes.Clear();
        }

        bool CanServerAcceptPaint(PaintStroke stroke)
        {
            if (!IsServer ||
                player == null ||
                player.Role.Value != PlayerRole.Hider ||
                !player.Alive.Value ||
                ChameleonRoundManager.Instance == null ||
                !IsPaintingPhase(ChameleonRoundManager.Instance.Phase.Value) ||
                Strokes.Count >= MaxStrokesPerRound ||
                stroke.Part > PaintPart.Body ||
                !IsValidBrushRadius(stroke.Radius))
                return false;

            return NetworkManager.ServerTime.Time - lastServerStrokeAt >= ServerMinStrokeInterval;
        }

        static bool IsValidBrushRadius(byte radius)
        {
            return radius is 2 or 4 or 7 or GiantBrushRadius;
        }

        static byte FindClosestPaletteIndex(Color32 color)
        {
            var closestIndex = 0;
            var closestDistance = int.MaxValue;
            for (var i = 0; i < ChameleonPalette.Colors.Length; i++)
            {
                var paletteColor = ChameleonPalette.Colors[i];
                var red = color.r - paletteColor.r;
                var green = color.g - paletteColor.g;
                var blue = color.b - paletteColor.b;
                var distance = red * red + green * green + blue * blue;
                if (distance >= closestDistance) continue;

                closestDistance = distance;
                closestIndex = i;
            }

            return (byte)closestIndex;
        }

        bool CanPaintNow()
        {
            return NetworkObject.IsLocalPlayer &&
                   player != null &&
                   player.Role.Value == PlayerRole.Hider &&
                   player.Alive.Value &&
                   ChameleonRoundManager.Instance != null &&
                   IsPaintingPhase(ChameleonRoundManager.Instance.Phase.Value);
        }

        static bool IsPaintingPhase(GamePhase phase)
        {
            return phase is GamePhase.Paint or GamePhase.Hunt;
        }

        void ExitPaintMode()
        {
            IsPaintMode = false;
            hasPreviousUv = false;
            isPaintDrag = false;
            isCameraDrag = false;
            cameraOrbitYaw = 0f;
            player?.SetPaintCameraOrbit(0f);
            SetPaintColliders(false);
            SetBrushOutlineVisible(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void SetPaintColliders(bool enabled)
        {
            if (headPaintCollider != null) headPaintCollider.enabled = enabled;
            if (bodyPaintCollider != null) bodyPaintCollider.enabled = enabled;
        }

        void CachePaintGeometry()
        {
            headGeometry = CreatePaintGeometry(headPaintCollider);
            bodyGeometry = CreatePaintGeometry(bodyPaintCollider);
        }

        static PaintMeshGeometry CreatePaintGeometry(MeshCollider collider)
        {
            var mesh = collider != null ? collider.sharedMesh : null;
            return mesh != null && mesh.uv != null && mesh.uv.Length == mesh.vertexCount
                ? new PaintMeshGeometry(mesh)
                : null;
        }

        void EnsureBrushOutline()
        {
            if (brushOutline != null) return;

            var outlineObject = new GameObject("Brush Outline");
            outlineObject.transform.SetParent(transform, false);
            brushOutline = outlineObject.AddComponent<LineRenderer>();
            brushOutline.useWorldSpace = true;
            brushOutline.loop = true;
            brushOutline.positionCount = BrushOutlineSegments;
            brushOutline.startWidth = 0.008f;
            brushOutline.endWidth = 0.008f;
            brushOutline.shadowCastingMode = ShadowCastingMode.Off;
            brushOutline.receiveShadows = false;
            brushOutline.textureMode = LineTextureMode.Stretch;

            brushOutlineMaterial = new Material(
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Standard"))
            {
                color = new Color(0.1f, 1f, 1f, 0.95f)
            };
            brushOutline.material = brushOutlineMaterial;
            SetBrushOutlineVisible(false);
        }

        void UpdateBrushOutline(bool hasPaintHit, PaintPart part, Vector2 centerUv, RaycastHit hit)
        {
            if (!hasPaintHit)
            {
                SetBrushOutlineVisible(false);
                return;
            }

            EnsureBrushOutline();
            var collider = part == PaintPart.Head ? headPaintCollider : bodyPaintCollider;
            var geometry = part == PaintPart.Head ? headGeometry : bodyGeometry;
            if (collider == null || geometry == null)
            {
                SetBrushOutlineVisible(false);
                return;
            }

            var uvRadius = (brushRadius + 0.5f) / (TextureSize - 1f);

            for (var i = 0; i < brushOutline.positionCount; i++)
            {
                var angle = i / (float)brushOutline.positionCount * Mathf.PI * 2f;
                var outlineUv = centerUv + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * uvRadius;
                outlineUv.y = Mathf.Clamp01(outlineUv.y);
                if (!TryMapUvToWorld(geometry, collider.transform, outlineUv, hit.point, out var point))
                {
                    SetBrushOutlineVisible(false);
                    return;
                }

                brushOutline.SetPosition(i, point + hit.normal * 0.006f);
            }

            SetBrushOutlineVisible(true);
        }

        static bool TryMapUvToWorld(
            PaintMeshGeometry geometry,
            Transform meshTransform,
            Vector2 targetUv,
            Vector3 anchor,
            out Vector3 worldPoint)
        {
            worldPoint = default;
            var bestDistance = float.PositiveInfinity;
            var found = false;

            for (var i = 0; i < geometry.Triangles.Length; i += 3)
            {
                var index0 = geometry.Triangles[i];
                var index1 = geometry.Triangles[i + 1];
                var index2 = geometry.Triangles[i + 2];
                var uv0 = UnwrapUvNear(geometry.Uvs[index0], targetUv.x);
                var uv1 = UnwrapUvNear(geometry.Uvs[index1], targetUv.x);
                var uv2 = UnwrapUvNear(geometry.Uvs[index2], targetUv.x);

                if (!TryGetBarycentric(targetUv, uv0, uv1, uv2, out var barycentric))
                    continue;

                var localPoint =
                    geometry.Vertices[index0] * barycentric.x +
                    geometry.Vertices[index1] * barycentric.y +
                    geometry.Vertices[index2] * barycentric.z;
                var candidate = meshTransform.TransformPoint(localPoint);
                var distance = (candidate - anchor).sqrMagnitude;
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                worldPoint = candidate;
                found = true;
            }

            return found;
        }

        static Vector2 UnwrapUvNear(Vector2 uv, float targetU)
        {
            uv.x += Mathf.Round(targetU - uv.x);
            return uv;
        }

        static bool TryGetBarycentric(
            Vector2 point,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            out Vector3 barycentric)
        {
            barycentric = default;
            var v0 = b - a;
            var v1 = c - a;
            var v2 = point - a;
            var denominator = v0.x * v1.y - v1.x * v0.y;
            if (Mathf.Abs(denominator) < 0.000001f) return false;

            var inverse = 1f / denominator;
            var bWeight = (v2.x * v1.y - v1.x * v2.y) * inverse;
            var cWeight = (v0.x * v2.y - v2.x * v0.y) * inverse;
            var aWeight = 1f - bWeight - cWeight;
            const float tolerance = -0.0001f;
            if (aWeight < tolerance || bWeight < tolerance || cWeight < tolerance)
                return false;

            barycentric = new Vector3(aWeight, bWeight, cWeight);
            return true;
        }

        void SetBrushOutlineVisible(bool visible)
        {
            if (brushOutline != null)
                brushOutline.enabled = visible;
        }

        void InitializeTextures()
        {
            if (headRenderer != null)
            {
                headMaterial = headRenderer.material;
                headTexture = CreatePaintTexture("Head Paint");
                ConfigurePaintMaterial(headMaterial, headTexture);
            }

            if (bodyRenderer != null)
            {
                bodyMaterial = bodyRenderer.material;
                bodyTexture = CreatePaintTexture("Body Paint");
                ConfigurePaintMaterial(bodyMaterial, bodyTexture);
            }
        }

        static void ConfigurePaintMaterial(Material material, Texture2D texture)
        {
            material.mainTexture = texture;
            material.color = Color.white;

            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0.15f);

            if (!material.HasProperty("_EmissionMap") || !material.HasProperty("_EmissionColor"))
                return;

            material.SetTexture("_EmissionMap", texture);
            material.SetColor("_EmissionColor", Color.white * PaintEmissionStrength);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        static Texture2D CreatePaintTexture(string name)
        {
            return new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                anisoLevel = 0
            };
        }

        void OnStrokeListChanged(NetworkListEvent<PaintStroke> change)
        {
            if (change.Type == NetworkListEvent<PaintStroke>.EventType.Clear)
            {
                predictedSequences.Clear();
                RebuildTextures();
                return;
            }

            if (change.Type != NetworkListEvent<PaintStroke>.EventType.Add &&
                change.Type != NetworkListEvent<PaintStroke>.EventType.Insert)
                return;

            if (NetworkObject.IsLocalPlayer)
                predictedSequences.Remove(change.Value.Sequence);
            DrawStroke(change.Value);
        }

        void RebuildTextures()
        {
            var head = player != null ? player.HeadColor.Value : new Color32(255, 255, 255, 255);
            var body = player != null ? player.BodyColor.Value : new Color32(255, 255, 255, 255);
            RebuildTextures(head, body);
        }

        void RebuildTextures(Color32 head, Color32 body)
        {
            FillTexture(headTexture, head);
            FillTexture(bodyTexture, body);
            headDirty = headTexture != null;
            bodyDirty = bodyTexture != null;

            for (var i = 0; i < Strokes.Count; i++)
                DrawStroke(Strokes[i]);
        }

        static void FillTexture(Texture2D texture, Color32 color)
        {
            if (texture == null) return;
            var pixels = texture.GetPixelData<Color32>(0);
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = color;
        }

        void DrawStroke(PaintStroke stroke)
        {
            var texture = stroke.Part == PaintPart.Head ? headTexture : bodyTexture;
            if (texture == null) return;

            DrawWrappedLine(
                texture.GetPixelData<Color32>(0),
                stroke.StartUv,
                stroke.EndUv,
                stroke.Radius,
                stroke.Color);

            if (stroke.Part == PaintPart.Head)
                headDirty = true;
            else
                bodyDirty = true;
        }

        static void DrawWrappedLine(
            NativeArray<Color32> pixels,
            Vector2 startUv,
            Vector2 endUv,
            int radius,
            Color32 color)
        {
            var deltaU = endUv.x - startUv.x;
            if (deltaU > 0.5f)
                startUv.x += 1f;
            else if (deltaU < -0.5f)
                endUv.x += 1f;

            var dx = Mathf.Abs(endUv.x - startUv.x) * TextureSize;
            var dy = Mathf.Abs(endUv.y - startUv.y) * TextureSize;
            var steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(dx, dy)));

            for (var step = 0; step <= steps; step++)
            {
                var t = step / (float)steps;
                var uv = Vector2.Lerp(startUv, endUv, t);
                var x = Mathf.RoundToInt(Mathf.Repeat(uv.x, 1f) * (TextureSize - 1));
                var y = Mathf.RoundToInt(Mathf.Clamp01(uv.y) * (TextureSize - 1));
                StampBrush(pixels, x, y, radius, color);
            }
        }

        static void StampBrush(NativeArray<Color32> pixels, int centerX, int centerY, int radius, Color32 color)
        {
            var radiusSquared = radius * radius;
            for (var y = centerY - radius; y <= centerY + radius; y++)
            {
                if (y < 0 || y >= TextureSize) continue;

                for (var x = centerX - radius; x <= centerX + radius; x++)
                {
                    var dx = x - centerX;
                    var dy = y - centerY;
                    if (dx * dx + dy * dy > radiusSquared) continue;

                    var wrappedX = (x % TextureSize + TextureSize) % TextureSize;
                    pixels[y * TextureSize + wrappedX] = color;
                }
            }
        }
    }
}
