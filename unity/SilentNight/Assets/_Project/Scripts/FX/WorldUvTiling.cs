using UnityEngine;

namespace SilentNight.FX
{
    /// <summary>
    /// Persists world-scale texture tiling on a MeshRenderer across scene save/load using a MaterialPropertyBlock.
    /// Unity does not serialize MaterialPropertyBlocks in .unity scene files, so this lightweight [ExecuteAlways]
    /// component re-applies _BaseMap_ST and _BumpMap_ST automatically in both Edit Mode and Play Mode.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class WorldUvTiling : MonoBehaviour
    {
        public Vector2 scale = Vector2.one;
        public Vector2 offset = Vector2.zero;

        private static MaterialPropertyBlock _mpb;

        public void Configure(Vector2 tilingScale, Vector2 tilingOffset = default)
        {
            scale = tilingScale;
            offset = tilingOffset;
            Apply();
        }

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        public void Apply()
        {
            var r = GetComponent<MeshRenderer>();
            if (r == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            var st = new Vector4(Mathf.Max(0.05f, scale.x), Mathf.Max(0.05f, scale.y), offset.x, offset.y);
            _mpb.SetVector("_BaseMap_ST", st);
            _mpb.SetVector("_MainTex_ST", st);
            _mpb.SetVector("_BumpMap_ST", st);
            r.SetPropertyBlock(_mpb);
        }
    }
}
