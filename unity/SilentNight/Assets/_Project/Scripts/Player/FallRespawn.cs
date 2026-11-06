using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Player
{
    /// <summary>Trigger volume below the rooftops: an elf that falls in is put back at the spawn point.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public class FallRespawn : MonoBehaviour
    {
        private void Reset() => GetComponent<BoxCollider>().isTrigger = true;

        private void OnTriggerEnter(Collider other)
        {
            ElfController elf = other.GetComponentInParent<ElfController>();
            if (elf == null) return;
            GameObject spawn = null;
            try { spawn = GameObject.FindWithTag(GameConstants.TagSpawn); } catch (UnityException) { }
            Vector3 pos = spawn != null ? spawn.transform.position : Vector3.up;
            Quaternion rot = spawn != null ? spawn.transform.rotation : Quaternion.identity;
            elf.Teleport(pos, rot);
            GameEvents.RaiseToast("Эльф свалился в сугроб и забрался обратно на крышу.");
        }
    }
}
