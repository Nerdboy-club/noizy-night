using System.Collections.Generic;
using SilentNight.Core;
using SilentNight.Gifts;
using SilentNight.Multiplayer;
using SilentNight.Player;
using UnityEngine;

namespace SilentNight.Rooftop
{
    /// <summary>
    /// A tall brick chimney on the rooftops with:
    /// 1. Physical 3D bolted brass plaque ("CHIMNEY / Nº. 25") mounted directly on the brick wall.
    /// 2. Co-op Boost & Pull-Up vs Solo Ladder traversal:
    ///    - In Solo (1 player): a wooden ladder leans against the chimney so a solo elf can climb up to the rim.
    ///    - In Co-op (2+ players): the solo ladder is absent! Elves must help boost each other up at the base ([E]),
    ///      and the elf on top can pull teammates up / drop a rope ladder ([E]) before diving into the chimney.
    /// </summary>
    public class RooftopChimney : InteractableBase
    {
        [SerializeField] private TextMesh numberLabel;

        private readonly List<TextMesh> _plaqueTexts = new List<TextMesh>(2);
        private GameObject _soloLadder;
        private bool _coopLadderDropped;
        private bool _builtVisuals;

        public int HouseNumber { get; private set; }
        public bool IsTarget { get; private set; }
        public static int TargetNumber { get; set; }

        private float BaseDeckY => transform.position.y - 0.9f;
        private float RimTopY => BaseDeckY + 2.05f;

        protected override void Awake()
        {
            defaultColliderSize = new Vector3(1.65f, 2.8f, 1.65f);
            defaultColliderCenter = new Vector3(0f, 0.35f, 0f);
            base.Awake();
            EnsurePlaquesAndLadder();
        }

        private void Start()
        {
            EnsurePlaquesAndLadder();
            RefreshLabels();
            RefreshLadderVisibility();
        }

        private void Update()
        {
            RefreshLadderVisibility();
        }

        public void Setup(int houseNumber, bool isTarget)
        {
            HouseNumber = houseNumber;
            IsTarget = isTarget;
            EnsurePlaquesAndLadder();
            RefreshLabels();
        }

        private static bool IsCoopSessionActive()
        {
            if (GameServices.Players != null && GameServices.Players.Count > 1) return true;
            if (CoopNetworkManager.Instance != null && CoopNetworkManager.Instance.Role != CoopNetworkManager.NetRole.Offline)
                return true;
            return false;
        }

        private void RefreshLadderVisibility()
        {
            if (_soloLadder == null) return;
            bool showLadder = !IsCoopSessionActive() || _coopLadderDropped;
            if (_soloLadder.activeSelf != showLadder)
                _soloLadder.SetActive(showLadder);
        }

        private void RefreshLabels()
        {
            string numStr = HouseNumber > 0 ? HouseNumber.ToString() : "?";
            string plaqueText = $"CHIMNEY\nNº. {numStr}";

            if (numberLabel != null)
            {
                // Hide old floating billboard if it was above the chimney
                Billboard bb = numberLabel.GetComponent<Billboard>();
                if (bb != null)
                {
                    numberLabel.gameObject.SetActive(false);
                }
                else
                {
                    numberLabel.text = plaqueText;
                }
            }

            for (int i = 0; i < _plaqueTexts.Count; i++)
            {
                if (_plaqueTexts[i] != null)
                    _plaqueTexts[i].text = plaqueText;
            }
        }

