using System;
using MechaChameleon.Rooms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MechaChameleon.Editor
{
    public static class ModularContentTools
    {
        [MenuItem("GameObject/Mecha Chameleon/Room Module", false, 10)]
        static void CreateRoomModule(MenuCommand command)
        {
            var root = new GameObject("New Room Module");
            Undo.RegisterCreatedObjectUndo(root, "Create room module");
            GameObjectUtility.SetParentAndAlign(root, command.context as GameObject);

            var content = CreateChild(root.transform, "Content");
            var lobbyGroup = CreateChild(content.transform, "Lobby Spawns");
            var hiderGroup = CreateChild(content.transform, "Hider Spawns");
            var lobbySpawns = CreateSpawns(lobbyGroup.transform, "Lobby Spawn", -3.5f);
            var hiderSpawns = CreateSpawns(hiderGroup.transform, "Hider Spawn", 24f);

            var hunterSpawn = CreateChild(content.transform, "Hunter Spawn").transform;
            hunterSpawn.localPosition = new Vector3(0f, 1f, 20.8f);

            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "Hunter Choice Platform";
            platform.transform.SetParent(content.transform, false);
            platform.transform.localPosition = new Vector3(0f, 0.15f, -1.5f);
            platform.transform.localScale = new Vector3(4f, 0.15f, 3f);
            var renderer = platform.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = new Color(0.95f, 0.78f, 0.18f);

            var module = root.AddComponent<RoomModule>();
            module.Configure(
                $"room-{Guid.NewGuid():N}".Substring(0, 13),
                "New Room",
                content,
                lobbySpawns,
                hiderSpawns,
                hunterSpawn,
                platform.transform,
                new Vector3(4f, 3f, 3f));

            RegisterWithRoundManager(module);
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        static GameObject CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        static Transform[] CreateSpawns(Transform parent, string prefix, float z)
        {
            var spawns = new Transform[4];
            for (var i = 0; i < spawns.Length; i++)
            {
                spawns[i] = CreateChild(parent, $"{prefix} {i + 1}").transform;
                spawns[i].localPosition = new Vector3(-4f + i * 2.5f, 1f, z);
            }

            return spawns;
        }

        static void RegisterWithRoundManager(RoomModule module)
        {
            var roundManager = UnityEngine.Object.FindFirstObjectByType<ChameleonRoundManager>();
            if (roundManager == null) return;

            Undo.RecordObject(roundManager, "Register room module");
            var serializedManager = new SerializedObject(roundManager);
            var modules = serializedManager.FindProperty("roomModules");
            var index = modules.arraySize;
            modules.InsertArrayElementAtIndex(index);
            modules.GetArrayElementAtIndex(index).objectReferenceValue = module;
            serializedManager.ApplyModifiedProperties();
        }
    }
}
