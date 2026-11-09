using System.Collections.Generic;
using SilentNight.Player;
using UnityEngine;

namespace SilentNight.UI
{
    /// <summary>
    /// Renders an isolated, real-time 3D rotating showcase of the selected Elf into a RenderTexture
    /// for UI Toolkit display in the Character Customization card.
    /// </summary>
    [DisallowMultipleComponent]
    public class ElfPreviewStudio : MonoBehaviour
    {
        private RenderTexture _renderTexture;
        private Camera _cam;
        private Transform _turntable;
        private GameObject _currentModel;
        private readonly List<Material> _mats = new List<Material>();
        private int _currentTheme = -1;

        public RenderTexture PreviewTexture => _renderTexture;

        public static ElfPreviewStudio Create()
        {
            var root = new GameObject("[ElfPreviewStudio]");
            root.transform.position = new Vector3(800f, 800f, 800f);
            return root.AddComponent<ElfPreviewStudio>();
        }

        private void Awake()
        {
            _renderTexture = new RenderTexture(360, 360, 16, RenderTextureFormat.ARGB32)
            {
                name = "ElfPreview_RT",
                antiAliasing = 2,
                filterMode = FilterMode.Bilinear
            };
            _renderTexture.Create();

            // Setup Turntable at origin
            var tt = new GameObject("Turntable");
            tt.transform.SetParent(transform, false);
            tt.transform.localPosition = Vector3.zero;
            _turntable = tt.transform;

            // Setup Camera
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.45f, -1.95f);
            camGo.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);

            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.08f, 0.06f, 0.09f, 1f);
            _cam.fieldOfView = 34f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 20f;
            _cam.targetTexture = _renderTexture;

            // Studio Key Light (Front-Right Warm)
            var keyGo = new GameObject("KeyLight");
            keyGo.transform.SetParent(transform, false);
            keyGo.transform.localPosition = new Vector3(1.2f, 1.8f, -1.2f);
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.92f, 0.82f);
            key.intensity = 1.4f;
            keyGo.transform.LookAt(transform.position + Vector3.up * 0.4f);

            // Studio Rim Light (Back-Left Cool)
            var rimGo = new GameObject("RimLight");
            rimGo.transform.SetParent(transform, false);
            rimGo.transform.localPosition = new Vector3(-1.4f, 1.2f, 1.5f);
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(0.45f, 0.70f, 1.0f);
            rim.intensity = 1.6f;
            rimGo.transform.LookAt(transform.position + Vector3.up * 0.4f);
        }

        private void Update()
        {
            if (_turntable != null)
            {
                _turntable.Rotate(Vector3.up, 35f * Time.unscaledDeltaTime, Space.Self);
            }
        }

        public void SetTheme(int themeIndex)
        {
            if (_currentTheme == themeIndex && _currentModel != null) return;
            _currentTheme = themeIndex;

            if (_currentModel != null)
            {
                Destroy(_currentModel);
                _currentModel = null;
            }
            for (int i = 0; i < _mats.Count; i++)
            {
                if (_mats[i] != null) Destroy(_mats[i]);
            }
            _mats.Clear();

            string fbxPath = ElfRagdoll.GetVariantModelPath(themeIndex);
            GameObject prefab = null;
#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
#endif
            if (prefab == null) return;

            _currentModel = Instantiate(prefab, _turntable);
            _currentModel.name = "PreviewElf_Theme" + themeIndex;
            _currentModel.transform.localPosition = new Vector3(0f, -0.42f, 0f);
            _currentModel.transform.localRotation = Quaternion.identity;
            _currentModel.transform.localScale = Vector3.one * 0.95f;

            foreach (Animator a in _currentModel.GetComponentsInChildren<Animator>(true)) Destroy(a);
            foreach (Collider c in _currentModel.GetComponentsInChildren<Collider>(true)) Destroy(c);

            // Pose arms naturally down along the body instead of stiff A-pose
            foreach (Transform t in _currentModel.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                int idx = n.LastIndexOf(':');
                if (idx >= 0) n = n.Substring(idx + 1);
                if (n == "LeftArm")
                {
                    t.localRotation = Quaternion.Euler(4f, 0f, 48f) * t.localRotation;
                }
                else if (n == "LeftForeArm")
                {
                    t.localRotation = Quaternion.Euler(-14f, 0f, 6f) * t.localRotation;
                }
                else if (n == "RightArm")
                {
                    t.localRotation = Quaternion.Euler(4f, 0f, -48f) * t.localRotation;
                }
                else if (n == "RightForeArm")
                {
                    t.localRotation = Quaternion.Euler(-14f, 0f, -6f) * t.localRotation;
                }
            }

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("Standard");

            foreach (var smr in _currentModel.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
                Material[] mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = new Material(lit) { name = $"PreviewMat_T{themeIndex}" };
                    string vName = (themeIndex == 0) ? "Elf_Tripo" : (themeIndex == 1 ? "Elf_P2" : (themeIndex == 2 ? "Elf_P3" : "Elf_P4"));
#if UNITY_EDITOR
                    var alb = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                        $"Assets/_Project/Art/Characters/Elf/{vName}_Textures/{vName}_albedo.png");
                    if (alb != null) m.SetTexture("_BaseMap", alb);

                    var nrm = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                        $"Assets/_Project/Art/Characters/Elf/{vName}_Textures/{vName}_normal.png");
                    if (nrm != null)
                    {
                        m.SetTexture("_BumpMap", nrm);
                        m.EnableKeyword("_NORMALMAP");
                    }
#endif
                    Color c = (themeIndex == 4) ? new Color(0.85f, 0.35f, 0.45f) : Color.white;
                    m.SetColor("_BaseColor", c);
                    m.SetFloat("_Smoothness", 0.15f);
                    m.SetFloat("_Metallic", 0f);
                    m.SetFloat("_Surface", 0f);
                    m.SetFloat("_Blend", 0f);
                    m.SetFloat("_AlphaClip", 0f);
                    m.SetInt("_ZWrite", 1);
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                    m.SetOverrideTag("RenderType", "Opaque");
                    _mats.Add(m);
                    mats[i] = m;
                }
                smr.sharedMaterials = mats;
            }
        }

        private void OnDestroy()
        {
            if (_currentModel != null) Destroy(_currentModel);
            for (int i = 0; i < _mats.Count; i++)
            {
                if (_mats[i] != null) Destroy(_mats[i]);
            }
            _mats.Clear();

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
        }
    }
}
