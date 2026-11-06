using UnityEngine;
using UnityEngine.AI;
using SilentNight.Core;

namespace SilentNight.Stealth
{
    /// <summary>
    /// Builds complete NPC GameObjects for the greybox scene builder (works in edit mode and at runtime).
    /// Visual placeholders are generated at runtime by NPCVisual, so nothing extra is saved into the scene.
    /// </summary>
    public static class NPCFactory
    {
        /// <summary>
        /// Grandpa lying in bed. bedPos = floor-level centre of the bed; rot = bed facing
        /// (his forward = the side he gets up on; head lies towards -forward, i.e. the pillow).
        /// </summary>
        public static GameObject CreateGrandpa(Vector3 bedPos, Quaternion rot)
        {
            var go = new GameObject("Grandpa");
            go.transform.SetPositionAndRotation(bedPos, rot);
            int layer = LayerMask.NameToLayer(GameConstants.LayerNPC);
            if (layer >= 0) go.layer = layer;

            var visual = go.AddComponent<NPCVisual>();
            visual.Configure(NPCKind.Grandpa);

            var body = go.AddComponent<CapsuleCollider>();
            body.center = new Vector3(0f, 0.9f, 0f);
            body.height = 1.8f;
            body.radius = 0.35f;
            body.direction = 1; // Y axis
            body.enabled = false; // Grandpa enables it when he leaves the bed

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 1.8f;
            agent.baseOffset = 0f;
            agent.speed = 1.2f;
            agent.angularSpeed = 360f;
            agent.acceleration = 8f;
            agent.stoppingDistance = 0.3f;
            agent.autoBraking = true;
            agent.enabled = false; // Grandpa enables it next to the bed

            var eyes = new GameObject("Eyes");
            eyes.layer = go.layer;
            eyes.transform.SetParent(go.transform, false);
            eyes.transform.localPosition = new Vector3(0f, 1.6f, 0.15f);

            var lightGo = new GameObject("Flashlight");
            lightGo.layer = go.layer;
            lightGo.transform.SetParent(eyes.transform, false);
            lightGo.transform.localPosition = new Vector3(0.3f, -0.3f, 0.25f);
            lightGo.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(1f, 0.97f, 0.93f);
            light.range = 9f;
            light.spotAngle = 50f;
            light.innerSpotAngle = 25f;
            light.intensity = 0f;
            light.shadows = LightShadows.Soft;
            light.enabled = false;

            var vision = go.AddComponent<VisionCone>();
            vision.Configure(eyes.transform, 80f, 7.5f);

            var grandpa = go.AddComponent<Grandpa>();
            grandpa.Configure(visual, agent, vision, eyes.transform, light, body);
            return go;
        }

        /// <summary>Same as CreateGrandpa plus optional patrol points (night trips to the kitchen/bathroom).</summary>
        public static GameObject CreateGrandpa(Vector3 bedPos, Quaternion rot, Transform[] patrolPoints)
        {
            GameObject go = CreateGrandpa(bedPos, rot);
            go.GetComponent<Grandpa>().SetPatrolPoints(patrolPoints);
            return go;
        }

        /// <summary>Sleeping fat cat at pos (facing +Z; tail zone is behind it).</summary>
        public static GameObject CreateCat(Vector3 pos)
        {
            return CreateCat(pos, Quaternion.identity);
        }

        public static GameObject CreateCat(Vector3 pos, Quaternion rot)
        {
            var go = new GameObject("FatCat");
            go.transform.SetPositionAndRotation(pos, rot);
            int layer = LayerMask.NameToLayer(GameConstants.LayerNPC);
            if (layer >= 0) go.layer = layer;

            var visual = go.AddComponent<NPCVisual>();
            visual.Configure(NPCKind.Cat);

            var col = go.AddComponent<SphereCollider>();
            col.center = new Vector3(0f, 0.22f, 0f);
            col.radius = 0.28f;

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.25f;
            agent.height = 0.5f;
            agent.baseOffset = 0f;
            agent.speed = 0.9f;
            agent.angularSpeed = 400f;
            agent.acceleration = 6f;
            agent.stoppingDistance = 0.15f;
            agent.enabled = false;

            var cat = go.AddComponent<FatCat>();
            cat.Configure(visual, agent);
            return go;
        }

        /// <summary>Creaky floorboard / squeaky toy trigger box.</summary>
        public static GameObject CreateNoiseZone(Vector3 center, Vector3 size, NoiseSource source = NoiseSource.CreakyFloor, float loudness = 45f, float cooldown = 1.2f)
        {
            var go = new GameObject(source == NoiseSource.CreakyFloor ? "CreakyFloor" : "NoiseZone");
            go.transform.position = center;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            var zone = go.AddComponent<NoiseZone>();
            zone.Configure(source, loudness, cooldown);
            return go;
        }
    }
}
