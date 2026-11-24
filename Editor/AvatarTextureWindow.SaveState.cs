using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        internal enum SaveState { Idle = 0, NotSaved = 1, Saving = 2, Saved = 3 }

        [SerializeField] bool _savedBlockVisible;
        [SerializeField] string _lastBakePngGuid, _lastBakeMaterialGuid, _lastBakeRecipeGuid;
        [System.NonSerialized] bool _saving;

        int _savedFilesStamp = -1;
        (string png, string material, string edit, string pngGuid, string materialGuid, string editGuid) _savedFilesKey;
        (Texture2D png, Material material, TextureRecipe edit) _savedFiles;

        (Texture2D png, Material material, TextureRecipe edit) LastBakeFiles
        {
            get
            {
                var key = (_lastBakePng, _lastBakeMaterial, _lastBakeRecipe, _lastBakePngGuid, _lastBakeMaterialGuid, _lastBakeRecipeGuid);
                if (_savedFilesStamp != _envStamp || key != _savedFilesKey)
                {
                    _savedFilesStamp = _envStamp;
                    _savedFilesKey = key;
                    _savedFiles = (ResolveLastBakeFile<Texture2D>(_lastBakePng, _lastBakePngGuid),
                        ResolveLastBakeFile<Material>(_lastBakeMaterial, _lastBakeMaterialGuid),
                        ResolveLastBakeFile<TextureRecipe>(_lastBakeRecipe, _lastBakeRecipeGuid));
                }
                return _savedFiles;
            }
        }

        void RememberLastBakeFiles(Exporter.Result result)
        {
            _lastBakePng = result.pngPath;
            _lastBakeMaterial = result.materialPath;
            _lastBakeRecipe = result.recipePath;
            _lastBakePngGuid = SavedFileGuid(_lastBakePng);
            _lastBakeMaterialGuid = SavedFileGuid(_lastBakeMaterial);
            _lastBakeRecipeGuid = SavedFileGuid(_lastBakeRecipe);
        }

        static string SavedFileGuid(string path) => string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);

        static T ResolveLastBakeFile<T>(string savedPath, string guid) where T : Object
        {
            if (string.IsNullOrEmpty(savedPath)) return null;
            var path = string.IsNullOrEmpty(guid) ? savedPath : AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }

        void RevealLastBakeFile<T>(string savedPath, string guid) where T : Object
        {
            var asset = ResolveLastBakeFile<T>(savedPath, guid);
            if (asset != null) ShowFileAndLearn(AssetDatabase.GetAssetPath(asset));
            else Toast(Loc.T("This saved file is missing from the project."), MessageType.Info);
        }

        internal SaveState CurrentSaveState => _saving ? SaveState.Saving
            : (_savedBlockVisible && LastBakeFiles.png != null) ? SaveState.Saved
            : (_unsaved && _recipe != null && _recipe.layers.Count > 0) ? SaveState.NotSaved : SaveState.Idle;

        internal string SaveStateLabel()
        {
            switch (CurrentSaveState)
            {
                case SaveState.NotSaved: return $"NotSaved({_recipe.layers.Count})";
                case SaveState.Saving: return "Saving";
                case SaveState.Saved: return "Saved";
                default: return "Idle";
            }
        }
    }
}
