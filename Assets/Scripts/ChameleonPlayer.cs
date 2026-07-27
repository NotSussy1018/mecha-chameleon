using Unity.Netcode;
using UnityEngine;
using System.Linq;

namespace MechaChameleon
{
    public sealed class ChameleonPlayer : NetworkBehaviour
    {
        static readonly Vector3 LeanVisualOffset = new(-0.41f, 0.28f, 0f);
        static readonly Vector3 LieVisualOffset = new(0f, 0.36f, -0.63f);
        static readonly Vector3 LeanCollisionCenter = new(0f, 0.61f, 0f);
        static readonly Vector3 LeanCollisionHalfExtents = new(0.65f, 0.6f, 0.36f);
        static readonly Vector3 LieCollisionCenter = new(0f, 0.42f, 0f);
        static readonly Vector3 LieCollisionHalfExtents = new(0.36f, 0.41f, 0.7f);

        [SerializeField] private float moveSpeed = 4.5f;
        [SerializeField] private float jumpHeight = 1.4f;
        [SerializeField] private float gravity = -18f;
        [SerializeField] private float wallClimbSpeed = 2.8f;
        [SerializeField] private float wallCheckDistance = 0.3f;
        [SerializeField] private float mouseSensitivity = 2.2f;
        [SerializeField] private float fallRespawnY = -8f;
        [SerializeField] private Renderer headRenderer;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private GameObject gunRoot;
        [SerializeField] private LineRenderer shotLine;
        [SerializeField] private ChameleonPaint paint;

        public NetworkVariable<PlayerRole> Role { get; } = new(PlayerRole.Hider);
        public NetworkVariable<bool> Alive { get; } = new(true);
        public NetworkVariable<PoseId> Pose { get; } = new(PoseId.Stand);

        public NetworkVariable<Color32> HeadColor { get; } = new(new Color32(255, 255, 255, 255));
        public NetworkVariable<Color32> BodyColor { get; } = new(new Color32(255, 255, 255, 255));

        CharacterController controller;
        Material headMaterial;
        Material bodyMaterial;
        readonly RaycastHit[] climbHits = new RaycastHit[12];
        readonly RaycastHit[] poseMovementHits = new RaycastHit[16];
        float cameraPitch = 12f;
        float shotLineHideAt;
        float verticalVelocity;
        float paintCameraOrbitYaw;
        bool climbedDuringSpaceHold;
        bool isWallLatched;
        Vector3 wallLatchDirection;

        public static ChameleonPlayer Local { get; private set; }
        public string LastShotStatus { get; private set; } = "";
        public ChameleonPaint Paint => paint;

