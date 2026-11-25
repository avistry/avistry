using System;

namespace Avistry.Textures
{
    public partial class Compositor
    {
        [Flags]
        internal enum PreparationDemand { None = 0, ExactIslands = 1, PaddedIslands = 2, Source = 4, IslandTexture = 8 }

        MeshUV _preparationMesh;
        PreparationDemand _preparationDemand;
        double _preparationAfter;
        bool _preparationDisposed;
        const double PreparationDelay = .075;

        internal void RequestPreparation(MeshUV mesh, PreparationDemand demand)
        {
            if (_preparationDisposed) return;
            EnsureIslandGeometry(mesh);
            if (!ReferenceEquals(_preparationMesh, mesh) || _preparationDemand != demand)
            {
                bool exact = (demand & PreparationDemand.ExactIslands) != 0;
                bool padded = (demand & (PreparationDemand.PaddedIslands | PreparationDemand.IslandTexture)) != 0;
                if (!ReferenceEquals(_preparationMesh, mesh) || _pendingIslandIds != null &&
                    !(_islandPreparationExact && exact || _islandPreparationPadded && padded)) CancelIslandIdPreparation();
                if ((demand & PreparationDemand.Source) == 0) CancelSourcePreparation();
                _preparationMesh = mesh;
                _preparationDemand = demand;
                _preparationAfter = 0;
            }
            AdoptIslandPreparation(mesh);
        }

        internal bool ContinuePreparation(MeshUV mesh, PreparationDemand demand, double now)
        {
            RequestPreparation(mesh, demand);
            if (_preparationDisposed) return false;
            if (demand == PreparationDemand.None)
            {
                CancelIslandIdPreparation();
                CancelSourcePreparation();
                return false;
            }
            if (_preparationAfter == 0) _preparationAfter = now + PreparationDelay;
            if (now < _preparationAfter) return true;
            bool exact = (demand & PreparationDemand.ExactIslands) != 0;
            bool padded = (demand & (PreparationDemand.PaddedIslands | PreparationDemand.IslandTexture)) != 0;
            if (_pendingIslandIds == null && (exact && _islandIdsExact == null || padded && _islandIdsCpu == null) &&
                mesh != null && CanPrepareIslandMaps)
            {
                PrepareIslandIds(mesh, exact, padded);
                return true;
            }
            if (_pendingIslandIds != null) return false;
            if ((demand & PreparationDemand.Source) != 0 && ContinueSourcePreparation()) return true;
            if ((demand & PreparationDemand.Source) == 0) CancelSourcePreparation();
            if ((demand & PreparationDemand.IslandTexture) != 0 && _islandIds == null && _islandIdsCpu != null)
            {
                IslandIdTexture(mesh);
                return true;
            }
            return false;
        }

        internal void SuspendPreparation()
        {
            if (_preparationDisposed) return;
            CancelIslandIdPreparation();
            CancelSourcePreparation();
            _preparationAfter = 0;
        }

        internal bool TryPreparedExactIslands(MeshUV mesh, out ushort[] ids)
        {
            ids = ReferenceEquals(_preparationMesh, mesh) && ReferenceEquals(_islandCacheMesh, mesh) ? _islandIdsExact : null;
            return ids != null;
        }

        void DisposePreparation()
        {
            _preparationDisposed = true;
            _preparationMesh = null;
            _preparationDemand = PreparationDemand.None;
            CancelSourcePreparation();
            _sourceCpu = null;
        }
    }
}
