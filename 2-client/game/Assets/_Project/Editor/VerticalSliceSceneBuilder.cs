#if UNITY_EDITOR
using Pogranichye.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Pogranichye.Client.Editor
{
    public static class VerticalSliceSceneBuilder
    {
        [MenuItem("Tools/Pogranichye/Create v1 Vertical Slice Scene")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var controller = new GameObject("PogranichyeVerticalSlice");
            controller.AddComponent<VerticalSliceController>();
            EditorSceneManager.SaveScene(scene, "Assets/_Project/Scenes/VerticalSlice.unity");
            AssetDatabase.Refresh();
            Debug.Log("Created Assets/_Project/Scenes/VerticalSlice.unity. Start the Go server and press Play.");
        }
    }
}
#endif
