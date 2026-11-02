#if UNITY_EDITOR
using System;
using System.IO;
using Pogranichye.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Pogranichye.Client.Editor
{
    public static class VerticalSliceSceneBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/VerticalSlice.unity";

        [MenuItem("Tools/Pogranichye/Create v1 Vertical Slice Scene")]
        public static void CreateScene()
        {
            // Resolve the physical directory from this Unity project, not the process working directory.
            var scenesDirectory = Path.Combine(Application.dataPath, "_Project", "Scenes");
            Directory.CreateDirectory(scenesDirectory);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 8, -12);
            camera.transform.rotation = Quaternion.Euler(28, 0, 0);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.15f, 0.17f);

            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "TemporaryPresentationGround";
            ground.transform.localScale = new Vector3(2, 1, 2);

            var controller = new GameObject("PogranichyeVerticalSlice");
            controller.AddComponent<VerticalSliceController>();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Unity could not save the generated scene to " + ScenePath);

            AssetDatabase.Refresh();
            Debug.Log("Created " + ScenePath + ". Assign a URP Asset in Project Settings > Graphics before Play, then start the Go server.");
        }
    }
}
#endif
