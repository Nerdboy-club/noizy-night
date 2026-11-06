using UnityEngine;

namespace SilentNight.Rooftop
{
    /// <summary>Turns a label towards the main camera around the Y axis.</summary>
    public class Billboard : MonoBehaviour
    {
        private Transform _cam;

        private void LateUpdate()
        {
            if (_cam == null)
            {
                Camera c = Camera.main;
                if (c == null) return;
                _cam = c.transform;
            }
            Vector3 d = transform.position - _cam.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(d, Vector3.up);
        }
    }
}