        private void EnsurePlaquesAndLadder()
        {
            if (_builtVisuals) return;
            _builtVisuals = true;

            // Hide legacy floating NumberLabel if present
            Transform oldLabel = transform.Find("NumberLabel");
            if (oldLabel != null) oldLabel.gameObject.SetActive(false);

            Transform root = new GameObject("ChimneyAttachments").transform;
            root.SetParent(transform, false);
            // Place root at the chimney's world position with identity rotation & scale (1,1,1)
            root.position = new Vector3(transform.position.x, BaseDeckY, transform.position.z);
            root.rotation = Quaternion.identity;
            Vector3 lossy = transform.lossyScale;
            root.localScale = new Vector3(
                1f / Mathf.Max(0.01f, lossy.x),
                1f / Mathf.Max(0.01f, lossy.y),
                1f / Mathf.Max(0.01f, lossy.z));

            Material brassMat = MakeMaterial(new Color(0.82f, 0.67f, 0.28f), 0.78f, 0.62f);
            Material darkBrassMat = MakeMaterial(new Color(0.32f, 0.23f, 0.10f), 0.55f, 0.35f);
            Material woodMat = MakeMaterial(new Color(0.42f, 0.26f, 0.14f), 0.0f, 0.22f);

            // Build bolted brass plaques on South (-Z) and North (+Z) brick walls at eye level (y = 0.92m above roof deck)
            BuildBrassPlaque(root, "BrassPlaque_South", new Vector3(0f, 0.92f, -0.47f), 0f, brassMat, darkBrassMat);
            BuildBrassPlaque(root, "BrassPlaque_North", new Vector3(0f, 0.92f, 0.47f), 180f, brassMat, darkBrassMat);

            // Build top walkable platform collider so elves standing on top of the chimney rim have solid footing
            GameObject topRim = new GameObject("ChimneyTopStandCollider");
            topRim.transform.SetParent(root, false);
            topRim.transform.localPosition = new Vector3(0f, 1.92f, 0f);
            var topCol = topRim.AddComponent<BoxCollider>();
            topCol.size = new Vector3(1.12f, 0.16f, 1.12f);

            // Build Solo / Drop-Down Wooden Ladder on the West side (-X) of the chimney
            _soloLadder = BuildLadder(root, woodMat, brassMat);
        }

        private void BuildBrassPlaque(Transform parent, string name, Vector3 localPos, float yaw, Material brassMat, Material rivetMat)
        {
            Transform p = new GameObject(name).transform;
            p.SetParent(parent, false);
            p.localPosition = localPos;
            p.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // Dark backing border
            CreatePart(p, "Frame", PrimitiveType.Cube, new Vector3(0f, 0f, 0.004f), new Vector3(0.46f, 0.34f, 0.016f), rivetMat, false);
            // Main brushed brass plate
            CreatePart(p, "Plate", PrimitiveType.Cube, new Vector3(0f, 0f, -0.002f), new Vector3(0.43f, 0.31f, 0.018f), brassMat, false);

            // 4 corner bolts / rivets
            float rx = 0.185f, ry = 0.125f;
            CreatePart(p, "RivetTL", PrimitiveType.Sphere, new Vector3(-rx, ry, -0.012f), new Vector3(0.026f, 0.026f, 0.016f), rivetMat, false);
            CreatePart(p, "RivetTR", PrimitiveType.Sphere, new Vector3(rx, ry, -0.012f), new Vector3(0.026f, 0.026f, 0.016f), rivetMat, false);
            CreatePart(p, "RivetBL", PrimitiveType.Sphere, new Vector3(-rx, -ry, -0.012f), new Vector3(0.026f, 0.026f, 0.016f), rivetMat, false);
            CreatePart(p, "RivetBR", PrimitiveType.Sphere, new Vector3(rx, -ry, -0.012f), new Vector3(0.026f, 0.026f, 0.016f), rivetMat, false);

            // Engraved text directly on the brass surface
            GameObject txtGo = new GameObject("EngravedText");
            txtGo.transform.SetParent(p, false);
            txtGo.transform.localPosition = new Vector3(0f, 0.01f, -0.014f);
            txtGo.transform.localRotation = Quaternion.identity;

            var tm = txtGo.AddComponent<TextMesh>();
            tm.text = HouseNumber > 0 ? $"CHIMNEY\nNº. {HouseNumber}" : "CHIMNEY\nNº. ?";
            tm.fontSize = 72;
            tm.characterSize = 0.0082f;
            tm.lineSpacing = 0.92f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.color = new Color(0.18f, 0.11f, 0.04f, 1f); // deep engraved bronze ink

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                tm.font = font;
                var mr = txtGo.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = font.material;
            }
            _plaqueTexts.Add(tm);
        }

