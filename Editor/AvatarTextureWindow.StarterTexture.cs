using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        const int StarterTextureSize = 1024;

        bool CanStartUnityTexture => !_needsRebuild && _renderer != null && _comp == null && _material != null &&
            _material.shader != null && (_material.shader.name == "Standard" || _material.shader.name == "Standard (Specular setup)") &&
            _material.GetTexture("_MainTex") == null;

        void DrawStarterTexture()
        {
            if (!CanStartUnityTexture) return;
            GUILayout.Label(Loc.T("Start with a white texture on a material copy for this part."), StudioGUI.Hint);
            if (GUILayout.Button(Loc.T("Create a texture and paint"), EditorStyles.miniButton)) QueueStarterTexture();
            StudioGUI.Count("pick.starterTexture", GUILayoutUtility.GetLastRect());
        }

        void QueueStarterTexture()
        {
            var part = _renderer;
            var mesh = _builtTargetMesh;
            var original = _material;
            var slot = _materialIndex;
            var shader = original.shader;
            var revision = original.ComputeCRC();
            var recipe = _recipe;
            int history = _historyRevision;
            bool Current() => this != null && _tab == Tab.Target && CanStartUnityTexture && _renderer == part &&
                _material == original && _materialIndex == slot && original.shader == shader && original.ComputeCRC() == revision &&
                _recipe == recipe && _historyRevision == history && MeshOf(part) == mesh && SlotUsesMaterial(part, slot, original);

            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                if (!Current()) { ShowStarterTargetChanged(); return; }
                if (!PlanTargetChange("texture", out var applyTarget)) return;
                if (!Current()) { ShowStarterTargetChanged(); return; }
                var output = Exporter.WritableFolder(AssetDatabase.GetAssetPath(original));
                string texturePath = AssetDatabase.GenerateUniqueAssetPath(output + "/Paint base.asset");
                var texture = new Texture2D(StarterTextureSize, StarterTextureSize, TextureFormat.RGBA32, true)
                    { name = "Paint base", wrapMode = TextureWrapMode.Repeat };
                bool assigned = false;
                try
                {
                    var pixels = texture.GetPixelData<uint>(0);
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = uint.MaxValue;
                    texture.Apply();
                    AssetDatabase.CreateAsset(texture, texturePath);
                    if (AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath) != texture)
                        throw new InvalidOperationException("Unity did not import the starter texture asset.");
                    var users = new List<TargetResolver.SlotRef> { new TargetResolver.SlotRef { renderer = part, materialIndex = slot } };
                    string materialPath = Exporter.CopyMaterial(original, users, Current, copy => copy.SetTexture("_MainTex", texture), applyTarget);
                    if (materialPath == null) { ShowStarterTargetChanged(); return; }
                    assigned = true;
                    _pendingTexture = texture;
                    _needsRebuild = true;
                    Rebuild();
                    if (_comp != null) SetTab(Tab.Paint);
                    Toast(Loc.T("Added a white texture on a new material. Ctrl+Z restores the previous material."), MessageType.Info);
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    Toast(Loc.T("The starter texture couldn't be created. Check the Console and try again."), MessageType.Error);
                }
                finally
                {
                    if (!assigned && !StarterTextureInSlot(part, slot, texture))
                    {
                        if (!EditorUtility.IsPersistent(texture)) DestroyImmediate(texture);
                        else if (AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath) == texture && AssetDatabase.GetAssetPath(texture) == texturePath)
                            AssetDatabase.DeleteAsset(texturePath);
                    }
                    Repaint();
                    SceneView.RepaintAll();
                }
            };
        }

        static bool StarterTextureInSlot(Renderer part, int index, Texture2D texture)
        {
            if (part == null) return false;
            var materials = part.sharedMaterials;
            return index >= 0 && index < materials.Length && materials[index] != null && materials[index].mainTexture == texture;
        }

        void ShowStarterTargetChanged()
        {
            _needsRebuild = true;
            Toast(Loc.T("The part or material changed. Choose it again before creating a texture."), MessageType.Info);
        }
    }
}