        public override void OnNetworkSpawn()
        {
            controller = GetComponent<CharacterController>();
            EnsureRuntimeVisuals();
            if (paint == null) paint = GetComponent<ChameleonPaint>();
            headMaterial = headRenderer != null ? headRenderer.material : null;
            bodyMaterial = bodyRenderer != null ? bodyRenderer.material : null;

            HeadColor.OnValueChanged += (_, color) => ApplyColors();
            BodyColor.OnValueChanged += (_, color) => ApplyColors();
            Pose.OnValueChanged += (_, pose) => ApplyPose(pose);
            Alive.OnValueChanged += (_, alive) => SetVisible(alive);

            ApplyColors();
            ApplyPose(Pose.Value);
            SetVisible(Alive.Value);
            SetShotLineVisible(false);

            if (playerCamera != null)
            {
                playerCamera.enabled = NetworkObject.IsLocalPlayer;
                if (NetworkObject.IsLocalPlayer)
                {
                    Local = this;
                    DisableOverviewCameras();
                    UpdateCameraPosition();
                    LockCursor();
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Local != this) return;
            Local = null;
            ReleaseCursor();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (Local != this) return;
            Local = null;
            ReleaseCursor();
        }

        void Update()
        {
            UpdateShotLine();
            if (!NetworkObject.IsLocalPlayer || !Alive.Value) return;
            RespawnIfFallen();
            if (paint != null && paint.IsPaintMode) return;
            if (!CaptureCursor()) return;

            HandleMovement();
            HandleActions();
            UpdateCameraPosition();
        }

        public void ResetForLobby()
        {
            paint?.ClearFromServer();
            SetServerState(PlayerRole.Hider, alive: true, Color.white, Color.white, PoseId.Stand);
        }

        public void SetServerState(PlayerRole role, bool alive, Color32 head, Color32 body, PoseId pose)
        {
            if (!IsServer) return;

            Role.Value = role;
            Alive.Value = alive;
            Pose.Value = pose;
            HeadColor.Value = head;
            BodyColor.Value = body;
        }

        void HandleMovement()
        {
            var x = Input.GetAxisRaw("Horizontal");
            var z = Input.GetAxisRaw("Vertical");
            var move = transform.forward * z + transform.right * x;
            move = Vector3.ClampMagnitude(move, 1f);

            if (controller != null)
            {
                var grounded = controller.isGrounded;
                var dropFromWall = isWallLatched && Input.GetKeyDown(KeyCode.Space);

                if (grounded)
                {
                    ReleaseWallLatch();
                    if (verticalVelocity < 0f)
                        verticalVelocity = -2f;
                }

                if (dropFromWall)
                {
                    ReleaseWallLatch();
                    verticalVelocity += gravity * Time.deltaTime;
                }
                else if (isWallLatched)
                {
                    if (IsNearClimbableWall(wallLatchDirection))
                    {
                        move = Vector3.zero;
                        verticalVelocity = 0f;
                    }
                    else
                    {
                        ReleaseWallLatch();
                        verticalVelocity += gravity * Time.deltaTime;
                    }
                }
                else if (grounded && Input.GetKeyDown(KeyCode.Space))
                {
                    verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                }
                else if (!grounded &&
                         Input.GetKey(KeyCode.Space) &&
                         TryGetClimbDirection(move, out var climbDirection))
                {
                    wallLatchDirection = climbDirection;
                    climbedDuringSpaceHold = true;
                    verticalVelocity = wallClimbSpeed;
                }
                else
                {
                    verticalVelocity += gravity * Time.deltaTime;
                }

                if (!grounded &&
                    climbedDuringSpaceHold &&
                    Input.GetKeyUp(KeyCode.Space))
                {
                    if (IsNearClimbableWall(wallLatchDirection))
                    {
                        isWallLatched = true;
                        move = Vector3.zero;
                        verticalVelocity = 0f;
                    }
                    else
                    {
                        ReleaseWallLatch();
                    }
                }

                var horizontalDisplacement = ConstrainPoseMovement(move * moveSpeed * Time.deltaTime);
                controller.Move(horizontalDisplacement + Vector3.up * verticalVelocity * Time.deltaTime);
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
                transform.position += (move * moveSpeed + Vector3.up * verticalVelocity) * Time.deltaTime;
            }

            var mouseYaw = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
            if (Mathf.Abs(mouseYaw) > 0.001f)
                transform.Rotate(Vector3.up, mouseYaw, Space.World);

            if (Role.Value == PlayerRole.Seeker && playerCamera != null)
            {
                var mousePitch = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
                if (Mathf.Abs(mousePitch) > 0.001f)
                {
                    cameraPitch = Mathf.Clamp(cameraPitch - mousePitch, -70f, 70f);
                    playerCamera.transform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
                }
            }
        }

        Vector3 ConstrainPoseMovement(Vector3 displacement)
        {
            if (Pose.Value == PoseId.Stand || displacement.sqrMagnitude < 0.000001f)
                return displacement;

            var center = Pose.Value == PoseId.Lie ? LieCollisionCenter : LeanCollisionCenter;
            var halfExtents = Pose.Value == PoseId.Lie
                ? LieCollisionHalfExtents
                : LeanCollisionHalfExtents;
            halfExtents -= Vector3.one * 0.025f;

            var distance = displacement.magnitude;
            var direction = displacement / distance;
            var worldCenter = transform.TransformPoint(center);
            var hitCount = Physics.BoxCastNonAlloc(
                worldCenter,
                halfExtents,
                direction,
                poseMovementHits,
                transform.rotation,
                distance + 0.02f,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < hitCount; i++)
            {
                var hit = poseMovementHits[i];
                if (hit.collider == controller ||
                    hit.collider.GetComponentInParent<ChameleonPlayer>() == this ||
                    Mathf.Abs(hit.normal.y) > 0.55f)
                    continue;

                displacement = Vector3.ProjectOnPlane(displacement, hit.normal);
            }

            return displacement;
        }

        bool TryGetClimbDirection(Vector3 move, out Vector3 direction)
        {
            direction = move.sqrMagnitude > 0.01f
                ? move.normalized
                : climbedDuringSpaceHold
                    ? wallLatchDirection
                    : transform.forward;
            return IsNearClimbableWall(direction);
        }

        bool IsNearClimbableWall(Vector3 direction)
        {
            direction = direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
            var origin = transform.position + Vector3.up * (controller.height * 0.55f);
            var radius = controller.radius * 0.75f;
            var distance = controller.radius + wallCheckDistance;
            var hitCount = Physics.SphereCastNonAlloc(
                origin,
                radius,
                direction,
                climbHits,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < hitCount; i++)
            {
                var hit = climbHits[i];
                if (hit.collider == controller || hit.collider.GetComponentInParent<ChameleonPlayer>() == this)
                    continue;

                if (Mathf.Abs(hit.normal.y) < 0.3f)
                    return true;
            }

            return (controller.collisionFlags & CollisionFlags.Sides) != 0;
        }

        void ReleaseWallLatch()
        {
            isWallLatched = false;
            climbedDuringSpaceHold = false;
            wallLatchDirection = Vector3.zero;
        }

        void HandleActions()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetPoseServerRpc(PoseId.Stand);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetPoseServerRpc(PoseId.Crouch);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetPoseServerRpc(PoseId.Lie);

            if (Input.GetKeyDown(KeyCode.Z)) CycleHeadColor();
            if (Input.GetKeyDown(KeyCode.X)) CycleBodyColor();
            if (Input.GetKeyDown(KeyCode.C)) SetColorsServerRpc(Color.white, Color.white);

            if (Role.Value == PlayerRole.Seeker &&
                (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.F)))
            {
                Shoot();
            }
        }

