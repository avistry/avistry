using System.Threading;
using System.Threading.Tasks;

namespace Avistry.Textures
{
    public partial class Compositor
    {
        const long MinimumIslandPreparationPixels = 1024L * 1024;
        const long MaximumIslandPreparationPixels = 4096L * 4096;

        MeshUV _islandPreparationSource, _islandPreparationInput, _islandCacheMesh;
        CancellationTokenSource _islandPreparationCancellation;
        Task<MeshUV.PreparedIslandIds> _pendingIslandIds;
        bool _islandPreparationExact, _islandPreparationPadded;
        internal bool LimitBackgroundRaster { get; set; }
        int _islandPreparationGeneration, _islandWorkerGeneration;
        int[] _preparedIslandTotals, _preparedIslandOwners;
        ushort[] _preparedOwnerIds;

        int IslandIdPadding => UnityEngine.Mathf.Max(4, Width / 128);
        long IslandPreparationPixels => (long)Width * Height;

        internal bool CanPrepareIslandMaps => IslandPreparationPixels >= MinimumIslandPreparationPixels &&
            IslandPreparationPixels <= MaximumIslandPreparationPixels;

        void StartPickedMap(TextureSlot slot, int maxSize, MeshUV mesh)
        {
            int width = slot.texture.width, height = slot.texture.height;
            if (UsesSourceImageFile(slot.assetPath) && UnityEditor.AssetImporter.GetAtPath(slot.assetPath) is UnityEditor.TextureImporter importer)
            {
                try { importer.GetSourceTextureWidthAndHeight(out width, out height); }
                catch (System.InvalidOperationException) { return; }
                catch (UnityEngine.UnityException) { return; }
            }
            SetWorkingSize(width, height, maxSize);
            if (!CanPrepareIslandMaps) return;
            RequestPreparation(mesh, PreparationDemand.PaddedIslands);
            PrepareIslandIds(mesh);
        }

        internal void PrepareIslandIds(MeshUV mesh) => PrepareIslandIds(mesh, false, true);

        void PrepareIslandIds(MeshUV mesh, bool exact, bool padded)
        {
            if (_preparationDisposed) return;
            EnsureIslandGeometry(mesh);
            exact &= _islandIdsExact == null;
            padded &= _islandIdsCpu == null;
            if (mesh == null || (!exact && !padded) || !CanPrepareIslandMaps) return;
            if (ReferenceEquals(mesh, _islandPreparationSource) && (!exact || _islandPreparationExact) &&
                (!padded || _islandPreparationPadded) && _pendingIslandIds?.IsCanceled == false &&
                _pendingIslandIds.IsFaulted == false) return;
            RetireIslandIdWorker();
            var input = mesh.CopyIslandRasterInput();
            input.islandCount = mesh.islandCount;
            input.LimitBackgroundRaster = LimitBackgroundRaster;
            var cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            int width = Width, height = Height, padding = padded ? IslandIdPadding : 0;
            var task = Task.Run(() => input.PrepareIslandOwnership(width, height, padding, exact, token), token);
            _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _islandPreparationSource = mesh;
            _islandPreparationInput = input;
            _islandPreparationCancellation = cancellation;
            _pendingIslandIds = task;
            _islandPreparationExact = exact;
            _islandPreparationPadded = padded;
            _islandWorkerGeneration = _islandPreparationGeneration;
        }

        internal void CancelIslandIdPreparation() => RetireIslandIdWorker();

        void RetireIslandIdWorker()
        {
            _islandPreparationGeneration++;
            _pendingIslandIds = null;
            _islandPreparationSource = _islandPreparationInput = null;
            _islandPreparationExact = _islandPreparationPadded = false;
            var cancellation = _islandPreparationCancellation;
            _islandPreparationCancellation = null;
            if (cancellation == null) return;
            cancellation.Cancel();
            cancellation.Dispose();
        }

        ushort[] IslandIdsCpu(MeshUV uv)
        {
            EnsureIslandGeometry(uv);
            if (_islandIdsCpu != null) return _islandIdsCpu;
            AdoptIslandPreparation(uv);
            if (_islandIdsCpu != null) return _islandIdsCpu;
            CancelIslandIdPreparation();
            return _islandIdsCpu = uv.RasterPaddedIslandIds(Width, Height, IslandIdPadding);
        }

        ushort[] ExactIslandIdsCpu(MeshUV uv)
        {
            EnsureIslandGeometry(uv);
            if (_islandIdsExact != null) return _islandIdsExact;
            AdoptIslandPreparation(uv);
            if (_islandIdsExact != null) return _islandIdsExact;
            CancelIslandIdPreparation();
            return _islandIdsExact = uv.RasterPaddedIslandIds(Width, Height, 0);
        }

        bool AdoptIslandPreparation(MeshUV mesh)
        {
            if (_pendingIslandIds == null || !_pendingIslandIds.IsCompleted) return false;
            bool matches = _pendingIslandIds.Status == TaskStatus.RanToCompletion &&
                _islandWorkerGeneration == _islandPreparationGeneration && ReferenceEquals(_islandPreparationSource, mesh) &&
                mesh != null && mesh.islandCount == _islandPreparationInput.islandCount &&
                mesh.MatchesIslandRasterInput(_islandPreparationInput);
            if (matches)
            {
                var ready = _pendingIslandIds.Result;
                _islandIdsExact ??= ready.exact;
                _islandIdsCpu ??= ready.padded;
                if (ready.exact != null && ReferenceEquals(_islandIdsExact, ready.exact))
                {
                    _preparedOwnerIds = ready.exact;
                    _preparedIslandTotals = ready.totals;
                    _preparedIslandOwners = ready.owners;
                }
            }
            RetireIslandIdWorker();
            return matches;
        }

        void EnsureIslandGeometry(MeshUV mesh)
        {
            if (_islandCacheMesh != null && !ReferenceEquals(_islandCacheMesh, mesh)) InvalidateGeometry();
            _islandCacheMesh = mesh;
        }

        internal bool TryPreparedWandGeometry(MeshUV mesh, ushort[] ids, out int[] totals, out int[] owners)
        {
            bool matches = ReferenceEquals(_islandCacheMesh, mesh) && ReferenceEquals(_preparedOwnerIds, ids);
            totals = matches ? _preparedIslandTotals : null;
            owners = matches ? _preparedIslandOwners : null;
            return totals != null && owners != null;
        }

        void ClearPreparedIslandOwners()
        {
            _preparedOwnerIds = null;
            _preparedIslandTotals = _preparedIslandOwners = null;
        }
    }
}
