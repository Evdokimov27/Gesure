using UnityEngine;

namespace GestureRecognition.Demo
{
    /// <summary>
    /// Simple helper that moves an object in a circular path around a pivot.
    /// Useful for testing the <see cref="GestureDetector"/> without manual input.
    /// </summary>
    public class CircularMover : MonoBehaviour
    {
        [SerializeField]
        private Transform pivot;

        [SerializeField]
        private float radius = 1.5f;

        [SerializeField]
        private Vector3 axis = Vector3.up;

        [SerializeField]
        [Tooltip("Time in seconds required to complete a full revolution.")]
        private float revolutionDuration = 4f;

        [SerializeField]
        [Tooltip("If enabled, the object will be repositioned to match the desired radius when play mode starts.")]
        private bool alignOnStart = true;

        private float angularSpeed;

        private void Awake()
        {
            axis = axis.sqrMagnitude > 0f ? axis.normalized : Vector3.up;
        }

        private void OnValidate()
        {
            radius = Mathf.Max(0.01f, radius);
            revolutionDuration = Mathf.Max(0.01f, revolutionDuration);
            axis = axis.sqrMagnitude > 0f ? axis.normalized : Vector3.up;
            angularSpeed = 360f / revolutionDuration;
        }

        private void Start()
        {
            if (pivot == null)
            {
                Debug.LogWarning($"[{nameof(CircularMover)}] No pivot assigned on '{name}'.");
                return;
            }

            angularSpeed = 360f / revolutionDuration;

            if (alignOnStart)
            {
                Vector3 offset = transform.position - pivot.position;
                offset = Vector3.ProjectOnPlane(offset, axis);
                if (offset.sqrMagnitude < 1e-6f)
                {
                    offset = Vector3.Cross(axis, Vector3.forward);
                    if (offset.sqrMagnitude < 1e-6f)
                    {
                        offset = Vector3.Cross(axis, Vector3.up);
                    }
                }

                offset = offset.normalized * radius;
                transform.position = pivot.position + offset;
            }
        }

        private void Update()
        {
            if (pivot == null)
            {
                return;
            }

            transform.RotateAround(pivot.position, axis, angularSpeed * Time.deltaTime);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (pivot == null)
            {
                return;
            }

            Vector3 normal = axis.sqrMagnitude > 0f ? axis.normalized : Vector3.up;
            Vector3 axisX = Vector3.ProjectOnPlane(Vector3.right, normal);
            if (axisX.sqrMagnitude < 1e-4f)
            {
                axisX = Vector3.ProjectOnPlane(Vector3.up, normal);
            }

            axisX.Normalize();
            Vector3 axisY = Vector3.Cross(normal, axisX).normalized;

            Vector3 center = pivot.position;
            Vector3 previous = center + axisX * radius;

            const int segments = 48;
            Gizmos.color = Color.yellow;
            for (int i = 1; i <= segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 next = center + (Mathf.Cos(angle) * axisX + Mathf.Sin(angle) * axisY) * radius;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }

            Gizmos.color = Color.white;
            Gizmos.DrawLine(center, transform.position);
        }
#endif
    }
}