        public void Shoot()
        {
            if (!NetworkObject.IsLocalPlayer) return;
            if (Role.Value != PlayerRole.Seeker)
            {
                LastShotStatus = "Only the Hunter can shoot.";
                return;
            }

            var ray = GetAimRay();
            var origin = ray.origin;
            var direction = ray.direction;
            ShootServerRpc(origin, direction);
        }

        public void ResetPaint()
        {
            if (!NetworkObject.IsLocalPlayer) return;
            paint?.RequestClear();
            SetColorsServerRpc(Color.white, Color.white);
        }

        public void CycleHeadColor()
        {
            if (!NetworkObject.IsLocalPlayer) return;
            SetColorsServerRpc(NextPaletteColor(HeadColor.Value), BodyColor.Value);
        }

        public void CycleBodyColor()
        {
            if (!NetworkObject.IsLocalPlayer) return;
            SetColorsServerRpc(HeadColor.Value, NextPaletteColor(BodyColor.Value));
        }

        void RespawnIfFallen()
        {
            if (transform.position.y >= fallRespawnY) return;

            var respawnPosition = ChameleonRoundManager.Instance != null
                ? ChameleonRoundManager.Instance.GetRespawnPosition(OwnerClientId)
                : Vector3.up;

            Teleport(respawnPosition + Vector3.up);
        }

        void Teleport(Vector3 position)
        {
            ReleaseWallLatch();
            verticalVelocity = 0f;
            if (controller != null) controller.enabled = false;
            transform.position = position;
            if (controller != null) controller.enabled = true;
        }