        private static GameObject BuildLadder(Transform parent, Material woodMat, Material rungAccentMat)
        {
            GameObject ladder = new GameObject("SoloOrCoopLadder");
            ladder.transform.SetParent(parent, false);
            ladder.transform.localPosition = new Vector3(-0.56f, 0f, 0f);
            ladder.transform.localRotation = Quaternion.Euler(0f, 0f, -14f); // leaning against the chimney wall

            // Two vertical wooden rails
            CreatePart(ladder.transform, "RailL", PrimitiveType.Cube, new Vector3(0f, 1.02f, -0.22f), new Vector3(0.06f, 2.10f, 0.05f), woodMat, false);
            CreatePart(ladder.transform, "RailR", PrimitiveType.Cube, new Vector3(0f, 1.02f, 0.22f), new Vector3(0.06f, 2.10f, 0.05f), woodMat, false);

            // 7 wooden rungs
            for (int i = 0; i < 7; i++)
            {
                float ry = 0.22f + i * 0.27f;
                CreatePart(ladder.transform, "Rung_" + i, PrimitiveType.Cube,
                    new Vector3(-0.02f, ry, 0f), new Vector3(0.05f, 0.04f, 0.48f),
                    i % 2 == 0 ? woodMat : rungAccentMat, false);
            }

            // Walkable ramp collider so an elf can also walk/hop right up the ladder
            GameObject ramp = new GameObject("LadderRampCollider");
            ramp.transform.SetParent(ladder.transform, false);
            ramp.transform.localPosition = new Vector3(-0.22f, 0.98f, 0f);
            ramp.transform.localRotation = Quaternion.Euler(0f, 0f, -26f);
            var bc = ramp.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.12f, 2.25f, 0.56f);

