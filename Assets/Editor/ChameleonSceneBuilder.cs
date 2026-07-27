using MechaChameleon;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MechaChameleon.Editor
{
    public static class ChameleonSceneBuilder
    {
        [MenuItem("Mecha Chameleon/Build MVP Scene")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateTexturedProp("Lobby Floor", Vector3.zero, new Vector3(18f, 0.2f, 12f),
                MakeWoodMaterial("Lobby Oak Floor", new Color(0.42f, 0.25f, 0.13f), new Color(0.60f, 0.39f, 0.21f)), new Vector2(6f, 4f));
            CreateProp("Lobby Ceiling", new Vector3(0f, 4.5f, 0f), new Vector3(18f, 0.15f, 12f), new Color(0.91f, 0.89f, 0.84f));
            var lobbyWallpaper = MakeWallpaperMaterial("Lobby Stripe Wallpaper", new Color(0.73f, 0.78f, 0.71f), new Color(0.56f, 0.65f, 0.57f), 0);
            CreateTexturedProp("Lobby North Wall", new Vector3(0f, 2.25f, 6f), new Vector3(18f, 4.5f, 0.4f), lobbyWallpaper, new Vector2(8f, 2f));
            CreateTexturedProp("Lobby South Wall", new Vector3(0f, 2.25f, -6f), new Vector3(18f, 4.5f, 0.4f), lobbyWallpaper, new Vector2(8f, 2f));
            CreateTexturedProp("Lobby West Wall", new Vector3(-9f, 2.25f, 0f), new Vector3(0.4f, 4.5f, 12f), lobbyWallpaper, new Vector2(8f, 2f));
            CreateTexturedProp("Lobby East Wall", new Vector3(9f, 2.25f, 0f), new Vector3(0.4f, 4.5f, 12f), lobbyWallpaper, new Vector2(8f, 2f));
            CreateTrim("Lobby Baseboard North", new Vector3(0f, 0.28f, 5.75f), new Vector3(17.6f, 0.22f, 0.12f));
            CreateTrim("Lobby Baseboard South", new Vector3(0f, 0.28f, -5.75f), new Vector3(17.6f, 0.22f, 0.12f));
            var hunterPlatform = CreateProp("Hunter Choice Platform", new Vector3(0f, 0.15f, -1.5f), new Vector3(4f, 0.15f, 3f), new Color(0.95f, 0.78f, 0.18f));

            BuildHidingRoom();

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            light.color = new Color(1f, 0.94f, 0.84f);
            light.intensity = 0.3f;
            light.shadows = LightShadows.Hard;
            light.shadowStrength = 0.35f;
            light.lightmapBakeType = LightmapBakeType.Mixed;
            light.renderMode = LightRenderMode.ForcePixel;
            CreateRoomLight("Lobby Light", new Vector3(0f, 3.8f, 0f), 12f, 1.2f);
            CreateRoomLight("Living Room Light", new Vector3(-5f, 3.8f, 28f), 11f, 1.15f);
            CreateRoomLight("Bedroom Light", new Vector3(6f, 3.8f, 28f), 11f, 1.05f);
            CreateLightProbes();
            CreateReflectionProbe("Lobby Reflection Probe", new Vector3(0f, 2.2f, 0f), new Vector3(17f, 4f, 11f));
            CreateReflectionProbe("Hiding Room Reflection Probe", new Vector3(0f, 2.2f, 28f), new Vector3(23f, 4f, 15f));
            ConfigureLighting();

            var networkManager = new GameObject("NetworkManager");
            var manager = networkManager.AddComponent<NetworkManager>();
            var transport = networkManager.AddComponent<UnityTransport>();
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.ForceSamePrefabs = false;

            var playerPrefab = BuildPlayerPrefab();
            var roundObject = new GameObject("RoundManager");
            var roundNetObj = roundObject.AddComponent<NetworkObject>();
            var round = roundObject.AddComponent<ChameleonRoundManager>();

            var so = new SerializedObject(round);
            so.FindProperty("playerPrefab").objectReferenceValue = playerPrefab.GetComponent<ChameleonPlayer>();
            so.FindProperty("hunterPlatform").objectReferenceValue = hunterPlatform.transform;
            so.FindProperty("hunterPlatformSize").vector3Value = new Vector3(4f, 3f, 3f);
            so.FindProperty("spawnPoints").arraySize = 4;
            for (var i = 0; i < 4; i++)
            {
                var spawn = new GameObject($"Spawn {i + 1}").transform;
                spawn.position = new Vector3(-4f + i * 2.5f, 1f, -3.5f);
                so.FindProperty("spawnPoints").GetArrayElementAtIndex(i).objectReferenceValue = spawn;
            }

            so.FindProperty("hiderSpawnPoints").arraySize = 4;
            for (var i = 0; i < 4; i++)
            {
                var spawn = new GameObject($"Hider Spawn {i + 1}").transform;
                spawn.position = new Vector3(-4f + i * 2.5f, 1f, 24f);
                so.FindProperty("hiderSpawnPoints").GetArrayElementAtIndex(i).objectReferenceValue = spawn;
            }

            var hunterSpawn = new GameObject("Hunter Spawn").transform;
            hunterSpawn.position = new Vector3(0f, 1f, 20.8f);
            so.FindProperty("hunterSpawnPoint").objectReferenceValue = hunterSpawn;
            so.ApplyModifiedProperties();

            DeleteAssetIfExists("Assets/NetworkPrefabs.asset");

            var prefabs = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            AssetDatabase.CreateAsset(prefabs, "Assets/NetworkPrefabs.asset");
            prefabs.Add(new NetworkPrefab { Prefab = playerPrefab });
            EditorUtility.SetDirty(prefabs);

            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabs);

            roundNetObj.SpawnWithObservers = true;

            var app = new GameObject("App");
            var connector = app.AddComponent<RoomConnector>();
            var hud = app.AddComponent<MvpHud>();
            var hudSo = new SerializedObject(hud);
            hudSo.FindProperty("connector").objectReferenceValue = connector;
            hudSo.ApplyModifiedProperties();

            var camera = new GameObject("Overview Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 3.4f, -4.8f);
            camera.transform.rotation = Quaternion.Euler(18f, 0f, 0f);
            camera.allowHDR = false;

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Mvp.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Mvp.unity", true) };
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Mecha Chameleon/Bake Optimized Lighting")]
        public static void BakeOptimizedLighting()
        {
            if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Mvp.unity")
                EditorSceneManager.OpenScene("Assets/Scenes/Mvp.unity", OpenSceneMode.Single);

            Lightmapping.Clear();
            if (!Lightmapping.Bake())
                Debug.LogError("Optimized lighting bake did not complete.");
            else
            {
                EditorSceneManager.SaveOpenScenes();
                AssetDatabase.SaveAssets();
                Debug.Log("Optimized lighting bake completed.");
            }
        }

        [MenuItem("Mecha Chameleon/Rebuild Player Prefab")]
        public static void RebuildPlayerPrefab()
        {
            BuildPlayerPrefab();
            AssetDatabase.SaveAssets();
        }

        static void BuildHidingRoom()
        {
            CreateTexturedProp("Hiding Room Floor", new Vector3(0f, 0f, 28f), new Vector3(24f, 0.2f, 16f),
                MakeWoodMaterial("Hiding Walnut Floor", new Color(0.30f, 0.16f, 0.09f), new Color(0.50f, 0.29f, 0.15f)), new Vector2(8f, 5f));
            CreateProp("Hiding Room Ceiling", new Vector3(0f, 4.5f, 28f), new Vector3(24f, 0.15f, 16f), new Color(0.93f, 0.91f, 0.86f));
            CreateTexturedProp("Hiding North Wall", new Vector3(0f, 2.25f, 36f), new Vector3(24f, 4.5f, 0.4f),
                MakeWallpaperMaterial("Blue Diamond Wallpaper", new Color(0.70f, 0.78f, 0.80f), new Color(0.38f, 0.53f, 0.60f), 1), new Vector2(10f, 2f));
            CreateTexturedProp("Hiding South Wall", new Vector3(0f, 2.25f, 20f), new Vector3(24f, 4.5f, 0.4f),
                MakeWallpaperMaterial("Warm Dot Wallpaper", new Color(0.84f, 0.76f, 0.66f), new Color(0.63f, 0.40f, 0.31f), 2), new Vector2(10f, 2f));
            CreateTexturedProp("Hiding West Wall", new Vector3(-12f, 2.25f, 28f), new Vector3(0.4f, 4.5f, 16f),
                MakeWallpaperMaterial("Sage Stripe Wallpaper", new Color(0.68f, 0.75f, 0.65f), new Color(0.39f, 0.53f, 0.40f), 0), new Vector2(7f, 2f));
            CreateTexturedProp("Hiding East Wall", new Vector3(12f, 2.25f, 28f), new Vector3(0.4f, 4.5f, 16f),
                MakeWallpaperMaterial("Green Lattice Wallpaper", new Color(0.73f, 0.77f, 0.68f), new Color(0.42f, 0.52f, 0.36f), 3), new Vector2(7f, 2f));

            CreateTrim("Baseboard North", new Vector3(0f, 0.28f, 35.75f), new Vector3(23.6f, 0.22f, 0.12f));
            CreateTrim("Baseboard South", new Vector3(0f, 0.28f, 20.25f), new Vector3(23.6f, 0.22f, 0.12f));
            CreateTrim("Baseboard West", new Vector3(-11.75f, 0.28f, 28f), new Vector3(0.12f, 0.22f, 15.6f));
            CreateTrim("Baseboard East", new Vector3(11.75f, 0.28f, 28f), new Vector3(0.12f, 0.22f, 15.6f));

            CreateWindow("Window Left", new Vector3(-4.2f, 2.55f, 35.76f));
            CreateWindow("Window Right", new Vector3(4.2f, 2.55f, 35.76f));

            CreateFurniture("Long Sofa", "loungeSofaLong", new Vector3(-9f, 0.1f, 28.5f), 0f, 1.25f);
            CreateFurniture("Lounge Chair", "loungeChair", new Vector3(-7.4f, 0.1f, 22.8f), 25f, 1.15f);
            var coffeeTable = CreateFurniture("Coffee Table", "tableCoffee", new Vector3(-5.3f, 0.1f, 28.5f), 0f, 1.2f);
            CreateFurniture("Living Room Rug", "rugRectangle", new Vector3(-5.5f, 0.11f, 28.5f), 90f, 2.1f, collidable: false);
            var tvCabinet = CreateFurniture("TV Cabinet", "cabinetTelevision", new Vector3(-1.2f, 0.1f, 28.5f), -90f, 1.25f);
            CreateFurniture("Television", "televisionModern",
                new Vector3(-1.2f, GetFurnitureTop(tvCabinet), 28.5f), -90f, 1.25f, collidable: false);
            CreateFurniture("Floor Lamp", "lampRoundFloor", new Vector3(-10f, 0.1f, 34f), 0f, 1.15f);
            CreateFurniture("Living Plant", "pottedPlant", new Vector3(-10.2f, 0.1f, 21.6f), 0f, 1.2f);

            CreateFurniture("Wide Bookcase", "bookcaseClosedWide", new Vector3(-6.5f, 0.1f, 34.6f), 180f, 1.25f);
            CreateFurniture("Open Bookcase", "bookcaseOpen", new Vector3(-2.7f, 0.1f, 34.6f), 180f, 1.25f);
            CreateFurniture("Book Stack", "books",
                new Vector3(-5.3f, GetFurnitureTop(coffeeTable), 28.5f), 15f, 1.1f, collidable: false);
            var writingDesk = CreateFurniture("Writing Desk", "desk", new Vector3(3.4f, 0.1f, 34.5f), 180f, 1.2f);
            CreateFurniture("Desk Chair", "chairDesk", new Vector3(3.4f, 0.1f, 31.1f), 0f, 1.1f);
            CreateFurniture("Desk Screen", "computerScreen",
                new Vector3(3.4f, GetFurnitureTop(writingDesk), 34.3f), 180f, 1.15f, collidable: false);

            CreateFurniture("Single Bed", "bedSingle", new Vector3(8.6f, 0.1f, 23.2f), 90f, 1.25f);
            CreateFurniture("Bedside Drawers", "sideTableDrawers", new Vector3(6.2f, 0.1f, 21.3f), 0f, 1.1f);
            CreateFurniture("Bedroom Plant", "plantSmall1", new Vector3(10.2f, 0.1f, 28.3f), 0f, 1.25f);
            CreateFurniture("Small Fridge", "kitchenFridgeSmall", new Vector3(10.2f, 0.1f, 34.1f), 180f, 1.15f);
            CreateFurniture("Closed Moving Box", "cardboardBoxClosed", new Vector3(7.1f, 0.1f, 29.4f), 12f, 1.4f);
            CreateFurniture("Open Moving Box", "cardboardBoxOpen", new Vector3(8.8f, 0.1f, 31f), -18f, 1.25f);
        }

        static GameObject CreateFurniture(string name, string model, Vector3 position, float yaw, float scale,
            bool collidable = true)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ThirdParty/KenneyFurniture/Models/{model}.fbx");
            if (prefab == null)
            {
                Debug.LogWarning($"Missing furniture model: {model}");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
            instance.transform.localScale = Vector3.one * (scale * 0.28f);

            // Treat the requested X/Z as the visible center and Y as the supporting surface.
            if (TryGetRendererBounds(instance, out var bounds))
            {
                instance.transform.position = new Vector3(
                    position.x - bounds.center.x,
                    position.y - bounds.min.y,
                    position.z - bounds.center.z);
                KeepInsideHidingRoom(instance);
            }

            SetFurnitureLighting(instance);

            if (collidable)
            {
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
                {
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                }
            }

            return instance;
        }

        static float GetFurnitureTop(GameObject furniture)
        {
            return furniture != null && TryGetRendererBounds(furniture, out var bounds) ? bounds.max.y : 0.1f;
        }

        static void KeepInsideHidingRoom(GameObject furniture)
        {
            if (!TryGetRendererBounds(furniture, out var bounds)) return;

            const float minX = -11.4f;
            const float maxX = 11.4f;
            const float minZ = 20.6f;
            const float maxZ = 35.4f;
            var correction = Vector3.zero;

            if (bounds.min.x < minX) correction.x += minX - bounds.min.x;
            if (bounds.max.x > maxX) correction.x -= bounds.max.x - maxX;
            if (bounds.min.z < minZ) correction.z += minZ - bounds.min.z;
            if (bounds.max.z > maxZ) correction.z -= bounds.max.z - maxZ;
            furniture.transform.position += correction;
        }

        static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        static void CreateWindow(string name, Vector3 position)
        {
            CreateProp(name + " Glass", position, new Vector3(3.2f, 2.1f, 0.08f), new Color(0.32f, 0.55f, 0.68f));
            CreateTrim(name + " Top", position + Vector3.up * 1.15f, new Vector3(3.6f, 0.16f, 0.16f));
            CreateTrim(name + " Bottom", position + Vector3.down * 1.15f, new Vector3(3.6f, 0.16f, 0.16f));
            CreateTrim(name + " Left", position + Vector3.left * 1.7f, new Vector3(0.16f, 2.45f, 0.16f));
            CreateTrim(name + " Right", position + Vector3.right * 1.7f, new Vector3(0.16f, 2.45f, 0.16f));
        }

        static void CreateTrim(string name, Vector3 position, Vector3 scale)
        {
            CreateProp(name, position, scale, new Color(0.26f, 0.17f, 0.11f));
        }

        static void CreateRoomLight(string name, Vector3 position, float range, float intensity)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.84f, 0.66f);
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.Soft;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.bounceIntensity = 0.85f;
            light.transform.position = position;
        }

        static void CreateLightProbes()
        {
            var group = new GameObject("Indoor Light Probes").AddComponent<LightProbeGroup>();
            var probes = new System.Collections.Generic.List<Vector3>();

            AddProbeGrid(probes, new Vector3(-8.2f, 0.55f, -5.2f), new Vector3(8.2f, 2.5f, 5.2f), 4, 2, 4);
            AddProbeGrid(probes, new Vector3(-11.1f, 0.55f, 20.7f), new Vector3(11.1f, 2.5f, 35.3f), 5, 2, 4);
            group.probePositions = probes.ToArray();
        }

        static void AddProbeGrid(System.Collections.Generic.List<Vector3> probes, Vector3 min, Vector3 max,
            int xCount, int yCount, int zCount)
        {
            for (var y = 0; y < yCount; y++)
            for (var z = 0; z < zCount; z++)
            for (var x = 0; x < xCount; x++)
            {
                probes.Add(new Vector3(
                    Mathf.Lerp(min.x, max.x, x / (float)(xCount - 1)),
                    Mathf.Lerp(min.y, max.y, y / (float)(yCount - 1)),
                    Mathf.Lerp(min.z, max.z, z / (float)(zCount - 1))));
            }
        }

        static void CreateReflectionProbe(string name, Vector3 position, Vector3 size)
        {
            var probe = new GameObject(name).AddComponent<ReflectionProbe>();
            probe.transform.position = position;
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = size;
            probe.resolution = 64;
            probe.boxProjection = true;
            probe.blendDistance = 1f;
            probe.intensity = 0.65f;
            probe.importance = 1;
        }

        static void ConfigureLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.34f, 0.38f, 0.43f);
            RenderSettings.ambientEquatorColor = new Color(0.30f, 0.27f, 0.24f);
            RenderSettings.ambientGroundColor = new Color(0.13f, 0.11f, 0.10f);
            RenderSettings.ambientIntensity = 0.75f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 64;
            RenderSettings.reflectionBounces = 1;
            RenderSettings.reflectionIntensity = 0.55f;

            if (!AssetDatabase.IsValidFolder("Assets/Lighting"))
                AssetDatabase.CreateFolder("Assets", "Lighting");

            const string settingsPath = "Assets/Lighting/Indoor Lighting Settings.lighting";
            DeleteAssetIfExists(settingsPath);
            var settings = new LightingSettings
            {
                bakedGI = true,
                realtimeGI = false,
                realtimeEnvironmentLighting = false,
                mixedBakeMode = MixedLightingMode.Subtractive,
                lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
                lightmapResolution = 12f,
                lightmapPadding = 2,
                lightmapMaxSize = 1024,
                maxBounces = 2,
                directSampleCount = 32,
                indirectSampleCount = 128,
                environmentSampleCount = 64,
                lightProbeSampleCountMultiplier = 2f,
                ao = true,
                aoMaxDistance = 1.25f,
                aoExponentIndirect = 1.15f
            };
            AssetDatabase.CreateAsset(settings, settingsPath);
            Lightmapping.lightingSettings = settings;
        }

        static void SetFurnitureLighting(GameObject root)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(child.gameObject,
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.ReflectionProbeStatic);

                if (!child.TryGetComponent<Renderer>(out var renderer)) continue;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            }
        }

        static GameObject BuildPlayerPrefab()
        {
            var root = new GameObject("ChameleonPlayer");
            var controller = root.AddComponent<CharacterController>();
            controller.height = 0.9f;
            controller.radius = 0.22f;
            controller.center = new Vector3(0f, 0.45f, 0f);
            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkTransform>().AuthorityMode = NetworkTransform.AuthorityModes.Owner;

            var visualRoot = new GameObject("Visual Root");
            visualRoot.transform.SetParent(root.transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(visualRoot.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            body.transform.localScale = new Vector3(0.35f, 0.45f, 0.35f);
            Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());
            var bodyPaintCollider = body.AddComponent<MeshCollider>();
            bodyPaintCollider.sharedMesh = body.GetComponent<MeshFilter>().sharedMesh;
            bodyPaintCollider.enabled = false;

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(visualRoot.transform, false);
            head.transform.localPosition = new Vector3(0f, 1f, 0f);
            head.transform.localScale = Vector3.one * 0.28f;
            Object.DestroyImmediate(head.GetComponent<SphereCollider>());
            var headPaintCollider = head.AddComponent<MeshCollider>();
            headPaintCollider.sharedMesh = head.GetComponent<MeshFilter>().sharedMesh;
            headPaintCollider.enabled = false;

            // A uniform ambient sample prevents abrupt darkening when a networked
            // player crosses baked probe tetrahedron boundaries.
            body.GetComponent<Renderer>().lightProbeUsage = LightProbeUsage.Off;
            head.GetComponent<Renderer>().lightProbeUsage = LightProbeUsage.Off;

            var cam = new GameObject("Player Camera").AddComponent<Camera>();
            cam.transform.SetParent(root.transform, false);
            cam.transform.localPosition = new Vector3(0f, 1.2f, -2.2f);
            cam.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
            cam.enabled = false;
            cam.allowHDR = false;

            var gunRoot = new GameObject("Hunter Gun");
            gunRoot.transform.SetParent(cam.transform, false);
            gunRoot.transform.localPosition = new Vector3(0.28f, -0.28f, 0.55f);
            gunRoot.transform.localRotation = Quaternion.Euler(0f, -4f, 0f);
            gunRoot.SetActive(false);

            var gunBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gunBody.name = "Gun Body";
            gunBody.transform.SetParent(gunRoot.transform, false);
            gunBody.transform.localPosition = Vector3.zero;
            gunBody.transform.localScale = new Vector3(0.22f, 0.16f, 0.48f);
            gunBody.GetComponent<Renderer>().sharedMaterial = MakeMaterial("Gun Dark Metal", new Color(0.08f, 0.09f, 0.10f));

            var gunBarrel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gunBarrel.name = "Gun Barrel";
            gunBarrel.transform.SetParent(gunRoot.transform, false);
            gunBarrel.transform.localPosition = new Vector3(0f, 0.03f, 0.32f);
            gunBarrel.transform.localScale = new Vector3(0.1f, 0.08f, 0.36f);
            gunBarrel.GetComponent<Renderer>().sharedMaterial = MakeMaterial("Gun Barrel Gray", new Color(0.33f, 0.34f, 0.35f));

            var gunGrip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gunGrip.name = "Gun Grip";
            gunGrip.transform.SetParent(gunRoot.transform, false);
            gunGrip.transform.localPosition = new Vector3(0f, -0.17f, -0.08f);
            gunGrip.transform.localRotation = Quaternion.Euler(-16f, 0f, 0f);
            gunGrip.transform.localScale = new Vector3(0.12f, 0.24f, 0.12f);
            gunGrip.GetComponent<Renderer>().sharedMaterial = gunBody.GetComponent<Renderer>().sharedMaterial;

            var shotLineObject = new GameObject("Shot Ray");
            shotLineObject.transform.SetParent(root.transform, false);
            var shotLine = shotLineObject.AddComponent<LineRenderer>();
            shotLine.positionCount = 2;
            shotLine.startWidth = 0.045f;
            shotLine.endWidth = 0.018f;
            shotLine.useWorldSpace = true;
            shotLine.material = MakeMaterial("Shot Ray Yellow", new Color(1f, 0.92f, 0.16f), unlit: true);
            shotLine.enabled = false;

            var player = root.AddComponent<ChameleonPlayer>();
            var paint = root.AddComponent<ChameleonPaint>();

            var playerSo = new SerializedObject(player);
            playerSo.FindProperty("headRenderer").objectReferenceValue = head.GetComponent<Renderer>();
            playerSo.FindProperty("bodyRenderer").objectReferenceValue = body.GetComponent<Renderer>();
            playerSo.FindProperty("playerCamera").objectReferenceValue = cam;
            playerSo.FindProperty("visualRoot").objectReferenceValue = visualRoot.transform;
            playerSo.FindProperty("gunRoot").objectReferenceValue = gunRoot;
            playerSo.FindProperty("shotLine").objectReferenceValue = shotLine;
            playerSo.FindProperty("paint").objectReferenceValue = paint;
            playerSo.ApplyModifiedProperties();

            var paintSo = new SerializedObject(paint);
            paintSo.FindProperty("player").objectReferenceValue = player;
            paintSo.FindProperty("headRenderer").objectReferenceValue = head.GetComponent<Renderer>();
            paintSo.FindProperty("bodyRenderer").objectReferenceValue = body.GetComponent<Renderer>();
            paintSo.FindProperty("playerCamera").objectReferenceValue = cam;
            paintSo.FindProperty("headPaintCollider").objectReferenceValue = headPaintCollider;
            paintSo.FindProperty("bodyPaintCollider").objectReferenceValue = bodyPaintCollider;
            paintSo.ApplyModifiedProperties();

            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/ChameleonPlayer.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void DeleteAssetIfExists(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                AssetDatabase.DeleteAsset(path);
        }

        static GameObject CreateProp(string name, Vector3 position, Vector3 scale, Color color)
        {
            var prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prop.name = name;
            prop.transform.position = position;
            prop.transform.localScale = scale;
            prop.GetComponent<Renderer>().sharedMaterial = MakeMaterial(name, color);
            GameObjectUtility.SetStaticEditorFlags(prop,
                StaticEditorFlags.ContributeGI |
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic);
            return prop;
        }

        static GameObject CreateTexturedProp(string name, Vector3 position, Vector3 scale, Material material, Vector2 tiling)
        {
            var prop = CreateProp(name, position, scale, Color.white);
            material.mainTextureScale = tiling;
            prop.GetComponent<Renderer>().sharedMaterial = material;
            EditorUtility.SetDirty(material);
            return prop;
        }

        static Material MakeWallpaperMaterial(string name, Color background, Color accent, int pattern)
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGB24, true)
            {
                name = name + " Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };

            for (var y = 0; y < texture.height; y++)
            for (var x = 0; x < texture.width; x++)
            {
                var marked = pattern switch
                {
                    0 => x % 16 < 4,
                    1 => Mathf.Abs(Mathf.Abs(x % 16 - 8) + Mathf.Abs(y % 16 - 8) - 7) < 1.5f,
                    2 => Mathf.Pow(x % 16 - 8, 2) + Mathf.Pow(y % 16 - 8, 2) < 8,
                    _ => x % 16 < 2 || y % 16 < 2
                };
                texture.SetPixel(x, y, marked ? accent : background);
            }

            texture.Apply();
            return SaveTexturedMaterial(name, texture);
        }

        static Material MakeWoodMaterial(string name, Color dark, Color light)
        {
            var texture = new Texture2D(128, 128, TextureFormat.RGB24, true)
            {
                name = name + " Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };

            for (var y = 0; y < texture.height; y++)
            for (var x = 0; x < texture.width; x++)
            {
                var row = y / 16;
                var seam = y % 16 < 2 || (x + (row % 2) * 32) % 64 < 2;
                var grain = 0.45f + Mathf.Sin(x * 0.24f + y * 0.11f) * 0.13f;
                texture.SetPixel(x, y, seam ? dark * 0.55f : Color.Lerp(dark, light, grain));
            }

            texture.Apply();
            return SaveTexturedMaterial(name, texture);
        }

        static Material SaveTexturedMaterial(string name, Texture2D texture)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");

            var texturePath = $"Assets/Materials/{name} Texture.asset";
            DeleteAssetIfExists(texturePath);
            AssetDatabase.CreateAsset(texture, texturePath);

            var material = MakeMaterial(name, Color.white);
            material.mainTexture = texture;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material MakeMaterial(string name, Color color, bool unlit = false)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");

            var shader = unlit
                ? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard")
                : Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader)
            {
                name = name,
                color = color
            };

            var path = $"Assets/Materials/{name}.mat";
            DeleteAssetIfExists(path);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