        public void TeleportFromServer(Vector3 position)
        {
            if (!IsServer) return;

            Teleport(position);
            TeleportClientRpc(position, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            });
        }

        [ClientRpc]
        void TeleportClientRpc(Vector3 position, ClientRpcParams clientRpcParams = default)
        {
            if (!NetworkObject.IsLocalPlayer) return;
            Teleport(position);
        }

        [ServerRpc]
        void SetPoseServerRpc(PoseId pose)
        {
            Pose.Value = pose;
        }

        [ServerRpc]
        void SetColorsServerRpc(Color32 head, Color32 body)
        {
            HeadColor.Value = head;
            BodyColor.Value = body;
        }

        [ServerRpc]
        void ShootServerRpc(Vector3 origin, Vector3 direction, ServerRpcParams rpcParams = default)
        {
            var hit = TryResolveShot(origin, direction, applyHit: true, out var endpoint);
            ShowShotRayClientRpc(origin, endpoint);
            ShotFeedbackClientRpc(hit ? "Hit hider." : "Missed.", new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            });
        }

        public bool ShootFromServer(Vector3 origin, Vector3 direction)
        {
            return TryResolveShot(origin, direction, applyHit: true, out _);
        }

        bool TryResolveShot(Vector3 origin, Vector3 direction, bool applyHit, out Vector3 endpoint)
        {
            endpoint = origin + direction.normalized * 60f;
            if (!IsServer || Role.Value != PlayerRole.Seeker) return false;

            var hits = Physics.RaycastAll(origin, direction, 60f)
                .OrderBy(hit => hit.distance);

            foreach (var hit in hits)
            {
                var target = hit.collider.GetComponentInParent<ChameleonPlayer>();
                if (target == this)
                    continue;

                endpoint = hit.point;

                if (target == null)
                    return false;

                if (applyHit)
                    ChameleonRoundManager.Instance?.ReportHit(target);

                return true;
            }

            return false;
        }

        Ray GetAimRay()
        {
            if (playerCamera == null)
                return new Ray(transform.position + Vector3.up, transform.forward);

            if (Cursor.lockState == CursorLockMode.Locked)
                return playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            var mousePosition = Input.mousePosition;
            if (mousePosition.x < 0f || mousePosition.x > Screen.width || mousePosition.y < 0f || mousePosition.y > Screen.height)
                mousePosition = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);

            return playerCamera.ScreenPointToRay(mousePosition);
        }

        [ClientRpc]
        void ShotFeedbackClientRpc(string message, ClientRpcParams clientRpcParams = default)
        {
            LastShotStatus = message;
        }

        [ClientRpc]
        void ShowShotRayClientRpc(Vector3 origin, Vector3 endpoint)
        {
            if (shotLine == null) return;

            shotLine.positionCount = 2;
            shotLine.SetPosition(0, origin);
            shotLine.SetPosition(1, endpoint);
            shotLineHideAt = Time.time + 0.35f;
            SetShotLineVisible(true);
        }

        void ApplyColors()
        {
            if (paint != null && paint.ApplyBaseColors(HeadColor.Value, BodyColor.Value))
                return;

            if (headMaterial != null) headMaterial.color = HeadColor.Value;
            if (bodyMaterial != null) bodyMaterial.color = BodyColor.Value;
        }

        void ApplyPose(PoseId pose)
        {
            transform.localScale = Vector3.one;
            EnsureRuntimeVisuals();

            var poseRotation = pose switch
            {
                PoseId.Crouch => Quaternion.Euler(0f, 0f, -50f),
                PoseId.Lie => Quaternion.Euler(82f, 0f, 0f),
                _ => Quaternion.identity
            };

            if (visualRoot != null)
            {
                visualRoot.localPosition = pose switch
                {
                    PoseId.Crouch => LeanVisualOffset,
                    PoseId.Lie => LieVisualOffset,
                    _ => Vector3.zero
                };
                visualRoot.localRotation = poseRotation;
                ResetMovementController();
                if (bodyRenderer != null)
                    bodyRenderer.transform.localRotation = Quaternion.identity;

                if (headRenderer != null)
                    headRenderer.transform.localRotation = Quaternion.identity;

                UpdateCameraPosition();
                return;
            }

            if (bodyRenderer != null)
                bodyRenderer.transform.localRotation = poseRotation;

            if (headRenderer != null)
                headRenderer.transform.localRotation = poseRotation;

            UpdateCameraPosition();
        }

        void ResetMovementController()
        {
            if (controller == null) return;

            controller.radius = 0.22f;
            controller.height = 0.9f;
            controller.center = new Vector3(0f, 0.45f, 0f);
        }

        public void SetPaintCameraOrbit(float yaw)
        {
            paintCameraOrbitYaw = yaw;
            UpdateCameraPosition();
        }

        void SetVisible(bool visible)
        {
            if (headRenderer != null) headRenderer.enabled = visible;
            if (bodyRenderer != null) bodyRenderer.enabled = visible;
            if (gunRoot != null) gunRoot.SetActive(visible && Role.Value == PlayerRole.Seeker);
        }

        void DisableOverviewCameras()
        {
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (camera != playerCamera && camera.CompareTag("MainCamera"))
                    camera.enabled = false;
            }
        }

        void UpdateCameraPosition()
        {
            if (playerCamera == null || !NetworkObject.IsLocalPlayer) return;

            if (Role.Value == PlayerRole.Seeker)
            {
                playerCamera.transform.localPosition = new Vector3(0f, 1.05f, 0.12f);
                playerCamera.transform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
                SetLocalRenderersVisible(false);
                SetGunVisible(true);
                return;
            }

            SetLocalRenderersVisible(true);
            SetGunVisible(false);
            cameraPitch = 12f;
            playerCamera.transform.localRotation = Quaternion.Euler(14f, paintCameraOrbitYaw, 0f);

            var focus = transform.position + Vector3.up * 0.85f;
            var orbitRotation = Quaternion.Euler(0f, paintCameraOrbitYaw, 0f);
            var desiredWorld = transform.TransformPoint(orbitRotation * new Vector3(0f, 1.2f, -2.2f));
            var direction = desiredWorld - focus;
            var distance = direction.magnitude;

            if (distance > 0.01f)
            {
                var hits = Physics.SphereCastAll(focus, 0.18f, direction.normalized, distance, ~0, QueryTriggerInteraction.Ignore)
                    .OrderBy(hit => hit.distance);

                foreach (var hit in hits)
                {
                    if (hit.collider.GetComponentInParent<ChameleonPlayer>() == this)
                        continue;

                    desiredWorld = focus + direction.normalized * Mathf.Max(0.35f, hit.distance - 0.12f);
                    break;
                }
            }

            playerCamera.transform.position = desiredWorld;
        }

        void SetLocalRenderersVisible(bool visible)
        {
            if (headRenderer != null) headRenderer.enabled = visible && Alive.Value;
            if (bodyRenderer != null) bodyRenderer.enabled = visible && Alive.Value;
        }

        void SetGunVisible(bool visible)
        {
            if (gunRoot != null) gunRoot.SetActive(visible && Alive.Value);
        }

        void UpdateShotLine()
        {
            if (shotLine != null && shotLine.enabled && Time.time >= shotLineHideAt)
                SetShotLineVisible(false);
        }

        void SetShotLineVisible(bool visible)
        {
            if (shotLine != null)
                shotLine.enabled = visible;
        }

        static bool CaptureCursor()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ReleaseCursor();
                return false;
            }

            if (Cursor.lockState == CursorLockMode.Locked)
                return true;

            if (Input.GetMouseButtonDown(0) && Input.mousePosition.x > 340f)
            {
                LockCursor();
                return false;
            }

            return false;
        }

        static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        static void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        static Color32 NextPaletteColor(Color32 current)
        {
            var index = 0;
            var bestDistance = int.MaxValue;
            for (var i = 0; i < ChameleonPalette.Colors.Length; i++)
            {
                var distance = ColorDistance(current, ChameleonPalette.Colors[i]);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                index = i;
            }

            return ChameleonPalette.Colors[(index + 1) % ChameleonPalette.Colors.Length];
        }

        static int ColorDistance(Color32 a, Color32 b)
        {
            var r = a.r - b.r;
            var g = a.g - b.g;
            var blue = a.b - b.b;
            return r * r + g * g + blue * blue;
        }

        void EnsureRuntimeVisuals()
        {
            if (paint == null)
                paint = GetComponent<ChameleonPaint>();

            if (visualRoot == null && (bodyRenderer != null || headRenderer != null))
            {
                var root = new GameObject("Visual Root");
                visualRoot = root.transform;
                visualRoot.SetParent(transform, false);

                if (bodyRenderer != null)
                    bodyRenderer.transform.SetParent(visualRoot, true);

                if (headRenderer != null)
                    headRenderer.transform.SetParent(visualRoot, true);
            }

            if (gunRoot == null && playerCamera != null)
                gunRoot = CreateRuntimeGun(playerCamera.transform);

            if (shotLine == null)
                shotLine = CreateRuntimeShotLine(transform);
        }

        static GameObject CreateRuntimeGun(Transform parent)
        {
            var root = new GameObject("Hunter Gun");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0.28f, -0.28f, 0.55f);
            root.transform.localRotation = Quaternion.Euler(0f, -4f, 0f);

            var dark = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            {
                color = new Color(0.08f, 0.09f, 0.10f)
            };

            var gray = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            {
                color = new Color(0.33f, 0.34f, 0.35f)
            };

            CreateGunPart("Gun Body", root.transform, Vector3.zero, Quaternion.identity, new Vector3(0.22f, 0.16f, 0.48f), dark);
            CreateGunPart("Gun Barrel", root.transform, new Vector3(0f, 0.03f, 0.32f), Quaternion.identity, new Vector3(0.1f, 0.08f, 0.36f), gray);
            CreateGunPart("Gun Grip", root.transform, new Vector3(0f, -0.17f, -0.08f), Quaternion.Euler(-16f, 0f, 0f), new Vector3(0.12f, 0.24f, 0.12f), dark);

            root.SetActive(false);
            return root;
        }

        static void CreateGunPart(string name, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;
            var renderer = part.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material = material;
        }

        static LineRenderer CreateRuntimeShotLine(Transform parent)
        {
            var lineObject = new GameObject("Shot Ray");
            lineObject.transform.SetParent(parent, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.startWidth = 0.045f;
            line.endWidth = 0.018f;
            line.useWorldSpace = true;
            line.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard"))
            {
                color = new Color(1f, 0.92f, 0.16f)
            };
            line.enabled = false;
            return line;
        }
    }
}