            return ladder;
        }

        private static GameObject CreatePart(Transform parent, string name, PrimitiveType type, Vector3 localPos, Vector3 localScale, Material mat, bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (!keepCollider)
            {
                Collider c = go.GetComponent<Collider>();
                if (c != null) Destroy(c);
            }
            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            return go;
        }

        private static Material MakeMaterial(Color color, float metallic, float smoothness)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            if (s == null) return null;
            var m = new Material(s);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        // =====================================================================
        // CO-OP BOOST / PULL-UP & SOLO LADDER INTERACTION
        // =====================================================================

        private bool IsInteractorOnTopRim(IInteractor interactor)
        {
            if (interactor == null || interactor.Transform == null) return false;
            return interactor.Transform.position.y >= BaseDeckY + 1.15f;
        }

        private ElfController FindNearbyTeammateAtBase(Transform excludeTransform, out float bestDist)
        {
            bestDist = float.MaxValue;
            ElfController best = null;
            var players = GameServices.Players;
            if (players == null) return null;

            Vector3 basePos = new Vector3(transform.position.x, BaseDeckY, transform.position.z);
            for (int i = 0; i < players.Count; i++)
            {
                IPlayerStatus p = players[i];
                if (p == null || p.Transform == null || p.Transform == excludeTransform) continue;
                Vector3 tp = p.Transform.position;
                if (tp.y >= BaseDeckY + 1.25f) continue; // already on top
                float d = Vector2.Distance(new Vector2(tp.x, tp.z), new Vector2(basePos.x, basePos.z));
                if (d < 2.8f && d < bestDist)
                {
                    bestDist = d;
                    best = p.Transform.GetComponent<ElfController>();
                }
            }
            return best;
        }

        public override string Prompt
        {
            get
            {
                IPlayerStatus local = GameServices.LocalPlayer;
                bool onTop = local != null && local.Transform != null && local.Transform.position.y >= BaseDeckY + 1.15f;
                bool coop = IsCoopSessionActive();

                if (!onTop)
                {
                    if (!coop || _coopLadderDropped)
                        return $"Залезть по лестнице на дымоход (дом № {HouseNumber})";

                    ElfController friend = FindNearbyTeammateAtBase(local != null ? local.Transform : null, out _);
                    if (friend != null)
                        return $"Подсадить друг друга наверх дымохода (дом № {HouseNumber})";

                    return $"Позвать друга для подсадки на дымоход (дом № {HouseNumber})";
                }

                if (coop && !_coopLadderDropped)
                {
                    ElfController friendBelow = FindNearbyTeammateAtBase(local != null ? local.Transform : null, out _);
                    if (friendBelow != null)
                        return "Подать руку другу и сбросить лестницу вниз";
                }

                return IsTarget
                    ? $"Нырнуть в дымоход (дом № {HouseNumber})"
                    : $"Заглянуть в дымоход (дом № {HouseNumber})";
            }
        }

        public override void Interact(IInteractor interactor)
        {
            if (interactor == null || interactor.Transform == null) return;
            var meCtrl = interactor.Transform.GetComponent<ElfController>();
            bool onTop = IsInteractorOnTopRim(interactor);
            bool coop = IsCoopSessionActive();

            // 1. If the player is at the bottom of the chimney:
            if (!onTop)
            {
                if (!coop || _coopLadderDropped)
                {
                    // Solo ladder or already dropped rope ladder: climb straight to the top rim!
                    TeleportElfToRim(meCtrl, 0);
                    GameEvents.RaiseToast($"Вы забрались по лестнице на дымоход № {HouseNumber}!");
                    return;
                }

                // Co-op mode without ladder dropped yet: require a teammate near the chimney base to boost!
                ElfController friend = FindNearbyTeammateAtBase(interactor.Transform, out _);
                if (friend != null)
                {
                    TeleportElfToRim(meCtrl, 0);
                    GameEvents.RaiseToast("Напарник подсадил вас наверх трубы! Теперь подайте ему руку сверху [E].");
                }
                else
                {
                    GameEvents.RaiseToast("Труба слишком высокая! Подойдите вдвоём с другом, чтобы подсадить друг друга.");
                }
                return;
            }

            // 2. Player is on top of the chimney rim:
            if (coop && !_coopLadderDropped)
            {
                ElfController friendBelow = FindNearbyTeammateAtBase(interactor.Transform, out _);
                _coopLadderDropped = true;
                RefreshLadderVisibility();
                SilentNight.Multiplayer.CoopNetworkManager.Instance?.BroadcastLadderDropped(HouseNumber);
                if (friendBelow != null)
                {
                    TeleportElfToRim(friendBelow, 1);
                    GameEvents.RaiseToast("Вы вытянули друга наверх дымохода и сбросили лестницу!");
                    return;
                }
            }

            // 3. Dive into the chimney!
            if (IsTarget)
            {
                SilentNight.FX.SootPuff.Spawn(transform.position + Vector3.up * 1.0f, 0.8f);
                SilentNight.Multiplayer.CoopNetworkManager.RequestSynchronizedHouseEntry();
            }
            else
            {
                GameEvents.RaiseToast("Дом № " + HouseNumber + " — не тот адрес. Ищите № " + TargetNumber + ".");
            }
        }

        public void DropLadderRemote()
        {
            _coopLadderDropped = true;
            RefreshLadderVisibility();
        }

        private void TeleportElfToRim(ElfController elf, int slotOffset)
        {
            if (elf == null) return;
            Vector3 offset = slotOffset == 0 ? new Vector3(-0.22f, 0f, -0.22f) : new Vector3(0.22f, 0f, 0.22f);
            Vector3 targetPos = new Vector3(transform.position.x, RimTopY, transform.position.z) + offset;
            elf.Teleport(targetPos, elf.transform.rotation);
        }
    }
}
