using UnityEngine;

namespace JustReadTheInstructions
{
    public class CameraSynchronizer : MonoBehaviour
    {
        public Camera SourceCamera { get; set; }

        void OnPreRender()
        {
            if (SourceCamera == null || SourceCamera.transform == null || transform == null)
                return;

            transform.position = ScaledSpace.LocalToScaledSpace(SourceCamera.transform.localPosition);
            transform.rotation = SourceCamera.transform.rotation;
        }
    }
}