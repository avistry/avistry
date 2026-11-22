using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        internal enum StatusFix { None = 0, PickAnotherMesh = 1 }

        [System.NonSerialized] internal StatusFix _statusFix;
        [SerializeField] bool _slotChosenByUser;   // true when the user (or a harness/explicit path) chose the texture slot

        internal static void OpenConsole() => EditorApplication.ExecuteMenuItem("Window/General/Console");

        internal static void ShowFile(string assetPath)
        {
            var obj = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (obj == null) return;
            EditorGUIUtility.PingObject(obj);
            Selection.activeObject = obj;
        }

        internal void UseCopyInstead()
        {
            Undo.RecordObject(this, "Use a copy instead");
            _outputMode = OutputMode.DuplicateMaterial;
            Repaint();
        }

        internal void AffectEverything(Layer layer = null)
        {
            if (layer != null)
            {
                Undo.RecordObject(_recipe, "Affect everything");
                layer.maskMode = MaskMode.WholeTexture;
                layer.islands.Clear();
                layer.invertMask = false;
                MarkEdited();
                return;
            }
            SetSelection(System.Linq.Enumerable.Empty<int>());
        }

        internal void SetOpacity100(Layer layer)
        {
            Undo.RecordObject(_recipe, "Set to 100%");
            layer.opacity = 1f;
            MarkEdited();
        }

        internal void ShowAllParts()
        {
            Undo.RecordObject(this, "Show all parts");
            _focusWhilePainting = false;
            Repaint();
        }

        internal void TickAllMeshes() => SetMeshesIncluded(_textureUsers, true);

        internal bool SwitchFeatureOn(TextureSlot slot) => SwitchFeatureOn(_material, slot);

        internal bool SwitchFeatureOn(Material material, TextureSlot slot)
        {
            if (slot == null || string.IsNullOrEmpty(slot.featureToggle) || material == null) return false;
            MaterialWriter.EnsureEditable(new[] { material });
            Undo.RecordObject(material, "Switch feature on");
            material.SetFloat(slot.featureToggle, 1f);
            MaterialWriter.FixPoiyomiKeywords(new[] { material });
            EditorUtility.SetDirty(material);
            return true;
        }

        /// <summary>For toast buttons: switches the effect on for the material the toast was about, then rebuilds if it is still the target.</summary>
        void SwitchFeatureOnFor(Material material, TextureSlot slot)
        {
            if (!SwitchFeatureOn(material, slot)) return;
            if (material == _material) _needsRebuild = true;
            Repaint();
        }

        internal bool ConfirmUnlock(IReadOnlyList<Material> mats, string purpose)
        {
            var names = new List<string>();
            if (mats != null)
                foreach (var m in mats)
                    if (m != null && !names.Contains(m.name)) names.Add(m.name);
            string body = Msg.M9LockedMaterial();
            if (names.Count == 1) body = $"“{names[0]}”: {body}";
            else if (names.Count > 1)
            {
                string shown = string.Join(", ", names.Take(4));
                string head = names.Count > 4 ? Loc.F("{0} materials need unlocking ({1} and {2} more).", names.Count, shown, names.Count - 4)
                                              : Loc.F("{0} materials need unlocking ({1}).", names.Count, shown);
                body = $"{head}\n\n{body}";
            }
            return StudioDialog.Ask(Loc.T("Avistry"), body, Msg.BtnUnlock, Msg.BtnCancel);
        }

        internal void ChooseSlot(int index)
        {
            _slotIndex = index;
            _slotChosenByUser = true;
            _needsRebuild = true;
            Repaint();
        }
    }
}
